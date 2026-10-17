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
using System.Diagnostics;

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
        Automation = new AutomationView(Controller, OpenBrowser);
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
        if (disposed || browser is null) return;
        var bet = adapter.CurrentRoundBet;
        browser.Publish(new
        {
            title = session.Title,
            next = session.Engine?.Recommendation.Message ?? "Apply strategy settings in the desktop app.",
            balance = session.Balance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            status = active ? Controller.Snapshot.Message : "Session inactive. Select it in the desktop app.",
            wager = bet is null ? "No simulated wager accepted for this round" : $"Accepted: {bet.Stake:0.##} on {bet.Side} · {bet.OperationId}",
            chips = bet is null ? "" : "Selected chips: " + string.Join(" + ", bet.Chips),
            cards,
            roundKey,
            canDeal = CanDeal()
        });
    }

    private async Task<bool> DealBrowserAsync(string expectedKey) => await Dispatcher.UIThread.InvokeAsync(() =>
    {
        if (disposed || expectedKey != roundKey || !CanDeal()) return false;
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
    });

    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(StartBrowser().AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            Controller.Pause("Could not open the local browser simulator. Check your default browser and local firewall settings.");
        }
    }

    internal Uri StartBrowser()
    {
        if (disposed) throw new ObjectDisposedException(nameof(SessionTools));
        if (browser is null)
        {
            browser = new LocalSimulationServer(DealBrowserAsync);
            adapter.LimitOneBetPerRound = true;
            adapter.BeginRound();
        }
        active = true;
        PublishBrowser();
        return browser.BrowserAddress;
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
        Pause("Application closed. Pending simulation cancelled.");
        Controller.SnapshotChanged -= PublishAutomation;
        Automation.Dispose();
        Viewer.Dispose();
    }
}
