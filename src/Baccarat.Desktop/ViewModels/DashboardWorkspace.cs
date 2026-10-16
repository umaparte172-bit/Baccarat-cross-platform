using System.Collections.ObjectModel;
using System.Globalization;
using Baccarat.Core;
using Baccarat.Core.Strategies;

namespace Baccarat.Desktop.ViewModels;

// The dashboard owns the ledger; imported calculations use the existing core engine.
public sealed record DashboardField(string Key, string Label, string Hint, string Kind = "amount");
public sealed record DashboardStrategy(string Name, IReadOnlyList<DashboardField> Fields);
public sealed record DashboardHand(Round Round, bool BankerPair, bool PlayerPair,
    StrategyRound? StrategyResult = null, int StrategyGeneration = 0, decimal SessionProfit = 0);

public sealed partial class DashboardSession
{
    public static IReadOnlyList<DashboardStrategy> Strategies { get; } = Array.AsReadOnly(new[]
    {
        new DashboardStrategy("PATTERN", new DashboardField[] {
            new("first", "First bet on", "P or B", "side"),
            new("limit", "Run ends at bet (units)", "Not verified"),
            new("wins", "Single wins before pause", "Not verified", "integer") }),
        new DashboardStrategy("Martingale", new DashboardField[] {
            new("first", "First bet on", "P or B", "side"),
            new("start", "Starting stake (units)", "1"),
            new("multiplier", "Multiply after a loss", "2") }),
        new DashboardStrategy("Grinding", new DashboardField[] {
            new("first", "First bet on", "P or B", "side"), new("grind", "Grind unit (units)", "e.g. 2") }),
        new DashboardStrategy("Sequence", new DashboardField[] {
            new("sides", "Sides", "e.g. B,P,P,B", "sides"), new("stakes", "Stakes (units)", "e.g. 1,2,3", "amounts") }),
        new DashboardStrategy("Odd Even", new DashboardField[] {
            new("odd", "Odd results on", "P or B", "side"), new("stakes", "Stakes (units)", "e.g. 2,4,6", "amounts") }),
        new DashboardStrategy("ODD Diff", new DashboardField[] { new("stakes", "Stakes (units)", "e.g. 1,2,3", "amounts") }),
        new DashboardStrategy("ODD Looser", new DashboardField[] { new("stakes", "Stakes (units)", "e.g. 1,2,3", "amounts") }),
        new DashboardStrategy("ODD is God", new DashboardField[] { new("stakes", "Stakes (units)", "e.g. 5,10", "amounts") })
    });

    private readonly List<DashboardHand> hands = new();
    private readonly List<int> shoeStarts = new() { 0 };
    private readonly Dictionary<string, Dictionary<string, string>> drafts = new();
    private string appliedFingerprint = "";
    private int strategyGeneration;
    public int StrategyRun => strategyGeneration;
    public StrategySessionEngine? Engine { get; private set; }
    public StrategyDefinition? LoadedDefinition { get; private set; }
    public bool IsVideoMode => VideoStrategyFactory.IsVideoMode(LoadedDefinition);
    public string ActiveStrategyName => Engine?.StrategyName ?? Strategy.Name;
    public decimal Profit => hands.Count == 0 ? 0 : hands[^1].SessionProfit;
    public decimal Balance => StartingBankroll + Profit;
    public int CurrentShoe => shoeStarts.Count;
    public int ShoeHandCount => hands.Count - shoeStarts[^1];
    public int ShoeRevision { get; private set; }
    public bool CanUndo => hands.Count > 0 || CurrentShoe > 1;
    public bool CanStartShoe => ShoeHandCount > 0 && hands.Count < 10000;
    public IEnumerable<DashboardHand> ShoeHands => hands.Skip(shoeStarts[^1]);
    public bool CanRecord => hands.Count < 10000 &&
        (Engine is null || Engine.Recommendation.State is StrategyEngineState.Ready or StrategyEngineState.Paused);
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Title { get; private set; }
    public IReadOnlyList<DashboardHand> Hands { get; }
    public DashboardStrategy Strategy { get; private set; } = Strategies[0];
    public string BaseUnitDraft { get; set; } = "1";
    public string BankrollDraft { get; set; } = "1000";
    public decimal BaseUnit { get; private set; } = 1;
    public decimal StartingBankroll { get; private set; } = 1000;
    public bool BankerPairDraft { get; set; }
    public bool PlayerPairDraft { get; set; }
    public bool HasUnappliedSettings => Fingerprint() != appliedFingerprint;
    public string SidebarLabel => $"{Title}\n{ActiveStrategyName} · {hands.Count} hands";

    internal DashboardSession(int number)
    {
        Title = $"Session {number} · {DateTimeOffset.Now:MMM d, HH:mm}";
        Hands = hands.AsReadOnly();
        foreach (var strategy in Strategies)
            drafts[strategy.Name] = strategy.Fields.ToDictionary(field => field.Key, _ => "");
        appliedFingerprint = Fingerprint();
    }

    public void SelectStrategy(string name)
    {
        var selected = Strategies.Single(s => s.Name == name);
        if (selected != Strategy && IsVideoMode) BeginDraftRun();
        Strategy = selected;
    }

    public void BeginDraftRun()
    {
        if (Engine is null) return;
        Engine = null;
        LoadedDefinition = null;
        strategyGeneration++;
        // Previous settlements are immutable; a new draft run starts with no
        // pending recommendation or automation approval.
    }
    public string GetField(string key) => drafts[Strategy.Name][key];
    public void SetField(string key, string value)
    {
        if (!drafts[Strategy.Name].ContainsKey(key)) throw new ArgumentException("Unknown setting", nameof(key));
        drafts[Strategy.Name][key] = value;
    }

    public bool ApplySettings(out string message)
    {
        if (!PositiveAmount(BaseUnitDraft, out var unit) || !PositiveAmount(BankrollDraft, out var bankroll))
        {
            message = "Base unit and starting bankroll must be positive numbers up to 1,000,000,000. Use a decimal point, without separators.";
            return false;
        }
        if (hands.Count > 0 && bankroll != StartingBankroll)
        {
            message = "Starting bankroll cannot change after recording results. Create a new session for a different bankroll.";
            return false;
        }
        if (Engine is not null && !IsVideoMode)
        {
            message = "An imported strategy is active. Return to strategy drafts to start a new run with dashboard settings.";
            return false;
        }
        foreach (var field in Strategy.Fields)
        {
            var value = GetField(field.Key).Trim();
            if (value.Length == 0) continue; // Empty means unspecified, not an invented algorithm default.
            var parts = value.Split(',').Select(part => part.Trim()).ToArray();
            var valid = field.Kind switch
            {
                "side" => value is "P" or "B",
                "sides" => parts.Length <= 64 && parts.All(part => part is "P" or "B"),
                "amounts" => parts.Length <= 64 && parts.All(part => PositiveAmount(part, out _)),
                "integer" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is > 0 and <= 10000,
                _ => PositiveAmount(value, out _)
            };
            if (!valid)
            {
                message = $"Check {field.Label}: {field.Hint}. Lists accept up to 64 entries; amounts must be positive.";
                return false;
            }
        }
        if (IsVideoMode && !HasUnappliedSettings)
        {
            message = "These settings are already active; the current strategy run was not reset.";
            return true;
        }
        StrategyDefinition? video = null;
        string DefaultField(string key, string fallback) => string.IsNullOrWhiteSpace(GetField(key)) ? fallback : GetField(key).Trim();
        decimal Amount(string key, string fallback) => decimal.Parse(DefaultField(key, fallback), CultureInfo.InvariantCulture);
        var first = Strategy.Fields.Any(f => f.Key == "first") ? GetField("first") : "";
        BetSide? firstSide = first == "P" ? BetSide.Player : first == "B" ? BetSide.Banker : null;
        if (Strategy.Name == "Martingale")
        {
            if (!PositiveAmount(DefaultField("start", "1"), out var start) || !PositiveAmount(DefaultField("multiplier", "2"), out var multiplier) ||
                start * unit > StrategyDefinitionValidator.MaximumSupportedAmount)
            {
                message = "Enter a positive starting stake and loss multiplier; starting stake times base unit must be within the supported amount.";
                return false;
            }
            video = VideoStrategyFactory.Martingale(firstSide ?? BetSide.Player, start * unit, multiplier);
        }
        else if (Strategy.Name == "PATTERN")
            video = VideoStrategyFactory.Client("PATTERN", firstSide ?? BetSide.Player, [unit],
                Amount("limit", "45"), (int)Amount("wins", "7"));
        else if (Strategy.Name == "Grinding")
            video = VideoStrategyFactory.Client("Grinding", firstSide ?? BetSide.Player, [Amount("grind", "1") * unit]);
        else if (Strategy.Name == "Sequence")
        {
            var sides = DefaultField("sides", "B,P,P").Split(',').Select(p => p.Trim() == "P" ? BetSide.Player : BetSide.Banker).ToArray();
            var stakes = DefaultField("stakes", "1,2,3").Split(',').Select(p => decimal.Parse(p.Trim(), CultureInfo.InvariantCulture) * unit).ToArray();
            if (stakes.Any(value => value > StrategyDefinitionValidator.MaximumSupportedAmount))
            {
                message = "A sequence stake exceeds the supported amount after multiplying by the base unit.";
                return false;
            }
            video = VideoStrategyFactory.Sequence(sides, stakes);
        }
        else
        {
            var stakes = DefaultField("stakes", "1,2,3").Split(',').Select(p => decimal.Parse(p.Trim(), CultureInfo.InvariantCulture) * unit).ToArray();
            var oddSide = Strategy.Name == "Odd Even" && DefaultField("odd", "B") == "P" ? BetSide.Player : BetSide.Banker;
            video = VideoStrategyFactory.Client(Strategy.Name, oddSide, stakes);
        }
        if (video is not null && StrategyDefinitionValidator.Validate(video).Count != 0)
        {
            message = "The scaled stake or setting exceeds the supported strategy limits.";
            return false;
        }
        BaseUnit = unit;
        StartingBankroll = bankroll;
        appliedFingerprint = Fingerprint();
        if (video is not null) LoadStrategy(video);
        message = $"{Strategy.Name} client-reference run is active. Empty settings use defaults. Changing applied settings starts a new run.";
        return true;
    }

    private static bool PositiveAmount(string value, out decimal amount) =>
        decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount is > 0 and <= 1000000000m;

    private string Fingerprint() => string.Join("\n", new[] { Strategy.Name, BaseUnitDraft, BankrollDraft }
        .Concat(Strategy.Fields.Select(field => GetField(field.Key))));

    public bool Record(Outcome outcome)
    {
        if (hands.Count >= 10000) return false;
        if (!Enum.IsDefined(typeof(Outcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        // The same existing engine handles entries from either screen. Never invent a
        // wager while its protected selector or amount still needs manual resolution.
        var strategyResult = Engine?.Record(outcome);
        hands.Add(new DashboardHand(new Round(hands.Count + 1, outcome, DateTimeOffset.UtcNow), BankerPairDraft, PlayerPairDraft, strategyResult, strategyGeneration,
            Profit + (strategyResult?.ProfitChange ?? 0)));
        BankerPairDraft = PlayerPairDraft = false;
        return true;
    }

    public bool Undo()
    {
        if (CurrentShoe > 1 && ShoeHandCount == 0)
        {
            shoeStarts.RemoveAt(shoeStarts.Count - 1);
            ShoeRevision++;
            return true;
        }
        if (hands.Count == 0) return false;
        var last = hands[^1];
        if (last.StrategyResult is not null && last.StrategyGeneration == strategyGeneration)
            Engine?.Undo();
        hands.RemoveAt(hands.Count - 1);
        return true;
    }

    public int Count(Outcome outcome) => hands.Count(h => h.Round.Outcome == outcome);
    public bool NewShoe()
    {
        if (!CanStartShoe) return false;
        shoeStarts.Add(hands.Count);
        BankerPairDraft = PlayerPairDraft = false;
        ShoeRevision++;
        return true;
    }
    public void LoadStrategy(StrategyDefinition definition)
    {
        var replacement = new StrategySessionEngine(definition);
        Engine = replacement;
        LoadedDefinition = definition;
        strategyGeneration++;
    }

    public void ResetStrategy()
    {
        Engine?.Reset();
        if (Engine is not null) strategyGeneration++;
        // Previous recorded hands and their settlements remain immutable. Reset
        // starts a new strategy run; it is not permission to erase session history.
    }

    public void Clear()
    {
        hands.Clear();
        shoeStarts.Clear();
        shoeStarts.Add(0);
        ShoeRevision++;
        BankerPairDraft = PlayerPairDraft = false;
        ResetStrategy();
    }

    public int ShoeOf(DashboardHand hand) => shoeStarts.Count(start => start < hand.Round.Number);
    public string ToCsv() => "Round,Outcome,BankerPair,PlayerPair,RecordedAt,Side,Stake,ProfitChange,SessionProfit,Balance,Shoe,ShoeHand\n" + string.Join("\n",
        hands.Select(h => FormattableString.Invariant($"{h.Round.Number},{h.Round.Outcome},{h.BankerPair},{h.PlayerPair},{h.Round.RecordedAt:O},{h.StrategyResult?.Side},{h.StrategyResult?.Stake},{h.StrategyResult?.ProfitChange},{h.SessionProfit},{StartingBankroll + h.SessionProfit},{ShoeOf(h)},{h.Round.Number - shoeStarts[ShoeOf(h) - 1]}")));
}

public sealed partial class DashboardWorkspace
{
    private readonly List<DashboardSession> sessions = new();
    public ReadOnlyCollection<DashboardSession> Sessions { get; }
    public DashboardSession? Selected { get; private set; }
    public DashboardWorkspace() => Sessions = sessions.AsReadOnly();
    public bool CreateSession()
    {
        if (sessions.Count >= 100) return false;
        Selected = new DashboardSession(sessions.Count + 1);
        sessions.Add(Selected);
        return true;
    }
    public void Select(DashboardSession session)
    {
        if (!sessions.Contains(session)) throw new ArgumentException("Session does not belong to this workspace", nameof(session));
        Selected = session;
    }
}
