using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Baccarat.Infrastructure.Automation;

// Serves only our embedded training page. No configurable upstream URL or casino adapter.
public sealed class LocalSimulationServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly Func<string, Task<bool>> deal;
    private readonly SemaphoreSlim requests = new(8, 8);
    private readonly CancellationTokenSource lifetime = new();
    private string state = "{}";
    private long lastPoll;
    private volatile bool disposed;
    public Uri Address { get; }
    public Uri BrowserAddress => new(Address.AbsoluteUri + "#" + token);
    public bool BrowserConnected => Environment.TickCount64 - Interlocked.Read(ref lastPoll) < 5000;

    public LocalSimulationServer(Func<string, Task<bool>> deal)
    {
        this.deal = deal;
        listener.Start(8);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Address = new Uri($"http://127.0.0.1:{port}/");
        Interlocked.Exchange(ref lastPoll, Environment.TickCount64 - 10000);
        _ = ListenAsync();
    }

    public void Publish(object snapshot) => Volatile.Write(ref state, JsonSerializer.Serialize(snapshot));

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
                await WriteAsync(stream, 200, path == "/" ? Page : Script, path == "/" ? "text/html" : "text/javascript", timeout.Token);
                return;
            }
            if (!headers.TryGetValue("X-Simulation-Token", out var credential) || !string.Equals(credential, token, StringComparison.Ordinal))
            { await WriteAsync(stream, 403, "Forbidden", "text/plain", timeout.Token); return; }
            if (method == "GET" && path == "/state")
            {
                Interlocked.Exchange(ref lastPoll, Environment.TickCount64);
                await WriteAsync(stream, 200, Volatile.Read(ref state), "application/json", timeout.Token);
            }
            else if (method == "POST" && path == "/deal")
            {
                if (!headers.TryGetValue("X-Simulation-Round", out var key) || key.Length > 64)
                { await WriteAsync(stream, 400, "Missing round", "text/plain", timeout.Token); return; }
                var accepted = await deal(key);
                await WriteAsync(stream, accepted ? 200 : 409, accepted ? "Settled" : "Not ready or stale round. Review the desktop app.", "text/plain", timeout.Token);
            }
            else await WriteAsync(stream, 404, "Not found", "text/plain", timeout.Token);
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

    private const string Page = """
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Baccarat · Local training table</title>
        <style>
        body{margin:0;background:#101d35;color:#eff5ff;font:16px system-ui}main{max-width:960px;margin:auto;padding:32px}
        .badge{color:#6cf2b2;font-weight:700}h1{font-size:32px}section{background:#192e4e;border:1px solid #365173;border-radius:14px;padding:24px;margin:16px 0}
        #cards{white-space:pre-line;font-size:24px;line-height:1.6}#wager{font-size:22px;color:#ffd580}button{padding:14px 24px;border:0;border-radius:7px;background:#32dda0;color:#102637;font-weight:bold;cursor:pointer;margin:8px 10px 8px 0}button:disabled{opacity:.4;cursor:default}.muted{color:#afc3df}#message{min-height:24px}
        </style><main><div class="badge">LOCAL SIMULATION · NO MONEY · NO CASINO CONNECTION</div>
        <h1>Baccarat training table</h1><p id="connection" role="status">Connecting to desktop…</p>
        <section><h2 id="session">Session</h2><p id="next"></p><p id="wager">No wager accepted</p><p id="chips"></p><p id="balance"></p></section>
        <section><h2>Dealt cards</h2><div id="cards">No cards dealt yet</div></section>
        <button id="deal" disabled>Deal / settle simulated hand</button><button id="reconnect">Reconnect</button>
        <p id="message" role="status"></p><p class="muted">In the desktop app: Connect simulation → Prepare initial bet → Approve pending bet. Enable auto-bet to prepare the next recommendation automatically; each placement still needs approval. Virtual/no-bet rounds can be observed without a wager.</p>
        </main><script src="/app.js"></script></html>
        """;

    private const string Script = """
        'use strict';
        const token=location.hash.slice(1), el=id=>document.getElementById(id);
        let state=null,busy=false,stopped=false,timer,polling=false;
        async function request(path,options={}) {
          const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),5000);
          try{const r=await fetch(path,{...options,cache:'no-store',headers:{'X-Simulation-Token':token,...options.headers},signal:controller.signal});
            if(!r.ok)throw Error(r.status===403?'Access denied. Reopen this table from the desktop app.':'Table not ready; review the desktop app.');
            return r;
          }finally{clearTimeout(timeout);}
        }
        async function poll(){
          if(stopped||polling)return;clearTimeout(timer);polling=true;
          try{state=await (await request('/state')).json();
            el('connection').textContent='Connected to local desktop · '+state.status;
            el('session').textContent=state.title;
            el('next').textContent='Next recommendation: '+state.next;
            el('balance').textContent='Calculated training balance: '+state.balance;
            el('wager').textContent=state.wager;
            el('chips').textContent=state.chips;
            el('cards').textContent=state.cards||'No cards dealt yet';
            el('deal').disabled=busy||!state.canDeal;
          }catch(e){state=null;el('connection').textContent='Disconnected · '+e.message;el('deal').disabled=true;}
          polling=false;timer=setTimeout(poll,600);
        }
        el('deal').onclick=async()=>{if(busy||!state)return;busy=true;el('deal').disabled=true;
          try{await request('/deal',{method:'POST',headers:{'X-Simulation-Round':state.roundKey}});el('message').textContent='Hand settled in desktop session.';}
          catch(e){el('message').textContent=e.message;}finally{busy=false;}
        };
        el('reconnect').onclick=()=>{el('message').textContent='Refreshing state only; no wager is replayed.';poll();};
        window.addEventListener('pagehide',()=>{stopped=true;clearTimeout(timer);});poll();
        """;
}
