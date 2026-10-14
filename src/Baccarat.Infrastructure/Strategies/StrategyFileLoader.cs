using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baccarat.Core.Strategies;

namespace Baccarat.Infrastructure.Strategies;

public sealed class StrategyFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class StrategyFileLoader
{
    public const int MaximumFileBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16
    };

    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "strategy_name", "rule_condition", "initial_condition", "win_condition", "loss_condition",
        "stop_profit", "stop_loss", "stop_consecutive_loss", "pause_consecutive_loss"
    };
    private static readonly HashSet<string> ConditionProperties = new(StringComparer.Ordinal) { "bet_on", "amount" };
    private static readonly HashSet<string> AmountProperties = new(StringComparer.Ordinal) { "type", "value" };
    private static readonly HashSet<string> StopProperties = new(StringComparer.Ordinal) { "action", "count" };
    private static readonly HashSet<string> PauseProperties = new(StringComparer.Ordinal) { "pause_bets", "count" };

    public async ValueTask<StrategyDefinition> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("The strategy stream must be readable.", nameof(stream));

        try
        {
            await using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > MaximumFileBytes)
                    throw new StrategyFormatException($"Strategy files cannot exceed {MaximumFileBytes} bytes.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            if (buffer.Length == 0) throw new StrategyFormatException("The strategy file is empty.");
            cancellationToken.ThrowIfCancellationRequested();
            using var parsed = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(parsed.RootElement);
            RejectUnknownProperties(parsed.RootElement, RootProperties, "strategy");
            RejectCondition(parsed.RootElement, "initial_condition");
            RejectCondition(parsed.RootElement, "win_condition");
            RejectCondition(parsed.RootElement, "loss_condition");
            RejectNestedObject(parsed.RootElement, "stop_consecutive_loss", StopProperties);
            RejectNestedObject(parsed.RootElement, "pause_consecutive_loss", PauseProperties);
            var document = parsed.RootElement.Deserialize<StrategyDocument>(JsonOptions) ??
                throw new StrategyFormatException("The strategy file does not contain an object.");
            var definition = Convert(document);
            var errors = StrategyDefinitionValidator.Validate(definition);
            if (errors.Count != 0) throw new StrategyFormatException(string.Join(" ", errors));
            return definition;
        }
        catch (JsonException exception)
        {
            throw new StrategyFormatException("The strategy file is not valid supported JSON.", exception);
        }
        catch (FormatException exception)
        {
            throw new StrategyFormatException(exception.Message, exception);
        }
        catch (OverflowException exception)
        {
            throw new StrategyFormatException("A strategy number is outside the supported range.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new StrategyFormatException($"Duplicate JSON property '{property.Name}' is not allowed.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static void RejectCondition(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var condition) || condition.ValueKind == JsonValueKind.Null) return;
        RejectUnknownProperties(condition, ConditionProperties, name);
        RejectNestedObject(condition, "amount", AmountProperties);
    }

    private static void RejectNestedObject(
        JsonElement parent,
        string name,
        IReadOnlySet<string> allowedProperties)
    {
        if (!parent.TryGetProperty(name, out var nested) || nested.ValueKind == JsonValueKind.Null) return;
        RejectUnknownProperties(nested, allowedProperties, name);
    }

    private static void RejectUnknownProperties(
        JsonElement element,
        IReadOnlySet<string> allowedProperties,
        string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new StrategyFormatException($"{context} must be a JSON object.");

        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
                throw new StrategyFormatException($"Unknown JSON property '{property.Name}' is not allowed in {context}.");
        }
    }

    private static StrategyDefinition Convert(StrategyDocument document) => new(
        Required(document.StrategyName, "strategy_name"),
        EmptyToNull(document.RuleCondition),
        ConvertRule(document.InitialCondition, "initial_condition"),
        ConvertRule(document.WinCondition, "win_condition"),
        ConvertRule(document.LossCondition, "loss_condition"),
        OptionalPositiveDecimal(document.StopProfit, "stop_profit"),
        OptionalPositiveDecimal(document.StopLoss, "stop_loss"),
        ConvertStop(document.StopConsecutiveLoss),
        OptionalPositiveInt(document.PauseConsecutiveLoss?.Count, "pause_consecutive_loss.count"),
        OptionalPositiveInt(document.PauseConsecutiveLoss?.PauseBets, "pause_consecutive_loss.pause_bets") ?? 0);

    private static StrategyRule ConvertRule(ConditionDocument? condition, string name)
    {
        if (condition is null) throw new FormatException($"{name} is required.");
        if (condition.Amount is null) throw new FormatException($"{name}.amount is required.");
        return new(
            Required(condition.BetOn, $"{name}.bet_on"),
            ParseAmountRule(Required(condition.Amount.Type, $"{name}.amount.type")),
            ParseValues(Required(condition.Amount.Value, $"{name}.amount.value"), $"{name}.amount.value"));
    }

    private static int? ConvertStop(StopConsecutiveLossDocument? stop)
    {
        if (stop is null) return null;
        var count = OptionalPositiveInt(stop.Count, "stop_consecutive_loss.count");
        if (count is not null && !string.Equals(stop.Action?.Trim(), "STOP BET", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("stop_consecutive_loss.action must be STOP BET when a count is supplied.");
        return count;
    }

    private static AmountRuleKind ParseAmountRule(string value) => value.Trim().ToUpperInvariant() switch
    {
        "SET" => AmountRuleKind.Set,
        "RESET AMOUNT" => AmountRuleKind.ResetAmount,
        "MULTIPLY AMOUNT" => AmountRuleKind.MultiplyAmount,
        "RESET SEQUENCE" => AmountRuleKind.ResetSequence,
        "NEXT SEQUENCE" => AmountRuleKind.NextSequence,
        "GRINDING" => AmountRuleKind.Grinding,
        "PATTERN" => AmountRuleKind.Pattern,
        _ => throw new FormatException($"Unsupported amount rule '{value}'.")
    };

    private static IReadOnlyList<decimal> ParseValues(string value, string name)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException($"{name} must contain a positive number.");
        if (parts.Length > 100) throw new FormatException($"{name} contains too many values.");

        var result = new decimal[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!decimal.TryParse(parts[index], NumberStyles.Number, CultureInfo.InvariantCulture, out result[index]) ||
                result[index] <= 0)
                throw new FormatException($"{name} contains an invalid positive number.");
        }

        return result;
    }

    private static decimal? OptionalPositiveDecimal(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new FormatException($"{name} must be a positive number when supplied.");
        return parsed;
    }

    private static int? OptionalPositiveInt(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new FormatException($"{name} must be a positive whole number when supplied.");
        return parsed;
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new FormatException($"{name} is required.") : value.Trim();

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record StrategyDocument
    {
        [JsonPropertyName("strategy_name")]
        public string? StrategyName { get; init; }

        [JsonPropertyName("rule_condition")]
        public string? RuleCondition { get; init; }

        [JsonPropertyName("initial_condition")]
        public ConditionDocument? InitialCondition { get; init; }

        [JsonPropertyName("win_condition")]
        public ConditionDocument? WinCondition { get; init; }

        [JsonPropertyName("loss_condition")]
        public ConditionDocument? LossCondition { get; init; }

        [JsonPropertyName("stop_profit")]
        public string? StopProfit { get; init; }

        [JsonPropertyName("stop_loss")]
        public string? StopLoss { get; init; }

        [JsonPropertyName("stop_consecutive_loss")]
        public StopConsecutiveLossDocument? StopConsecutiveLoss { get; init; }

        [JsonPropertyName("pause_consecutive_loss")]
        public PauseConsecutiveLossDocument? PauseConsecutiveLoss { get; init; }
    }

    private sealed record ConditionDocument
    {
        [JsonPropertyName("bet_on")]
        public string? BetOn { get; init; }

        [JsonPropertyName("amount")]
        public AmountDocument? Amount { get; init; }
    }

    private sealed record AmountDocument
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    private sealed record StopConsecutiveLossDocument
    {
        [JsonPropertyName("action")]
        public string? Action { get; init; }

        [JsonPropertyName("count")]
        public string? Count { get; init; }
    }

    private sealed record PauseConsecutiveLossDocument
    {
        [JsonPropertyName("pause_bets")]
        public string? PauseBets { get; init; }

        [JsonPropertyName("count")]
        public string? Count { get; init; }
    }
}
