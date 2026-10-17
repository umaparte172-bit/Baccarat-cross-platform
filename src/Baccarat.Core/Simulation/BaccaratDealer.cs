using System.Security.Cryptography;
using System.Text;

namespace Baccarat.Core.Simulation;

public sealed record SimulatedHand(IReadOnlyList<int> Player, IReadOnlyList<int> Banker, int CardsUsed)
{
    public int PlayerTotal => Player.Sum(BaccaratDealer.Points) % 10;
    public int BankerTotal => Banker.Sum(BaccaratDealer.Points) % 10;
    public bool PlayerPair => Player[0] % 13 == Player[1] % 13;
    public bool BankerPair => Banker[0] % 13 == Banker[1] % 13;
    public Outcome Outcome => PlayerTotal == BankerTotal ? Outcome.Tie : PlayerTotal > BankerTotal ? Outcome.Player : Outcome.Banker;
    public string Description => $"Player: {string.Join(" ", Player.Select(BaccaratDealer.CardName))} = {PlayerTotal}\n" +
        $"Banker: {string.Join(" ", Banker.Select(BaccaratDealer.CardName))} = {BankerTotal}\n{Outcome}";
}

// Reproducible training shoes only. Not a casino RNG or an external table adapter.
public static class BaccaratDealer
{
    public static int Points(int card) => card % 13 < 9 ? card % 13 + 1 : 0;
    public static string CardName(int card) => new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }[card % 13] +
        new[] { "♣", "♦", "♥", "♠" }[card / 13 % 4];

    public static bool BankerDraws(int total, int? playerThird) => playerThird is null ? total <= 5 : total switch
    {
        <= 2 => true,
        3 => playerThird != 8,
        4 => playerThird is >= 2 and <= 7,
        5 => playerThird is >= 4 and <= 7,
        6 => playerThird is 6 or 7,
        _ => false
    };

    public static SimulatedHand Deal(IReadOnlyList<int> cards, int offset = 0)
    {
        if (offset < 0 || cards.Count - offset < 6) throw new ArgumentException("At least six cards must remain.", nameof(cards));
        var cursor = offset;
        int Take()
        {
            var card = cards[cursor++];
            if (card is < 0 or >= 416) throw new ArgumentException("Invalid eight-deck card.", nameof(cards));
            return card;
        }
        var p = new List<int> { Take() };
        var b = new List<int> { Take() };
        p.Add(Take()); b.Add(Take());
        var pt = p.Sum(Points) % 10;
        var bt = b.Sum(Points) % 10;
        if (pt < 8 && bt < 8)
        {
            int? third = null;
            if (pt <= 5) { p.Add(Take()); third = Points(p[2]); }
            if (BankerDraws(bt, third)) b.Add(Take());
        }
        return new(p.AsReadOnly(), b.AsReadOnly(), cursor - offset);
    }

    public static int[] CreateShoe(Guid session, int shoe)
    {
        if (shoe < 1) throw new ArgumentOutOfRangeException(nameof(shoe));
        var cards = Enumerable.Range(0, 416).ToArray();
        var seed = $"baccarat-training-v1:{session:N}:{shoe.ToString(System.Globalization.CultureInfo.InvariantCulture)}:";
        // Hash-counter sampling with rejection avoids modulo bias and runtime-specific Random sequences.
        var counter = 0;
        for (var i = cards.Length - 1; i > 0; i--)
        {
            var size = (uint)(i + 1);
            var threshold = unchecked(0u - size) % size;
            uint value;
            do
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed + (counter++).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash);
            } while (value < threshold);
            var j = (int)(value % size);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return cards;
    }

    public static SimulatedHand? HandAt(Guid session, int shoe, int hand)
    {
        if (hand is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(hand));
        var cards = CreateShoe(session, shoe);
        var offset = 0;
        for (var index = 0; index <= hand; index++)
        {
            if (cards.Length - offset < 6) return null;
            var result = Deal(cards, offset);
            if (index == hand) return result;
            offset += result.CardsUsed;
        }
        return null;
    }
}
