using System.Globalization;
using System.Text;

namespace Baccarat.Core.Strategies;

public enum BetSide
{
    Player,
    Banker
}

public enum StrategyEngineState
{
    Ready,
    AwaitingSide,
    AwaitingStake,
    Paused,
    Stopped
}

public sealed record BetRecommendation(
    string Selector,
    decimal? Stake,
    BetSide? Side,
    StrategyEngineState State,
    string Message);

public sealed record StrategyRound(
    int Number,
    Outcome Outcome,
    string? Selector,
    BetSide? Side,
    decimal? Stake,
    decimal ProfitChange,
    decimal TotalProfit,
    bool WasBet,
    bool? Won);

public sealed class StrategySessionEngine
{
    private readonly StrategyDefinition definition;
    private readonly List<StrategyRound> rounds = [];
    private readonly Stack<EngineSnapshot> snapshots = [];
    private StrategyRule currentRule;
    private int sequenceIndex;
    private decimal? currentStake;
    private BetSide? manualSide;
    private BetSide? lastWinner;
    private int pauseRemaining;

    public StrategySessionEngine(StrategyDefinition definition)
    {
        var errors = StrategyDefinitionValidator.Validate(definition);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(definition));

        this.definition = definition;
        currentRule = definition.Initial;
        currentStake = definition.Initial.Values[0];
    }

    public IReadOnlyList<StrategyRound> Rounds => rounds;
    public decimal Profit { get; private set; }
    public int ConsecutiveLosses { get; private set; }
    public string? StopReason { get; private set; }

    public BetRecommendation Recommendation
    {
        get
        {
            if (StopReason is not null)
                return new(currentRule.Selector, currentStake, null, StrategyEngineState.Stopped, StopReason);
            if (pauseRemaining > 0)
                return new(currentRule.Selector, null, null, StrategyEngineState.Paused, $"Observe {pauseRemaining} more round(s) before betting.");

            var side = manualSide ?? ResolveSide(currentRule.Selector);
            if (side is null)
                return new(currentRule.Selector, currentStake, null, StrategyEngineState.AwaitingSide,
                    $"Choose Player or Banker for selector '{currentRule.Selector}'.");
            if (currentStake is null)
                return new(currentRule.Selector, null, side, StrategyEngineState.AwaitingStake,
                    $"Enter a stake for amount rule '{currentRule.AmountRule}'.");

            return new(currentRule.Selector, currentStake, side, StrategyEngineState.Ready,
                $"Bet {currentStake:0.##} on {side}.");
        }
    }

    public void ChooseSide(BetSide side)
    {
        EnsureRunning();
        if (pauseRemaining > 0) throw new InvalidOperationException("The strategy is paused.");
        manualSide = side;
    }

    public void SetManualStake(decimal stake)
    {
        EnsureRunning();
        if (stake <= 0) throw new ArgumentOutOfRangeException(nameof(stake), "Stake must be positive.");
        currentStake = stake;
    }

    public StrategyRound Record(Outcome outcome)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        EnsureRunning();

        if (pauseRemaining > 0)
        {
            snapshots.Push(Capture());
            pauseRemaining--;
            RememberWinner(outcome);
            var observation = new StrategyRound(
                rounds.Count + 1, outcome, null, null, null, 0, Profit, false, null);
            rounds.Add(observation);
            return observation;
        }

        var recommendation = Recommendation;
        if (recommendation.State != StrategyEngineState.Ready || recommendation.Side is null || recommendation.Stake is null)
            throw new InvalidOperationException(recommendation.Message);

        snapshots.Push(Capture());
        var side = recommendation.Side.Value;
        var stake = recommendation.Stake.Value;
        bool? won = outcome == Outcome.Tie ? null :
            side == BetSide.Player ? outcome == Outcome.Player : outcome == Outcome.Banker;
        var change = won switch
        {
            true when side == BetSide.Banker => stake * 0.95m,
            true => stake,
            false => -stake,
            null => 0
        };

        Profit += change;
        if (won == false) ConsecutiveLosses++;
        else if (won == true) ConsecutiveLosses = 0;

        var completed = new StrategyRound(
            rounds.Count + 1, outcome, currentRule.Selector, side, stake, change, Profit, true, won);
        rounds.Add(completed);
        RememberWinner(outcome);

        if (won is not null)
        {
            currentRule = won.Value ? definition.AfterWin : definition.AfterLoss;
            manualSide = null;
            ApplyAmountRule(currentRule);
            ApplyLimits();
        }

        return completed;
    }

    public bool Undo()
    {
        if (rounds.Count == 0) return false;
        var snapshot = snapshots.Pop();
        rounds.RemoveAt(rounds.Count - 1);
        Restore(snapshot);
        return true;
    }

    public string ToCsv()
    {
        var result = new StringBuilder("Round,Outcome,Selector,Side,Stake,ProfitChange,TotalProfit,WasBet,Won\n");
        foreach (var round in rounds)
        {
            result.Append(round.Number).Append(',')
                .Append(round.Outcome).Append(',')
                .Append(EscapeCsv(round.Selector)).Append(',')
                .Append(round.Side?.ToString()).Append(',')
                .Append(Format(round.Stake)).Append(',')
                .Append(Format(round.ProfitChange)).Append(',')
                .Append(Format(round.TotalProfit)).Append(',')
                .Append(round.WasBet).Append(',')
                .Append(round.Won?.ToString())
                .Append('\n');
        }

        return result.ToString();
    }

    public void Reset()
    {
        rounds.Clear();
        snapshots.Clear();
        Profit = 0;
        ConsecutiveLosses = 0;
        StopReason = null;
        pauseRemaining = 0;
        sequenceIndex = 0;
        currentRule = definition.Initial;
        currentStake = definition.Initial.Values[0];
        manualSide = null;
        lastWinner = null;
    }

    private void ApplyAmountRule(StrategyRule rule)
    {
        switch (rule.AmountRule)
        {
            case AmountRuleKind.Set:
                sequenceIndex = 0;
                currentStake = rule.Values[0];
                break;
            case AmountRuleKind.ResetAmount:
                sequenceIndex = 0;
                currentStake = definition.Initial.Values[0];
                break;
            case AmountRuleKind.MultiplyAmount:
                currentStake *= rule.Values[0];
                break;
            case AmountRuleKind.ResetSequence:
                sequenceIndex = 0;
                currentStake = definition.Initial.Values[0];
                break;
            case AmountRuleKind.NextSequence:
                sequenceIndex++;
                if (sequenceIndex >= definition.Initial.Values.Count)
                    StopReason = "The configured amount sequence is complete.";
                else
                    currentStake = definition.Initial.Values[sequenceIndex];
                break;
            case AmountRuleKind.Grinding:
            case AmountRuleKind.Pattern:
                currentStake = null;
                break;
            default:
                throw new InvalidOperationException("Unsupported amount rule.");
        }
    }

    private void ApplyLimits()
    {
        if (definition.StopProfit is { } stopProfit && Profit >= stopProfit)
            StopReason = "The stop-profit limit was reached.";
        else if (definition.StopLoss is { } stopLoss && Profit <= -stopLoss)
            StopReason = "The stop-loss limit was reached.";
        else if (definition.StopAfterConsecutiveLosses is { } stopLosses && ConsecutiveLosses >= stopLosses)
            StopReason = "The consecutive-loss limit was reached.";
        else if (definition.PauseAfterConsecutiveLosses is { } pauseLosses &&
                 ConsecutiveLosses >= pauseLosses && definition.PauseRounds > 0)
        {
            pauseRemaining = definition.PauseRounds;
            ConsecutiveLosses = 0;
        }
    }

    private BetSide? ResolveSide(string selector) => selector.Trim().ToUpperInvariant() switch
    {
        "PLAYER" => BetSide.Player,
        "BANKER" => BetSide.Banker,
        "WINNER" => lastWinner,
        _ => null
    };

    private void RememberWinner(Outcome outcome)
    {
        if (outcome == Outcome.Player) lastWinner = BetSide.Player;
        if (outcome == Outcome.Banker) lastWinner = BetSide.Banker;
    }

    private void EnsureRunning()
    {
        if (StopReason is not null) throw new InvalidOperationException(StopReason);
    }

    private EngineSnapshot Capture() => new(
        Profit,
        ConsecutiveLosses,
        StopReason,
        currentRule,
        sequenceIndex,
        currentStake,
        manualSide,
        lastWinner,
        pauseRemaining);

    private void Restore(EngineSnapshot snapshot)
    {
        Profit = snapshot.Profit;
        ConsecutiveLosses = snapshot.ConsecutiveLosses;
        StopReason = snapshot.StopReason;
        currentRule = snapshot.CurrentRule;
        sequenceIndex = snapshot.SequenceIndex;
        currentStake = snapshot.CurrentStake;
        manualSide = snapshot.ManualSide;
        lastWinner = snapshot.LastWinner;
        pauseRemaining = snapshot.PauseRemaining;
    }

    private static string Format(decimal? value) =>
        value?.ToString("0.############################", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record EngineSnapshot(
        decimal Profit,
        int ConsecutiveLosses,
        string? StopReason,
        StrategyRule CurrentRule,
        int SequenceIndex,
        decimal? CurrentStake,
        BetSide? ManualSide,
        BetSide? LastWinner,
        int PauseRemaining);
}
