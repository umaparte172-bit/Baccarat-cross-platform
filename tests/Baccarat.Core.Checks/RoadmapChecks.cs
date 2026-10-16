using Baccarat.Core;
using Baccarat.Core.Roadmaps;

internal static class RoadmapChecks
{
    public static void Run()
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Roadmaps: " + message); }
        static RoadHand[] Hands(string text) => text.Select(c => new RoadHand(c switch
        { 'B' => Outcome.Banker, 'P' => Outcome.Player, _ => Outcome.Tie })).ToArray();
        static RoadmapSnapshot Road(string text) => RoadmapCalculator.Calculate(Hands(text));
        static string Colors(IReadOnlyList<RoadCell> road) => string.Concat(road.Select(c => c.Color == RoadColor.Red ? 'R' : 'B'));
        static string Positions(IReadOnlyList<RoadCell> road) => string.Join(" ", road.Select(c => $"{c.Column},{c.Row}"));
        var empty = Road("");
        Check(empty.BigRoad.Count == 0 && empty.BankerAsk == new RoadAsk(null, null, null), "empty road and Ask");
        var ties = Road("TT");
        Check(ties.Beads.Count == 2 && ties.BigRoad.Single() is { Outcome: null, Ties: 2, Column: 0, Row: 0 }, "leading ties");
        var tied = RoadmapCalculator.Calculate([new(Outcome.Tie, true), new(Outcome.Tie, PlayerPair: true), new(Outcome.Banker), new(Outcome.Tie)]);
        Check(tied.BigRoad.Single() is { Outcome: Outcome.Banker, Ties: 3, BankerPair: true, PlayerPair: true }, "leading ties merge and preserve pair observations");
        Check(Positions(Road("BPTBPTB").Beads) == "0,0 0,1 0,2 0,3 0,4 0,5 1,0", "beads fill downward and wrap after six");
        Check(Positions(Road("BBBBBBBBPPPPPPPP").BigRoad) ==
            "0,0 0,1 0,2 0,3 0,4 0,5 1,5 2,5 1,0 1,1 1,2 1,3 1,4 2,4 3,4 4,4", "bottom and collision tails must remain horizontal");
        Check(Positions(Road("BPBPBP").BigRoad) == "0,0 1,0 2,0 3,0 4,0 5,0", "alternation starts successive columns");
        Check(Road("BP").BigEye.Count == 0 && Road("BPP").BigEye.Count == 1 && Road("BPB").BigEye.Count == 1, "Big Eye start boundaries");
        Check(Road("BPB").Small.Count == 0 && Road("BPBB").Small.Count == 1 && Road("BPBP").Small.Count == 1, "Small start boundaries");
        Check(Road("BPBP").Cockroach.Count == 0 && Road("BPBPP").Cockroach.Count == 1 && Road("BPBPB").Cockroach.Count == 1, "Cockroach start boundaries");
        var fixture = Road("BBBPPBPPPPBB");
        Check(Colors(fixture.BigEye) == "RBBBRRBR", "Big Eye mixed-run reference");
        Check(Colors(fixture.Small) == "BRBRBB", "Small mixed-run reference");
        Check(Colors(fixture.Cockroach) == "RRBBR", "Cockroach mixed-run reference");
        Check(fixture.BankerAsk == new RoadAsk(RoadColor.Red, RoadColor.Red, RoadColor.Blue) &&
            fixture.PlayerAsk == new RoadAsk(RoadColor.Blue, RoadColor.Blue, RoadColor.Red), "explicit Ask reference");
        var withTies = Road("TBTBTBTPPTBTPPPPTBBTT");
        Check(Colors(withTies.BigEye) == Colors(fixture.BigEye) && Colors(withTies.Small) == Colors(fixture.Small) &&
            Colors(withTies.Cockroach) == Colors(fixture.Cockroach), "ties cannot create derived marks");
        Check(Colors(Road("PPPBBPBBBBPP").BigEye) == Colors(fixture.BigEye), "color symmetry when swapping Banker and Player");
        for (var bits = 0; bits < 1024; bits++)
        {
            var input = Enumerable.Range(0, 10).Select(i => new RoadHand((bits & (1 << i)) == 0 ? Outcome.Banker : Outcome.Player)).ToArray();
            var before = RoadmapCalculator.Calculate(input);
            foreach (var outcome in new[] { Outcome.Banker, Outcome.Player })
            {
                var after = RoadmapCalculator.Calculate(input.Append(new RoadHand(outcome)));
                var ask = outcome == Outcome.Banker ? before.BankerAsk : before.PlayerAsk;
                static RoadColor? Added(IReadOnlyList<RoadCell> a, IReadOnlyList<RoadCell> b) => b.Count > a.Count ? b[^1].Color : null;
                Check(ask == new RoadAsk(Added(before.BigEye, after.BigEye), Added(before.Small, after.Small), Added(before.Cockroach, after.Cockroach)), "Ask must equal the next recorded mark");
                Check(before.Beads.Count == 10 && input.Length == 10, "Ask cannot mutate history");
            }
        }
        foreach (var input in new[] { new string('B', 10000), string.Concat(Enumerable.Range(0, 1250).Select(i => new string(i % 2 == 0 ? 'B' : 'P', 8))) })
        {
            var road = Road(input);
            foreach (var cells in new[] { road.Beads, road.BigRoad, road.BigEye, road.Small, road.Cockroach })
            {
                Check(cells.All(c => c.Row is >= 0 and < 6 && c.Column >= 0), "all cells stay in six rows");
                Check(cells.Select(c => (c.Column, c.Row)).Distinct().Count() == cells.Count, "no collision in 10,000-hand long roads");
            }
        }
        try { RoadmapCalculator.Calculate([new((Outcome)999)]); throw new Exception("Invalid outcome accepted"); }
        catch (ArgumentException) { }
        try { Road(new string('B', 10001)); throw new Exception("Oversized shoe accepted"); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: five roads, leading/attached ties, pairs, six-row tails, derived boundaries, mixed-run vectors, 2,048 Ask comparisons and maximum-size shoes");
    }
}
