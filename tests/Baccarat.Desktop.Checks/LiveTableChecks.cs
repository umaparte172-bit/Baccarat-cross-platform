using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Baccarat.Core;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Automation;

// Drives the live-table adapter against a local mock table (same structure a casino page has:
// a table inside an iframe, status banner, chips, bet spots, bet total, result history) in a real browser.
internal static class LiveTableChecks
{
    private const string Outer = "<!doctype html><title>Mock lobby</title><iframe id=f src=\"/table\" style=\"width:700px;height:500px;border:0\"></iframe>";
    private const string Table = """
        <!doctype html><title>Mock table</title>
        <style>body{margin:0;font:14px sans-serif}button,.spot{display:inline-block;margin:6px;padding:14px;border:1px solid #444;cursor:pointer}.sel{background:#fd0}</style>
        <div id=status>WAIT FOR NEXT GAME</div>
        <div id=chips><button data-value="1">1</button><button data-value="5">5</button><button data-value="25">25</button><button data-value="100">100</button></div>
        <div><div class=spot data-role="player" id=p>PLAYER</div><div class=spot data-role="banker" id=b>BANKER</div></div>
        <div>Total: <span id=total></span> <span id=bal>Balance 1,000.00</span></div>
        <ul id=results></ul>
        <script>
        let chip=null,total=0;
        document.querySelectorAll('#chips button').forEach(b=>b.onclick=()=>{chip=+b.dataset.value;document.querySelectorAll('#chips button').forEach(x=>x.classList.toggle('sel',x===b));});
        for(const id of ['p','b'])document.getElementById(id).onclick=()=>{if(chip&&document.getElementById('status').textContent.includes('PLACE YOUR BETS')){total+=chip;document.getElementById('total').textContent=total;}};
        async function poll(){try{const t=await (await fetch('/cmd')).text();for(const c of t.split('\n').filter(Boolean)){
          if(c==='open'){document.getElementById('status').textContent='PLACE YOUR BETS';}
          else if(c==='close'){document.getElementById('status').textContent='WAIT FOR NEXT GAME';}
          else if(c==='reset'){total=0;document.getElementById('total').textContent='';}
          else if(c.startsWith('result:')){const li=document.createElement('li');li.className=c.slice(7);li.textContent=c.slice(7)[0].toUpperCase();document.getElementById('results').append(li);}}}catch(e){}
          setTimeout(poll,100);}
        poll();
        </script>
        """;

    private static readonly ConcurrentQueue<string> Commands = new();
    private static int mockPort;
    private static HttpListener? mock;

    private static void StartMock()
    {
        mockPort = 18080 + Random.Shared.Next(0, 800);
        mock = new HttpListener();
        mock.Prefixes.Add($"http://127.0.0.1:{mockPort}/");
        mock.Start();
        var listener = mock;
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var path = ctx.Request.Url!.AbsolutePath;
                string body = path == "/table" ? Table : path == "/cmd" ? Drain() : Outer;
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.ContentType = path == "/cmd" ? "text/plain" : "text/html";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
        static string Drain() { var sb = new StringBuilder(); while (Commands.TryDequeue(out var c)) sb.Append(c).Append((char)10); return sb.ToString(); }
    }

    private static void OpenTab(int debugPort, string url)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"http://127.0.0.1:{debugPort}/json/new?{url}");
        client.Send(request).EnsureSuccessStatusCode();
    }

    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("BACCARAT_DOM_CHECKS") != "1")
        { Console.WriteLine("SKIP: live-table browser checks require BACCARAT_DOM_CHECKS=1 and installed Chrome/Edge."); return; }
        StartMock();
        try
        {
            Task.Run(RunCore).GetAwaiter().GetResult();
            RunSession();
        }
        finally { mock?.Stop(); }
    }

    // The app path: profile file -> SessionTools -> controller -> live adapter -> recorded hands and approved bet.
    private static void RunSession()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Pump(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(40);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            if (!task.IsCompleted) throw new TimeoutException("live session check timed out");
            task.GetAwaiter().GetResult();
        }
        bool Until(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!condition() && DateTime.UtcNow < until) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
            return condition();
        }
        var profileDirectory = Path.Combine(Path.GetTempPath(), "Baccarat-Live-Profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDirectory);
        var profile = new LiveTableProfile
        {
            Name = "Mock",
            StatusSelector = "#status", ChipSelector = "#chips button", ChipValueSource = "data-value",
            PlayerSelector = "[data-role=player]", BankerSelector = "[data-role=banker]", BetTotalSelector = "#total",
            ResultSelector = "#results li", NewestResult = "last", PollMilliseconds = 150,
            ResultRules = [new("player", Outcome.Player), new("banker", Outcome.Banker), new("tie", Outcome.Tie)]
        };
        File.WriteAllText(Path.Combine(profileDirectory, "mock.json"), System.Text.Json.JsonSerializer.Serialize(profile));
        Environment.SetEnvironmentVariable("BACCARAT_LIVE_PROFILES", profileDirectory);
        var workspace = new Baccarat.Desktop.ViewModels.DashboardWorkspace();
        workspace.CreateSession();
        var session = workspace.Selected!;
        session.SelectStrategy("Martingale");
        Check(session.ApplySettings(out _), "Apply fixture");
        Baccarat.Desktop.SessionTools? tools = null;
        using var context = tools = new Baccarat.Desktop.SessionTools(session, Baccarat.Core.Diagnostics.NullDiagnosticSink.Instance, () => tools!.Refresh());
        context.LiveOptions = new LiveBrowserOptions(19600 + Random.Shared.Next(0, 300), Path.Combine(profileDirectory, "browser"));
        Check(context.LiveProfileNames().Contains("Mock"), "Profile files must be listed");
        Commands.Enqueue("reset"); Commands.Enqueue("close");
        Pump(context.OpenLiveAsync("Mock"));
        Thread.Sleep(1500);

        var connect = context.ConnectLiveAsync("Mock"); Pump(connect);
        Check(connect.Result.StartsWith("Watching"), "Watch message: " + connect.Result);
        Thread.Sleep(2500);
        Check(context.Controller.Snapshot.State == AutomationRunState.Disconnected, "Nothing may connect before a table is opened");
        OpenTab(context.LiveOptions.DebugPort, $"http://127.0.0.1:{mockPort}/");
        Check(Until(() => !context.Controller.Snapshot.IsSimulation && context.Controller.Snapshot.State == AutomationRunState.Ready),
            "Opening a table must be detected and connected automatically: " + context.Controller.Snapshot.Message);
        Thread.Sleep(500);
        Commands.Enqueue("result:banker");
        Check(Until(() => session.Hands.Count == 1 && session.Hands[0].Round.Outcome == Outcome.Banker), "Live result must be recorded in the session");
        Commands.Enqueue("result:player");
        Check(Until(() => session.Hands.Count == 2), "Second live result recorded");
        Commands.Enqueue("open");
        Thread.Sleep(600);
        Pump(context.Controller.InitialBetAsync(session.Engine!.Recommendation).AsTask());
        Check(context.Controller.Snapshot.State == AutomationRunState.AwaitingApproval, "Live bets must wait for approval");
        Pump(context.Controller.ApprovePendingAsync().AsTask());
        Check(context.Controller.Snapshot.AcceptedBets == 1, "Approved bet must be placed on the live table: " + context.Controller.Snapshot.Message);
        // A profile with no selectors must still detect the open table and save its structure for calibration.
        Pump(context.DisconnectLiveAsync());
        File.WriteAllText(Path.Combine(profileDirectory, "bare.json"), System.Text.Json.JsonSerializer.Serialize(new LiveTableProfile { Name = "Bare" }));
        var bare = context.ConnectLiveAsync("Bare"); Pump(bare);
        Check(bare.Result.StartsWith("Watching"), "Bare profile must start watching: " + bare.Result);
        Check(Until(() => Directory.GetFiles(profileDirectory, "inspection-*.json").Length == 1), "Bare profile must save an inspection report once the table is detected");
        Pump(context.CloseLiveBrowserAsync());
        Console.WriteLine("PASS: live table through the app: profile, connect, results recorded, approval-gated placement");
    }




    private static void RunCore()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        T Get<T>(Task<T> task) { if (!task.Wait(TimeSpan.FromSeconds(40))) throw new TimeoutException("live check timed out"); return task.Result; }
        void Wait(Task task) { if (!task.Wait(TimeSpan.FromSeconds(40))) throw new TimeoutException("live check timed out"); }
        T GetV<T>(ValueTask<T> task) => Get(task.AsTask());
        IReadOnlyList<decimal> WaitV2(ValueTask<IReadOnlyList<decimal>> task) => Get(task.AsTask());
        void WaitV(ValueTask task) => Wait(task.AsTask());

        var commands = Commands;
        var port = mockPort;
        _ = 0;

        var profile = new LiveTableProfile
        {
            Name = "Mock", StartUrl = "", HostContains = "127.0.0.1", FrameUrlContains = "/table",
            StatusSelector = "#status", ChipSelector = "#chips button", ChipValueSource = "data-value",
            PlayerSelector = "[data-role=player]", BankerSelector = "xpath://*[@data-role='banker']", BetTotalSelector = "#total",
            ResultSelector = "#results li", NewestResult = "last", BalanceSelector = "#bal", PollMilliseconds = 150,
            ResultRules = [new("player", Outcome.Player), new("banker", Outcome.Banker), new("tie", Outcome.Tie)]
        };
        var profileDirectory = Path.Combine(Path.GetTempPath(), "Baccarat-Live-Check-" + Guid.NewGuid().ToString("N"));
        var debugPort = 19014 + Random.Shared.Next(0, 500);
        var adapter = new LiveBrowserTableAdapter(profile, new LiveBrowserOptions(debugPort, profileDirectory));
        var seen = new ConcurrentQueue<LiveRoundResult>();
        LiveRoundResult first = null!;
        adapter.ResultObserved += seen.Enqueue;
        adapter.Faulted += m => Console.WriteLine("FAULT: " + m);
        try
        {
            Check(!adapter.IsConnected, "Starts disconnected");
            Wait(adapter.OpenBrowserAsync());
            Thread.Sleep(1500);

            // Not tied to any URL: no host filter, no frame hint. Nothing is detected on a blank tab; once the
            // user opens a page showing a table (inside an iframe here) it is found and locked to that host.
            var detector = new LiveBrowserTableAdapter(profile with { HostContains = "", StartUrl = "", FrameUrlContains = null },
                new LiveBrowserOptions(debugPort, profileDirectory, LaunchBrowser: false));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var waiting = detector.WaitForTableAsync(cts.Token);
            Thread.Sleep(2500);
            Check(!waiting.IsCompleted && !detector.IsConnected, "Must keep waiting while no table is open");
            OpenTab(debugPort, $"http://127.0.0.1:{port}/");
            Wait(waiting);
            Check(detector.DetectedHost == "127.0.0.1", "Detected host must be locked to the page the user opened");
            WaitV(detector.ConnectAsync());
            Check(WaitV2(detector.GetChipsAsync()).SequenceEqual([1m, 5m, 25m, 100m]), "Auto-detected frame must be readable without any URL configured");
            Wait(detector.DisposeAsync().AsTask());

            WaitV(adapter.ConnectAsync());
            Check(adapter.IsConnected && !adapter.IsSimulation, "Live adapter connects to the browser tab and is not a simulation");

            var report = Get(adapter.InspectAsync());
            Check(report.Contains("data-role") || report.Contains("player"), "Inspection must expose table structure for calibration");

            var chips = GetV(adapter.GetChipsAsync());
            Check(chips.SequenceEqual([1m, 5m, 25m, 100m]), "Chips must be read from the table (found: " + string.Join(",", chips) + ")");

            // Betting window closed: refuse and click nothing.
            WaitV(adapter.ClearSelectionAsync());
            WaitV(adapter.SelectChipAsync(25m));
            var closed = new AutomationBetRequest(Guid.NewGuid(), BetSide.Player, 25m, [25m], false);
            Check(GetV(adapter.PlaceBetAsync(closed)).Status == BetPlacementStatus.Rejected, "Closed window must reject placement");

            commands.Enqueue("open");
            Thread.Sleep(600);
            Check(adapter.IsBettingOpen == true, "Open banner must be recognised (state " + adapter.IsBettingOpen + ")");
            Check(adapter.Balance == "Balance 1,000.00", "Balance must be read");

            var request = new AutomationBetRequest(Guid.NewGuid(), BetSide.Banker, 30m, [25m, 5m], false);
            WaitV(adapter.ClearSelectionAsync());
            WaitV(adapter.SelectChipAsync(25m));
            WaitV(adapter.SelectChipAsync(5m));
            var placed = GetV(adapter.PlaceBetAsync(request));
            Check(placed.Status == BetPlacementStatus.Accepted, "Placement must be confirmed by the table total: " + placed.Message);
            Check(GetV(adapter.PlaceBetAsync(request)).Status == BetPlacementStatus.Accepted, "Replaying an operation id must not click again");
            var second = new AutomationBetRequest(Guid.NewGuid(), BetSide.Player, 25m, [25m], false);
            WaitV(adapter.ClearSelectionAsync());
            WaitV(adapter.SelectChipAsync(25m));
            Check(GetV(adapter.PlaceBetAsync(second)).Status == BetPlacementStatus.Rejected, "A stake already on the table must block another placement");

            commands.Enqueue("result:player");
            for (var i = 0; i < 40 && seen.IsEmpty; i++) Thread.Sleep(100);
            Check(seen.TryDequeue(out first!) && first.Outcome == Outcome.Player, "Player result must be observed once");
            commands.Enqueue("result:banker");
            for (var i = 0; i < 40 && seen.IsEmpty; i++) Thread.Sleep(100);
            Check(seen.TryDequeue(out var next) && next.Outcome == Outcome.Banker && next.Key != first.Key && seen.IsEmpty, "Banker result observed, no duplicates");

            // Wrong site: the host filter must refuse to attach.
            var other = new LiveBrowserTableAdapter(profile with { HostContains = "not-this-site.test", StartUrl = "" }, new LiveBrowserOptions(debugPort, profileDirectory, LaunchBrowser: false));
            var refused = false;
            try { WaitV(other.ConnectAsync()); } catch (AggregateException e) when (e.InnerException is AutomationDisconnectedException) { refused = true; }
            Check(refused, "Adapter must not attach to a tab outside the profile host");
            Check(Baccarat.Infrastructure.Automation.LiveTableChecksAccess.ParseChip("1K") == 1000m &&
                  Baccarat.Infrastructure.Automation.LiveTableChecksAccess.ParseChip("chip 25") == 25m &&
                  Baccarat.Infrastructure.Automation.LiveTableChecksAccess.ParseChip("5 25") is null, "Chip parsing must be exact");
            Console.WriteLine("PASS: live browser adapter attach, iframe read, chips, guarded placement with table confirmation, results, host guard");
        }
        finally
        {
            try { Wait(adapter.CloseBrowserAsync()); } catch { }
            Wait(adapter.DisposeAsync().AsTask());
        }
    }
}
