using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Baccarat.Core;
using Baccarat.Core.Roadmaps;
using Baccarat.Desktop.ViewModels;

namespace Baccarat.Desktop.Views;

public sealed class DashboardView : UserControl
{
    public DashboardWorkspace Workspace { get; }
    public event Action? WorkspaceChanged;
    public void RefreshSharedState() => Refresh(true);
    public void SetStorageStatus(string status) => notice.Text = status;
    private readonly ListBox sessionList = new() { Name = "SessionList", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly Border sidebar;
    private readonly TextBlock title = DashboardUi.Text("Baccarat Workspace", 17, bold: true);
    private readonly TextBlock subtitle = DashboardUi.Text("Create a session to begin", 12, DashboardUi.Muted);
    private readonly TextBlock notice = DashboardUi.Text("Local workspace · Sessions are in memory only. Export results before closing.", 12, DashboardUi.Muted);
    private readonly ContentControl body = new();
    private readonly StackPanel sessionBody;
    private readonly StackPanel empty;
    private readonly ComboBox strategy = new() { Name = "StrategySelector", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox unit = DashboardUi.Input("BaseUnit", "1");
    private readonly TextBox bankroll = DashboardUi.Input("StartingBankroll", "1000");
    private readonly StackPanel fields = new() { Spacing = 8 };
    private readonly TextBlock settingsStatus = DashboardUi.Text("", 12, DashboardUi.Muted);
    private readonly CheckBox bankerPair = new() { Name = "BankerPair", Content = "Banker pair", FontSize = 12 };
    private readonly CheckBox playerPair = new() { Name = "PlayerPair", Content = "Player pair", FontSize = 12 };
    private readonly TextBlock handCount = DashboardUi.Text("Shoe 1 · 0 hands", 12, DashboardUi.Muted);
    private readonly TextBlock next = DashboardUi.Text("Not calculated", 19, bold: true);
    private readonly TextBlock nextDescription = DashboardUi.Text("", 12, DashboardUi.Muted);
    private readonly TextBlock balance = DashboardUi.Text("—");
    private readonly TextBlock profit = DashboardUi.Text("Session P/L  0.00", 12);
    private readonly List<Control> draftControls = new();
    private Grid? unitRow;
    private readonly TextBlock stopDescription = DashboardUi.Text("", 12, DashboardUi.Muted);
    private readonly StackPanel history = new() { Spacing = 2 };
    private readonly TextBlock historyNotice = DashboardUi.Text("", 12, DashboardUi.Muted);
    private readonly StackPanel statistics = new() { Spacing = 12 };
    private readonly RoadmapsView roadmaps = new();
    private readonly Button undo;
    private readonly Button export;
    private readonly Button beginDraft;
    private Button newShoe = null!;
    private readonly List<Button> resultButtons = new();
    private bool refreshing;

    public DashboardView(Action openExistingTools, DashboardWorkspace? workspace = null)
    {
        Workspace = workspace ?? new DashboardWorkspace();
        Background = DashboardUi.Surface;
        Foreground = DashboardUi.Ink;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var sidebarContent = new DockPanel { Margin = new Thickness(12) };
        var sidebarHeading = DashboardUi.Stack(DashboardUi.Text("SESSIONS", 11, DashboardUi.Muted, true),
            DashboardUi.Text("Your local workspace", 12, DashboardUi.Muted));
        DockPanel.SetDock(sidebarHeading, Dock.Top);
        sidebarContent.Children.Add(sidebarHeading);
        var sidebarFoot = DashboardUi.Text("Local workspace\nNo server connection", 11, DashboardUi.Muted);
        sidebarFoot.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(sidebarFoot, Dock.Bottom);
        sidebarContent.Children.Add(sidebarFoot);
        sessionList.Margin = new Thickness(0, 12, 0, 0);
        sessionList.ItemTemplate = new FuncDataTemplate<DashboardSession>((item, _) =>
            DashboardUi.Text(item?.SidebarLabel ?? "", 12));
        sessionList.SelectionChanged += (_, _) =>
        {
            if (!refreshing && sessionList.SelectedItem is DashboardSession selected)
            {
                Workspace.Select(selected);
                Refresh(true);
            }
        };
        sidebarContent.Children.Add(sessionList);
        sidebar = new Border
        {
            Name = "SessionSidebar",
            Width = 204,
            Background = Brush.Parse("#ECEEF2"),
            BorderBrush = DashboardUi.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebarContent
        };
        root.Children.Add(sidebar);

        var main = new DockPanel();
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(16, 12) };
        var toggle = DashboardUi.Button("☰", () => sidebar.IsVisible = !sidebar.IsVisible);
        toggle.Name = "ToggleSidebar";
        Avalonia.Automation.AutomationProperties.SetName(toggle, "Show or hide sessions");
        ToolTip.SetTip(toggle, "Show or hide session sidebar");
        toolbar.Children.Add(toggle);
        Add(toolbar, DashboardUi.Button("+ New session", CreateSession), 1);
        Add(toolbar, DashboardUi.Stack(title, subtitle), 2);
        var toolsButton = DashboardUi.Button("Tools", openExistingTools);
        ToolTip.SetTip(toolsButton, "Use strategy, automation, viewer and account tools for this session in the same window.");
        Add(toolbar, toolsButton, 3);
        DockPanel.SetDock(toolbar, Dock.Top);
        main.Children.Add(toolbar);
        notice.Margin = new Thickness(16, 8);
        DockPanel.SetDock(notice, Dock.Bottom);
        main.Children.Add(notice);
        main.Children.Add(body);

        empty = DashboardUi.Stack(DashboardUi.Text("No Session", 28, bold: true),
            DashboardUi.Text("Create a session to start entering results. No sign-in is required.", 14, DashboardUi.Muted),
            DashboardUi.Button("New Session", CreateSession, "#1769CE"));
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Margin = new Thickness(30);
        foreach (var child in empty.Children) child.HorizontalAlignment = HorizontalAlignment.Center;

        undo = DashboardUi.Button("Undo", () => { Workspace.Selected?.Undo(); Refresh(false); });
        export = DashboardUi.Button("Export CSV", () => _ = ExportAsync());
        beginDraft = DashboardUi.Button("Return to strategy drafts", () =>
        {
            Workspace.Selected?.BeginDraftRun();
            Refresh(true);
        });
        sessionBody = new StackPanel { Name = "DashboardContent", Spacing = 14, Margin = new Thickness(16, 0, 16, 16), MinWidth = 704 };
        main.SizeChanged += (_, _) => sessionBody.Width = Math.Max(704, main.Bounds.Width - 32);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*"), ColumnSpacing = 14 };
        top.Children.Add(BuildEntry());
        Add(top, BuildStrategy(), 1);
        sessionBody.Children.Add(top);
        sessionBody.Children.Add(roadmaps);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,196"), ColumnSpacing = 14 };
        var ledger = DashboardUi.Stack(HistoryRow("#", "Result / pairs", "Suggested", "Stake", "Net", "Balance", true), history, historyNotice);
        bottom.Children.Add(DashboardUi.Card("History · All shoes", ledger));
        Add(bottom, DashboardUi.Card("Statistics", statistics), 1);
        sessionBody.Children.Add(bottom);
        Content = root;
        Refresh(true);
    }

    private static void Add(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private Control BuildEntry()
    {
        var results = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 8 };
        var outcomes = new[] { Outcome.Banker, Outcome.Player, Outcome.Tie };
        for (var i = 0; i < outcomes.Length; i++)
        {
            var outcome = outcomes[i];
            var button = DashboardUi.Button(outcome.ToString(), () => Record(outcome), outcome switch
            {
                Outcome.Banker => "#D92F22",
                Outcome.Player => "#086CE8",
                _ => "#119C50"
            });
            button.Height = 56;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Name = $"Record{outcome}";
            resultButtons.Add(button);
            Add(results, button, i);
        }
        var pairs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pairs.Children.Add(bankerPair);
        pairs.Children.Add(playerPair);
        bankerPair.IsCheckedChanged += (_, _) => { if (!refreshing && Workspace.Selected is { } s) { s.BankerPairDraft = bankerPair.IsChecked == true; WorkspaceChanged?.Invoke(); } };
        playerPair.IsCheckedChanged += (_, _) => { if (!refreshing && Workspace.Selected is { } s) { s.PlayerPairDraft = playerPair.IsChecked == true; WorkspaceChanged?.Invoke(); } };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        newShoe = DashboardUi.Button("New Shoe", () => { Workspace.Selected?.NewShoe(); Refresh(false); });
        foreach (var button in new[] { undo, newShoe, export })
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            actions.Children.Add(button);
        }
        return DashboardUi.Card("Enter result", DashboardUi.Stack(results, pairs, actions, handCount,
            DashboardUi.Text("New Shoe resets shoe counts; balance and strategy carry forward. Undo reverses an empty new shoe. Pairs are observations, not bets.", 11, DashboardUi.Muted)));
    }

    private Control BuildStrategy()
    {
        strategy.ItemsSource = DashboardSession.Strategies.Select(s => s.Name).ToArray();
        strategy.SelectionChanged += (_, _) =>
        {
            if (refreshing || Workspace.Selected is not { } s || strategy.SelectedItem is not string name) return;
            s.SelectStrategy(name);
            Refresh(true);
        };
        unit.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !refreshing && Workspace.Selected is { } s) { s.BaseUnitDraft = unit.Text ?? ""; SettingsEdited(); } };
        bankroll.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !refreshing && Workspace.Selected is { } s) { s.BankrollDraft = bankroll.Text ?? ""; SettingsEdited(); } };
        var apply = DashboardUi.Button("Apply settings", ApplySettings);
        draftControls.AddRange(new Control[] { strategy, unit, bankroll, fields, apply });
        unitRow = Field("Base unit", unit);
        var editor = DashboardUi.Stack(Field("Strategy", strategy), unitRow, Field("Starting bankroll", bankroll), fields,
            new WrapPanel { Children = { apply, beginDraft } }, settingsStatus);
        editor.Spacing = 6;
        editor.MaxWidth = 390;
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        beginDraft.Margin = new Thickness(6, 0, 0, 0);
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source is TextBox) { ApplySettings(); e.Handled = true; }
        };
        var pause = new Expander
        {
            Header = "Stop and pause",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = stopDescription
        };
        editor.Children.Add(pause);
        var summary = DashboardUi.Stack(DashboardUi.Text("NEXT HAND", 11, DashboardUi.Muted, true), next, nextDescription,
            new Separator(), DashboardUi.Text("Balance", 12, DashboardUi.Muted), balance,
            profit, DashboardUi.Text("Calculated from recorded strategy results, not a casino account. Use Tools for import and manual decisions.", 11, DashboardUi.Muted));
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        columns.Children.Add(editor);
        Add(columns, summary, 1);
        // Keep the summary alongside settings on desktop; stack it at narrower widths.
        columns.SizeChanged += (_, _) =>
        {
            var compact = columns.Bounds.Width < 450;
            columns.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "Auto,*");
            columns.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
            Grid.SetColumn(summary, compact ? 0 : 1);
            Grid.SetRow(summary, compact ? 1 : 0);
            columns.RowSpacing = compact ? 16 : 0;
        };
        return DashboardUi.Card("Strategy", columns);
    }

    private static Grid Field(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,170"), ColumnSpacing = 8 };
        var text = DashboardUi.Text(label, 12);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        Add(grid, input, 1);
        Avalonia.Automation.AutomationProperties.SetName(input, label);
        return grid;
    }

    private static Grid HistoryRow(string number, string outcome, string suggested, string stake, string net, string amount, bool heading = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,78,42,38,62"), ColumnSpacing = 4, MinHeight = 30 };
        var values = new[] { number, outcome, suggested, stake, net, amount };
        for (var i = 0; i < values.Length; i++)
        {
            var cell = DashboardUi.Text(values[i], heading ? 11 : 12, heading ? DashboardUi.Muted : DashboardUi.Ink, heading);
            cell.VerticalAlignment = VerticalAlignment.Center;
            Add(row, cell, i);
        }
        return row;
    }

    private void CreateSession()
    {
        if (!Workspace.CreateSession()) { notice.Text = "Session limit reached (100). Export records before closing."; return; }
        Refresh(true);
    }

    private void Record(Outcome outcome)
    {
        if (Workspace.Selected is not { } s) return;
        try
        {
            if (!s.Record(outcome)) notice.Text = "Hand limit reached (10,000). Create another session.";
        }
        catch (InvalidOperationException ex) { notice.Text = ex.Message; }
        Refresh(false);
    }

    private void SettingsEdited()
    {
        settingsStatus.Text = Workspace.Selected?.Engine is null
            ? "Unapplied draft · Press Enter or Apply settings to start."
            : "Unapplied draft · Current run still uses the previously applied settings.";
        WorkspaceChanged?.Invoke();
    }

    private void ApplySettings()
    {
        if (Workspace.Selected is not { } s) return;
        var valid = s.ApplySettings(out var message);
        if (valid) Refresh(true);
        settingsStatus.Text = message;
        settingsStatus.Foreground = valid ? DashboardUi.Muted : DashboardUi.OutcomeColor(Outcome.Banker);
        UpdateSummary(s);
        WorkspaceChanged?.Invoke();
    }

    private void UpdateSummary(DashboardSession s)
    {
        next.Text = s.Engine?.Recommendation.Message ?? "Not calculated";
        nextDescription.Text = s.Engine is null ? $"{s.Strategy.Name} · Apply supported settings or load a strategy in Tools." :
            s.IsVideoMode ? $"{s.Engine.StrategyName} · Reference-rule run · {s.Engine.Recommendation.State}." :
            $"{s.Engine.StrategyName} · Imported strategy · {s.Engine.Recommendation.State}. Use Tools for manual decisions.";
        balance.Text = s.Balance.ToString("N2", CultureInfo.CurrentCulture);
        profit.Text = $"Session P/L  {s.Profit:N2}";
    }

    private void Refresh(bool settings)
    {
        refreshing = true;
        try
        {
            // Rebuild labels when a session's strategy/count changes; never retain stale subtitles.
            sessionList.ItemsSource = null;
            sessionList.ItemsSource = Workspace.Sessions;
            sessionList.SelectedItem = Workspace.Selected;
            if (Workspace.Selected is not { } s) { body.Content = empty; return; }
            if (body.Content is not ScrollViewer)
                body.Content = new ScrollViewer { Content = sessionBody, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            title.Text = s.Title;
            subtitle.Text = $"{s.ActiveStrategyName} · Shoe {s.CurrentShoe} · Hand {s.ShoeHandCount}";
            handCount.Text = $"Shoe {s.CurrentShoe} · {s.ShoeHandCount} hands · Session {s.Hands.Count} hands";
            bankerPair.IsChecked = s.BankerPairDraft;
            playerPair.IsChecked = s.PlayerPairDraft;
            undo.IsEnabled = s.CanUndo;
            export.IsEnabled = s.Hands.Count > 0;
            newShoe.IsEnabled = s.CanStartShoe;
            foreach (var button in resultButtons) button.IsEnabled = s.CanRecord;
            if (settings)
            {
                var imported = s.Engine is not null && !s.IsVideoMode;
                foreach (var control in draftControls) control.IsEnabled = !imported;
                beginDraft.IsVisible = s.Engine is not null;
                strategy.ItemsSource = !imported ? DashboardSession.Strategies.Select(d => d.Name).ToArray() : new[] { s.Engine!.StrategyName };
                strategy.SelectedItem = s.ActiveStrategyName;
                unit.Text = imported ? s.LoadedDefinition!.Initial.Values[0].ToString(CultureInfo.InvariantCulture) : s.BaseUnitDraft;
                ((TextBlock)unitRow!.Children[0]).Text = imported ? "Initial amount" : "Base unit";
                bankroll.Text = s.BankrollDraft;
                fields.Children.Clear();
                foreach (var field in imported ? Array.Empty<ViewModels.DashboardField>() : s.Strategy.Fields)
                {
                    var input = DashboardUi.Input($"Setting_{field.Key}", field.Hint);
                    input.Text = s.GetField(field.Key);
                    input.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == TextBox.TextProperty && !refreshing) { s.SetField(field.Key, input.Text ?? ""); SettingsEdited(); }
                    };
                    fields.Children.Add(Field(field.Label, input));
                }
                if (imported && s.LoadedDefinition is { } definition)
                {
                    fields.Children.Add(DashboardUi.Text($"Imported selector: {definition.Initial.Selector}\nAfter win: {definition.AfterWin.Selector} / {definition.AfterWin.AmountRule}\nAfter loss: {definition.AfterLoss.Selector} / {definition.AfterLoss.AmountRule}", 12, DashboardUi.Muted));
                    fields.IsEnabled = true;
                    stopDescription.Text = $"Imported limits · Profit: {definition.StopProfit?.ToString() ?? "none"}; loss: {definition.StopLoss?.ToString() ?? "none"}; consecutive losses: {definition.StopAfterConsecutiveLosses?.ToString() ?? "none"}; pause after losses: {definition.PauseAfterConsecutiveLosses?.ToString() ?? "none"}; pause rounds: {definition.PauseRounds}.";
                }
                else stopDescription.Text = s.Strategy.Name == "PATTERN" ?
                    "IHG defaults: close a run after a settled bet of 45+ units; pause after 7 isolated wins. Simultaneous limits require review because the PDF conflicts." :
                    "Client test-case rules are active after Apply. Empty settings use the documented defaults.";
                settingsStatus.Foreground = DashboardUi.Muted;
                settingsStatus.Text = imported ? "Imported rules are active. Return to drafts to configure a new run." :
                    s.HasUnappliedSettings ? "Unapplied settings draft" :
                    s.IsVideoMode ? "Reference-rule run active" : "Draft settings · Apply to start a supported run";
            }
            UpdateSummary(s);
            roadmaps.Update(RoadmapCalculator.Calculate(s.ShoeHands.Select(h =>
                new RoadHand(h.Round.Outcome, h.BankerPair, h.PlayerPair))));
            history.Children.Clear();
            foreach (var hand in s.Hands.Reverse().Take(100))
            {
                var pairs = (hand.BankerPair ? " · BP" : "") + (hand.PlayerPair ? " · PP" : "");
                var result = hand.StrategyResult;
                var row = HistoryRow(hand.Round.Number.ToString(CultureInfo.InvariantCulture), $"S{s.ShoeOf(hand)} · {hand.Round.Outcome}{pairs}",
                    result?.Side?.ToString() ?? "Observed", result?.Stake?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—",
                    result?.ProfitChange.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) ?? "—",
                    (s.StartingBankroll + hand.SessionProfit).ToString("0.00", CultureInfo.InvariantCulture));
                ((TextBlock)row.Children[1]).Foreground = DashboardUi.OutcomeColor(hand.Round.Outcome);
                history.Children.Add(row);
            }
            historyNotice.Text = s.Hands.Count == 0 ? "No results yet. Choose Banker, Player or Tie above." :
                $"Latest {Math.Min(100, s.Hands.Count)} of {s.Hands.Count} results · CSV includes all results. BP/PP = observed pairs.";
            statistics.Children.Clear();
            statistics.Children.Add(DashboardUi.Text($"SHOE {s.CurrentShoe}", 11, DashboardUi.Muted, true));
            foreach (var outcome in new[] { Outcome.Banker, Outcome.Player, Outcome.Tie })
            {
                var count = s.ShoeHands.Count(h => h.Round.Outcome == outcome);
                var percent = s.ShoeHandCount == 0 ? 0 : 100d * count / s.ShoeHandCount;
                statistics.Children.Add(DashboardUi.Text($"{outcome}   {count}   {percent:F1}%", 13, DashboardUi.OutcomeColor(outcome)));
            }
            statistics.Children.Add(new Separator());
            var stats = DashboardStatistics.Calculate(s);
            statistics.Children.Add(DashboardUi.Text($"Streak   {stats.StreakSide?.ToString() ?? "—"} {stats.Streak}\nChop   {stats.Chop} (ties ignored)", 12, DashboardUi.Muted));
            statistics.Children.Add(DashboardUi.Text("SESSION", 11, DashboardUi.Muted, true));
            statistics.Children.Add(DashboardUi.Text($"Recorded bets   {stats.Bets}\nW / L / P   {stats.Wins} / {stats.Losses} / {stats.Pushes}\nStake total   {stats.StakeTotal:N2}\nPeak balance   {stats.PeakBalance:N2}\nDrawdown   {stats.Drawdown:N2}\nMax drawdown   {stats.MaximumDrawdown:N2}", 12, DashboardUi.Muted));
            statistics.Children.Add(DashboardUi.Text("No real bets are placed.", 11, DashboardUi.Muted));
        }
        finally { refreshing = false; }
        WorkspaceChanged?.Invoke();
    }

    private async Task ExportAsync()
    {
        if (Workspace.Selected is not { } s || TopLevel.GetTopLevel(this) is not { } top) return;
        var csv = s.ToCsv(); // Snapshot before the asynchronous picker; session selection can change.
        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export recorded results",
                SuggestedFileName = "baccarat-results.csv",
                DefaultExtension = "csv",
                FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(csv);
            await writer.FlushAsync();
            notice.Text = $"Exported results from {s.Title}. CSV includes all shoes; it is not a full workspace backup.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { notice.Text = "Export failed. Choose a writable location and try again."; }
    }
}
