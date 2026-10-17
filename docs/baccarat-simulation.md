# No-money baccarat simulation

Create a session, choose a strategy and press **Apply settings**. Press **Deal
simulated hand** to deal cards and resolve the recommendation that was displayed
before the deal. The resulting next recommendation, calculated balance, ledger,
roadmaps and pair observations use the same engine as manual result entry.
No casino connection, wallet, real money or hosted server is involved.

## Browser-connected training table

1. Create a session and Apply settings on the desktop dashboard.
2. Open Tools → Automation → **Open browser simulator**. The default browser opens
   the page embedded in this app, on a random `127.0.0.1` port.
3. In the desktop Automation page click **Connect simulation**, **Prepare initial
   bet**, then **Approve pending bet**. The browser displays the accepted side,
   stake, operation identifier and selected chips.
4. In the browser click **Deal / settle simulated hand**. Cards are dealt by the
   existing eight-deck simulator. The desktop session, balance, strategy and
   roadmaps update together. Virtual/no-bet recommendations can be observed
   without an approved wager.
5. **Enable auto-bet** prepares each next eligible recommendation after a result;
   every placement still requires explicit approval in the desktop app.
6. Browser **Reconnect** refreshes table state only. It never places another
   wager or replays a deal. If the desktop adapter disconnects, use **Recover
   connection** and review the existing state before continuing.

The local table service is authoritative: its placement acknowledgement is not
an acknowledgement from an external casino or browser driver. Closing a browser
tab does not erase a previously accepted simulated wager. Reopen it from the
same session to see the state. Closing the desktop app stops its local listener;
browser placement/approval state is transient and must be prepared again after
restart, while recorded session results retain their existing persistence.

Switching sessions, changing strategy/shoe, Undo and clearing a session invalidate
stale browser round commands. Only one simulated wager is accepted per round.
Changing these controls before settlement cancels that simulated round's table
state. Use a dedicated session for the browser workflow rather than mixing manual
result entry with pending simulated placements.

### Local transport protections

The listener binds only IPv4 loopback and uses a random per-instance capability
token passed in the URL fragment and subsequent request headers. Tokens are not
saved to workspace files. Do not share the browser URL: it grants access to that
local training session. Host and Origin checks, restrictive CSP, no external
resources, bodyless requests, 8 KiB header limits, eight concurrent connections,
five-second I/O limits and round-key checks constrain the service. It is not a
general web server, proxy, casino connector or configurable remote-control API.

Automated checks exercise desktop→browser state, browser→desktop settlement,
initial/auto-bet approval, chips, duplicate/stale commands, inactive sessions,
unauthenticated/cross-origin requests and reconnect idempotency. A browser UI
check also confirmed an accepted simulated wager, card settlement and the next
approval-required state. Physical Safari/macOS acceptance remains separate.

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
