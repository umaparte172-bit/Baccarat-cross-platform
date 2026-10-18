using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Baccarat.Core;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Automation;

// A table drawn entirely on one canvas (no buttons or text in the DOM) that talks Socket.io-style messages over a
// WebSocket, like the play-money demo table. Checks: canvas detection, point clicks, window/results/acks from messages.
internal static class CanvasTableChecks
{
    private const string Page = """
        <!doctype html><title>Mock canvas table</title><body style="margin:0;background:#222">
        <canvas id=c width=800 height=450 style="width:640px;height:360px;display:block;margin:20px"></canvas>
        <script>
        const c = document.getElementById('c'), g = c.getContext('2d'); g.fillStyle = '#143'; g.fillRect(0, 0, 800, 450);
        const ws = new WebSocket('ws://' + location.host + '/ws');
        let chip = 1, pending = 0, side = '', timer = null;
        const spots = {chip1: [0.40, 0.926], chip5: [0.45, 0.926], chip10: [0.50, 0.926], player: [0.415, 0.62], banker: [0.582, 0.62]};
        c.addEventListener('click', e => {
          const r = c.getBoundingClientRect(), fx = (e.clientX - r.left) / r.width, fy = (e.clientY - r.top) / r.height;
          const near = p => Math.abs(fx - p[0]) < 0.03 && Math.abs(fy - p[1]) < 0.05;
          if (near(spots.chip1)) chip = 1; else if (near(spots.chip5)) chip = 5; else if (near(spots.chip10)) chip = 10;
          else if (near(spots.player) || near(spots.banker)) {
            side = near(spots.player) ? 'PLAYER' : 'BANKER'; pending += chip; clearTimeout(timer);
            timer = setTimeout(() => { ws.send('42["REQ_BET",' + JSON.stringify(JSON.stringify({bets: [{type: side, amount: pending}]})) + ']'); pending = 0; }, 300);
          }
        });
        </script>
        """;

    private static readonly ConcurrentQueue<string> Commands = new();

    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("BACCARAT_DOM_CHECKS") != "1")
        { Console.WriteLine("SKIP: canvas-table browser checks require BACCARAT_DOM_CHECKS=1 and installed Chrome/Edge."); return; }
        Task.Run(RunCore).GetAwaiter().GetResult();
    }

    private static async Task RunCore()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var port = 19200 + Random.Shared.Next(0, 300);
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var open = false;
        var totals = new Dictionary<string, decimal>();
        var ws = (WebSocket?)null;
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                if (ctx.Request.Url!.AbsolutePath == "/ws" && ctx.Request.IsWebSocketRequest)
                {
                    var accepted = await ctx.AcceptWebSocketAsync(null);
                    ws = accepted.WebSocket;
                    _ = Task.Run(() => Serve(ws));
                    continue;
                }
                var bytes = Encoding.UTF8.GetBytes(Page);
                ctx.Response.ContentType = "text/html";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
        async Task Send(WebSocket socket, string text)
        {
            try { await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None); } catch { }
        }
        async Task Serve(WebSocket socket)
        {
            var buffer = new byte[8192];
            _ = Task.Run(async () =>
            {
                while (socket.State == WebSocketState.Open)
                {
                    await Task.Delay(250);
                    while (Commands.TryDequeue(out var cmd))
                    {
                        if (cmd == "open") open = true;
                        else if (cmd == "close") { open = false; await Send(socket, "42[\"ROUND_BETTING_END\",\"{\\\"round_id\\\":1}\"]"); }
                        else if (cmd.StartsWith("settle:", StringComparison.Ordinal))
                        {
                            var parts = cmd.Split(':');
                            var pp = parts.Length > 2 && parts[2] == "pp";
                            await Send(socket, "42[\"ROUND_SETTLEMENT\",\"{\\\"outcome\\\":{\\\"winner\\\":" + parts[1] + ",\\\"score\\\":6,\\\"is_player_pair\\\":" + (pp ? "true" : "false") + ",\\\"is_banker_pair\\\":false}}\"]");
                        }
                    }
                    if (open) await Send(socket, "42[\"ROUND_BETTING\",\"{\\\"round_id\\\":1,\\\"countdown\\\":9}\"]");
                }
            });
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try { result = await socket.ReceiveAsync(buffer, CancellationToken.None); } catch { return; }
                if (result.MessageType == WebSocketMessageType.Close) return;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (!open || !text.Contains("REQ_BET")) continue;
                var m = Regex.Match(text.Replace("\\\"", "\""), "\"type\":\"(PLAYER|BANKER)\",\"amount\":([0-9.]+)");
                if (m.Success)
                {
                    // Like the real table: the running total per side, not just this request.
                    totals[m.Groups[1].Value] = totals.GetValueOrDefault(m.Groups[1].Value) + decimal.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    await Send(socket, "42[\"RES_BET\",\"{\\\"result_code\\\":0,\\\"value\\\":{\\\"bets\\\":[{\\\"type\\\":\\\"" + m.Groups[1].Value + "\\\",\\\"amount\\\":" + totals[m.Groups[1].Value].ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\\\"fee_amount\\\":0}]}}\"]");
                }
            }
        }

        var profile = LiveTableProfileStore.MultiplierBaccaratDemo with { Name = "Mock canvas", StartClick = "", PlacementWaitSeconds = 0 };
        var debugPort = 19600 + Random.Shared.Next(0, 300);
        var adapter = new LiveBrowserTableAdapter(profile, new LiveBrowserOptions(debugPort, Path.Combine(Path.GetTempPath(), "Baccarat-Canvas-" + Guid.NewGuid().ToString("N"))));
        var seen = new ConcurrentQueue<LiveRoundResult>();
        adapter.ResultObserved += seen.Enqueue;
        try
        {
            await adapter.OpenBrowserAsync(default, $"http://127.0.0.1:{port}/");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await adapter.WaitForTableAsync(cts.Token);
            Check(adapter.DetectedHost == "127.0.0.1", "A canvas-only page must be detected by its canvas");
            await adapter.ConnectAsync();
            Check(profile.CanBet && profile.CanRead, "Canvas profile must be usable for reading and betting");
            for (var i = 0; i < 40 && ws is null; i++) await Task.Delay(100);
            Check((await adapter.GetChipsAsync()).SequenceEqual([1m, 5m, 10m, 50m, 100m]), "Static chips must be offered");

            // Window closed: nothing may be clicked.
            await adapter.ClearSelectionAsync(); await adapter.SelectChipAsync(5m);
            var closed = await adapter.PlaceBetAsync(new AutomationBetRequest(Guid.NewGuid(), BetSide.Banker, 5m, [5m], false));
            Check(closed.Status == BetPlacementStatus.Rejected, "Closed betting window must reject: " + closed.Message);

            Commands.Enqueue("open");
            for (var i = 0; i < 40 && adapter.IsBettingOpen != true; i++) await Task.Delay(100);
            Check(adapter.IsBettingOpen == true, "Betting window must be read from table messages");
            var request = new AutomationBetRequest(Guid.NewGuid(), BetSide.Banker, 6m, [5m, 1m], false);
            await adapter.ClearSelectionAsync(); await adapter.SelectChipAsync(5m); await adapter.SelectChipAsync(1m);
            var placed = await adapter.PlaceBetAsync(request);
            Check(placed.Status == BetPlacementStatus.Accepted, "Canvas placement must be confirmed by the table message: " + placed.Message);
            Check((await adapter.PlaceBetAsync(request)).Status == BetPlacementStatus.Accepted, "Replaying an operation id must not click again");
            var again = new AutomationBetRequest(Guid.NewGuid(), BetSide.Player, 5m, [5m], false);
            await adapter.ClearSelectionAsync(); await adapter.SelectChipAsync(5m);
            Check((await adapter.PlaceBetAsync(again)).Status == BetPlacementStatus.Rejected, "A stake already placed this round must block another");

            Commands.Enqueue("close");
            Commands.Enqueue("settle:3");
            for (var i = 0; i < 40 && seen.IsEmpty; i++) await Task.Delay(100);
            Check(seen.TryDequeue(out var first) && first.Outcome == Outcome.Banker, "Banker result must come from the settlement message");
            Commands.Enqueue("settle:0:pp");
            for (var i = 0; i < 40 && seen.IsEmpty; i++) await Task.Delay(100);
            Check(seen.TryDequeue(out var second) && second.Outcome == Outcome.Player && second.PlayerPair && !second.BankerPair, "Player result with pair flag");
            Commands.Enqueue("settle:2");
            for (var i = 0; i < 40 && seen.IsEmpty; i++) await Task.Delay(100);
            Check(seen.TryDequeue(out var third) && third.Outcome == Outcome.Tie, "Tie result");
            Check(LiveTableChecksAccess.NormalizeSocketText("42[\"EVT\",\"{\\\"a\\\":1}\"]") == "EVT {\"a\":1}", "Socket.io frames must be normalised");
            Console.WriteLine("PASS: canvas table: canvas detection, point clicks, betting window, bet confirmation and results from socket messages");
        }
        finally
        {
            try { await adapter.CloseBrowserAsync(); } catch { }
            await adapter.DisposeAsync();
            listener.Stop();
        }
    }
}
