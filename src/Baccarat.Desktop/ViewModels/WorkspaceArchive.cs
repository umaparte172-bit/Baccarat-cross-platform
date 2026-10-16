using System.Text.Json;
using Baccarat.Core;
using Baccarat.Core.Strategies;

namespace Baccarat.Desktop.ViewModels;

public sealed record WorkspaceArchive(int Version, Guid? Selected, SessionArchive[] Sessions);
public sealed record SessionArchive(Guid Id, string Title, string Strategy, string BaseUnitDraft, string BankrollDraft,
    decimal BaseUnit, decimal StartingBankroll, bool BankerPair, bool PlayerPair, string AppliedFingerprint,
    Dictionary<string, Dictionary<string, string>> Drafts, int StrategyRun, StrategyEngineArchive? Engine,
    DashboardHand[] Hands, int[] ShoeStarts);

public sealed partial class DashboardSession
{
    internal SessionArchive ExportArchive() => new(Id, Title, Strategy.Name, BaseUnitDraft, BankrollDraft,
        BaseUnit, StartingBankroll, BankerPairDraft, PlayerPairDraft, appliedFingerprint,
        drafts.ToDictionary(d => d.Key, d => new Dictionary<string, string>(d.Value)), strategyGeneration,
        Engine?.ExportArchive(), hands.ToArray(), shoeStarts.ToArray());

    internal static DashboardSession RestoreArchive(SessionArchive archive)
    {
        static void Require(bool value) { if (!value) throw new InvalidDataException("Invalid session archive."); }
        Require(archive.Id != Guid.Empty && !string.IsNullOrWhiteSpace(archive.Title) && archive.Title.Length <= 100);
        Require(archive.BaseUnit is > 0 and <= 1000000000m && archive.StartingBankroll is > 0 and <= 1000000000m);
        Require(archive.BaseUnitDraft is { Length: <= 512 } && archive.BankrollDraft is { Length: <= 512 });
        Require(archive.AppliedFingerprint is { Length: <= 8192 });
        Require(archive.Hands is { Length: <= 10000 } && archive.ShoeStarts is { Length: > 0 and <= 10001 });
        Require(archive.StrategyRun >= 0 && archive.Drafts is not null && archive.Drafts.Count == Strategies.Count);
        var session = new DashboardSession(1)
        {
            Id = archive.Id,
            Title = archive.Title,
            BaseUnitDraft = archive.BaseUnitDraft,
            BankrollDraft = archive.BankrollDraft,
            BaseUnit = archive.BaseUnit,
            StartingBankroll = archive.StartingBankroll,
            BankerPairDraft = archive.BankerPair,
            PlayerPairDraft = archive.PlayerPair,
            appliedFingerprint = archive.AppliedFingerprint,
            strategyGeneration = archive.StrategyRun
        };
        session.SelectStrategy(archive.Strategy);
        foreach (var strategy in Strategies)
        {
            Require(archive.Drafts!.TryGetValue(strategy.Name, out var values) && values is not null && values.Count == strategy.Fields.Count);
            foreach (var field in strategy.Fields)
            {
                Require(values!.TryGetValue(field.Key, out var value) && value is { Length: <= 512 });
                session.drafts[strategy.Name][field.Key] = value!;
            }
        }
        session.shoeStarts.Clear();
        var previousStart = -1;
        foreach (var start in archive.ShoeStarts!)
        {
            Require(start > previousStart && start <= archive.Hands!.Length && (previousStart != -1 || start == 0));
            session.shoeStarts.Add(start);
            previousStart = start;
        }
        decimal total = 0, runProfit = 0;
        var generation = 0;
        var runRound = 0;
        foreach (var hand in archive.Hands!)
        {
            Require(hand?.Round is not null && hand.Round.Number == session.hands.Count + 1 &&
                Enum.IsDefined(typeof(Outcome), hand.Round.Outcome) && hand.Round.RecordedAt != default);
            Require(hand!.StrategyGeneration >= generation && hand.StrategyGeneration <= archive.StrategyRun);
            if (hand.StrategyGeneration != generation) { generation = hand.StrategyGeneration; runRound = 0; runProfit = 0; }
            if (hand.StrategyResult is { } result)
            {
                Require(generation > 0 && result.Outcome == hand.Round.Outcome && result.Number == ++runRound);
                decimal change = 0;
                if (result.WasBet)
                {
                    Require(result.Side is { } side && Enum.IsDefined(typeof(BetSide), side) && result.Stake is > 0 and <= StrategyDefinitionValidator.MaximumSupportedAmount);
                    bool? won = result.Outcome == Outcome.Tie ? null :
                        result.Side == BetSide.Player ? result.Outcome == Outcome.Player : result.Outcome == Outcome.Banker;
                    Require(won == result.Won);
                    change = won is null ? 0 : won == false ? -result.Stake!.Value :
                        result.Stake!.Value * (result.Side == BetSide.Banker ? 0.95m : 1m);
                }
                else Require(result.Side is null && result.Stake is null && result.Won is null && result.Selector is null);
                runProfit += change;
                Require(result.ProfitChange == change && result.TotalProfit == runProfit);
                total += change;
            }
            Require(hand.SessionProfit == total);
            session.hands.Add(hand);
        }
        if (archive.Engine is not null)
        {
            session.Engine = StrategySessionEngine.RestoreArchive(archive.Engine);
            session.LoadedDefinition = archive.Engine.Definition;
            Require(archive.StrategyRun > 0 && session.Engine.Rounds.SequenceEqual(session.hands
                .Where(h => h.StrategyGeneration == archive.StrategyRun).Select(h => h.StrategyResult)));
        }
        else Require(archive.StrategyRun == 0 || session.hands.All(h =>
            h.StrategyGeneration != archive.StrategyRun || h.StrategyResult is null));
        return session;
    }
}

public sealed partial class DashboardWorkspace
{
    public const int MaximumArchiveBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions ArchiveOptions = new() { MaxDepth = 32 };

    public byte[] Serialize()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new WorkspaceArchive(1, Selected?.Id,
            sessions.Select(s => s.ExportArchive()).ToArray()), ArchiveOptions);
        if (data.Length > MaximumArchiveBytes) throw new InvalidDataException("Workspace exceeds the 32 MiB save limit. Export records before continuing.");
        return data;
    }

    public static DashboardWorkspace Deserialize(byte[] data)
    {
        try
        {
            if (data.Length > MaximumArchiveBytes) throw new InvalidDataException("Workspace exceeds the supported size.");
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 32 });
            CheckShape(document.RootElement, typeof(WorkspaceArchive));
            var archive = document.RootElement.Deserialize<WorkspaceArchive>(ArchiveOptions)!;
            if (archive.Version != 1) throw new InvalidDataException("Unsupported workspace version. This file must not be overwritten.");
            if (archive.Sessions is null || archive.Sessions.Length > 100 || archive.Sessions.Any(s => s?.Hands is null))
                throw new InvalidDataException("Workspace session/hand limit exceeded.");
            var workspace = new DashboardWorkspace();
            foreach (var session in archive.Sessions) workspace.sessions.Add(DashboardSession.RestoreArchive(session));
            if (workspace.sessions.Select(s => s.Id).Distinct().Count() != workspace.sessions.Count)
                throw new InvalidDataException("Duplicate session identifiers.");
            workspace.Selected = workspace.sessions.SingleOrDefault(s => s.Id == archive.Selected);
            if ((workspace.sessions.Count > 0 && workspace.Selected is null) || (workspace.sessions.Count == 0 && archive.Selected is not null))
                throw new InvalidDataException("Invalid selected session.");
            return workspace;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { throw new InvalidDataException("Workspace contents failed validation. Original files were not changed.", ex); }
    }

    // Explicit complete shapes also work on the macOS 11/net6 build, without relying
    // on newer serializer options that silently accept unknown/missing properties.
    private static void CheckShape(JsonElement element, Type type)
    {
        if (element.ValueKind == JsonValueKind.Null) return; // Semantic validation follows.
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (element.ValueKind == JsonValueKind.String && element.GetString()!.Length > 16384)
            throw new InvalidDataException("Workspace text exceeds the supported limit.");
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
        {
            var itemType = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
            foreach (var item in element.EnumerateArray()) CheckShape(item, itemType);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            var dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .ToDictionary(p => p.Name, p => p.PropertyType);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name) || (!dictionary && !properties.ContainsKey(property.Name)))
                    throw new InvalidDataException("Duplicate or unknown workspace property.");
                CheckShape(property.Value, dictionary ? type.GetGenericArguments()[1] : properties[property.Name]);
            }
            if (!dictionary && seen.Count != properties.Count) throw new InvalidDataException("Missing workspace property.");
        }
    }
}
