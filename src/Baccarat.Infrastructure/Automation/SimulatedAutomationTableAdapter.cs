using Baccarat.Core.Automation;

namespace Baccarat.Infrastructure.Automation;

public sealed class SimulatedAutomationTableAdapter : IAutomationTableAdapter
{
    private readonly IReadOnlyList<decimal> chips;
    private readonly List<decimal> selection = [];
    private readonly Dictionary<Guid, BetPlacementResult> completed = [];

    public SimulatedAutomationTableAdapter(IReadOnlyList<decimal>? chips = null)
    {
        this.chips = chips ?? [1m, 5m, 10m, 25m, 100m, 500m, 1_000m];
    }

    public string Name => "Local simulation";
    public bool IsSimulation => true;
    public bool IsConnected { get; private set; }
    public int FailedConnectionAttemptsRemaining { get; set; }
    public bool DisconnectBeforePlacement { get; set; }
    public IReadOnlyList<AutomationBetRequest> AcceptedRequests => acceptedRequests;

    private readonly List<AutomationBetRequest> acceptedRequests = [];

    public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailedConnectionAttemptsRemaining > 0)
        {
            FailedConnectionAttemptsRemaining--;
            throw new AutomationDisconnectedException("Simulated connection failure.");
        }
        IsConnected = true;
        selection.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = false;
        selection.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<decimal>> GetChipsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        return ValueTask.FromResult(chips);
    }

    public ValueTask ClearSelectionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        selection.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask SelectChipAsync(decimal chip, CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        if (!chips.Contains(chip)) throw new InvalidOperationException("The requested chip is unavailable.");
        selection.Add(chip);
        return ValueTask.CompletedTask;
    }

    public ValueTask<BetPlacementResult> PlaceBetAsync(
        AutomationBetRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected(cancellationToken);
        if (completed.TryGetValue(request.OperationId, out var existing)) return ValueTask.FromResult(existing);
        if (DisconnectBeforePlacement)
        {
            DisconnectBeforePlacement = false;
            IsConnected = false;
            throw new AutomationDisconnectedException("Simulated disconnect during placement.");
        }
        if (selection.Sum() != request.Stake || !selection.SequenceEqual(request.Chips))
            return ValueTask.FromResult(new BetPlacementResult(BetPlacementStatus.Rejected, "Selected chips do not match the requested stake."));

        var accepted = new BetPlacementResult(BetPlacementStatus.Accepted,
            $"Simulation accepted {request.Stake:0.##} on {request.Side}.");
        completed[request.OperationId] = accepted;
        acceptedRequests.Add(request);
        selection.Clear();
        return ValueTask.FromResult(accepted);
    }

    private void EnsureConnected(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected) throw new AutomationDisconnectedException("The simulation table is disconnected.");
    }
}
