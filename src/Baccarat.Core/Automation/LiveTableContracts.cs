namespace Baccarat.Core.Automation;

// A finished round read from a live table page.
public sealed record LiveRoundResult(string Key, Outcome Outcome, bool BankerPair, bool PlayerPair);

// Read-only view of an external table: lets the desktop record results the table shows.
public interface ILiveTableSource
{
    event Action<LiveRoundResult>? ResultObserved;
    event Action<string>? Faulted;
    // null when the table does not expose a recognisable betting-window indicator.
    bool? IsBettingOpen { get; }
    string? Balance { get; }
}
