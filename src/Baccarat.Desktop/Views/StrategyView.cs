using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Baccarat.Core;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Strategies;
using Baccarat.Desktop.ViewModels;

namespace Baccarat.Desktop.Views;

public sealed class StrategyView : StackPanel
{
    private readonly StrategyFileLoader loader;
    private readonly Action<StrategySessionEngine>? changed;
    private readonly TextBlock strategyName = Text("No strategy loaded", 24);
    private readonly TextBlock recommendation = Text("Load a strategy file to begin.", 17, "#E9BC72");
    private readonly TextBlock totals = Text("Profit 0.00  ·  Bets 0  ·  Observed rounds 0", 16, "#A8B5CA");
    private readonly StackPanel history = new() { Spacing = 6 };
    private readonly TextBox manualStake = new() { Watermark = "Manual stake", Width = 150 };
    private readonly Button reset = Button("Reset strategy");
    private readonly Button undo = Button("Undo strategy round");
    private readonly Button export = Button("Export strategy CSV");
    private readonly Button choosePlayer = Button("Use Player");
    private readonly Button chooseBanker = Button("Use Banker");
    private readonly Button applyStake = Button("Apply stake");
    private readonly Button playerResult = Button("Player result");
    private readonly Button bankerResult = Button("Banker result");
    private readonly Button tieResult = Button("Tie result");
    private StrategySessionEngine? standaloneEngine;
    private readonly DashboardSession? sharedSession;
    private StrategySessionEngine? engine => sharedSession?.Engine ?? standaloneEngine;

    public StrategyView(
        StrategyFileLoader loader,
        StrategyDefinition? initialDefinition = null,
        Action<StrategySessionEngine>? changed = null,
        DashboardSession? sharedSession = null)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.changed = changed;
        this.sharedSession = sharedSession;
        Margin = new Thickness(0);
        Spacing = 0;
        var page = WorkspaceUi.Page("STRATEGY WORKSPACE", "Strategy session",
            sharedSession is null ? "Import a recovered strategy, review its recommendation, and record observed table outcomes." :
            "These tools use the selected dashboard session. Loading or resetting starts a new strategy run; prior session results are retained.");
        Children.Add(page);

        var fileActions = Row();
        var load = Button("Load strategy");
        load.Background = LegacyTheme.Green;
        load.Foreground = Brushes.White;
        fileActions.Children.Add(load);
        fileActions.Children.Add(reset);
        fileActions.Children.Add(undo);
        fileActions.Children.Add(export);
        var fileCard = WorkspaceUi.Card("STRATEGY FILE", fileActions);

        var current = new StackPanel { Spacing = 10 };
        strategyName.Foreground = LegacyTheme.Text;
        strategyName.FontWeight = FontWeight.Bold;
        current.Children.Add(strategyName);
        current.Children.Add(recommendation);
        current.Children.Add(totals);
        recommendation.FontSize = 20;
        var currentCard = WorkspaceUi.Card("CURRENT RECOMMENDATION", current);
        page.Children.Add(WorkspaceUi.Columns(currentCard, fileCard));

        var resolution = Row();
        resolution.Children.Add(choosePlayer);
        resolution.Children.Add(chooseBanker);
        resolution.Children.Add(manualStake);
        resolution.Children.Add(applyStake);
        var manualCard = WorkspaceUi.Card("MANUAL RESOLUTION", resolution);
        page.Children.Add(Text(
            "Protected rules such as PATTERN require your side and stake decisions. Their automatic algorithms are not yet verified. " +
            "Settlement currently uses Player 1:1, Banker 0.95:1 and tie pushes; confirm these against the original before relying on comparisons.",
            14, "#91A1B9"));

        var outcomes = Row();
        playerResult.Background = DashboardUi.OutcomeColor(Outcome.Player);
        bankerResult.Background = DashboardUi.OutcomeColor(Outcome.Banker);
        tieResult.Background = DashboardUi.OutcomeColor(Outcome.Tie);
        playerResult.Foreground = bankerResult.Foreground = tieResult.Foreground = Brushes.White;
        outcomes.Children.Add(playerResult);
        outcomes.Children.Add(bankerResult);
        outcomes.Children.Add(tieResult);
        page.Children.Add(WorkspaceUi.Columns(manualCard, WorkspaceUi.Card("OBSERVED TABLE RESULT", outcomes)));
        page.Children.Add(WorkspaceUi.Card("STRATEGY HISTORY", history));

        load.Click += async (_, _) => await LoadAsync();
        reset.Click += (_, _) => { if (sharedSession is null) engine?.Reset(); else sharedSession.ResetStrategy(); Refresh(); NotifyChanged(); };
        undo.Click += (_, _) => { if (sharedSession is null) engine?.Undo(); else sharedSession.Undo(); Refresh(); NotifyChanged(); };
        export.Click += async (_, _) => await ExportAsync();
        choosePlayer.Click += (_, _) => Choose(BetSide.Player);
        chooseBanker.Click += (_, _) => Choose(BetSide.Banker);
        applyStake.Click += (_, _) => SetStake();
        playerResult.Click += (_, _) => Record(Outcome.Player);
        bankerResult.Click += (_, _) => Record(Outcome.Banker);
        tieResult.Click += (_, _) => Record(Outcome.Tie);

        if (initialDefinition is not null) Load(initialDefinition);
        else Refresh();
    }

    public void Load(StrategyDefinition definition)
    {
        if (sharedSession is null) standaloneEngine = new StrategySessionEngine(definition);
        else sharedSession.LoadStrategy(definition);
        strategyName.Text = definition.Name;
        Refresh();
        NotifyChanged();
    }

    private async Task LoadAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load strategy",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Strategy JSON") { Patterns = ["*.txt", "*.json"] }
                ]
            });
            var file = files.SingleOrDefault();
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            Load(await loader.LoadAsync(stream));
        }
        catch (Exception exception) when (exception is StrategyFormatException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            recommendation.Text = $"Strategy could not be loaded: {exception.Message}";
            recommendation.Foreground = WorkspaceUi.Tone("#E48787");
        }
    }

    private void Choose(BetSide side)
    {
        try
        {
            engine?.ChooseSide(side);
            Refresh();
            NotifyChanged();
        }
        catch (InvalidOperationException exception)
        {
            ShowError(exception.Message);
        }
    }

    private void SetStake()
    {
        if (engine is null) return;
        if (!decimal.TryParse(manualStake.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            ShowError("Enter a positive stake using digits and an optional decimal point.");
            return;
        }

        try
        {
            engine.SetManualStake(value);
            manualStake.Text = string.Empty;
            Refresh();
            NotifyChanged();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            ShowError(exception.Message);
        }
    }

    private void Record(Outcome outcome)
    {
        if (engine is null) return;
        try
        {
            if (sharedSession is null) engine.Record(outcome);
            else if (!sharedSession.Record(outcome)) throw new InvalidOperationException("Session hand limit reached (10,000).");
            Refresh();
            NotifyChanged();
        }
        catch (InvalidOperationException exception)
        {
            ShowError(exception.Message);
        }
    }

    private async Task ExportAsync()
    {
        if (engine is null || engine.Rounds.Count == 0) return;
        var csv = engine.ToCsv();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export strategy session",
                SuggestedFileName = "strategy-session.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(csv);
            await writer.FlushAsync();
            recommendation.Text = "Strategy session exported.";
            recommendation.Foreground = WorkspaceUi.Tone("#7BD88F");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowError("Export failed. Choose a writable location and try again.");
        }
    }

    public void Refresh()
    {
        strategyName.Text = engine?.StrategyName ?? "No strategy loaded";
        var next = engine?.Recommendation;
        recommendation.Text = next?.Message ?? "Load a strategy file to begin.";
        recommendation.Foreground = WorkspaceUi.Tone(next?.State == StrategyEngineState.Stopped ? "#E48787" : "#E9BC72");

        var rounds = engine?.Rounds ?? [];
        totals.Text = $"Profit {engine?.Profit ?? 0:0.00}  ·  Bets {rounds.Count(round => round.WasBet)}  ·  Observed rounds {rounds.Count}";
        reset.IsEnabled = engine is not null;
        undo.IsEnabled = export.IsEnabled = rounds.Count > 0;
        choosePlayer.IsEnabled = chooseBanker.IsEnabled = next?.State == StrategyEngineState.AwaitingSide;
        manualStake.IsEnabled = applyStake.IsEnabled = next?.State == StrategyEngineState.AwaitingStake;
        var canRecord = (next?.State is StrategyEngineState.Ready or StrategyEngineState.Paused) &&
            (sharedSession is null || sharedSession.CanRecord);
        playerResult.IsEnabled = bankerResult.IsEnabled = tieResult.IsEnabled = canRecord;

        history.Children.Clear();
        if (rounds.Count == 0)
        {
            history.Children.Add(Text("No strategy rounds recorded.", 15, "#91A1B9"));
            return;
        }

        foreach (var round in rounds.Reverse().Take(30))
        {
            var action = round.WasBet
                ? $"{round.Side} {round.Stake:0.##}  ·  {(round.Won is null ? "Push" : round.Won.Value ? "Win" : "Loss")}  ·  {round.ProfitChange:+0.00;-0.00;0.00}"
                : "Observed while paused";
            history.Children.Add(WorkspaceUi.HistoryRow($"#{round.Number:000}  {round.Outcome}  ·  {action}"));
        }
    }

    private void ShowError(string message)
    {
        recommendation.Text = message;
        recommendation.Foreground = WorkspaceUi.Tone("#E48787");
    }

    private void NotifyChanged()
    {
        if (engine is not null) changed?.Invoke(engine);
    }

    private static WrapPanel Row() => WorkspaceUi.WrapRow();

    private static Button Button(string content) => WorkspaceUi.Action(content);

    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = WorkspaceUi.Tone(color), TextWrapping = TextWrapping.Wrap };
}
