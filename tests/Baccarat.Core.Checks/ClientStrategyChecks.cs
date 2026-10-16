using Baccarat.Core;
using Baccarat.Core.Strategies;

internal static class ClientStrategyChecks
{
    internal static void Run()
    {
        StrategyDefinition Client(string mode, decimal unit = 1, BetSide first = BetSide.Banker, decimal[]? stakes = null) =>
            VideoStrategyFactory.Client(mode, first, stakes ?? [unit, 2 * unit, 3 * unit]);
        var martingale = VideoStrategyFactory.Martingale(BetSide.Player, 1);
        Vector("M1", martingale, "BPP", "P1 B2 P4 P1", 1001);
        Vector("M2", martingale, "TBTB", "P1 P1 B2 B2 B1", 1000.90m);
        var grinding = Client("Grinding", first: BetSide.Player, stakes: [1]);
        Vector("G1", grinding, "BBB", "P1 B1 B2 B1", 1001.85m);
        Vector("G2", grinding, "BPPP", "P1 B1 P1 P2 P1", 1001);
        Vector("G3", Client("Grinding", first: BetSide.Player, stakes: [2]), "PBB", "P2 P2 B2 B4", 1001.90m);
        var sequence = VideoStrategyFactory.Sequence([BetSide.Banker, BetSide.Player, BetSide.Player], [1, 2, 3]);
        Vector("S1", sequence, "PBBB", "B1 P2 P3 B1 B1", 994.95m);
        Vector("S2", sequence, "BT", "B1 B1 B1", 1000.95m);
        Vector("E1", Client("Odd Even"), "PBTB", "B1 P2 B3 B3 P1", 999.85m);
        Vector("E2", Client("Odd Even", first: BetSide.Player), "PPB", "P1 B1 P2 B3", 998);
        Vector("E3", Client("Odd Even", stakes: [2, 4, 6]), "PBPB", "B2 P4 B6 P2 B4", 986);
        Vector("D1", Client("ODD Diff"), "BPPBB", "N B1 B2 N N B3", 997);
        Vector("D2", Client("ODD Diff"), "PBP", "N P1 P2 P1", 1001);
        Vector("D3", Client("ODD Diff"), "BTB", "N B1 B1 B1", 1000.95m);
        Vector("L1", Client("ODD Looser"), "BPPBB", "N P1 P1 N N P1", 1002);
        Vector("L2", Client("ODD Looser"), "PBP", "N B1 B1 B2", 999.95m);
        Vector("L3", Client("ODD Looser"), "BTB", "N P1 P1 P2", 999);
        Vector("O1", Client("ODD is God"), "BPP", "N B1 B2 P3", 997);
        Vector("O2", Client("ODD is God"), "TPB", "N N P1 P2", 999);
        Vector("O3", Client("ODD is God", stakes: [5, 10]), "BPB", "N B5 B10 B5", 1004.50m);
        var pattern = VideoStrategyFactory.Client("PATTERN", BetSide.Player, [1]);
        Vector("P4", pattern, "PTPB", "P1 P1 P1 P1 P1", 1001);
        var labels = new StrategySessionEngine(pattern);
        foreach (var (outcome, label) in new[] { (Outcome.Player, "Follow"), (Outcome.Tie, "Follow"),
            (Outcome.Player, "Streak"), (Outcome.Banker, "Chop") })
        {
            labels.Record(outcome);
            Check(labels.Recommendation.Message.Contains(label, StringComparison.Ordinal), "P4 pattern label");
        }
        // IHG Example 1, first 14 hands (PDF pages 3–4, explanation pages 8–9).
        History("P1", VideoStrategyFactory.Client("PATTERN", BetSide.Player, [1], 2),
            "PPPBBPPPBPPBPB", "P1 P1 P1 P1 P1 P1 P2 B1 P1 P1 B2 P1 P1 B1", 1001.95m);
        History("IHG reference", pattern, "PPPBBPPPBPPBPB",
            "P1 P1 P1 P1 P1 P1 P2 B1 P1 P1 B2 P1 P1 B4", 1004.80m);

        var limit = new StrategySessionEngine(Client("Grinding", first: BetSide.Player, stakes: [1_000_000_000_000m]));
        limit.Record(Outcome.Banker); limit.Record(Outcome.Banker);
        Check(limit.Recommendation.State == StrategyEngineState.Stopped, "Grinding amount limit");
        var paused = new StrategySessionEngine(VideoStrategyFactory.Client("PATTERN", BetSide.Player, [1], singleWins: 1));
        foreach (var c in "PPBPP") paused.Record(OutcomeOf(c));
        Check(paused.Recommendation.State == StrategyEngineState.Paused, "Isolated-win trigger must start virtual play");
        var cash = paused.Profit;
        var beforeTie = paused.Recommendation;
        paused.Record(Outcome.Tie);
        Check(paused.Recommendation == beforeTie, "Virtual tie must leave counters unchanged");
        foreach (var c in "BPB") Check(!paused.Record(OutcomeOf(c)).WasBet, "Virtual outcomes must not place bets");
        Check(paused.Profit == cash && Token(paused.Recommendation) == "P1", "Two virtual wins resume at one unit without changing cash");
        Check(StrategySessionEngine.RestoreArchive(paused.ExportArchive()).Recommendation == paused.Recommendation, "Virtual replay");
        PdfReference(pattern);
        Console.WriteLine("PASS: all 21 supplied client cases, IHG 14-hand reference, next sides/stakes, balances, no-bet, labels, Undo/replay and amount bound");
    }

    private static string Token(BetRecommendation next) => next.State == StrategyEngineState.Paused ? "N" :
        (next.Side == BetSide.Player ? "P" : "B") + next.Stake?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static Outcome OutcomeOf(char value) => value == 'P' ? Outcome.Player : value == 'B' ? Outcome.Banker : Outcome.Tie;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static void Vector(string id, StrategyDefinition definition, string outcomes, string expected, decimal balance)
    {
        var engine = new StrategySessionEngine(definition);
        var next = expected.Split(' ');
        Check(next.Length == outcomes.Length + 1, id + " fixture length");
        Check(Token(engine.Recommendation) == next[0], id + " initial");
        for (var i = 0; i < outcomes.Length; i++)
        {
            var before = engine.Recommendation;
            engine.Record(OutcomeOf(outcomes[i]));
            Check(Token(engine.Recommendation) == next[i + 1], $"{id} hand {i + 1}: expected {next[i + 1]}, got {Token(engine.Recommendation)}");
            var restored = StrategySessionEngine.RestoreArchive(engine.ExportArchive());
            Check(restored.Rounds.SequenceEqual(engine.Rounds) && restored.Recommendation == engine.Recommendation, id + " replay");
            Check(restored.Undo() && restored.Recommendation == before, id + " Undo");
        }
        Check(engine.Profit + 1000 == balance, $"{id} balance: expected {balance}, got {1000 + engine.Profit}");
        engine.Reset();
        Check(engine.Rounds.Count == 0 && engine.Profit == 0 && Token(engine.Recommendation) == next[0], id + " reset");
    }

    private static void History(string id, StrategyDefinition definition, string outcomes, string expected, decimal balance)
    {
        var engine = new StrategySessionEngine(definition);
        var bets = expected.Split(' ');
        for (var i = 0; i < outcomes.Length; i++)
        {
            Check(Token(engine.Recommendation) == bets[i], $"{id} wager {i + 1}: {Token(engine.Recommendation)} != {bets[i]}");
            engine.Record(OutcomeOf(outcomes[i]));
        }
        Check(engine.Profit + 1000 == balance, id + " balance");
        var restored = StrategySessionEngine.RestoreArchive(engine.ExportArchive());
        Check(restored.Rounds.SequenceEqual(engine.Rounds), id + " history replay");
        while (restored.Undo()) { }
        Check(restored.Profit == 0 && Token(restored.Recommendation) == "P1", id + " full Undo");
    }

    private static void PdfReference(StrategyDefinition definition)
    {
        var path = Environment.GetEnvironmentVariable("BACCARAT_IHG_REFERENCE_PATH");
        if (string.IsNullOrEmpty(path)) return;
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var engine = new StrategySessionEngine(definition);
        var count = 0;
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("example").GetInt32() == 1))
        {
            var expected = row.GetProperty("side").GetString() + row.GetProperty("stake").GetInt32();
            Check(Token(engine.Recommendation) == expected, $"IHG full example hand {count + 1}: {Token(engine.Recommendation)} != {expected}");
            engine.Record(OutcomeOf(row.GetProperty("outcome").GetString()![0]));
            Check(engine.Profit == decimal.Parse(row.GetProperty("net").GetString()!, System.Globalization.CultureInfo.InvariantCulture), "IHG example cumulative net");
            count++;
        }
        Check(count == 216, "Complete IHG Example 1 fixture required");
        Console.WriteLine("PASS: 216 independent IHG PDF Example 1 rows, exact wagers and cumulative profits");
    }
}
