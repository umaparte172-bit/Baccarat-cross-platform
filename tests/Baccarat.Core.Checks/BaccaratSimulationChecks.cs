using Baccarat.Core.Simulation;

internal static class BaccaratSimulationChecks
{
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        int Card(int points) => points == 0 ? 9 : points - 1;
        string[] draws = ["0123456789", "0123456789", "0123456789", "012345679", "234567", "4567", "67", "", "", ""];
        for (var p = 0; p <= 9; p++)
            for (var b = 0; b <= 9; b++)
                for (var third = 0; third <= 9; third++)
                {
                    var hand = BaccaratDealer.Deal([Card(p), Card(b), 9, 9, Card(third), 2]);
                    var natural = p >= 8 || b >= 8;
                    var playerDraw = !natural && p <= 5;
                    var bankerDraw = !natural && (playerDraw ? draws[b].Contains((char)('0' + third)) : b <= 5);
                    Check(hand.Player.Count == (playerDraw ? 3 : 2) && hand.Banker.Count == (bankerDraw ? 3 : 2),
                        $"Third-card rule mismatch P{p} B{b} third{third}");
                    Check(hand.PlayerTotal == (p + (playerDraw ? third : 0)) % 10, "Player total");
                    Check(hand.BankerTotal == (b + (bankerDraw ? playerDraw ? 3 : third : 0)) % 10, "Banker total");
                }
        var id = Guid.Parse("94e59c86-7db8-45ed-8235-d01be58413ec");
        var shoe = BaccaratDealer.CreateShoe(id, 1);
        Check(shoe.Length == 416 && shoe.Distinct().Count() == 416, "Eight complete decks without replacement");
        Check(shoe.SequenceEqual(BaccaratDealer.CreateShoe(id, 1)), "Reproducible shoe");
        Check(!shoe.SequenceEqual(BaccaratDealer.CreateShoe(id, 2)), "New shoe must change order");
        var offset = 0;
        var hands = 0;
        while (416 - offset >= 6)
        {
            var direct = BaccaratDealer.Deal(shoe, offset);
            var replay = BaccaratDealer.HandAt(id, 1, hands)!;
            Check(direct.Player.SequenceEqual(replay.Player) && direct.Banker.SequenceEqual(replay.Banker), "Shoe replay");
            offset += direct.CardsUsed;
            hands++;
        }
        Check(BaccaratDealer.HandAt(id, 1, hands) is null, "Exhausted shoe must not silently reshuffle");
        var pair = BaccaratDealer.Deal([0, 12, 52, 64, 4, 5]);
        Check(pair.PlayerPair && pair.BankerPair, "Pairs compare rank, not point value");
        var faces = BaccaratDealer.Deal([10, 12, 11, 64, 4, 5]);
        Check(!faces.PlayerPair, "Jack and queen are not a pair");
        try { BaccaratDealer.Deal([0, 1, 2]); throw new Exception("Short shoe accepted"); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: baccarat simulation, 1,000 drawing-rule combinations, eight decks, pairs, naturals, replay and exhaustion");
    }
}
