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
            pauseRemaining--;
            RememberWinner(outcome);
            var observation = new StrategyRound(
                rounds.Count + 1, outcome, null, null, 0, Profit, false, null);
            rounds.Add(observation);
            return observation;
        }

        var recommendation = Recommendation;
        if (recommendation.State != StrategyEngineState.Ready || recommendation.Side is null || recommendation.Stake is null)
            throw new InvalidOperationException(recommendation.Message);

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
            rounds.Count + 1, outcome, side, stake, change, Profit, true, won);
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

    public void Reset()
    {
        rounds.Clear();
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
}
