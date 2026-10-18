using System.Globalization;
using Baccarat.Core;
using Baccarat.Desktop.ViewModels;

// The client's written acceptance cases, run exactly as a tester would: new session, strategy, base unit 1,
// bankroll 1000, optional settings, Start, then click each result and compare "Next hand" and the balance.
internal static class StrategyAcceptanceChecks
{
    private sealed record Case(string Id, string Strategy, (string Key, string Value)[] Settings, string Start, (string Click, string Next)[] Steps, decimal Balance);

    private static (string, string)[] S(params (string, string)[] items) => items;
    private static (string Click, string Next)[] Steps(string text) =>
        text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => { var i = part.IndexOf('>'); return (part[..i].Trim(), part[(i + 1)..].Trim()); }).ToArray();

    private static readonly Case[] Cases =
    [
        new("M1", "Martingale", S(), "Player 1", Steps("B>Banker 2; P>Player 4; P>Player 1"), 1001m),
        new("M2", "Martingale", S(), "Player 1", Steps("T>Player 1; B>Banker 2; T>Banker 2; B>Banker 1"), 1000.90m),
        new("G1", "Grinding", S(), "Player 1", Steps("B>Banker 1; B>Banker 2; B>Banker 1"), 1001.85m),
        new("G2", "Grinding", S(), "Player 1", Steps("B>Banker 1; P>Player 1; P>Player 2; P>Player 1"), 1001m),
        new("G3", "Grinding", S(("grind", "2")), "Player 2", Steps("P>Player 2; B>Banker 2; B>Banker 4"), 1001.90m),
        new("S1", "Sequence", S(), "Banker 1", Steps("P>Player 2; B>Player 3; B>Banker 1; B>Banker 1"), 994.95m),
        new("S2", "Sequence", S(), "Banker 1", Steps("B>Banker 1; T>Banker 1"), 1000.95m),
        new("P4", "PATTERN", S(), "Player 1", Steps("P>Player 1; T>Player 1; P>Player 1; B>Player 1"), 1001m),
        new("E1", "Odd Even", S(), "Banker 1", Steps("P>Player 2; B>Banker 3; T>Banker 3; B>Player 1"), 999.85m),
        new("E2", "Odd Even", S(("odd", "P")), "Player 1", Steps("P>Banker 1; P>Player 2; B>Banker 3"), 998m),
        new("E3", "Odd Even", S(("stakes", "2, 4, 6")), "Banker 2", Steps("P>Player 4; B>Banker 6; P>Player 2; B>Banker 4"), 986m),
        new("D1", "ODD Diff", S(), "No bet", Steps("B>Banker 1; P>Banker 2; P>No bet; B>No bet; B>Banker 3"), 997m),
        new("D2", "ODD Diff", S(), "No bet", Steps("P>Player 1; B>Player 2; P>Player 1"), 1001m),
        new("D3", "ODD Diff", S(), "No bet", Steps("B>Banker 1; T>Banker 1; B>Banker 1"), 1000.95m),
        new("L1", "ODD Looser", S(), "No bet", Steps("B>Player 1; P>Player 1; P>No bet; B>No bet; B>Player 1"), 1002m),
        new("L2", "ODD Looser", S(), "No bet", Steps("P>Banker 1; B>Banker 1; P>Banker 2"), 999.95m),
        new("L3", "ODD Looser", S(), "No bet", Steps("B>Player 1; T>Player 1; B>Player 2"), 999m),
        new("O1", "ODD is God", S(), "No bet", Steps("B>Banker 1; P>Banker 2; P>Player 3"), 997m),
        new("O2", "ODD is God", S(), "No bet", Steps("T>No bet; P>Player 1; B>Player 2"), 999m),
        new("O3", "ODD is God", S(("stakes", "5, 10")), "No bet", Steps("B>Banker 5; P>Banker 10; B>Banker 5"), 1004.50m)
    ];

    internal static void Run()
    {
        var failures = new List<string>();
        foreach (var test in Cases)
        {
            try { Execute(test); }
            catch (Exception ex) { failures.Add($"{test.Id}: {ex.Message}"); }
        }
        // P1: run limit 2 over the client's click list; last bet must be Banker 1 and the balance 1001.95.
        try
        {
            var session = NewSession("PATTERN", S(("limit", "2")));
            foreach (var c in "P P P B B P P P B P P B P B".Split(' ')) session.Record(Parse(c));
            // "The last bet" in History is the bet settled by the final click (B1, not B4).
            var last = session.Hands[^1].StrategyResult;
            var placed = last is { Side: { } side, Stake: { } stake } ? $"{side} {stake.ToString("0.##", CultureInfo.InvariantCulture)}" : "no bet";
            if (placed != "Banker 1") failures.Add($"P1: the last bet in History is '{placed}', expected 'Banker 1'");
            if (session.Balance != 1001.95m) failures.Add($"P1: balance {session.Balance}, expected 1001.95");
        }
        catch (Exception ex) { failures.Add("P1: " + ex.Message); }
        if (failures.Count > 0) throw new Exception("Strategy acceptance failures:\n" + string.Join("\n", failures));
        Console.WriteLine($"PASS: {Cases.Length + 1} client strategy acceptance cases (Martingale, Grinding, Sequence, PATTERN, Odd Even, ODD Diff/Looser/is God)");
    }

    private static DashboardSession NewSession(string strategy, (string Key, string Value)[] settings)
    {
        var workspace = new DashboardWorkspace();
        workspace.CreateSession();
        var session = workspace.Selected!;
        session.SelectStrategy(strategy);
        session.BaseUnitDraft = "1";
        session.BankrollDraft = "1000";
        foreach (var (key, value) in settings) session.SetField(key, value);
        if (!session.ApplySettings(out var message)) throw new Exception("Settings rejected: " + message);
        return session;
    }

    private static Outcome Parse(string click) => click switch
    {
        "P" => Outcome.Player,
        "B" => Outcome.Banker,
        "T" => Outcome.Tie,
        _ => throw new ArgumentException(click)
    };

    private static string Describe(DashboardSession session)
    {
        var next = session.Engine!.Recommendation;
        return next.Side is { } side && next.Stake is { } stake
            ? $"{side} {stake.ToString("0.##", CultureInfo.InvariantCulture)}" : "No bet";
    }

    private static void Execute(Case test)
    {
        var session = NewSession(test.Strategy, test.Settings);
        if (Describe(session) != test.Start) throw new Exception($"start shows '{Describe(session)}', expected '{test.Start}'");
        var number = 0;
        foreach (var (click, expected) in test.Steps)
        {
            number++;
            session.Record(Parse(click));
            var actual = Describe(session);
            if (actual != expected) throw new Exception($"after click {number} ({click}) next hand is '{actual}', expected '{expected}'");
        }
        if (session.Balance != test.Balance) throw new Exception($"balance {session.Balance}, expected {test.Balance}");
    }
}
