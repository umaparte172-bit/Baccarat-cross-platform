using Baccarat.Core.Strategies;

namespace Baccarat.Core.Automation;

public enum AutomationRunState
{
    Disconnected,
    Connecting,
    Ready,
    AwaitingApproval,
    SelectingChips,
    PlacingBet,
    Recovering,
    Paused,
    Stopped,
    Faulted
}

public enum AutomationActionOutcome
{
    Accepted,
    ApprovalRequired,
    Skipped,
    Rejected,
    Indeterminate,
    Unavailable
}

public enum BetPlacementStatus
{
    Accepted,
    Rejected,
    Indeterminate
}

public sealed record AutomationSafetyPolicy(
    decimal MaximumStake,
    decimal MaximumSessionStake,
    int MaximumChipClicks,
    int MaximumReconnectAttempts,
    TimeSpan InitialReconnectDelay,
    TimeSpan MaximumReconnectDelay,
    bool RequireApproval)
{
    public static AutomationSafetyPolicy Default { get; } = new(
        10_000m,
        50_000m,
        100,
        3,
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(2),
        true);

    public void Validate()
    {
        if (MaximumStake <= 0 || MaximumStake > 10_000m)
            throw new ArgumentOutOfRangeException(nameof(MaximumStake));
        if (MaximumSessionStake < MaximumStake || MaximumSessionStake > 1_000_000m)
            throw new ArgumentOutOfRangeException(nameof(MaximumSessionStake));
        if (MaximumChipClicks is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumChipClicks));
        if (MaximumReconnectAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MaximumReconnectAttempts));
        if (InitialReconnectDelay < TimeSpan.Zero || InitialReconnectDelay > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(InitialReconnectDelay));
        if (MaximumReconnectDelay < InitialReconnectDelay || MaximumReconnectDelay > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(MaximumReconnectDelay));
    }
}

public sealed record AutomationBetRequest(
    Guid OperationId,
    BetSide Side,
    decimal Stake,
    IReadOnlyList<decimal> Chips,
    bool IsInitialBet);

public sealed record BetPlacementResult(BetPlacementStatus Status, string Message);

public sealed record AutomationActionResult(
    AutomationActionOutcome Outcome,
    string Message,
    AutomationBetRequest? Request = null);

public sealed record AutomationSnapshot(
    AutomationRunState State,
    bool AutoBetEnabled,
    bool IsSimulation,
    decimal SessionStake,
    int AcceptedBets,
    Guid? PendingOperationId,
    string Message);

public interface IAutomationTableAdapter
{
    string Name { get; }
    bool IsSimulation { get; }
    bool IsConnected { get; }

    ValueTask ConnectAsync(CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<decimal>> GetChipsAsync(CancellationToken cancellationToken = default);
    ValueTask ClearSelectionAsync(CancellationToken cancellationToken = default);
    ValueTask SelectChipAsync(decimal chip, CancellationToken cancellationToken = default);
    ValueTask<BetPlacementResult> PlaceBetAsync(
        AutomationBetRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AutomationDisconnectedException(string message) : IOException(message);
