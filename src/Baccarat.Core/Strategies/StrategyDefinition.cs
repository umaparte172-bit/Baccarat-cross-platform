namespace Baccarat.Core.Strategies;

public enum AmountRuleKind
{
    Set,
    ResetAmount,
    MultiplyAmount,
    ResetSequence,
    NextSequence,
    Grinding,
    Pattern
}

public sealed record StrategyRule(
    string Selector,
    AmountRuleKind AmountRule,
    IReadOnlyList<decimal> Values);

public sealed record StrategyDefinition(
    string Name,
    string? RuleCondition,
    StrategyRule Initial,
    StrategyRule AfterWin,
    StrategyRule AfterLoss,
    decimal? StopProfit,
    decimal? StopLoss,
    int? StopAfterConsecutiveLosses,
    int? PauseAfterConsecutiveLosses,
    int PauseRounds);

public static class StrategyDefinitionValidator
{
    public static IReadOnlyList<string> Validate(StrategyDefinition? definition)
    {
        if (definition is null) return ["A strategy definition is required."];

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.Name)) errors.Add("Strategy name is required.");
        if (definition.Name.Length > 100) errors.Add("Strategy name must not exceed 100 characters.");
        ValidateRule(definition.Initial, "Initial", errors);
        ValidateRule(definition.AfterWin, "Win", errors);
        ValidateRule(definition.AfterLoss, "Loss", errors);
        if (definition.Initial.AmountRule != AmountRuleKind.Set)
            errors.Add("The initial amount rule must be Set.");
        if (definition.StopProfit is <= 0) errors.Add("Stop profit must be positive when supplied.");
        if (definition.StopLoss is <= 0) errors.Add("Stop loss must be positive when supplied.");
        if (definition.StopAfterConsecutiveLosses is <= 0)
            errors.Add("Consecutive-loss stop count must be positive when supplied.");
        if (definition.PauseAfterConsecutiveLosses is <= 0)
            errors.Add("Consecutive-loss pause count must be positive when supplied.");
        if (definition.PauseRounds < 0)
            errors.Add("Pause rounds cannot be negative.");
        if (definition.PauseRounds > 0 && definition.PauseAfterConsecutiveLosses is null)
            errors.Add("A pause trigger is required when pause rounds are supplied.");
        if (definition.PauseAfterConsecutiveLosses is not null && definition.PauseRounds == 0)
            errors.Add("Pause rounds are required when a pause trigger is supplied.");
        return errors;
    }

    private static void ValidateRule(StrategyRule rule, string label, ICollection<string> errors)
    {
        if (rule is null)
        {
            errors.Add($"{label} rule is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(rule.Selector)) errors.Add($"{label} selector is required.");
        if (rule.Selector.Length > 200) errors.Add($"{label} selector must not exceed 200 characters.");
        if (rule.Values.Count == 0) errors.Add($"{label} amount value is required.");
        if (rule.Values.Count > 100) errors.Add($"{label} amount sequence is too long.");
        if (rule.Values.Any(value => value <= 0)) errors.Add($"{label} amount values must be positive.");
    }
}
