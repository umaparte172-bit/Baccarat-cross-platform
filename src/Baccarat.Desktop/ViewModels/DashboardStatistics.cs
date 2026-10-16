using Baccarat.Core;

namespace Baccarat.Desktop.ViewModels;

public sealed record DashboardStatistics(int Bets, int Wins, int Losses, int Pushes, decimal StakeTotal,
    decimal PeakBalance, decimal Drawdown, decimal MaximumDrawdown, Outcome? StreakSide, int Streak, int Chop)
{
    public static DashboardStatistics Calculate(DashboardSession session)
    {
        var bets = session.Hands.Select(h => h.StrategyResult).Where(r => r?.WasBet == true).ToArray();
        var peak = session.StartingBankroll;
        decimal maximumDrawdown = 0;
        foreach (var hand in session.Hands)
        {
            var balance = session.StartingBankroll + hand.SessionProfit;
            peak = Math.Max(peak, balance);
            maximumDrawdown = Math.Max(maximumDrawdown, peak - balance);
        }
        // Ties do not break a decisive-result streak or alternation.
        var decisive = session.ShoeHands.Select(h => h.Round.Outcome).Where(o => o != Outcome.Tie).ToArray();
        var streak = decisive.Length == 0 ? 0 : decisive.Reverse().TakeWhile(o => o == decisive[^1]).Count();
        var chop = decisive.Length == 0 ? 0 : 1;
        for (var i = decisive.Length - 1; i > 0 && decisive[i] != decisive[i - 1]; i--) chop++;
        if (chop < 2) chop = 0;
        return new(bets.Length, bets.Count(b => b!.Won == true), bets.Count(b => b!.Won == false),
            bets.Count(b => b!.Won is null), bets.Sum(b => b!.Stake ?? 0), peak, peak - session.Balance,
            maximumDrawdown, decisive.Length == 0 ? null : decisive[^1], streak, chop);
    }
}
