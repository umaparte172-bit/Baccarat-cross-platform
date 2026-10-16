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

public sealed record BaccaratPayoutRules(decimal PlayerWinProfit, decimal BankerWinProfit)
{
    public static BaccaratPayoutRules Standard { get; } = new(1m, 0.95m);

    public void Validate()
    {
        if (PlayerWinProfit <= 0 || BankerWinProfit <= 0)
            throw new ArgumentOutOfRangeException(nameof(PlayerWinProfit), "Win-profit multipliers must be positive.");
    }
}

public sealed record StrategyDecision(Outcome Outcome, BetSide? ManualSide, decimal? Stake);
public sealed record StrategyEngineArchive(StrategyDefinition Definition, BaccaratPayoutRules Payouts,
    StrategyDecision[] Decisions, BetSide? ManualSide, decimal? Stake);

public sealed class StrategySessionEngine
{
    private readonly StrategyDefinition definition;
    private readonly BaccaratPayoutRules payoutRules;
    private readonly BetSide[]? videoSequenceSides;
    private readonly List<StrategyRound> rounds = [];
    private readonly Stack<EngineSnapshot> snapshots = [];
    private StrategyRule currentRule;
    private int sequenceIndex;
    private int videoSequencePosition;
    private decimal? currentStake;
    private BetSide? manualSide;
    private BetSide? lastWinner;
    private int pauseRemaining;

    public StrategySessionEngine(StrategyDefinition definition, BaccaratPayoutRules? payoutRules = null)
    {
        var errors = StrategyDefinitionValidator.Validate(definition);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(definition));

        this.definition = definition;
        this.payoutRules = payoutRules ?? BaccaratPayoutRules.Standard;
        this.payoutRules.Validate();
        if (definition.RuleCondition?.StartsWith(VideoStrategyFactory.SequenceTag, StringComparison.Ordinal) == true)
        {
            if (definition.Initial.Selector != "VIDEO_SEQUENCE" ||
                definition.AfterWin.Selector != "VIDEO_SEQUENCE" || definition.AfterLoss.Selector != "VIDEO_SEQUENCE" ||
                definition.AfterWin.AmountRule != AmountRuleKind.ResetSequence ||
                definition.AfterLoss.AmountRule != AmountRuleKind.NextSequence)
                throw new ArgumentException("Invalid video sequence definition.", nameof(definition));
            var encoded = definition.RuleCondition[VideoStrategyFactory.SequenceTag.Length..];
            var sides = encoded.Split(',');
            if (sides.Length is < 1 or > 64 || sides.Any(s => s is not ("P" or "B")))
                throw new ArgumentException("Invalid video sequence sides.", nameof(definition));
            videoSequenceSides = sides.Select(s => s == "P" ? BetSide.Player : BetSide.Banker).ToArray();
        }
        currentRule = definition.Initial;
        currentStake = definition.Initial.Values[0];
    }

    public IReadOnlyList<StrategyRound> Rounds => rounds;
    public string StrategyName => definition.Name;
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
        if (!Enum.IsDefined(typeof(BetSide), side)) throw new ArgumentOutOfRangeException(nameof(side));
        EnsureRunning();
        if (pauseRemaining > 0) throw new InvalidOperationException("The strategy is paused.");
        manualSide = side;
    }

    public void SetManualStake(decimal stake)
    {
        EnsureRunning();
        if (stake <= 0 || stake > StrategyDefinitionValidator.MaximumSupportedAmount)
            throw new ArgumentOutOfRangeException(nameof(stake), "Stake must be positive and within the supported limit.");
        currentStake = stake;
    }

    public StrategyRound Record(Outcome outcome)
    {
        if (!Enum.IsDefined(typeof(Outcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
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
            true when side == BetSide.Banker => stake * payoutRules.BankerWinProfit,
            true => stake * payoutRules.PlayerWinProfit,
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
            if (videoSequenceSides is not null)
                videoSequencePosition = won.Value ? 0 : (videoSequencePosition + 1) % videoSequenceSides.Length;
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
        videoSequencePosition = 0;
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
                decimal multiplied;
                try
                {
                    multiplied = checked(currentStake!.Value * rule.Values[0]);
                }
                catch (OverflowException)
                {
                    StopReason = "The stake progression exceeded the supported limit.";
                    break;
                }

                if (multiplied > StrategyDefinitionValidator.MaximumSupportedAmount)
                    StopReason = "The stake progression exceeded the supported limit.";
                else currentStake = multiplied;
                break;
            case AmountRuleKind.ResetSequence:
                sequenceIndex = 0;
                currentStake = definition.Initial.Values[0];
                break;
            case AmountRuleKind.NextSequence:
                sequenceIndex = videoSequenceSides is null ? sequenceIndex + 1 :
                    (sequenceIndex + 1) % definition.Initial.Values.Count;
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
        "VIDEO_SEQUENCE" when videoSequenceSides is not null => videoSequenceSides[videoSequencePosition],
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

    // Persist decisions, not trusted calculated totals. Replay uses this same engine
    // and rebuilds the exact pre-round snapshots needed by Undo.
    public StrategyEngineArchive ExportArchive()
    {
        var before = snapshots.Reverse().ToArray();
        return new(definition, payoutRules, rounds.Select((r, i) =>
            new StrategyDecision(r.Outcome, before[i].ManualSide, before[i].CurrentStake)).ToArray(), manualSide, currentStake);
    }

    public static StrategySessionEngine RestoreArchive(StrategyEngineArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Decisions is null || archive.Decisions.Length > 10000 || archive.Payouts != BaccaratPayoutRules.Standard)
            throw new ArgumentException("Unsupported strategy archive.", nameof(archive));
        var engine = new StrategySessionEngine(archive.Definition, archive.Payouts);
        foreach (var decision in archive.Decisions)
        {
            if (decision is null) throw new ArgumentException("Missing strategy decision.", nameof(archive));
            engine.RestoreDecision(decision.ManualSide, decision.Stake);
            engine.Record(decision.Outcome);
        }
        engine.RestoreDecision(archive.ManualSide, archive.Stake);
        return engine;
    }

    private void RestoreDecision(BetSide? side, decimal? stake)
    {
        if (side != manualSide)
        {
            if (side is null) throw new ArgumentException("Invalid manual-side transition.");
            ChooseSide(side.Value);
        }
        if (stake != currentStake)
        {
            if (stake is null) throw new ArgumentException("Invalid stake transition.");
            SetManualStake(stake.Value);
        }
    }

    private EngineSnapshot Capture() => new(
        Profit,
        ConsecutiveLosses,
        StopReason,
        currentRule,
        sequenceIndex,
        videoSequencePosition,
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
        videoSequencePosition = snapshot.VideoSequencePosition;
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
        int VideoSequencePosition,
        decimal? CurrentStake,
        BetSide? ManualSide,
        BetSide? LastWinner,
        int PauseRemaining);
}
