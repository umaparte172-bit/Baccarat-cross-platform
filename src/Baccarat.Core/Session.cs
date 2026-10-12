using System.Collections.ObjectModel;

namespace Baccarat.Core;

public enum Outcome { Player, Banker, Tie }
public sealed record Round(int Number, Outcome Outcome, DateTimeOffset RecordedAt);

// Manual records are independent of any unverified remote service.
public sealed class Session
{
    private readonly ObservableCollection<Round> rounds = new();
    public ReadOnlyObservableCollection<Round> Rounds { get; }
    public Session() => Rounds = new(rounds);
    public void Record(Outcome outcome)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        rounds.Add(new(rounds.Count + 1, outcome, DateTimeOffset.UtcNow));
    }
    public bool Undo()
    {
        if (rounds.Count == 0) return false;
        rounds.RemoveAt(rounds.Count - 1);
        return true;
    }
    public void Clear() => rounds.Clear();
    public int Count(Outcome outcome) => rounds.Count(r => r.Outcome == outcome);
    public string ToCsv() => "Round,Outcome,RecordedAt\n" + string.Join("\n",
        rounds.Select(r => $"{r.Number},{r.Outcome},{r.RecordedAt:O}"));
}
