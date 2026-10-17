using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;

namespace Baccarat.Desktop.Views;

public sealed class AutomationView : StackPanel, IDisposable
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
    private string? recommendationKey;
    private bool disposed;

    public void Dispose()
    {
        disposed = true;
        controller.SnapshotChanged -= OnSnapshotChanged;
    }

    public AutomationView(AutomationController controller, Action? openBrowser = null, Func<string, Task>? domAction = null)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Margin = new Thickness(0);
        Spacing = 0;
        var page = WorkspaceUi.Page("AUTOMATION WORKSPACE", "Controlled automation",
            "Review each recommendation and approve every simulated placement.");
        Children.Add(page);

        var state = new StackPanel { Spacing = 12 };
        state.Children.Add(status);
        state.Children.Add(totals);
        page.Children.Add(WorkspaceUi.Card("SIMULATION STATUS", state));
        page.Children.Add(WorkspaceUi.Card("NEXT RECOMMENDATION", recommendation));

        var connectionActions = Row();
        connectionActions.Children.Add(connect);
        connectionActions.Children.Add(recover);
        connectionActions.Children.Add(pause);
        page.Children.Add(WorkspaceUi.Card("CONNECTION", connectionActions));
        if (openBrowser is not null)
        {
            var browser = Button("Open browser simulator");
            browser.Click += (_, _) => openBrowser();
            connectionActions.Children.Add(browser);
        }

        var bettingActions = Row();
        bettingActions.Children.Add(initialBet);
        bettingActions.Children.Add(approve);
        bettingActions.Children.Add(enableAuto);
        bettingActions.Children.Add(disableAuto);
        approve.Background = LegacyTheme.Green;
        approve.Foreground = Brushes.White;
        page.Children.Add(WorkspaceUi.Card("BET CONTROLS", bettingActions));

        page.Children.Add(Text(
            "Local simulation is the only enabled adapter. Every placement requires approval.",
            14,
            "#91A1B9"));
        page.Children.Add(Text(
            "The browser simulator is a local training table only. It never connects to a casino or uses real money.",
            14,
            "#91A1B9"));

        if (domAction is null)
        {
            connect.Click += async (_, _) => await RunAsync(() => controller.ConnectAsync());
            recover.Click += async (_, _) => await RunAsync(() => controller.RecoverAsync());
            pause.Click += (_, _) => controller.Pause();
            initialBet.Click += async (_, _) => await PrepareInitialAsync();
            approve.Click += async (_, _) => await RunAsync(() => controller.ApprovePendingAsync());
            enableAuto.Click += (_, _) => controller.SetAutoBet(true);
            disableAuto.Click += (_, _) => controller.SetAutoBet(false);
        }
        else
        {
            foreach (var (button, action) in new[] { (connect, "connect"), (recover, "recover"), (pause, "pause"),
                (initialBet, "initial"), (approve, "approve"), (enableAuto, "enable-auto"), (disableAuto, "disable-auto") })
                button.Click += async (_, _) =>
                {
                    try { await domAction(action); }
                    catch { controller.Pause("DOM simulator unavailable. Reopen it before continuing."); }
                };
        }
        controller.SnapshotChanged += OnSnapshotChanged;
        Refresh(controller.Snapshot);
    }

    public void UpdateStrategy(StrategySessionEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var next = engine.Recommendation;
        var nextKey = $"{engine.StrategyName}|{engine.Rounds.Count}|{next.State}|{next.Side}|{next.Stake}|{next.Selector}";
        if (recommendationKey is not null && !string.Equals(recommendationKey, nextKey, StringComparison.Ordinal))
            controller.CancelPending();
        if (engine.Rounds.Count < currentRoundCount)
        {
            if (controller.Snapshot.State != AutomationRunState.Disconnected)
                controller.Pause("History was undone. Review the recommendation before re-enabling automation.");
            lastAutomatedRound = -1;
        }
        recommendationKey = nextKey;
        currentRecommendation = next;
        currentRoundCount = engine.Rounds.Count;
        recommendation.Text = currentRecommendation.Message;
        // Manual selectors can publish several incomplete states for the same round.
        // Do not consume that round until both the side and stake are resolved.
        if (controller.Snapshot.AutoBetEnabled && next.State == StrategyEngineState.Ready &&
            currentRoundCount > 0 && currentRoundCount != lastAutomatedRound)
        {
            lastAutomatedRound = currentRoundCount;
            _ = RunAutoBetAsync(currentRecommendation);
        }
        Refresh(controller.Snapshot);
    }

    public void ClearStrategy()
    {
        controller.CancelPending("Strategy run ended.");
        recommendationKey = null;
        currentRecommendation = null;
        currentRoundCount = 0;
        lastAutomatedRound = -1;
        recommendation.Text = "Apply a supported strategy or load one in Tools.";
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
            if (disposed) return;
            status.Text = result.Message;
        }
        catch (OperationCanceledException)
        {
            if (disposed) return;
            status.Text = "Automation operation cancelled.";
        }
        catch
        {
            controller.Pause("Automation paused after an unexpected adapter error.");
        }
        if (!disposed) Refresh(controller.Snapshot);
    }

    private void OnSnapshotChanged(AutomationSnapshot snapshot)
    {
        if (disposed) return;
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
        status.Foreground = WorkspaceUi.Tone(snapshot.State is AutomationRunState.Faulted or AutomationRunState.Stopped
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

    private static StackPanel Row() => WorkspaceUi.Row();
    private static Button Button(string content) => WorkspaceUi.Action(content);
    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = WorkspaceUi.Tone(color), TextWrapping = TextWrapping.Wrap };
}
