using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Baccarat.Infrastructure.Automation;

// DOM driver restricted to the app-owned local training page. Never attaches to personal tabs.
public sealed class LocalDomBrowser : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> replies = new();
    private readonly SemaphoreSlim send = new(1, 1);
    private readonly SemaphoreSlim dom = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Uri page;
    private readonly Func<string, JsonElement, Task<string?>> command;
    private readonly Action<string> fault;
    private int serial;
    private string snapshot = "{}";
    private Task? polling;
    private Process? browserProcess;
    private int disposed;
    public bool IsConnected => socket.State == WebSocketState.Open && !lifetime.IsCancellationRequested;

    public LocalDomBrowser(Uri page, Func<string, JsonElement, Task<string?>> command, Action<string> fault)
    {
        if (page.Scheme != "http" || page.Host != "127.0.0.1" || page.AbsolutePath != "/" || page.Fragment.Length < 32)
            throw new ArgumentException("Only an app-owned loopback training page is supported.", nameof(page));
        this.page = page;
        this.command = command;
        this.fault = fault;
    }

    public async Task StartAsync()
    {
        var candidates = OperatingSystem.IsWindows() ? new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe") } :
            new[] { "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/usr/bin/chromium", "/usr/bin/google-chrome" };
        var executable = candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("Install Chrome or Edge to use the DOM simulator.");
        var profile = Path.Combine(Path.GetTempPath(), "Baccarat-DOM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profile,
            "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--disable-extensions", "--new-window", "about:blank" })
            start.ArgumentList.Add(arg);
        browserProcess = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated browser.");
        var activePort = Path.Combine(profile, "DevToolsActivePort");
        for (var i = 0; i < 100 && !File.Exists(activePort); i++) await Task.Delay(150, lifetime.Token);
        var lines = await File.ReadAllLinesAsync(activePort, lifetime.Token);
        if (lines.Length < 2 || !int.TryParse(lines[0], out var port) || port is < 1024 or > 65535)
            throw new InvalidOperationException("Browser did not expose a valid local debugging port.");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var targets = JsonDocument.Parse(await client.GetStringAsync($"http://127.0.0.1:{port}/json/list", lifetime.Token));
        using (targets)
        {
            var target = targets.RootElement.EnumerateArray().First(t => t.GetProperty("type").GetString() == "page" && t.GetProperty("url").GetString() == "about:blank");
            var endpoint = new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!);
            if (endpoint.Scheme != "ws" || endpoint.Host != "127.0.0.1" || endpoint.Port != port)
                throw new InvalidOperationException("Invalid browser debugger address.");
            await socket.ConnectAsync(endpoint, lifetime.Token);
        }
        _ = Task.Run(ReceiveAsync);
        await SendAsync("Page.enable");
        await SendAsync("Fetch.enable", new { patterns = new[] { new { urlPattern = "*" } } });
        await SendAsync("Page.setDownloadBehavior", new { behavior = "deny" });
        await SendAsync("Page.navigate", new { url = page.AbsoluteUri });
        for (var i = 0; i < 60; i++)
        {
            var ready = await EvaluateAsync("document.querySelector('#dom-command') !== null", false);
            if (ready.ValueKind == JsonValueKind.True) { polling = Task.Run(PollAsync); return; }
            await Task.Delay(100, lifetime.Token);
        }
        throw new InvalidOperationException("The simulator DOM did not load.");
    }

    public void Publish(object state) => Volatile.Write(ref snapshot, JsonSerializer.Serialize(state));

    public async Task ClickAsync(string action)
    {
        var confirmedClear = action == "clear-confirmed";
        if (confirmedClear) action = "clear";
        if (!AllowedActions.Contains(action)) throw new ArgumentException("Unknown simulator control.", nameof(action));
        await dom.WaitAsync(lifetime.Token);
        try
        {
            await ConsumeAsync();
            await EvaluateAsync("document.activeElement.blur()");
            await RenderAsync();
            if (confirmedClear) await EvaluateAsync("document.querySelector('[data-action=\"clear\"]').dataset.confirmed='true'");
            var clicked = await EvaluateAsync($"(()=>{{const b=document.querySelector('[data-action=\"{action}\"]');if(!b||b.disabled)return false;b.click();return true;}})()");
            if (clicked.ValueKind != JsonValueKind.True) throw new InvalidOperationException("The simulator control is unavailable.");
            await ConsumeAsync();
            await RenderAsync();
        }
        finally { dom.Release(); }
    }

    private static readonly HashSet<string> AllowedActions = ["player", "banker", "tie", "player-pair", "banker-pair", "deal", "undo", "shoe", "clear", "apply", "draft", "connect", "recover", "initial", "approve", "auto", "enable-auto", "disable-auto", "pause"];

    private async Task PollAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await dom.WaitAsync(lifetime.Token);
                try { await ConsumeAsync(); await RenderAsync(); }
                finally { dom.Release(); }
                await Task.Delay(200, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch { fault("Browser disconnected or left the training page. Reopen the simulator to continue."); lifetime.Cancel(); }
    }

    private async Task ConsumeAsync()
    {
        var value = await EvaluateAsync("document.querySelector('#dom-command').textContent");
        var text = value.GetString();
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > 8192) throw new InvalidOperationException("Oversized DOM command.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        var action = root.GetProperty("action").GetString()!;
        if (!AllowedActions.Contains(action)) throw new InvalidOperationException("Unknown DOM command.");
        // Clear before dispatch, so errors/reconnect never replay a click.
        await EvaluateAsync("document.querySelector('#dom-command').textContent=''");
        var error = await command(action, root.Clone());
        await EvaluateAsync($"document.querySelector('#message').textContent={JsonSerializer.Serialize(error ?? "Applied to desktop training session.")}");
    }

    private Task<JsonElement> RenderAsync() => EvaluateAsync("(()=>{const s=" + Volatile.Read(ref snapshot) + ";" + """
        const text=(id,value)=>document.getElementById(id).textContent=value||'';
        text('session',s.title);text('next',s.next);text('balance','Training balance: '+s.balance);
        text('status',s.status);text('cards',s.cards);text('wager',s.wager);text('chips',s.chips);
        document.body.dataset.round=s.roundKey;
        document.body.dataset.active=s.active?'true':'false';
        for(const b of document.querySelectorAll('[data-action]'))b.disabled=!s.active;
        document.querySelector('[data-action="deal"]').disabled=!s.canDeal;
        for(const side of ['player','banker'])document.querySelector('[data-action="'+side+'-pair"]').setAttribute('aria-pressed',String(s[side+'Pair']));
        const form=document.getElementById('settings');
        if(!form.contains(document.activeElement)||!['INPUT','SELECT'].includes(document.activeElement.tagName)){
          document.getElementById('strategy').value=s.strategy;
          document.getElementById('unit').value=s.unit;
          document.getElementById('bankroll').value=s.bankroll;
          const box=document.getElementById('fields');box.replaceChildren();
          for(const field of s.fields||[]){const label=document.createElement('label');label.textContent=field.label;
            const input=document.createElement('input');input.dataset.field=field.key;input.value=field.value;input.placeholder=field.hint;label.append(input);box.append(label);}
        }
        })()
        """);

    private async Task<JsonElement> EvaluateAsync(string expression, bool checkPage = true)
    {
        if (checkPage)
        {
            var location = await EvaluateAsync("location.href", false);
            if (location.GetString() != page.AbsoluteUri) throw new InvalidOperationException("DOM control is limited to the local simulator.");
        }
        var result = await SendAsync("Runtime.evaluate", new { expression, returnByValue = true });
        if (result.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Simulator DOM operation failed.");
        return result.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    private async Task<JsonElement> SendAsync(string method, object? parameters = null)
    {
        var id = Interlocked.Increment(ref serial);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        replies[id] = reply;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { } });
            await send.WaitAsync(lifetime.Token);
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, lifetime.Token); }
            finally { send.Release(); }
            return await reply.Task.WaitAsync(TimeSpan.FromSeconds(15), lifetime.Token);
        }
        finally { replies.TryRemove(id, out _); }
    }

    private async Task ReceiveAsync()
    {
        var buffer = new byte[8192];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Browser closed");
                    data.Write(buffer, 0, part.Count);
                    if (data.Length > 1024 * 1024) throw new IOException("Oversized browser response");
                } while (!part.EndOfMessage);
                using var parsed = JsonDocument.Parse(data.ToArray());
                var root = parsed.RootElement;
                if (root.TryGetProperty("id", out var id) && replies.TryGetValue(id.GetInt32(), out var reply))
                {
                    if (root.TryGetProperty("error", out _)) reply.TrySetException(new IOException("Browser command rejected"));
                    else reply.TrySetResult(root.GetProperty("result").Clone());
                }
                else if (root.TryGetProperty("method", out var method) && method.GetString() == "Fetch.requestPaused")
                    _ = FilterRequestAsync(root.GetProperty("params").Clone());
            }
        }
        catch (Exception ex)
        {
            foreach (var reply in replies.Values) reply.TrySetException(new IOException("Browser disconnected", ex));
            if (!lifetime.IsCancellationRequested) fault("Browser disconnected. No actions will be replayed.");
            lifetime.Cancel();
        }
    }

    private async Task FilterRequestAsync(JsonElement request)
    {
        try
        {
            var url = new Uri(request.GetProperty("request").GetProperty("url").GetString()!);
            var id = request.GetProperty("requestId").GetString();
            var allowed = url.Scheme == page.Scheme && url.Authority == page.Authority && url.AbsolutePath is "/" or "/app.js";
            if (allowed) await SendAsync("Fetch.continueRequest", new { requestId = id });
            else await SendAsync("Fetch.failRequest", new { requestId = id, errorReason = "BlockedByClient" });
        }
        catch { lifetime.Cancel(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (socket.State == WebSocketState.Open && !lifetime.IsCancellationRequested)
        { try { await SendAsync("Browser.close"); } catch { } }
        try { if (browserProcess is { HasExited: false }) browserProcess.CloseMainWindow(); } catch (InvalidOperationException) { }
        lifetime.Cancel();
        socket.Abort();
        socket.Dispose();
        browserProcess?.Dispose();
    }
}
