namespace Baccarat.Core.Strategies;

// Rules derived from the supplied Baccarat Assistant video, not the older
// protected Wetalkbet executable. Modes without enough evidence remain manual.
public static class VideoStrategyFactory
{
    public const string MartingaleTag = "video:martingale:v1";
    public const string PatternManualTag = "video:pattern-manual:v1";
    public const string SequenceTag = "video:sequence:v1:";
    public const string ClientTag = "client-cases:v1:";

    public static bool IsVideoMode(StrategyDefinition? definition) =>
        definition?.RuleCondition is MartingaleTag or PatternManualTag ||
        definition?.RuleCondition?.StartsWith(SequenceTag, StringComparison.Ordinal) == true ||
        definition?.RuleCondition?.StartsWith(ClientTag, StringComparison.Ordinal) == true;

    public static StrategyDefinition Client(string mode, BetSide first, IReadOnlyList<decimal> stakes,
        decimal runLimit = 45, int singleWins = 7) => new(
        mode, ClientTag + mode,
        new(first.ToString().ToUpperInvariant(), AmountRuleKind.Set, stakes),
        new("CLIENT", AmountRuleKind.ResetSequence, [runLimit]),
        new("CLIENT", AmountRuleKind.NextSequence, [singleWins]),
        null, null, null, null, 0);

    public static StrategyDefinition Martingale(BetSide first, decimal unit, decimal multiplier = 2m) => new(
        "Martingale", MartingaleTag,
        new(first.ToString().ToUpperInvariant(), AmountRuleKind.Set, [unit]),
        new("WINNER", AmountRuleKind.ResetAmount, [unit]),
        new("WINNER", AmountRuleKind.MultiplyAmount, [multiplier]),
        null, null, null, null, 0);

    public static StrategyDefinition PatternManual(BetSide? first, decimal unit) => new(
        "PATTERN", PatternManualTag,
        new(first?.ToString().ToUpperInvariant() ?? "PATTERN", AmountRuleKind.Set, [unit]),
        new("PATTERN", AmountRuleKind.Pattern, [2m]),
        new("PATTERN", AmountRuleKind.Pattern, [2m]),
        null, null, null, null, 0);

    public static StrategyDefinition Sequence(IReadOnlyList<BetSide> sides, IReadOnlyList<decimal> stakes) => new(
        "Sequence", SequenceTag + string.Join(',', sides.Select(s => s == BetSide.Player ? "P" : "B")),
        new("VIDEO_SEQUENCE", AmountRuleKind.Set, stakes),
        new("VIDEO_SEQUENCE", AmountRuleKind.ResetSequence, [1m]),
        new("VIDEO_SEQUENCE", AmountRuleKind.NextSequence, [1m]),
        null, null, null, null, 0);
}
