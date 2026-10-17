using Baccarat.Core;
using Baccarat.Core.Automation;
using Baccarat.Core.Communication;
using Baccarat.Core.Diagnostics;
using Baccarat.Desktop.ViewModels;
using Baccarat.Desktop.Views;
using Baccarat.Infrastructure.Automation;
using Baccarat.Infrastructure.Communication;
using Baccarat.Infrastructure.Strategies;
using Avalonia.Threading;
using Baccarat.Core.Strategies;
using System.Text.Json;

namespace Baccarat.Desktop;

// One tool context per dashboard session, not a second workspace/data model.
internal sealed class SessionTools : IDisposable
{
    private readonly DashboardSession session;
    private readonly IDiagnosticSink diagnostics;
    private readonly ViewerCommunicationModule communication = new(new LoopbackViewerMessageTransport());
    private int lastRun = -1;
    private int lastShoeRevision = -1;
    private readonly SimulatedAutomationTableAdapter adapter = new();
    private readonly SwitchableTableAdapter switcher;
    private readonly LiveTableProfileStore profileStore = new();
    private LiveBrowserTableAdapter? live;
    internal LiveBrowserOptions LiveOptions { get; set; } = new();
    private string? lastLiveKey;
    private CancellationTokenSource? watching;
    private event Action<string>? LiveStatus;
    private readonly Action changed;
    private LocalSimulationServer? browser;
    private LocalDomBrowser? domBrowser;
    private string roundKey = Guid.NewGuid().ToString("N");
    private string? contextKey;
    private string cards = "";
    private bool disposed;
    private bool active = true;
    public AutomationController Controller { get; }
    public StrategyView Strategies { get; }
    public AutomationView Automation { get; }
    public ViewerView Viewer { get; }

    public SessionTools(DashboardSession session, IDiagnosticSink diagnostics, Action changed)
    {
        this.session = session;
        this.diagnostics = diagnostics;
        this.changed = changed;
        switcher = new SwitchableTableAdapter(adapter);
        Controller = new AutomationController(switcher);
        Automation = new AutomationView(Controller, OpenBrowser, ExecuteControlAsync, new LiveTableControls(
            LoadProfileNames, OpenLiveAsync, ConnectLiveAsync, DisconnectLiveAsync, InspectLiveAsync, profileStore.Directory,
            handler => LiveStatus += handler));
        Viewer = new ViewerView(communication);
        Strategies = new StrategyView(new StrategyFileLoader(), changed: _ => changed(), sharedSession: session);
        Controller.SnapshotChanged += PublishAutomation;
        Publish(new CommunicationStatusPayload("offline", "Tools and dashboard share this selected session. Local simulation only."));
        Refresh();
    }

    public void Refresh()
    {
        if (disposed) return;
        active = true;
        SynchronizeRound();
        if (lastRun != session.StrategyRun || lastShoeRevision != session.ShoeRevision)
        {
            Pause("Strategy or shoe changed. Review the recommendation before resuming simulation.");
            lastRun = session.StrategyRun;
            lastShoeRevision = session.ShoeRevision;
        }
        active = true;
        Strategies.Refresh();
        Publish(new SessionSnapshotPayload(session.Hands.Count, session.Count(Outcome.Player),
            session.Count(Outcome.Banker), session.Count(Outcome.Tie)));
        if (session.Engine is { } engine)
        {
            Automation.UpdateStrategy(engine);
            Publish(new StrategySnapshotPayload(engine.StrategyName, engine.Profit, engine.Rounds.Count,
                engine.Rounds.Count(r => r.WasBet), engine.Recommendation.State, engine.Recommendation.Message));
        }
        else Automation.ClearStrategy();
        PublishAutomation(Controller.Snapshot);
    }

    public void Pause(string message)
    {
        active = false;
        Controller.CancelPending(message);
        if (Controller.Snapshot.State != AutomationRunState.Disconnected) Controller.Pause(message);
    }

    private void PublishAutomation(AutomationSnapshot snapshot)
    {
        Publish(new AutomationSnapshotPayload(snapshot.State, snapshot.AutoBetEnabled, snapshot.IsSimulation,
            snapshot.SessionStake, snapshot.AcceptedBets, snapshot.Message));
        if (Dispatcher.UIThread.CheckAccess()) PublishBrowser();
        else Dispatcher.UIThread.Post(PublishBrowser);
    }

    private void SynchronizeRound()
    {
        var next = session.Engine?.Recommendation;
        var key = $"{session.StrategyRun}|{session.ShoeRevision}|{session.Hands.Count}|{next}";
        if (key == contextKey) return;
        contextKey = key;
        roundKey = Guid.NewGuid().ToString("N");
        adapter.BeginRound();
        cards = "";
    }

    private bool CanDeal()
    {
        if (!active || disposed || !adapter.IsConnected || !session.CanRecord || session.Engine is null) return false;
        var next = session.Engine.Recommendation;
        return next.State == StrategyEngineState.Paused ||
            (adapter.CurrentRoundBet is { } bet && next.Side == bet.Side && next.Stake == bet.Stake);
    }

    private void PublishBrowser()
    {
        if (disposed || domBrowser is null) return;
        var bet = adapter.CurrentRoundBet;
        domBrowser.Publish(new
        {
            title = session.Title,
            next = session.Engine?.Recommendation.Message ?? "Apply strategy settings in the desktop app.",
            balance = session.Balance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            status = active ? Controller.Snapshot.Message : "Session inactive. Select it in the desktop app.",
            wager = bet is null ? "No simulated wager accepted for this round" : $"Accepted: {bet.Stake:0.##} on {bet.Side} · {bet.OperationId}",
            chips = bet is null ? "" : "Selected chips: " + string.Join(" + ", bet.Chips),
            cards,
            roundKey,
            canDeal = CanDeal(),
            active,
            strategy = session.Strategy.Name,
            unit = session.BaseUnitDraft,
            bankroll = session.BankrollDraft,
            playerPair = session.PlayerPairDraft,
            bankerPair = session.BankerPairDraft,
            fields = session.Strategy.Fields.Select(f => new { key = f.Key, label = f.Label, hint = f.Hint, value = session.GetField(f.Key) }).ToArray()
        });
    }

    private bool DealBrowser()
    {
        if (disposed || !CanDeal()) return false;
        var result = session.DealSimulation();
        if (result is null)
        {
            Controller.Pause("Training shoe exhausted. Start a New Shoe in the dashboard.");
            active = false;
            PublishBrowser();
            return false;
        }
        changed();
        cards = result.Description;
        PublishBrowser();
        return true;
    }

    private async void OpenBrowser()
    {
        try
        {
            await StartBrowserAsync();
        }
        catch
        {
            Controller.Pause("Could not open the local browser simulator. Check your default browser and local firewall settings.");
        }
    }

    internal async Task StartBrowserAsync()
    {
        if (disposed) throw new ObjectDisposedException(nameof(SessionTools));
        if (domBrowser?.IsConnected == true) return;
        if (domBrowser is not null) await domBrowser.DisposeAsync();
        if (browser is null)
        {
            browser = new LocalSimulationServer();
            adapter.LimitOneBetPerRound = true;
            adapter.BeginRound();
        }
        active = true;
        domBrowser = new LocalDomBrowser(browser.BrowserAddress,
            async (action, payload) => await Dispatcher.UIThread.InvokeAsync(() => CommandAsync(action, payload)),
            message => Dispatcher.UIThread.Post(() => Pause(message)));
        PublishBrowser();
        try { await domBrowser.StartAsync(); }
        catch { await domBrowser.DisposeAsync(); throw; }
    }

    internal async Task CloseBrowserAsync()
    {
        if (domBrowser is not null) await domBrowser.DisposeAsync();
    }

    internal async Task<bool> TryDomActionAsync(string action)
    {
        if (domBrowser is null) return false;
        PublishBrowser();
        await domBrowser.ClickAsync(action);
        return true;
    }

    private async Task ExecuteControlAsync(string action)
    {
        if (!await TryDomActionAsync(action)) await CommandAsync(action, default);
    }

    private async Task<string?> CommandAsync(string action, JsonElement payload)
    {
        if (disposed || !active) return "Select this session in the desktop app first.";
        if (payload.ValueKind != JsonValueKind.Undefined &&
            (!payload.TryGetProperty("roundKey", out var key) || key.GetString() != roundKey)) return "Stale round; no action was applied.";
        try
        {
            switch (action)
            {
                case "player":
                case "banker":
                case "tie":
                    if (!session.CanRecord) return "Strategy is stopped or requires a manual decision.";
                    session.Record(action == "player" ? Outcome.Player : action == "banker" ? Outcome.Banker : Outcome.Tie);
                    break;
                case "player-pair": session.PlayerPairDraft = !session.PlayerPairDraft; break;
                case "banker-pair": session.BankerPairDraft = !session.BankerPairDraft; break;
                case "undo": session.Undo(); break;
                case "shoe": session.NewShoe(); break;
                case "clear": session.Clear(); break;
                case "draft": session.BeginDraftRun(); break;
                case "deal": return DealBrowser() ? null : "Approve a simulated wager or start a new shoe first.";
                case "apply":
                    var name = payload.GetProperty("strategy").GetString()!;
                    if (!DashboardSession.Strategies.Any(s => s.Name == name)) return "Unknown strategy.";
                    session.SelectStrategy(name);
                    session.BaseUnitDraft = payload.GetProperty("unit").GetString() ?? "";
                    session.BankrollDraft = payload.GetProperty("bankroll").GetString() ?? "";
                    foreach (var field in payload.GetProperty("fields").EnumerateObject())
                        if (session.Strategy.Fields.Any(f => f.Key == field.Name)) session.SetField(field.Name, field.Value.GetString() ?? "");
                    if (!session.ApplySettings(out var error)) { changed(); return error; }
                    break;
                case "connect": await Controller.ConnectAsync(); break;
                case "recover": await Controller.RecoverAsync(); break;
                case "initial":
                    if (session.Engine is null) return "Apply a strategy first.";
                    await Controller.InitialBetAsync(session.Engine.Recommendation); break;
                case "approve": await Controller.ApprovePendingAsync(); break;
                case "auto": Controller.SetAutoBet(!Controller.Snapshot.AutoBetEnabled); break;
                case "enable-auto": Controller.SetAutoBet(true); break;
                case "disable-auto": Controller.SetAutoBet(false); break;
                case "pause": Controller.CancelPending(); Controller.Pause(); break;
                default: return "Unsupported training action.";
            }
            changed();
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        { return "Could not apply this action. Review the strategy and table state."; }
    }

    // ---- Live browser table ----------------------------------------------------------------------------

    internal IReadOnlyList<string> LiveProfileNames() => LoadProfileNames();

    internal async Task CloseLiveBrowserAsync() { if (live is not null) await live.CloseBrowserAsync(); }

    private IReadOnlyList<string> LoadProfileNames()
    {
        try { return profileStore.Load().Select(p => p.Name).ToArray(); }
        catch (IOException) { return []; }
    }

    private LiveTableProfile FindProfile(string name) =>
        profileStore.Load().FirstOrDefault(p => p.Name == name) ?? throw new InvalidOperationException("Profile not found. Reload profiles.");

    internal async Task<string> OpenLiveAsync(string profileName, string? pageUrl = null)
    {
        try
        {
            var profile = FindProfile(profileName);
            if (live is not null && live.IsConnected) return "Live table already connected. Disconnect it first to switch profile.";
            live = new LiveBrowserTableAdapter(profile, LiveOptions);
            await live.OpenBrowserAsync(default, string.IsNullOrWhiteSpace(pageUrl) ? null : pageUrl.Trim());
            return "Live browser ready (its own profile, separate from your normal Chrome). Sign in there if asked, open the table in that window, and press Watch for table.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
        { return "Could not open the live browser: " + ex.Message; }
    }

    // No fixed site: the app waits until a tab shows a table, then connects by itself.
    internal async Task<string> ConnectLiveAsync(string profileName)
    {
        try
        {
            var profile = FindProfile(profileName);
            watching?.Cancel();
            if (live is null || live.Name != "Live table · " + profile.Name) live = new LiveBrowserTableAdapter(profile, LiveOptions);
            await Controller.DisconnectAsync();
            var adapterToUse = live;
            adapterToUse.ResultObserved -= OnLiveResult;
            adapterToUse.Faulted -= OnLiveFaulted;
            adapterToUse.ResultObserved += OnLiveResult;
            adapterToUse.Faulted += OnLiveFaulted;
            watching = new CancellationTokenSource();
            _ = WatchLiveAsync(adapterToUse, profile, watching.Token);
            return "Watching for a live table. Open one in the live browser; the app connects as soon as it appears.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
        { return "Could not start watching: " + ex.Message; }
    }

    internal async Task WatchLiveAsync(LiveBrowserTableAdapter adapterToUse, LiveTableProfile profile, CancellationToken token)
    {
        void Say(string text) => Dispatcher.UIThread.Post(() => LiveStatus?.Invoke(text));
        try
        {
            string? lastProgress = null;
            void OnProgress(string text) { if (text != lastProgress) { lastProgress = text; Say(text); } }
            adapterToUse.Progress += OnProgress;
            try { await adapterToUse.WaitForTableAsync(token); }
            finally { adapterToUse.Progress -= OnProgress; }
            if (disposed || token.IsCancellationRequested) return;
            Say($"Table detected on {adapterToUse.DetectedHost}. Connecting…");
            var result = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                lastLiveKey = null;
                switcher.Use(adapterToUse);
                var connected = await Controller.ConnectAsync();
                if (connected.Outcome != AutomationActionOutcome.Accepted) switcher.Use(adapter);
                return connected;
            });
            if (result.Outcome == AutomationActionOutcome.Accepted && !profile.CanRead)
            {
                // Detection works on any page; reading results needs selectors, so save the page structure to calibrate them.
                Say($"Table detected on {adapterToUse.DetectedHost}. This profile has no result selectors yet; saving the page structure…");
                Say(await Dispatcher.UIThread.InvokeAsync(InspectLiveAsync) + " Send that file (or edit the profile) to finish calibration.");
                return;
            }
            Say(result.Outcome != AutomationActionOutcome.Accepted ? result.Message : profile.CanBet
                ? $"Connected to the table on {adapterToUse.DetectedHost}. Results are recorded automatically; every bet still needs your approval."
                : $"Connected to {adapterToUse.DetectedHost} for reading results only (profile cannot place bets yet).");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        { Say("Live table watch stopped: " + ex.Message); }
    }

    internal async Task<string> DisconnectLiveAsync()
    {
        watching?.Cancel();
        await Controller.DisconnectAsync();
        if (live is not null) { live.ResultObserved -= OnLiveResult; live.Faulted -= OnLiveFaulted; await live.DisposeAsync(); }
        switcher.Use(adapter);
        return "Live table disconnected. The browser stays open.";
    }

    private async Task<string> InspectLiveAsync()
    {
        if (live is null || !live.IsConnected) return "Connect the live table first.";
        var report = await live.InspectAsync();
        Directory.CreateDirectory(profileStore.Directory);
        var path = Path.Combine(profileStore.Directory, $"inspection-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, report);
        return "Page structure saved to " + path;
    }

    private void OnLiveFaulted(string message) =>
        Dispatcher.UIThread.Post(() => { if (!disposed) Pause(message); });

    private void OnLiveResult(LiveRoundResult result) => Dispatcher.UIThread.Post(() =>
    {
        if (disposed || !active || result.Key == lastLiveKey) return;
        lastLiveKey = result.Key;
        if (!session.CanRecord) { Controller.Pause("A live result arrived but this session needs a manual decision or is stopped. Review before resuming."); return; }
        try
        {
            session.BankerPairDraft = result.BankerPair;
            session.PlayerPairDraft = result.PlayerPair;
            session.Record(result.Outcome);
        }
        catch (InvalidOperationException ex) { Controller.Pause(ex.Message); }
        finally { session.BankerPairDraft = false; session.PlayerPairDraft = false; }
        changed();
    });

    private void Publish(ViewerPayload payload) => _ = PublishAsync(payload);
    private async Task PublishAsync(ViewerPayload payload)
    {
        try { await communication.PublishAsync(payload); }
        catch { diagnostics.Write(DiagnosticLevel.Error, "tools.publish_failed", "A local viewer document could not be published."); }
    }

    public void Dispose()
    {
        disposed = true;
        watching?.Cancel();
        browser?.Dispose();
        if (live is not null) { live.ResultObserved -= OnLiveResult; live.Faulted -= OnLiveFaulted; _ = live.DisposeAsync().AsTask(); }
        if (domBrowser is not null) _ = domBrowser.DisposeAsync();
        Pause("Application closed. Pending simulation cancelled.");
        Controller.SnapshotChanged -= PublishAutomation;
        Automation.Dispose();
        Viewer.Dispose();
    }
}
