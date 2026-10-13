using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;

namespace Baccarat.Desktop.Views;

public sealed class AutomationView : StackPanel
{
    private readonly AutomationController controller;
    private readonly TextBlock status = Text("Automation is disconnected.", 17, "#E9BC72");
    private readonly TextBlock recommendation = Text("Load a strategy to prepare an initial bet.", 16, "#A8B5CA");
    private readonly TextBlock totals = Text("Accepted bets 0 · Session stake 0.00", 16, "#A8B5CA");
    private readonly Button connect = Button("Connect simulation");
    private readonly Button initialBet = Button("Prepare initial bet");
    private readonly Button approve = Button("Approve pending bet");
    private readonly Button enableAuto = Button("Enable auto-bet");
    private readonly Button disableAuto = Button("Disable auto-bet");
    private readonly Button recover = Button("Recover connection");
    private readonly Button pause = Button("Pause");
    private BetRecommendation? currentRecommendation;
    private int currentRoundCount;
    private int lastAutomatedRound = -1;

    public AutomationView(AutomationController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Margin = new Thickness(24);
        Spacing = 16;

        Children.Add(Text("Controlled automation", 30));
        Children.Add(Text(
            "Local simulation is the only enabled adapter. Every bet requires approval, limits are enforced, and uncertain placements are never retried.",
            15,
            "#A8B5CA"));
        Children.Add(status);
        Children.Add(recommendation);
        Children.Add(totals);

        var connectionActions = Row();
        connectionActions.Children.Add(connect);
        connectionActions.Children.Add(recover);
        connectionActions.Children.Add(pause);
        Children.Add(connectionActions);

        var bettingActions = Row();
        bettingActions.Children.Add(initialBet);
        bettingActions.Children.Add(approve);
        bettingActions.Children.Add(enableAuto);
        bettingActions.Children.Add(disableAuto);
        Children.Add(bettingActions);

        Children.Add(Text(
            "Live table automation remains unavailable until verified selectors, chip values, table-state checks and placement acknowledgements are supplied.",
            14,
            "#91A1B9"));

        connect.Click += async (_, _) => await RunAsync(() => controller.ConnectAsync());
        recover.Click += async (_, _) => await RunAsync(() => controller.RecoverAsync());
        pause.Click += (_, _) => controller.Pause();
        initialBet.Click += async (_, _) => await PrepareInitialAsync();
        approve.Click += async (_, _) => await RunAsync(() => controller.ApprovePendingAsync());
        enableAuto.Click += (_, _) => controller.SetAutoBet(true);
        disableAuto.Click += (_, _) => controller.SetAutoBet(false);
        controller.SnapshotChanged += OnSnapshotChanged;
        Refresh(controller.Snapshot);
    }

    public void UpdateStrategy(StrategySessionEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        currentRecommendation = engine.Recommendation;
        currentRoundCount = engine.Rounds.Count;
        recommendation.Text = currentRecommendation.Message;
        if (controller.Snapshot.AutoBetEnabled && currentRoundCount > 0 && currentRoundCount != lastAutomatedRound)
        {
            lastAutomatedRound = currentRoundCount;
            _ = RunAutoBetAsync(currentRecommendation);
        }
        Refresh(controller.Snapshot);
    }

    private async Task PrepareInitialAsync()
    {
        if (currentRecommendation is null) return;
        await RunAsync(() => controller.InitialBetAsync(currentRecommendation));
    }

    private async Task RunAutoBetAsync(BetRecommendation next)
    {
        await RunAsync(() => controller.AutoBetAsync(next));
    }

    private async Task RunAsync(Func<ValueTask<AutomationActionResult>> operation)
    {
        try
        {
            var result = await operation();
            status.Text = result.Message;
        }
        catch (OperationCanceledException)
        {
            status.Text = "Automation operation cancelled.";
        }
        catch
        {
            controller.Pause("Automation paused after an unexpected adapter error.");
        }
        Refresh(controller.Snapshot);
    }

    private void OnSnapshotChanged(AutomationSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSnapshotChanged(snapshot));
            return;
        }
        Refresh(snapshot);
    }

    private void Refresh(AutomationSnapshot snapshot)
    {
        status.Text = $"{snapshot.State}: {snapshot.Message}";
        status.Foreground = Brush.Parse(snapshot.State is AutomationRunState.Faulted or AutomationRunState.Stopped
            ? "#E48787"
            : snapshot.State == AutomationRunState.Ready ? "#7BD88F" : "#E9BC72");
        totals.Text = $"Accepted bets {snapshot.AcceptedBets} · Session stake {snapshot.SessionStake:0.00} · " +
                      (snapshot.IsSimulation ? "Simulation" : "External adapter");
        if (currentRecommendation is not null) recommendation.Text = currentRecommendation.Message;

        var connected = snapshot.State is not AutomationRunState.Disconnected and not AutomationRunState.Connecting;
        connect.IsEnabled = !connected;
        initialBet.IsEnabled = connected && currentRecommendation?.State == StrategyEngineState.Ready &&
                               snapshot.State is AutomationRunState.Ready or AutomationRunState.Paused;
        approve.IsEnabled = snapshot.State == AutomationRunState.AwaitingApproval;
        enableAuto.IsEnabled = connected && !snapshot.AutoBetEnabled &&
                               snapshot.State is not AutomationRunState.Faulted and not AutomationRunState.Stopped;
        disableAuto.IsEnabled = snapshot.AutoBetEnabled;
        recover.IsEnabled = snapshot.State is AutomationRunState.Disconnected or AutomationRunState.Faulted;
        pause.IsEnabled = connected && snapshot.State is not AutomationRunState.Stopped;
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private static Button Button(string content) => new() { Content = content, Padding = new Thickness(18, 12) };
    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
}
