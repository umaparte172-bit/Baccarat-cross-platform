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
        IAuthenticationService? authenticationService = null,
        IDiagnosticSink? diagnostics = null,
        IAuthenticationSessionStore? authenticationSessions = null,
        StrategyDefinition? initialStrategy = null,
        ViewerCommunicationModule? viewerCommunication = null,
        AutomationController? automationController = null)
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

        var overview = WorkspaceUi.Page("TABLE WORKSPACE", "Session tracker", "Record outcomes and review the running table history.");
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

        var account = new AccountView(
            authenticationService ?? defaults.Authentication,
            this.diagnostics,
            authenticationSessions ?? defaults.AuthenticationSessions);
        var viewer = new ViewerView(this.viewerCommunication);
        var controlledAutomation = automationController ?? defaults.Automation;
        controlledAutomation.SnapshotChanged += PublishAutomation;
        var automation = new AutomationView(controlledAutomation);
        var strategies = new StrategyView(new StrategyFileLoader(), initialStrategy, engine =>
        {
            PublishStrategy(engine);
            automation.UpdateStrategy(engine);
        });

        var tabs = new TabControl
        {
            ItemsSource = new[] {
            new TabItem { Header = "SESSION", Content = new ScrollViewer { Content = overview } },
            new TabItem { Header = "STRATEGIES", Content = new ScrollViewer { Content = strategies } },
            new TabItem { Header = "AUTOMATION", Content = new ScrollViewer { Content = automation } },
            new TabItem { Header = "VIEWER", Content = new ScrollViewer { Content = viewer } },
            new TabItem { Header = "ACCOUNT", Content = new ScrollViewer { Content = account } }
        }
        };
        account.Authenticated += () => tabs.SelectedIndex = 0;
        var layout = new DockPanel { Margin = new Thickness(28, 16, 28, 18) };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            Margin = new Thickness(0, 0, 0, 10)
        };
        header.Children.Add(LegacyTheme.Brand(138, 72));
        var heading = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(Label("BACCARAT (EVOLUTION)", 20, "#E9A43A"));
        heading.Children.Add(Label("WETALKBET CONTROL PANEL", 12, "#A7B3C7"));
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        status.Margin = new Thickness(0, 12, 0, 0);
        status.Foreground = LegacyTheme.Muted;
        DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        layout.Children.Add(new Border
        {
            Background = LegacyTheme.Panel,
            BorderBrush = LegacyTheme.FieldBorder,
            BorderThickness = new Thickness(1),
            Child = tabs
        });
        Content = layout;
        Refresh();
        Publish(new CommunicationStatusPayload("offline", "Local versioned communication is ready."));
        PublishSession();
        PublishAutomation(controlledAutomation.Snapshot);
    }

    private static TextBlock Label(string text, double size, string color = "#E9EEF6") =>
        new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };

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
