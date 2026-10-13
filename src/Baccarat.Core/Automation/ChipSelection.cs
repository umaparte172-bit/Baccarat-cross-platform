namespace Baccarat.Core.Automation;

public static class ChipSelection
{
    public static IReadOnlyList<decimal> SelectExact(
        decimal stake,
        IReadOnlyList<decimal> availableChips,
        int maximumClicks)
    {
        if (stake <= 0 || decimal.Round(stake, 2) != stake)
            throw new ArgumentOutOfRangeException(nameof(stake), "Stake must be positive with at most two decimal places.");
        ArgumentNullException.ThrowIfNull(availableChips);
        if (maximumClicks is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(maximumClicks));

        var target = checked((int)(stake * 100));
        if (target > 1_000_000) throw new ArgumentOutOfRangeException(nameof(stake), "Stake exceeds the chip solver limit.");
        var chips = availableChips
            .Where(value => value > 0 && decimal.Round(value, 2) == value && value <= stake)
            .Distinct()
            .OrderByDescending(value => value)
            .Select(value => (Value: value, Units: checked((int)(value * 100))))
            .ToArray();
        if (chips.Length == 0) throw new InvalidOperationException("No valid chips are available.");

        var counts = Enumerable.Repeat(int.MaxValue, target + 1).ToArray();
        var previous = new int[target + 1];
        var usedChip = new int[target + 1];
        counts[0] = 0;
        for (var amount = 1; amount <= target; amount++)
        {
            for (var index = 0; index < chips.Length; index++)
            {
                var prior = amount - chips[index].Units;
                if (prior < 0 || counts[prior] == int.MaxValue || counts[prior] + 1 >= counts[amount]) continue;
                counts[amount] = counts[prior] + 1;
                previous[amount] = prior;
                usedChip[amount] = index;
            }
        }

        if (counts[target] == int.MaxValue)
            throw new InvalidOperationException("The selected stake cannot be composed from the available chips.");
        if (counts[target] > maximumClicks)
            throw new InvalidOperationException("The selected stake requires too many chip actions.");

        var result = new List<decimal>(counts[target]);
        for (var amount = target; amount > 0; amount = previous[amount])
            result.Add(chips[usedChip[amount]].Value);
        return result;
    }
}
