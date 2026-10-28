// Plays scenarios through the C# reference engine and prints every pick, stake and balance.
//   dotnet run --project tools/classic-reference -- scenarios.json traces.json
// The TypeScript engines must reproduce these traces exactly (see test/classic.reference.test.ts).
using System.Globalization;
using System.Text.Json;
using Baccarat.Core;
using Baccarat.Core.Strategies;

var input = JsonDocument.Parse(File.ReadAllText(args[0]));
var results = new List<object>();
foreach (var s in input.RootElement.EnumerateArray())
{
    var id = s.GetProperty("id").GetString()!;
    var mode = s.GetProperty("mode").GetString()!;
    var first = s.GetProperty("first").GetString() == "P" ? BetSide.Player : BetSide.Banker;
    var unit = s.GetProperty("unit").GetDecimal();
    var multiplier = s.GetProperty("multiplier").GetDecimal();
    var stakes = s.GetProperty("stakes").EnumerateArray().Select(x => x.GetDecimal()).ToArray();
    var sides = s.GetProperty("sides").EnumerateArray().Select(x => x.GetString() == "P" ? BetSide.Player : BetSide.Banker).ToArray();
    var outcomes = s.GetProperty("outcomes").GetString()!;

    StrategyDefinition definition = mode switch
    {
        "Martingale" => VideoStrategyFactory.Martingale(first, unit, multiplier),
        "Sequence" => VideoStrategyFactory.Sequence(sides, stakes),
        "Grinding" => VideoStrategyFactory.Client("Grinding", first, [unit]),
        _ => VideoStrategyFactory.Client(mode, first, stakes),
    };

    var engine = new StrategySessionEngine(definition);
    var rows = new List<object[]>();
    foreach (var c in outcomes)
    {
        var rec = engine.Recommendation;
        if (rec.State is StrategyEngineState.Stopped) break;
        var pick = rec.State == StrategyEngineState.Paused ? "N" : rec.Side == BetSide.Player ? "P" : "B";
        var outcome = c == 'P' ? Outcome.Player : c == 'B' ? Outcome.Banker : Outcome.Tie;
        var round = engine.Record(outcome);
        rows.Add([pick, rec.Stake?.ToString(CultureInfo.InvariantCulture) ?? "", round.WasBet,
            round.Won is null ? "" : round.Won.Value ? "W" : "L",
            round.ProfitChange.ToString(CultureInfo.InvariantCulture), round.TotalProfit.ToString(CultureInfo.InvariantCulture)]);
    }
    results.Add(new { id, rows });
}
File.WriteAllText(args[1], JsonSerializer.Serialize(results));
Console.WriteLine($"{results.Count} scenarios traced");
