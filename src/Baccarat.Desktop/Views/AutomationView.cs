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
    private readonly TextBlock statePillText = new();
    private Border? statePill;
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

    public AutomationView(AutomationController controller, Action? openBrowser = null, Func<string, Task>? domAction = null, LiveTableControls? live = null)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Margin = new Thickness(0);
        Spacing = 0;
        var page = WorkspaceUi.Page("AUTOMATION WORKSPACE", "Controlled automation",
            "Review each recommendation and approve every simulated placement.");
        Children.Add(page);

        // Top row: what is happening now, and what the strategy wants next.
        statePill = WorkspaceUi.Pill(statePillText, Brush.Parse("#7A8294"));
        var statusBody = new StackPanel { Spacing = 10 };
        statusBody.Children.Add(statePill);
        status.FontSize = 15;
        statusBody.Children.Add(status);
        statusBody.Children.Add(totals);
        totals.FontSize = 13;
        recommendation.FontSize = 22;
        recommendation.FontWeight = FontWeight.SemiBold;
        recommendation.Foreground = WorkspaceUi.Tone("#E9EEF6");
        page.Children.Add(WorkspaceUi.Columns(
            WorkspaceUi.Card("STATUS", statusBody),
            WorkspaceUi.Card("NEXT RECOMMENDATION", WorkspaceUi.Callout(recommendation, Brush.Parse("#1769CE")))));

        // Second row: the actions you use every round, next to the connection controls.
        var bettingActions = Row();
        bettingActions.Children.Add(initialBet);
        bettingActions.Children.Add(approve);
        bettingActions.Children.Add(enableAuto);
        bettingActions.Children.Add(disableAuto);
        approve.Background = LegacyTheme.Green;
        approve.Foreground = Brushes.White;
        var connectionActions = Row();
        connectionActions.Children.Add(connect);
        connectionActions.Children.Add(recover);
        connectionActions.Children.Add(pause);
        if (openBrowser is not null)
        {
            var browser = Button("Open browser simulator");
            browser.Click += (_, _) => openBrowser();
            connectionActions.Children.Add(browser);
        }
        page.Children.Add(WorkspaceUi.Columns(
            WorkspaceUi.Card("BET CONTROLS", bettingActions),
            WorkspaceUi.Card("CONNECTION (SIMULATION)", connectionActions)));

        if (live is not null) page.Children.Add(BuildLiveCard(live));

        var notes = Text("Local simulation is the only enabled adapter until you connect a live table below. Every placement requires approval. The browser simulator is a local training table: it never connects to a casino or uses real money.", 12, "#91A1B9");
        page.Children.Add(notes);

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

    private Control BuildLiveCard(LiveTableControls live)
    {
        var profiles = new ComboBox { MinWidth = 260, PlaceholderText = "Choose a table profile", Name = "LiveProfile" };
        var message = Text("Opens your own Chrome/Edge on a separate profile. Press Watch for table, then sign in and open any live table yourself: the app detects it and starts. It never sees your password and is not tied to one website.", 13, "#91A1B9");
        void Reload()
        {
            var names = live.Profiles();
            profiles.ItemsSource = names;
            if (profiles.SelectedItem is null && names.Count > 0) profiles.SelectedIndex = 0;
        }
        var address = new TextBox { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "Table page address to open in the live browser (optional)", Name = "LiveAddress" };
        async Task Run(Func<string, Task<string>> action)
        {
            if (profiles.SelectedItem is not string name) { message.Text = "Choose a profile first."; return; }
            message.Text = "Working…";
            try { message.Text = await action(name); } catch (Exception ex) { message.Text = "Live table error: " + ex.Message; }
        }
        var reload = Button("Reload profiles");
        reload.Click += (_, _) => { Reload(); message.Text = $"Profiles are JSON files in {live.ProfileFolder}"; };
        var open = Button("Open live browser");
        open.Click += async (_, _) => await Run(name => live.Open(name, address.Text));
        var connect = Button("Watch for table");
        connect.Name = "ConnectLive";
        connect.Click += async (_, _) => await Run(live.Connect);
        var disconnect = Button("Disconnect live table");
        disconnect.Click += async (_, _) => { try { message.Text = await live.Disconnect(); } catch (Exception ex) { message.Text = ex.Message; } };
        var inspect = Button("Inspect page");
        inspect.Click += async (_, _) => { try { message.Text = await live.Inspect(); } catch (Exception ex) { message.Text = ex.Message; } };
        var row = Row();
        foreach (var control in new Control[] { reload, open, connect, disconnect, inspect }) row.Children.Add(control);
        var panel = new StackPanel { Spacing = 10 };
        profiles.MinWidth = 280;
        panel.Children.Add(profiles);
        panel.Children.Add(row);
        panel.Children.Add(address);
        panel.Children.Add(message);
        panel.Children.Add(Text("Live tables use real money. Placements need your approval each time, never repeat after an error, and only act on the tab they were detected on.", 13, "#E48787"));
        live.SubscribeStatus(text => message.Text = text);
        Reload();
        return WorkspaceUi.Card("LIVE TABLE (REAL BROWSER)", panel);
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
        statePillText.Text = snapshot.State.ToString();
        if (statePill is not null)
            statePill.Background = Brush.Parse(snapshot.State switch
            {
                AutomationRunState.Ready => "#119C50",
                AutomationRunState.AwaitingApproval => "#1769CE",
                AutomationRunState.PlacingBet or AutomationRunState.SelectingChips or AutomationRunState.Connecting => "#C47F0A",
                AutomationRunState.Faulted or AutomationRunState.Stopped => "#B42331",
                _ => "#7A8294"
            });
        status.Foreground = WorkspaceUi.Tone(snapshot.State is AutomationRunState.Faulted or AutomationRunState.Stopped
            ? "#E48787"
            : snapshot.State == AutomationRunState.Ready ? "#7BD88F" : "#E9BC72");
        totals.Text = $"Accepted bets {snapshot.AcceptedBets} · Session stake {snapshot.SessionStake:0.00} · " +
                      (snapshot.IsSimulation ? "Simulation" : "LIVE TABLE");
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

    private static WrapPanel Row() => WorkspaceUi.WrapRow();
    private static Button Button(string content) => WorkspaceUi.Action(content);
    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = WorkspaceUi.Tone(color), TextWrapping = TextWrapping.Wrap };
}
