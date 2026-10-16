namespace Baccarat.Core.Strategies;

public sealed partial class StrategySessionEngine
{
    private string? clientMode;
    private ClientState? clientState;
    private sealed record ClientState(BetSide? Side, int Decisions = 0, int OddBalance = 0,
        int StakeIndex = 0, decimal RunProfit = 0, string History = "", int TwoRemaining = 0,
        string Pattern = "First bet", bool PreviousWin = false, int SingleWins = 0,
        bool Virtual = false, int VirtualWins = 0);

    private void InitializeClientRules()
    {
        if (definition.RuleCondition?.StartsWith(VideoStrategyFactory.ClientTag, StringComparison.Ordinal) != true) return;
        clientMode = definition.RuleCondition[VideoStrategyFactory.ClientTag.Length..];
        if (clientMode is not ("Grinding" or "Odd Even" or "ODD Diff" or "ODD Looser" or "ODD is God" or "PATTERN") ||
            definition.Initial.Selector is not ("PLAYER" or "BANKER") ||
            definition.AfterWin.Selector != "CLIENT" || definition.AfterLoss.Selector != "CLIENT" ||
            definition.AfterWin.AmountRule != AmountRuleKind.ResetSequence ||
            definition.AfterLoss.AmountRule != AmountRuleKind.NextSequence ||
            definition.AfterWin.Values.Count != 1 || definition.AfterLoss.Values.Count != 1 ||
            definition.AfterLoss.Values[0] != decimal.Truncate(definition.AfterLoss.Values[0]) ||
            definition.AfterLoss.Values[0] > 10000)
            throw new ArgumentException("Invalid client strategy contract.", nameof(definition));
        var first = definition.Initial.Selector == "PLAYER" ? BetSide.Player : BetSide.Banker;
        clientState = new(clientMode.StartsWith("ODD ", StringComparison.Ordinal) ? null : first);
    }

    private BetRecommendation ClientRecommendation()
    {
        var state = clientState!;
        if (state.Virtual)
            return new("CLIENT", null, null, StrategyEngineState.Paused,
                $"No bet · Virtual {state.Side} · {state.VirtualWins}/2 consecutive wins to resume.");
        var side = manualSide ?? state.Side;
        if (side is null)
            return new("CLIENT", null, null, StrategyEngineState.Paused, "No bet · Observe the next result.");
        var label = clientMode == "PATTERN" ? $" ({state.Pattern})" : "";
        return new("CLIENT", currentStake, side, StrategyEngineState.Ready, $"Bet {currentStake:0.##} on {side}.{label}");
    }

    private StrategyRound RecordClient(Outcome outcome)
    {
        snapshots.Push(Capture());
        var state = clientState!;
        var side = manualSide ?? state.Side;
        var isBet = side is not null && !state.Virtual;
        bool? won = outcome == Outcome.Tie || side is null ? null :
            side == BetSide.Player ? outcome == Outcome.Player : outcome == Outcome.Banker;
        var change = !isBet || won is null ? 0m : won == false ? -currentStake!.Value :
            currentStake!.Value * (side == BetSide.Banker ? payoutRules.BankerWinProfit : payoutRules.PlayerWinProfit);
        Profit += change;
        if (isBet && won == false) ConsecutiveLosses++;
        else if (isBet && won == true) ConsecutiveLosses = 0;
        var result = new StrategyRound(rounds.Count + 1, outcome, isBet ? "CLIENT" : null,
            isBet ? side : null, isBet ? currentStake : null, change, Profit, isBet, isBet ? won : null);
        rounds.Add(result);
        if (outcome == Outcome.Tie) return result;
        var winner = outcome == Outcome.Player ? BetSide.Player : BetSide.Banker;
        lastWinner = winner;
        manualSide = null;
        var decisions = state.Decisions + 1;
        if (clientMode == "PATTERN") AdvancePattern(state, winner, won == true, change);
        else if (clientMode == "Grinding")
        {
            var run = state.RunProfit + change;
            var unit = definition.Initial.Values[0];
            if (run >= unit) { run = 0; currentStake = unit; }
            else if (won == true) currentStake += unit;
            clientState = state with { Side = winner, RunProfit = run, Decisions = decisions };
        }
        else
        {
            var index = state.StakeIndex;
            if (isBet && won is not null)
                index = won == true ? 0 : (index + 1) % definition.Initial.Values.Count;
            currentStake = definition.Initial.Values[index];
            var balance = state.OddBalance;
            BetSide? nextSide = state.Side;
            if (clientMode == "Odd Even")
            {
                var oddSide = definition.Initial.Selector == "PLAYER" ? BetSide.Player : BetSide.Banker;
                nextSide = decisions % 2 == 0 ? oddSide : Opposite(oddSide);
            }
            else if (decisions % 2 == 1)
            {
                balance += winner == BetSide.Banker ? 1 : -1;
                nextSide = clientMode == "ODD is God" ? winner : balance == 0 ? null :
                    balance > 0 ? BetSide.Banker : BetSide.Player;
                if (clientMode == "ODD Looser" && nextSide is { } leading) nextSide = Opposite(leading);
            }
            clientState = state with { Decisions = decisions, Side = nextSide, OddBalance = balance, StakeIndex = index };
        }
        if (currentStake > StrategyDefinitionValidator.MaximumSupportedAmount)
            StopReason = "The stake progression exceeded the supported limit.";
        ApplyLimits();
        return result;
    }

    private static BetSide Opposite(BetSide side) => side == BetSide.Player ? BetSide.Banker : BetSide.Player;

    private void AdvancePattern(ClientState state, BetSide winner, bool won, decimal change)
    {
        var unit = definition.Initial.Values[0];
        var stakeUnits = currentStake!.Value / unit;
        var run = state.RunProfit + change / unit;
        var singles = state.SingleWins;
        var previousWin = state.PreviousWin;
        var virtualMode = state.Virtual;
        var virtualWins = state.VirtualWins;
        if (virtualMode)
        {
            virtualWins = won ? virtualWins + 1 : 0;
            if (virtualWins == 2) { virtualMode = false; virtualWins = 0; singles = 0; previousWin = false; currentStake = unit; }
        }
        else
        {
            if (!won && previousWin) singles++;
            var highBet = stakeUnits >= definition.AfterWin.Values[0];
            var pause = singles >= definition.AfterLoss.Values[0];
            if (highBet && pause)
                StopReason = "IHG simultaneous run-limit/pause boundary conflicts in the source PDF. Review before a new run.";
            if (run > 0 || highBet)
            {
                run = 0; singles = 0; previousWin = false; currentStake = unit;
            }
            else
            {
                previousWin = won;
                currentStake = won ? (decimal.Ceiling(Math.Max(0, -run)) + 1) * unit : unit;
                if (pause) { virtualMode = true; virtualWins = 0; }
            }
        }

        var history = state.History + (winner == BetSide.Player ? "P" : "B");
        if (history.Length > 4) history = history[^4..];
        var remaining = state.TwoRemaining;
        var nextSide = winner;
        var pattern = "Follow";
        if (remaining > 0 && won)
        {
            remaining--;
            nextSide = remaining == 0 ? Opposite(winner) : winner;
            if (remaining == 0) remaining = 2;
            pattern = "2-2";
        }
        else if (history.Length == 4 && history[0] == history[1] && history[2] == history[3] && history[0] != history[2])
        {
            nextSide = Opposite(winner); remaining = 2; pattern = "2-2";
        }
        else
        {
            remaining = 0;
            if (history.Length >= 2)
            {
                nextSide = history[^2] == 'P' ? BetSide.Player : BetSide.Banker;
                pattern = history[^1] == history[^2] ? "Streak" : "Chop";
            }
        }
        clientState = state with { Side = nextSide, Decisions = state.Decisions + 1, RunProfit = run,
            History = history, TwoRemaining = remaining, Pattern = pattern, PreviousWin = previousWin,
            SingleWins = singles, Virtual = virtualMode, VirtualWins = virtualWins };
    }
}
