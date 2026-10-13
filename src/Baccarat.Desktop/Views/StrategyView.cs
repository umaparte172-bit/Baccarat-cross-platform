using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Baccarat.Core;
using Baccarat.Core.Strategies;
using Baccarat.Infrastructure.Strategies;

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
    private StrategySessionEngine? engine;

    public StrategyView(
        StrategyFileLoader loader,
        StrategyDefinition? initialDefinition = null,
        Action<StrategySessionEngine>? changed = null)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.changed = changed;
        Margin = new Thickness(24);
        Spacing = 16;

        Children.Add(Text("Strategy session", 30));
        Children.Add(Text(
            "Import a recovered strategy, review each recommendation, then enter observed table outcomes. No bets are placed automatically.",
            15,
            "#A8B5CA"));
        Children.Add(strategyName);
        Children.Add(recommendation);
        Children.Add(totals);

        var fileActions = Row();
        var load = Button("Load strategy");
        fileActions.Children.Add(load);
        fileActions.Children.Add(reset);
        fileActions.Children.Add(undo);
        fileActions.Children.Add(export);
        Children.Add(fileActions);

        Children.Add(Text("MANUAL RESOLUTION", 13, "#91A1B9"));
        var resolution = Row();
        resolution.Children.Add(choosePlayer);
        resolution.Children.Add(chooseBanker);
        resolution.Children.Add(manualStake);
        resolution.Children.Add(applyStake);
        Children.Add(resolution);

        Children.Add(Text("OBSERVED TABLE RESULT", 13, "#91A1B9"));
        var outcomes = Row();
        outcomes.Children.Add(playerResult);
        outcomes.Children.Add(bankerResult);
        outcomes.Children.Add(tieResult);
        Children.Add(outcomes);
        Children.Add(Text("STRATEGY HISTORY", 13, "#91A1B9"));
        Children.Add(history);

        load.Click += async (_, _) => await LoadAsync();
        reset.Click += (_, _) => { engine?.Reset(); Refresh(); NotifyChanged(); };
        undo.Click += (_, _) => { engine?.Undo(); Refresh(); NotifyChanged(); };
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
        engine = new StrategySessionEngine(definition);
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
            recommendation.Foreground = Brush.Parse("#E48787");
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
            engine.Record(outcome);
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
            await writer.WriteAsync(engine.ToCsv());
            await writer.FlushAsync();
            recommendation.Text = "Strategy session exported.";
            recommendation.Foreground = Brush.Parse("#7BD88F");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowError("Export failed. Choose a writable location and try again.");
        }
    }

    private void Refresh()
    {
        var next = engine?.Recommendation;
        recommendation.Text = next?.Message ?? "Load a strategy file to begin.";
        recommendation.Foreground = Brush.Parse(next?.State == StrategyEngineState.Stopped ? "#E48787" : "#E9BC72");

        var rounds = engine?.Rounds ?? [];
        totals.Text = $"Profit {engine?.Profit ?? 0:0.00}  ·  Bets {rounds.Count(round => round.WasBet)}  ·  Observed rounds {rounds.Count}";
        reset.IsEnabled = engine is not null;
        undo.IsEnabled = export.IsEnabled = rounds.Count > 0;
        choosePlayer.IsEnabled = chooseBanker.IsEnabled = next?.State == StrategyEngineState.AwaitingSide;
        manualStake.IsEnabled = applyStake.IsEnabled = next?.State == StrategyEngineState.AwaitingStake;
        var canRecord = next?.State is StrategyEngineState.Ready or StrategyEngineState.Paused;
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
            history.Children.Add(Text($"{round.Number:000}  {round.Outcome}  ·  {action}", 15));
        }
    }

    private void ShowError(string message)
    {
        recommendation.Text = message;
        recommendation.Foreground = Brush.Parse("#E48787");
    }

    private void NotifyChanged()
    {
        if (engine is not null) changed?.Invoke(engine);
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 12 };

    private static Button Button(string content) => new() { Content = content, Padding = new Thickness(18, 12) };

    private static TextBlock Text(string value, double size, string color = "#E9EEF6") =>
        new() { Text = value, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
}
