using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Baccarat.Infrastructure.Automation;

public sealed class CdpException(string message) : IOException(message);

// Minimal Chrome DevTools Protocol client over one loopback WebSocket (flattened sessions).
internal sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> replies = new();
    private readonly SemaphoreSlim send = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int serial;
    private int disposed;
    public event Action<string, JsonElement, string?>? Event;
    public event Action<Exception>? Closed;
    public bool IsOpen => socket.State == WebSocketState.Open && !lifetime.IsCancellationRequested;

    public async Task ConnectAsync(Uri endpoint, CancellationToken token)
    {
        if (endpoint.Scheme != "ws" || !(endpoint.Host is "127.0.0.1" or "localhost" or "[::1]"))
            throw new InvalidOperationException("The browser debugger must be on this computer.");
        await socket.ConnectAsync(endpoint, token);
        _ = Task.Run(ReceiveAsync);
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null, string? sessionId = null, int timeoutSeconds = 15)
    {
        var id = Interlocked.Increment(ref serial);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        replies[id] = reply;
        try
        {
            var message = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new { } };
            if (sessionId is not null) message["sessionId"] = sessionId;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            await send.WaitAsync(lifetime.Token);
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, lifetime.Token); }
            finally { send.Release(); }
            return await reply.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), lifetime.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or TimeoutException)
        {
            throw new Baccarat.Core.Automation.AutomationDisconnectedException("Browser connection lost: " + ex.Message);
        }
        finally { replies.TryRemove(id, out _); }
    }

    public async Task<JsonElement> EvaluateAsync(string expression, string? sessionId = null)
    {
        var result = await SendAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = false }, sessionId);
        if (result.TryGetProperty("exceptionDetails", out var details))
            throw new CdpException("Page script failed: " + (details.TryGetProperty("text", out var t) ? t.GetString() : "error"));
        return result.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    private async Task ReceiveAsync()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Browser closed the connection.");
                    data.Write(buffer, 0, part.Count);
                    if (data.Length > 8 * 1024 * 1024) throw new IOException("Oversized browser message.");
                } while (!part.EndOfMessage);
                using var parsed = JsonDocument.Parse(data.ToArray());
                var root = parsed.RootElement;
                if (root.TryGetProperty("id", out var id) && replies.TryGetValue(id.GetInt32(), out var reply))
                {
                    if (root.TryGetProperty("error", out var error))
                        reply.TrySetException(new CdpException(error.TryGetProperty("message", out var m) ? m.GetString() ?? "rejected" : "rejected"));
                    else reply.TrySetResult(root.GetProperty("result").Clone());
                }
                else if (root.TryGetProperty("method", out var method))
                {
                    var session = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
                    Event?.Invoke(method.GetString()!, root.TryGetProperty("params", out var p) ? p.Clone() : default, session);
                }
            }
        }
        catch (Exception ex)
        {
            foreach (var reply in replies.Values) reply.TrySetException(new Baccarat.Core.Automation.AutomationDisconnectedException("Browser disconnected."));
            if (!lifetime.IsCancellationRequested) Closed?.Invoke(ex);
            lifetime.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        try { socket.Abort(); } catch { }
        socket.Dispose();
        await Task.CompletedTask;
    }
}
