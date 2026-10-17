using Baccarat.Core.Automation;

namespace Baccarat.Desktop;

// Lets one AutomationController run against the local simulator or a live browser table.
internal sealed class SwitchableTableAdapter(IAutomationTableAdapter initial) : IAutomationTableAdapter
{
    private IAutomationTableAdapter current = initial;
    public IAutomationTableAdapter Current => current;
    public void Use(IAutomationTableAdapter adapter) => current = adapter;
    public string Name => current.Name;
    public bool IsSimulation => current.IsSimulation;
    public bool IsConnected => current.IsConnected;
    public ValueTask ConnectAsync(CancellationToken cancellationToken = default) => current.ConnectAsync(cancellationToken);
    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) => current.DisconnectAsync(cancellationToken);
    public ValueTask<IReadOnlyList<decimal>> GetChipsAsync(CancellationToken cancellationToken = default) => current.GetChipsAsync(cancellationToken);
    public ValueTask ClearSelectionAsync(CancellationToken cancellationToken = default) => current.ClearSelectionAsync(cancellationToken);
    public ValueTask SelectChipAsync(decimal chip, CancellationToken cancellationToken = default) => current.SelectChipAsync(chip, cancellationToken);
    public ValueTask<BetPlacementResult> PlaceBetAsync(AutomationBetRequest request, CancellationToken cancellationToken = default) =>
        current.PlaceBetAsync(request, cancellationToken);
}

// Callbacks the Automation tool uses for the real-browser table; each returns a status line to show.
public sealed record LiveTableControls(
    Func<IReadOnlyList<string>> Profiles,
    Func<string, Task<string>> Open,
    Func<string, Task<string>> Connect,
    Func<Task<string>> Disconnect,
    Func<Task<string>> Inspect,
    string ProfileFolder);
