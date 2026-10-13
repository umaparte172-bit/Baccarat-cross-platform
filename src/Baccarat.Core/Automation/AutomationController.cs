using Baccarat.Core.Strategies;

namespace Baccarat.Core.Automation;

public sealed class AutomationController
{
    private readonly IAutomationTableAdapter adapter;
    private readonly AutomationSafetyPolicy policy;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly SemaphoreSlim gate = new(1, 1);
    private PendingBet? pending;
    private bool autoBetEnabled;
    private decimal sessionStake;
    private int acceptedBets;

    public AutomationController(
        IAutomationTableAdapter adapter,
        AutomationSafetyPolicy? policy = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        this.policy = policy ?? AutomationSafetyPolicy.Default;
        this.policy.Validate();
        this.delay = delay ?? Task.Delay;
        Snapshot = new(
            AutomationRunState.Disconnected,
            false,
            adapter.IsSimulation,
            0,
            0,
            null,
            "Automation is disconnected.");
    }

    public event Action<AutomationSnapshot>? SnapshotChanged;

    public AutomationSnapshot Snapshot { get; private set; }

    public async ValueTask<AutomationActionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectWithRetryAsync(false, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await adapter.DisconnectAsync(cancellationToken);
            pending = null;
            autoBetEnabled = false;
            Update(AutomationRunState.Disconnected, "Automation disconnected.");
        }
        finally
        {
            gate.Release();
        }
    }

    public void SetAutoBet(bool enabled)
    {
        autoBetEnabled = enabled;
        Update(Snapshot.State, enabled ? "Auto-bet is enabled with safety controls." : "Auto-bet is disabled.");
    }

    public async ValueTask<AutomationActionResult> InitialBetAsync(
        BetRecommendation recommendation,
        CancellationToken cancellationToken = default) =>
        await PrepareAsync(recommendation, true, true, cancellationToken);

    public async ValueTask<AutomationActionResult> AutoBetAsync(
        BetRecommendation recommendation,
        CancellationToken cancellationToken = default) =>
        await PrepareAsync(recommendation, false, autoBetEnabled, cancellationToken);

    public async ValueTask<AutomationActionResult> ApprovePendingAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (pending is null)
                return new(AutomationActionOutcome.Skipped, "There is no pending bet to approve.");
            var approved = pending;
            pending = null;
            return await ExecuteAsync(approved, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Pause(string message = "Automation paused by the user.")
    {
        autoBetEnabled = false;
        Update(AutomationRunState.Paused, message);
    }

    public async ValueTask<AutomationActionResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var result = await ConnectWithRetryAsync(true, cancellationToken);
            if (result.Outcome == AutomationActionOutcome.Accepted)
            {
                pending = null;
                autoBetEnabled = false;
                Update(AutomationRunState.Paused, "Connection recovered. Review table state before resuming.");
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<AutomationActionResult> PrepareAsync(
        BetRecommendation recommendation,
        bool isInitial,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!enabled)
                return new(AutomationActionOutcome.Skipped, "Auto-bet is disabled.");
            if (!adapter.IsConnected)
                return new(AutomationActionOutcome.Unavailable, "Automation is not connected.");
            if (Snapshot.State is AutomationRunState.Stopped or AutomationRunState.Faulted)
                return new(AutomationActionOutcome.Unavailable, Snapshot.Message);
            if (recommendation is not { State: StrategyEngineState.Ready, Side: not null, Stake: not null })
                return new(AutomationActionOutcome.Skipped, recommendation.Message);

            var stake = recommendation.Stake.Value;
            if (stake <= 0 || stake > policy.MaximumStake || sessionStake + stake > policy.MaximumSessionStake)
            {
                Update(AutomationRunState.Stopped, "The automation stake safety limit was reached.");
                return new(AutomationActionOutcome.Rejected, Snapshot.Message);
            }

            var prepared = new PendingBet(Guid.NewGuid(), recommendation.Side.Value, stake, isInitial);
            if (policy.RequireApproval)
            {
                pending = prepared;
                Update(AutomationRunState.AwaitingApproval, $"Approve {stake:0.##} on {prepared.Side}.", prepared.OperationId);
                return new(AutomationActionOutcome.ApprovalRequired, Snapshot.Message);
            }
            return await ExecuteAsync(prepared, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<AutomationActionResult> ExecuteAsync(PendingBet prepared, CancellationToken cancellationToken)
    {
        IReadOnlyList<decimal> chips;
        try
        {
            Update(AutomationRunState.SelectingChips, "Selecting an exact chip combination.", prepared.OperationId);
            var available = await adapter.GetChipsAsync(cancellationToken);
            chips = ChipSelection.SelectExact(prepared.Stake, available, policy.MaximumChipClicks);
            await adapter.ClearSelectionAsync(cancellationToken);
            foreach (var chip in chips) await adapter.SelectChipAsync(chip, cancellationToken);
        }
        catch (AutomationDisconnectedException)
        {
            var recovery = await ConnectWithRetryAsync(true, cancellationToken);
            if (recovery.Outcome != AutomationActionOutcome.Accepted) return recovery;
            Update(AutomationRunState.Paused, "Recovered before placement. Approve the bet again after reviewing the table.");
            return new(AutomationActionOutcome.Unavailable, Snapshot.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            Update(AutomationRunState.Paused, exception.Message);
            return new(AutomationActionOutcome.Rejected, exception.Message);
        }

        var request = new AutomationBetRequest(
            prepared.OperationId,
            prepared.Side,
            prepared.Stake,
            chips,
            prepared.IsInitial);
        Update(AutomationRunState.PlacingBet, $"Placing {prepared.Stake:0.##} on {prepared.Side}.", prepared.OperationId);
        BetPlacementResult placement;
        try
        {
            placement = await adapter.PlaceBetAsync(request, cancellationToken);
        }
        catch (AutomationDisconnectedException)
        {
            Update(AutomationRunState.Faulted,
                "Connection was lost during placement. Outcome is unknown; the bet will not be retried.",
                prepared.OperationId);
            return new(AutomationActionOutcome.Indeterminate, Snapshot.Message, request);
        }

        return placement.Status switch
        {
            BetPlacementStatus.Accepted => Accept(request, placement.Message),
            BetPlacementStatus.Rejected => Reject(request, placement.Message),
            _ => Indeterminate(request, placement.Message)
        };
    }

    private AutomationActionResult Accept(AutomationBetRequest request, string message)
    {
        sessionStake += request.Stake;
        acceptedBets++;
        Update(AutomationRunState.Ready, message);
        return new(AutomationActionOutcome.Accepted, message, request);
    }

    private AutomationActionResult Reject(AutomationBetRequest request, string message)
    {
        autoBetEnabled = false;
        Update(AutomationRunState.Paused, message);
        return new(AutomationActionOutcome.Rejected, message, request);
    }

    private AutomationActionResult Indeterminate(AutomationBetRequest request, string message)
    {
        autoBetEnabled = false;
        Update(AutomationRunState.Faulted, message, request.OperationId);
        return new(AutomationActionOutcome.Indeterminate, message, request);
    }

    private async ValueTask<AutomationActionResult> ConnectWithRetryAsync(
        bool recovery,
        CancellationToken cancellationToken)
    {
        var delayValue = policy.InitialReconnectDelay;
        for (var attempt = 1; attempt <= policy.MaximumReconnectAttempts; attempt++)
        {
            Update(recovery ? AutomationRunState.Recovering : AutomationRunState.Connecting,
                $"Connection attempt {attempt} of {policy.MaximumReconnectAttempts}.");
            try
            {
                await adapter.ConnectAsync(cancellationToken);
                await adapter.ClearSelectionAsync(cancellationToken);
                Update(AutomationRunState.Ready,
                    adapter.IsSimulation ? "Simulation table connected." : "Automation table connected.");
                return new(AutomationActionOutcome.Accepted, Snapshot.Message);
            }
            catch (AutomationDisconnectedException) when (attempt < policy.MaximumReconnectAttempts)
            {
                await delay(delayValue, cancellationToken);
                var nextMilliseconds = Math.Min(delayValue.TotalMilliseconds * 2, policy.MaximumReconnectDelay.TotalMilliseconds);
                delayValue = TimeSpan.FromMilliseconds(nextMilliseconds);
            }
            catch (AutomationDisconnectedException)
            {
                break;
            }
        }

        autoBetEnabled = false;
        Update(AutomationRunState.Faulted, "Automation could not reconnect within the configured limit.");
        return new(AutomationActionOutcome.Unavailable, Snapshot.Message);
    }

    private void Update(AutomationRunState state, string message, Guid? pendingOperationId = null)
    {
        Snapshot = new(
            state,
            autoBetEnabled,
            adapter.IsSimulation,
            sessionStake,
            acceptedBets,
            pendingOperationId,
            message);
        foreach (Action<AutomationSnapshot> handler in SnapshotChanged?.GetInvocationList()
                     .Cast<Action<AutomationSnapshot>>() ?? [])
        {
            try { handler(Snapshot); }
            catch { }
        }
    }

    private sealed record PendingBet(Guid OperationId, BetSide Side, decimal Stake, bool IsInitial);
}
