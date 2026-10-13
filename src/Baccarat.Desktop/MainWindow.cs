using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Baccarat.Core;
using Baccarat.Core.Authentication;
using Baccarat.Core.Diagnostics;
using Baccarat.Core.Strategies;
using Baccarat.Desktop.Views;
using Baccarat.Infrastructure.Strategies;

namespace Baccarat.Desktop;

public sealed class MainWindow : Window
{
    private readonly Session session = new();
    private readonly TextBlock stats = new() { FontSize = 22 };
    private readonly TextBlock status = new() { Text = "Ready · Manual session", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel history = new() { Spacing = 8 };
    private readonly Button undo;
    private readonly Button clear;
    private readonly Button export;

    public MainWindow(
        IAuthenticationService? authenticationService = null,
        IDiagnosticSink? diagnostics = null,
        IAuthenticationSessionStore? authenticationSessions = null,
        StrategyDefinition? initialStrategy = null)
    {
        Title = "Baccarat Workspace";
        Width = 1040; Height = 760; MinWidth = 700; MinHeight = 550;
        Background = Brush.Parse("#10151F");
        undo = ActionButton("Undo last", () => { session.Undo(); Refresh(); });
        clear = ActionButton("Clear session…", async () => await ConfirmClear());
        export = ActionButton("Export CSV", async () => await Export());

        var overview = new StackPanel { Spacing = 20, Margin = new Thickness(24) };
        overview.Children.Add(Label("SESSION", 13, "#91A1B9"));
        overview.Children.Add(Label("Track your table", 30));
        overview.Children.Add(Label("Record results manually. Records stay in memory until you export them.", 15, "#A8B5CA"));
        overview.Children.Add(stats);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var outcome in Enum.GetValues<Outcome>())
            actions.Children.Add(ActionButton($"+ {outcome}", () => { session.Record(outcome); Refresh(); }));
        overview.Children.Add(actions);
        var utilities = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        utilities.Children.Add(undo); utilities.Children.Add(clear); utilities.Children.Add(export);
        overview.Children.Add(utilities);
        overview.Children.Add(Label("ROUND HISTORY", 13, "#91A1B9"));
        overview.Children.Add(history);

        var account = new AccountView(
            authenticationService ?? ApplicationServices.CreateDefault().Authentication,
            diagnostics,
            authenticationSessions);
        var strategies = new StrategyView(new StrategyFileLoader(), initialStrategy);

        var tabs = new TabControl
        {
            ItemsSource = new[] {
            new TabItem { Header = "Session", Content = new ScrollViewer { Content = overview } },
            new TabItem { Header = "Strategies", Content = new ScrollViewer { Content = strategies } },
            new TabItem { Header = "Account", Content = new ScrollViewer { Content = account } }
        }
        };
        var layout = new DockPanel { Margin = new Thickness(24) };
        var heading = Label("BACCARAT  /  WORKSPACE", 18);
        heading.Margin = new Thickness(0, 0, 0, 20);
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        status.Margin = new Thickness(0, 16, 0, 0);
        DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        layout.Children.Add(tabs);
        Content = layout;
        Refresh();
    }

    private static TextBlock Label(string text, double size, string color = "#E9EEF6") =>
        new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(18, 12) };
        button.Click += (_, _) => action();
        return button;
    }

    private void Refresh()
    {
        stats.Text = $"{session.Rounds.Count} rounds    •    Player {session.Count(Outcome.Player)}    /    Banker {session.Count(Outcome.Banker)}    /    Tie {session.Count(Outcome.Tie)}";
        undo.IsEnabled = clear.IsEnabled = export.IsEnabled = session.Rounds.Count > 0;
        history.Children.Clear();
        if (session.Rounds.Count == 0) history.Children.Add(Label("No rounds yet. Choose a result above to begin.", 16, "#91A1B9"));
        foreach (var round in session.Rounds.Reverse())
            history.Children.Add(Label($"{round.Number:000}     {round.Outcome}     {round.RecordedAt.ToLocalTime():HH:mm:ss}", 17));
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
        if (await dialog.ShowDialog<bool>(this)) { session.Clear(); Refresh(); }
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
}
