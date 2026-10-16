using Baccarat.Core;
using Baccarat.Core.Automation;
using Baccarat.Core.Communication;
using Baccarat.Core.Diagnostics;
using Baccarat.Desktop.ViewModels;
using Baccarat.Desktop.Views;
using Baccarat.Infrastructure.Automation;
using Baccarat.Infrastructure.Communication;
using Baccarat.Infrastructure.Strategies;

namespace Baccarat.Desktop;

// One tool context per dashboard session, not a second workspace/data model.
internal sealed class SessionTools : IDisposable
{
    private readonly DashboardSession session;
    private readonly IDiagnosticSink diagnostics;
    private readonly ViewerCommunicationModule communication = new(new LoopbackViewerMessageTransport());
    private int lastRun = -1;
    private int lastShoeRevision = -1;
    public AutomationController Controller { get; } = new(new SimulatedAutomationTableAdapter());
    public StrategyView Strategies { get; }
    public AutomationView Automation { get; }
    public ViewerView Viewer { get; }

    public SessionTools(DashboardSession session, IDiagnosticSink diagnostics, Action changed)
    {
        this.session = session;
        this.diagnostics = diagnostics;
        Automation = new AutomationView(Controller);
        Viewer = new ViewerView(communication);
        Strategies = new StrategyView(new StrategyFileLoader(), changed: _ => changed(), sharedSession: session);
        Controller.SnapshotChanged += PublishAutomation;
        Publish(new CommunicationStatusPayload("offline", "Tools and dashboard share this selected session. Local simulation only."));
        Refresh();
    }

    public void Refresh()
    {
        if (lastRun != session.StrategyRun || lastShoeRevision != session.ShoeRevision)
        {
            Pause("Strategy or shoe changed. Review the recommendation before resuming simulation.");
            lastRun = session.StrategyRun;
            lastShoeRevision = session.ShoeRevision;
        }
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
        Controller.CancelPending(message);
        if (Controller.Snapshot.State != AutomationRunState.Disconnected) Controller.Pause(message);
    }

    private void PublishAutomation(AutomationSnapshot snapshot) => Publish(new AutomationSnapshotPayload(
        snapshot.State, snapshot.AutoBetEnabled, snapshot.IsSimulation, snapshot.SessionStake, snapshot.AcceptedBets, snapshot.Message));

    private void Publish(ViewerPayload payload) => _ = PublishAsync(payload);
    private async Task PublishAsync(ViewerPayload payload)
    {
        try { await communication.PublishAsync(payload); }
        catch { diagnostics.Write(DiagnosticLevel.Error, "tools.publish_failed", "A local viewer document could not be published."); }
    }

    public void Dispose()
    {
        Pause("Application closed. Pending simulation cancelled.");
        Controller.SnapshotChanged -= PublishAutomation;
        Automation.Dispose();
        Viewer.Dispose();
    }
}
