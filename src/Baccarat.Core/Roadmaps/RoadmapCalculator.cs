namespace Baccarat.Core.Roadmaps;

public enum RoadColor { Red, Blue }
public sealed record RoadHand(Outcome Outcome, bool BankerPair = false, bool PlayerPair = false);
public sealed record RoadCell(int Column, int Row, int Hand, Outcome? Outcome = null,
    RoadColor? Color = null, int Ties = 0, bool BankerPair = false, bool PlayerPair = false);
public sealed record RoadAsk(RoadColor? BigEye, RoadColor? Small, RoadColor? Cockroach);
public sealed record RoadmapSnapshot(IReadOnlyList<RoadCell> Beads, IReadOnlyList<RoadCell> BigRoad,
    IReadOnlyList<RoadCell> BigEye, IReadOnlyList<RoadCell> Small, IReadOnlyList<RoadCell> Cockroach,
    RoadAsk BankerAsk, RoadAsk PlayerAsk);

/// <summary>Pure current-shoe roads. Logical streaks determine derived colors;
/// six-row placement only determines display coordinates, including dragon tails.</summary>
public static class RoadmapCalculator
{
    public const int Rows = 6;
    public const int MaximumHands = 10000;

    public static RoadmapSnapshot Calculate(IEnumerable<RoadHand> hands)
    {
        ArgumentNullException.ThrowIfNull(hands);
        var beads = new List<RoadCell>();
        var big = new List<RoadCell>();
        var derived = new[] { new List<RoadCell>(), new List<RoadCell>(), new List<RoadCell>() };
        var bigLayout = new Placement();
        var layouts = new[] { new Placement(), new Placement(), new Placement() };
        var lengths = new List<int>();
        Outcome? previous = null;
        var leadingTies = 0;
        var leadingBankerPair = false;
        var leadingPlayerPair = false;
        foreach (var hand in hands)
        {
            if (beads.Count == MaximumHands) throw new ArgumentException("A shoe supports at most 10,000 hands.", nameof(hands));
            if (hand is null || !Enum.IsDefined(typeof(Outcome), hand.Outcome)) throw new ArgumentException("Invalid road outcome.", nameof(hands));
            var number = beads.Count + 1;
            beads.Add(new((number - 1) / Rows, (number - 1) % Rows, number,
                hand.Outcome, BankerPair: hand.BankerPair, PlayerPair: hand.PlayerPair));
            if (hand.Outcome == Outcome.Tie)
            {
                if (big.Count == 0)
                {
                    leadingTies++;
                    leadingBankerPair |= hand.BankerPair;
                    leadingPlayerPair |= hand.PlayerPair;
                }
                else
                {
                    var last = big[^1];
                    big[^1] = last with
                    {
                        Ties = last.Ties + 1,
                        BankerPair = last.BankerPair || hand.BankerPair,
                        PlayerPair = last.PlayerPair || hand.PlayerPair
                    };
                }
                continue;
            }
            var same = previous == hand.Outcome;
            if (same) lengths[^1]++;
            else lengths.Add(1);
            var position = bigLayout.Add(same);
            big.Add(new(position.Column, position.Row, number, hand.Outcome, Ties: leadingTies,
                BankerPair: hand.BankerPair || leadingBankerPair, PlayerPair: hand.PlayerPair || leadingPlayerPair));
            leadingTies = 0;
            leadingBankerPair = leadingPlayerPair = false;
            for (var offset = 1; offset <= 3; offset++)
            {
                var color = ColorAt(lengths, lengths.Count - 1, lengths[^1], offset);
                if (color is null) continue;
                var road = derived[offset - 1];
                var cell = layouts[offset - 1].Add(road.Count > 0 && road[^1].Color == color);
                road.Add(new(cell.Column, cell.Row, number, Color: color));
            }
            previous = hand.Outcome;
        }
        if (big.Count == 0 && leadingTies > 0)
            big.Add(new(0, 0, 1, Ties: leadingTies, BankerPair: leadingBankerPair, PlayerPair: leadingPlayerPair));
        RoadAsk Ask(Outcome side)
        {
            var column = previous == side ? lengths.Count - 1 : lengths.Count;
            var depth = previous == side ? lengths[^1] + 1 : 1;
            return new(ColorAt(lengths, column, depth, 1), ColorAt(lengths, column, depth, 2), ColorAt(lengths, column, depth, 3));
        }
        return new(beads.AsReadOnly(), big.AsReadOnly(), derived[0].AsReadOnly(), derived[1].AsReadOnly(),
            derived[2].AsReadOnly(), Ask(Outcome.Banker), Ask(Outcome.Player));
    }

    private static RoadColor? ColorAt(IReadOnlyList<int> lengths, int column, int depth, int offset)
    {
        if (depth == 1)
        {
            if (column <= offset) return null;
            return lengths[column - 1] == lengths[column - offset - 1] ? RoadColor.Red : RoadColor.Blue;
        }
        if (column < offset) return null;
        // Occupancy of this depth and the depth immediately above in the reference
        // streak differs only at its first missing cell. Both blank is red as well.
        return depth == lengths[column - offset] + 1 ? RoadColor.Blue : RoadColor.Red;
    }

    private sealed class Placement
    {
        private readonly HashSet<(int Column, int Row)> occupied = new();
        private int startColumn = -1;
        private int column;
        private int row;
        private bool tail;

        public (int Column, int Row) Add(bool same)
        {
            if (!same)
            {
                column = ++startColumn;
                while (occupied.Contains((column, 0))) column = ++startColumn;
                row = 0;
                tail = false;
            }
            else if (!tail && row < Rows - 1 && !occupied.Contains((column, row + 1))) row++;
            else { column++; tail = true; }
            if (!occupied.Add((column, row))) throw new InvalidOperationException("Road placement collision.");
            return (column, row);
        }
    }
}
