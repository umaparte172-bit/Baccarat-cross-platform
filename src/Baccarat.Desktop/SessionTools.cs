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
        Controller = new AutomationController(adapter);
        Automation = new AutomationView(Controller, OpenBrowser, ExecuteControlAsync);
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

    private void Publish(ViewerPayload payload) => _ = PublishAsync(payload);
    private async Task PublishAsync(ViewerPayload payload)
    {
        try { await communication.PublishAsync(payload); }
        catch { diagnostics.Write(DiagnosticLevel.Error, "tools.publish_failed", "A local viewer document could not be published."); }
    }

    public void Dispose()
    {
        disposed = true;
        browser?.Dispose();
        if (domBrowser is not null) _ = domBrowser.DisposeAsync();
        Pause("Application closed. Pending simulation cancelled.");
        Controller.SnapshotChanged -= PublishAutomation;
        Automation.Dispose();
        Viewer.Dispose();
    }
}
