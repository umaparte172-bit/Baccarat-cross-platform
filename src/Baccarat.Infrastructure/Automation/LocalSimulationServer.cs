using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Baccarat.Infrastructure.Automation;

// Serves only our embedded training page. No configurable upstream URL or casino adapter.
public sealed class LocalSimulationServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly SemaphoreSlim requests = new(8, 8);
    private readonly CancellationTokenSource lifetime = new();
    private volatile bool disposed;
    public Uri Address { get; }
    public Uri BrowserAddress => new(Address.AbsoluteUri + "#" + token);

    public LocalSimulationServer()
    {
        listener.Start(8);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Address = new Uri($"http://127.0.0.1:{port}/");
        _ = ListenAsync();
    }


    private async Task ListenAsync()
    {
        while (!disposed)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { break; }
            if (!requests.Wait(0)) { client.Dispose(); continue; }
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var stream = client.GetStream();
            // One bodyless request per connection, at most 8 KiB of headers and five seconds.
            // No chunking, uploads, proxying, persistent connections or filesystem routes.
            var buffer = new byte[8192];
            var count = 0;
            var end = -1;
            while (count < buffer.Length && end < 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
                if (read == 0) return;
                count += read;
                for (var i = 3; i < count; i++)
                    if (buffer[i - 3] == 13 && buffer[i - 2] == 10 && buffer[i - 1] == 13 && buffer[i] == 10) { end = i + 1; break; }
            }
            if (end < 0 || end != count) { await WriteAsync(stream, 400, "Invalid request", "text/plain", timeout.Token); return; }
            var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n", StringSplitOptions.None);
            var start = lines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length && lines[i].Length > 0; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0 || char.IsWhiteSpace(lines[i][0]) || !headers.TryAdd(lines[i][..colon], lines[i][(colon + 1)..].Trim()))
                { await WriteAsync(stream, 400, "Invalid headers", "text/plain", timeout.Token); return; }
            }
            if (start.Length != 3 || start[2] != "HTTP/1.1" || headers.ContainsKey("Transfer-Encoding") ||
                (headers.TryGetValue("Content-Length", out var length) && length != "0"))
            { await WriteAsync(stream, 400, "Bodyless HTTP/1.1 required", "text/plain", timeout.Token); return; }
            if (!headers.TryGetValue("Host", out var host) || host != Address.Authority ||
                (headers.TryGetValue("Origin", out var origin) && origin != Address.GetLeftPart(UriPartial.Authority)))
            { await WriteAsync(stream, 403, "Forbidden", "text/plain", timeout.Token); return; }
            var method = start[0];
            var path = start[1];
            if (method == "GET" && path is "/" or "/app.js")
            {
                await WriteAsync(stream, 200, path == "/" ? LocalSimulationPage.Html : LocalSimulationPage.Script, path == "/" ? "text/html" : "text/javascript", timeout.Token);
                return;
            }
            await WriteAsync(stream, 404, "No application APIs. Use the DOM controls.", "text/plain", timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
        catch { /* Close failed local requests; no internal exception details are returned. */ }
        finally
        {
            client.Dispose();
            requests.Release();
        }
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string value, string type, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: {type}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        listener.Stop();
    }

}
