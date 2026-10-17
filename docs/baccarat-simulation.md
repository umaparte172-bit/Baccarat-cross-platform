# No-money baccarat simulation

Create a session, choose a strategy and press **Apply settings**. Press **Deal
simulated hand** to deal cards and resolve the recommendation that was displayed
before the deal. The resulting next recommendation, calculated balance, ledger,
roadmaps and pair observations use the same engine as manual result entry.
No casino connection, wallet, real money or hosted server is involved.

## Rules and replay

- Eight standard decks, dealt without replacement in Player/Banker/Player/Banker order.
- Ace is one; ten and face cards are zero. Totals use modulo ten.
- Naturals stop drawing; otherwise standard punto banco third-card rules apply.
- Settlement retains the existing commission model (Banker profit 0.95 per unit,
  Player profit 1 per unit, ties return the Player/Banker stake). Pairs are observations,
  not side bets. Provider-specific bonus and no-commission variants are not simulated.
- Source for the drawing tableau: [GRA commission baccarat rules](https://www.gra.gov.sg/docs/default-source/game-rules/rws/baccarat-games/rws-game-rules---commission-baccarat-with-super-six-v4.pdf).
- Fewer than six remaining cards ends the training shoe; press **New Shoe**.
  Casino burn-card and cut-card rituals are intentionally not reproduced.
- A versioned hash-counter shuffle derives the shoe from the session ID and shoe
  number. This is reproducible training data, not a certified gambling RNG. Undo
  and restart reproduce the same deal; Clear Session also replays that session's
  first shoe. Create a new session for an independent shuffle.
- Manual entries consume a hand position in that shoe, but their outcomes remain
  user-entered. For a pure card-dealt simulation, use only Deal simulated hand in
  a fresh session. Cards displayed are the latest deal, not historical casino evidence.

## Verification and boundaries

Core checks cover 1,000 initial-total/third-card combinations, natural hands,
rank-based pairs, complete eight-deck permutations, replay and exhaustion.
Desktop checks cover simulated settlement and saved-session/Undo replay for all
eight strategies, plus the visible deal button and card display.

All 21 supplied strategy cases remain in `ClientStrategyChecks`. The independent
216-row IHG reference remains an optional local fixture, since the original PDF
is not redistributed. The contradictory simultaneous PATTERN run-limit/pause
boundary still stops explicitly. No claim is made of exact original-app behavior
outside the supplied evidence or of guaranteed profitability.
