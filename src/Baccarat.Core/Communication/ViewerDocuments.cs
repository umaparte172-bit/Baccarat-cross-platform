using Baccarat.Core.Automation;
using Baccarat.Core.Strategies;

namespace Baccarat.Core.Communication;

public readonly record struct DocumentVersion(int Major, int Minor)
{
    public static DocumentVersion Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("A document version is required.");
        var parts = value.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            major < 0 || minor < 0)
            throw new FormatException("Document versions must use non-negative major.minor notation.");
        return new(major, minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}

public static class ViewerDocumentContract
{
    public static DocumentVersion EnvelopeVersion { get; } = new(1, 0);
    public static DocumentVersion SessionSnapshotVersion { get; } = new(1, 0);
    public static DocumentVersion StrategySnapshotVersion { get; } = new(1, 0);
    public static DocumentVersion AutomationSnapshotVersion { get; } = new(1, 0);
    public static DocumentVersion StatusVersion { get; } = new(1, 0);

    public const string SessionSnapshotType = "session.snapshot";
    public const string StrategySnapshotType = "strategy.snapshot";
    public const string AutomationSnapshotType = "automation.snapshot";
    public const string StatusType = "communication.status";
}

public abstract record ViewerPayload;

public sealed record SessionSnapshotPayload(
    int RoundCount,
    int PlayerCount,
    int BankerCount,
    int TieCount) : ViewerPayload;

public sealed record StrategySnapshotPayload(
    string StrategyName,
    decimal Profit,
    int ObservedRounds,
    int Bets,
    StrategyEngineState State,
    string Recommendation) : ViewerPayload;

public sealed record CommunicationStatusPayload(string State, string Message) : ViewerPayload;

public sealed record AutomationSnapshotPayload(
    AutomationRunState State,
    bool AutoBetEnabled,
    bool IsSimulation,
    decimal SessionStake,
    int AcceptedBets,
    string Message) : ViewerPayload;

public sealed record ViewerDocument(
    DocumentVersion EnvelopeVersion,
    string DocumentType,
    DocumentVersion DocumentVersion,
    Guid DocumentId,
    long Sequence,
    DateTimeOffset CreatedAt,
    ViewerPayload Payload);

public static class ViewerDocumentValidator
{
    public static IReadOnlyList<string> Validate(ViewerDocument? document)
    {
        if (document is null) return ["A viewer document is required."];
        var errors = new List<string>();
        if (document.EnvelopeVersion != ViewerDocumentContract.EnvelopeVersion)
            errors.Add($"Unsupported envelope version {document.EnvelopeVersion}.");
        if (document.DocumentId == Guid.Empty) errors.Add("Document ID is required.");
        if (document.Sequence <= 0) errors.Add("Document sequence must be positive.");
        if (document.CreatedAt.Offset != TimeSpan.Zero) errors.Add("Document timestamp must use UTC.");

        switch (document.Payload)
        {
            case SessionSnapshotPayload session:
                ValidateContract(document, ViewerDocumentContract.SessionSnapshotType,
                    ViewerDocumentContract.SessionSnapshotVersion, errors);
                if (session.RoundCount < 0 || session.PlayerCount < 0 || session.BankerCount < 0 || session.TieCount < 0)
                    errors.Add("Session counters cannot be negative.");
                if (session.PlayerCount + session.BankerCount + session.TieCount != session.RoundCount)
                    errors.Add("Session counters must equal the total round count.");
                break;
            case StrategySnapshotPayload strategy:
                ValidateContract(document, ViewerDocumentContract.StrategySnapshotType,
                    ViewerDocumentContract.StrategySnapshotVersion, errors);
                if (string.IsNullOrWhiteSpace(strategy.StrategyName) || strategy.StrategyName.Length > 100)
                    errors.Add("Strategy name is required and must not exceed 100 characters.");
                if (strategy.ObservedRounds < 0 || strategy.Bets < 0 || strategy.Bets > strategy.ObservedRounds)
                    errors.Add("Strategy round counters are invalid.");
                if (!Enum.IsDefined(typeof(StrategyEngineState), strategy.State)) errors.Add("Strategy state is invalid.");
                if (string.IsNullOrWhiteSpace(strategy.Recommendation) || strategy.Recommendation.Length > 500)
                    errors.Add("Strategy recommendation is required and must not exceed 500 characters.");
                break;
            case CommunicationStatusPayload status:
                ValidateContract(document, ViewerDocumentContract.StatusType,
                    ViewerDocumentContract.StatusVersion, errors);
                if (string.IsNullOrWhiteSpace(status.State) || status.State.Length > 50)
                    errors.Add("Communication state is required and must not exceed 50 characters.");
                if (string.IsNullOrWhiteSpace(status.Message) || status.Message.Length > 500)
                    errors.Add("Communication message is required and must not exceed 500 characters.");
                break;
            case AutomationSnapshotPayload automation:
                ValidateContract(document, ViewerDocumentContract.AutomationSnapshotType,
                    ViewerDocumentContract.AutomationSnapshotVersion, errors);
                if (!Enum.IsDefined(typeof(AutomationRunState), automation.State)) errors.Add("Automation state is invalid.");
                if (automation.SessionStake < 0 || automation.AcceptedBets < 0)
                    errors.Add("Automation counters cannot be negative.");
                if (string.IsNullOrWhiteSpace(automation.Message) || automation.Message.Length > 500)
                    errors.Add("Automation message is required and must not exceed 500 characters.");
                break;
            default:
                errors.Add("Unsupported viewer payload type.");
                break;
        }

        return errors;
    }

    private static void ValidateContract(
        ViewerDocument document,
        string expectedType,
        DocumentVersion expectedVersion,
        ICollection<string> errors)
    {
        if (!string.Equals(document.DocumentType, expectedType, StringComparison.Ordinal))
            errors.Add($"Document type must be {expectedType}.");
        if (document.DocumentVersion != expectedVersion)
            errors.Add($"Unsupported {expectedType} document version {document.DocumentVersion}.");
    }
}

public enum ViewerDocumentApplyResult
{
    Applied,
    DuplicateOrOutOfOrder,
    Invalid
}

public sealed class ViewerDocumentStore
{
    private readonly List<ViewerDocument> documents = [];
    private readonly object gate = new();
    private readonly int capacity;
    private long lastSequence;

    public ViewerDocumentStore(int capacity = 100)
    {
        if (capacity is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    public IReadOnlyList<ViewerDocument> Documents
    {
        get
        {
            lock (gate) return documents.ToArray();
        }
    }

    public ViewerDocumentApplyResult Apply(ViewerDocument document)
    {
        if (ViewerDocumentValidator.Validate(document).Count != 0) return ViewerDocumentApplyResult.Invalid;

        lock (gate)
        {
            if (document.Sequence <= lastSequence) return ViewerDocumentApplyResult.DuplicateOrOutOfOrder;
            lastSequence = document.Sequence;
            documents.Add(document);
            if (documents.Count > capacity) documents.RemoveAt(0);
            return ViewerDocumentApplyResult.Applied;
        }
    }
}
