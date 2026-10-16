using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Baccarat.Core;
using Baccarat.Core.Authentication;
using Baccarat.Core.Automation;
using Baccarat.Core.Communication;
using Baccarat.Core.Diagnostics;
using Baccarat.Core.Strategies;
using Baccarat.Desktop.Views;
using Baccarat.Infrastructure.Communication;
using Baccarat.Infrastructure.Strategies;

namespace Baccarat.Desktop;

public sealed class MainWindow : Window
{
    private readonly Session session = new();
    private readonly ViewerCommunicationModule viewerCommunication;
    private readonly IDiagnosticSink diagnostics;
    private readonly TextBlock stats = new();
    private readonly TextBlock playerCount = new();
    private readonly TextBlock bankerCount = new();
    private readonly TextBlock tieCount = new();
    private readonly TextBlock status = new() { Text = "Ready · Manual session", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel history = new() { Spacing = 7 };
    private readonly Button undo;
    private readonly Button clear;
    private readonly Button export;

    public MainWindow(
        IDiagnosticSink? diagnostics = null,
        StrategyDefinition? initialStrategy = null,
        ViewerCommunicationModule? viewerCommunication = null,
        AutomationController? automationController = null,
        IAuthenticationService? authenticationService = null,
        IAuthenticationSessionStore? authenticationSessions = null,
        bool startOffline = false)
    {
        var defaults = ApplicationServices.CreateDefault();
        this.diagnostics = diagnostics ?? defaults.Diagnostics;
        this.viewerCommunication = viewerCommunication ?? defaults.ViewerCommunication;
        Title = "Baccarat (Evolution)";
        Width = 1100; Height = 780; MinWidth = 760; MinHeight = 600;
        Background = LegacyTheme.Window;
        undo = ActionButton("Undo last", () => { session.Undo(); Refresh(); PublishSession(); });
        clear = ActionButton("Clear session…", async () => await ConfirmClear());
        export = ActionButton("Export CSV", async () => await Export());

        var overview = WorkspaceUi.Page("TABLE WORKSPACE", "Session tracker", "Record outcomes and review the running table history.", centerHeading: true);
        var summary = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 10 };
        AddMetric(summary, 0, "ROUNDS", stats, LegacyTheme.Gold);
        AddMetric(summary, 1, "PLAYER", playerCount, Brush.Parse("#76A8FF"));
        AddMetric(summary, 2, "BANKER", bankerCount, Brush.Parse("#FF8D94"));
        AddMetric(summary, 3, "TIE", tieCount, LegacyTheme.Green);
        overview.Children.Add(summary);
        var actions = WorkspaceUi.Row();
        foreach (var outcome in Enum.GetValues<Outcome>())
        {
            var fill = outcome switch
            {
                Outcome.Player => Brush.Parse("#325C9C"),
                Outcome.Banker => Brush.Parse("#8D3E50"),
                _ => Brush.Parse("#2F7054")
            };
            var button = WorkspaceUi.Action($"+ {outcome}", fill);
            button.MinWidth = 130;
            button.Click += (_, _) => { session.Record(outcome); Refresh(); PublishSession(); };
            actions.Children.Add(button);
        }
        overview.Children.Add(WorkspaceUi.Card("RECORD TABLE RESULT", actions));
        var utilities = WorkspaceUi.Row();
        utilities.Children.Add(undo); utilities.Children.Add(clear); utilities.Children.Add(export);
        var recordTools = new StackPanel { Spacing = 18 };
        recordTools.Children.Add(utilities);
        recordTools.Children.Add(history);
        overview.Children.Add(WorkspaceUi.Card("ROUND HISTORY", recordTools));
        overview.Children.Add(WorkspaceUi.Text("MANUAL MODE · Data remains on this device until exported.", 12, LegacyTheme.Muted));

        // Injected test/demo services must never share the real account namespace.
        var sessions = authenticationSessions ?? (authenticationService is null
            ? defaults.AuthenticationSessions
            : NullAuthenticationSessionStore.Instance);
        var account = new AccountView(authenticationService ?? defaults.Authentication, this.diagnostics, sessions);
        var accountScreen = new ScrollViewer { Content = account };

        var viewer = new ViewerView(this.viewerCommunication);
        var controlledAutomation = automationController ?? defaults.Automation;
        controlledAutomation.SnapshotChanged += PublishAutomation;
        var automation = new AutomationView(controlledAutomation);
        var strategies = new StrategyView(new StrategyFileLoader(), initialStrategy, engine =>
        {
            PublishStrategy(engine);
            automation.UpdateStrategy(engine);
        });

        var workspaceHost = new ContentControl();
        StackPanel? dashboard = null;
        var sessionScreen = WorkspaceScreen(overview, ShowHome);
        var strategyScreen = WorkspaceScreen(strategies, ShowHome);
        var automationScreen = WorkspaceScreen(automation, ShowHome);
        var viewerScreen = WorkspaceScreen(viewer, ShowHome);
        dashboard = BuildDashboard(
            () => OpenWorkspace("Session", sessionScreen),
            () => OpenWorkspace("Strategies", strategyScreen),
            () => OpenWorkspace("Automation", automationScreen),
            () => OpenWorkspace("Viewer", viewerScreen));

        void ShowHome()
        {
            workspaceHost.Content = dashboard;
            Title = "Baccarat (Evolution) — Home";
        }

        void OpenWorkspace(string name, Control workspace)
        {
            workspaceHost.Content = workspace;
            Title = $"Baccarat (Evolution) — {name}";
        }

        var layout = new DockPanel { Margin = new Thickness(28, 16, 28, 18) };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var identity = WorkspaceUi.Row(18);
        identity.Children.Add(LegacyTheme.Brand(138, 72));
        var heading = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(Label("BACCARAT (EVOLUTION)", 20, "#E9A43A"));
        heading.Children.Add(Label("WETALKBET CONTROL PANEL", 12, "#A7B3C7"));
        identity.Children.Add(heading);
        header.Children.Add(identity);
        var signOut = WorkspaceUi.Action("SIGN OUT");
        signOut.VerticalAlignment = VerticalAlignment.Center;
        signOut.Click += async (_, _) => await account.SignOutAsync();
        Grid.SetColumn(signOut, 1);
        header.Children.Add(signOut);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        status.Margin = new Thickness(0, 12, 0, 0);
        status.Foreground = LegacyTheme.Muted;
        DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        layout.Children.Add(new Border
        {
            Background = LegacyTheme.Panel,
            BorderBrush = LegacyTheme.FieldBorder,
            BorderThickness = new Thickness(1),
            Child = workspaceHost
        });
        Closed += (_, _) =>
        {
            if (controlledAutomation.Snapshot.State != AutomationRunState.Disconnected)
            {
                controlledAutomation.CancelPending("Pending simulation cancelled on close.");
                controlledAutomation.Pause("Workspace closed.");
            }
            controlledAutomation.SnapshotChanged -= PublishAutomation;
        };
        account.Authenticated += () =>
        {
            signOut.Content = "SIGN OUT";
            Content = layout;
            ShowHome();
        };
        void OpenOfflineWorkspace()
        {
            signOut.Content = "BACK TO SIGN IN";
            Content = layout;
            ShowHome();
        }
        account.OfflineRequested += OpenOfflineWorkspace;
        account.SignedOut += () =>
        {
            controlledAutomation.CancelPending("Pending simulation cancelled on sign-out.");
            if (controlledAutomation.Snapshot.State != AutomationRunState.Disconnected)
                controlledAutomation.Pause("Signed out.");
            Content = accountScreen;
            Title = "Baccarat (Evolution) — Sign In";
        };
        Content = accountScreen;
        Title = "Baccarat (Evolution) — Sign In";
        if (startOffline) OpenOfflineWorkspace();
        Refresh();
        Publish(new CommunicationStatusPayload("offline", "Local versioned communication is ready."));
        PublishSession();
        PublishAutomation(controlledAutomation.Snapshot);
    }

    private static TextBlock Label(string text, double size, string color = "#E9EEF6") =>
        new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };

    private static StackPanel BuildDashboard(
        Action openSession,
        Action openStrategies,
        Action openAutomation,
        Action openViewer)
    {
        var page = WorkspaceUi.Page("WORKSPACE HOME", "Baccarat workspace",
            "Choose a workspace to begin. Current workspace tools are local; live table integration is not connected.", centerHeading: true);
        var choices = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 16
        };
        AddChoice(choices, 0, 0, "SESSION", "Record Player, Banker and Tie outcomes.", "OPEN SESSION", openSession);
        AddChoice(choices, 1, 0, "STRATEGIES", "Import strategies and track recommendations.", "OPEN STRATEGIES", openStrategies);
        AddChoice(choices, 0, 1, "AUTOMATION", "Review controlled local simulation.", "OPEN AUTOMATION", openAutomation);
        AddChoice(choices, 1, 1, "VIEWER", "Inspect local versioned snapshots.", "OPEN VIEWER", openViewer);
        page.Children.Add(choices);
        return page;
    }

    private static DockPanel WorkspaceScreen(Control content, Action showHome)
    {
        var screen = new DockPanel();
        var back = WorkspaceUi.Action("Back to home");
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(24, 16, 0, 0);
        back.Click += (_, _) => showHome();
        DockPanel.SetDock(back, Dock.Top);
        screen.Children.Add(back);
        screen.Children.Add(new ScrollViewer { Content = content });
        return screen;
    }

    private static void AddChoice(Grid grid, int column, int row, string title, string description, string action, Action open)
    {
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(WorkspaceUi.Text(description, 15, LegacyTheme.Muted));
        var button = WorkspaceUi.Action(action, LegacyTheme.Green, LegacyTheme.Ink);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += (_, _) => open();
        content.Children.Add(button);
        var card = WorkspaceUi.Card(title, content);
        Grid.SetColumn(card, column);
        Grid.SetRow(card, row);
        grid.Children.Add(card);
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = WorkspaceUi.Action(text);
        button.Click += (_, _) => action();
        return button;
    }

    private static void AddMetric(Grid grid, int column, string label, TextBlock value, IBrush accent)
    {
        var metric = WorkspaceUi.Metric(label, value, accent);
        Grid.SetColumn(metric, column);
        grid.Children.Add(metric);
    }

    private void Refresh()
    {
        stats.Text = $"{session.Rounds.Count} rounds";
        playerCount.Text = session.Count(Outcome.Player).ToString();
        bankerCount.Text = session.Count(Outcome.Banker).ToString();
        tieCount.Text = session.Count(Outcome.Tie).ToString();
        undo.IsEnabled = clear.IsEnabled = export.IsEnabled = session.Rounds.Count > 0;
        history.Children.Clear();
        if (session.Rounds.Count == 0) history.Children.Add(WorkspaceUi.Text("No rounds yet. Choose a result above to begin.", 15, LegacyTheme.Muted));
        foreach (var round in session.Rounds.Reverse())
            history.Children.Add(WorkspaceUi.HistoryRow($"#{round.Number:000}     {round.Outcome}     {round.RecordedAt.ToLocalTime():HH:mm:ss}",
                round.Outcome switch
                {
                    Outcome.Player => Brush.Parse("#76A8FF"),
                    Outcome.Banker => Brush.Parse("#FF8D94"),
                    _ => LegacyTheme.Green
                }));
        status.Text = "Offline · Manual records only · Not connected to Evolution";
    }

    private async Task ConfirmClear()
    {
        var dialog = new Window { Title = "Clear session?", Width = 420, Height = 200, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(Label("Clear all recorded rounds? Export first if you want to keep them.", 16));
        panel.Children.Add(ActionButton("Clear records", () => dialog.Close(true)));
        panel.Children.Add(ActionButton("Cancel", () => dialog.Close(false)));
        dialog.Content = panel;
        if (await dialog.ShowDialog<bool>(this)) { session.Clear(); Refresh(); PublishSession(); }
    }

    private async Task Export()
    {
        try
        {
            var csv = session.ToCsv();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export session",
                SuggestedFileName = "baccarat-session.csv",
                DefaultExtension = "csv",
                FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(csv);
            await writer.FlushAsync();
            status.Text = "Session exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { status.Text = "Export failed. Choose a writable location and try again."; }
    }

    private void PublishSession() => Publish(new SessionSnapshotPayload(
        session.Rounds.Count,
        session.Count(Outcome.Player),
        session.Count(Outcome.Banker),
        session.Count(Outcome.Tie)));

    private void PublishStrategy(StrategySessionEngine engine)
    {
        var next = engine.Recommendation;
        Publish(new StrategySnapshotPayload(
            engine.StrategyName,
            engine.Profit,
            engine.Rounds.Count,
            engine.Rounds.Count(round => round.WasBet),
            next.State,
            next.Message));
    }

    private void PublishAutomation(AutomationSnapshot snapshot) => Publish(new AutomationSnapshotPayload(
        snapshot.State,
        snapshot.AutoBetEnabled,
        snapshot.IsSimulation,
        snapshot.SessionStake,
        snapshot.AcceptedBets,
        snapshot.Message));

    private void Publish(ViewerPayload payload)
    {
        try
        {
            var operation = viewerCommunication.PublishAsync(payload);
            if (!operation.IsCompletedSuccessfully) _ = ObservePublishAsync(operation);
        }
        catch
        {
            diagnostics.Write(DiagnosticLevel.Error, "viewer.publish_failed", "A viewer document could not be published.");
        }
    }

    private async Task ObservePublishAsync(ValueTask operation)
    {
        try
        {
            await operation;
        }
        catch
        {
            diagnostics.Write(DiagnosticLevel.Error, "viewer.publish_failed", "A viewer document could not be published.");
        }
    }
}
