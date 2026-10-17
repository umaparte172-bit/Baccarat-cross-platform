# No-money baccarat simulation

Create a session, choose a strategy and press **Apply settings**. Press **Deal
simulated hand** to deal cards and resolve the recommendation that was displayed
before the deal. The resulting next recommendation, calculated balance, ledger,
roadmaps and pair observations use the same engine as manual result entry.
No casino connection, wallet, real money or hosted server is involved.

## Browser-connected training table

1. Create a session and Apply settings on the desktop dashboard.
2. Open Tools → Automation → **Open browser simulator**. Installed Chrome (or Edge
   if Chrome is unavailable) opens a separate temporary profile with the embedded
   page on a random `127.0.0.1` port. Personal tabs and profiles are not attached.
3. In the desktop Automation page click **Connect simulation**, **Prepare initial
   bet**, then **Approve pending bet**. The browser displays the accepted side,
   stake, operation identifier and selected chips.
4. In the browser click **Deal / settle simulated hand**. Cards are dealt by the
   existing eight-deck simulator. The desktop session, balance, strategy and
   roadmaps update together. Virtual/no-bet recommendations can be observed
   without an approved wager.
5. **Enable auto-bet** prepares each next eligible recommendation after a result;
   every placement still requires explicit approval in the desktop app.
6. Browser **Recover connection** recovers table state only. It never places another
   wager or replays a deal. If the desktop adapter disconnects, use **Recover
   connection** and review the existing state before continuing.

The desktop simulation engine is authoritative: its placement acknowledgement is not
an acknowledgement from an external casino. Closing a browser
tab does not erase a previously accepted simulated wager. Reopen it from the
same session to see the state. Closing the desktop app stops its local listener;
browser placement/approval state is transient and must be prepared again after
restart, while recorded session results retain their existing persistence.

Switching sessions, changing strategy/shoe, Undo and clearing a session invalidate
stale browser round commands. Only one simulated wager is accepted per round.
Changing these controls before settlement cancels that simulated round's table
state. Use a dedicated session for the browser workflow rather than mixing manual
result entry with pending simulated placements.

### DOM integration and local protections

Desktop actions click actual DOM buttons. Browser clicks write a bounded command
to a DOM output element; the driver reads it, updates the shared session, and
renders the result back into DOM elements. There are **no `/state` or `/deal`
application APIs**. The local HTTP listener serves only HTML and JavaScript.
Chrome DevTools Protocol is used internally as the browser-control transport,
not as a casino API or an application request/response API.

Result/pair entry, dealing, Undo, New Shoe, confirmed Clear Session, strategy
settings/drafts, connection recovery, initial-bet preparation, placement approval,
auto-bet controls and Pause are routed through DOM when the browser is open.
Session navigation, Tools navigation, import/export dialogs and application
settings remain native. Other tool views share the same session model; they are
not copies of browser DOM controls. Closing the browser does not silently switch
DOM-routed actions to native execution: reopen it to continue.

The driver verifies the exact owned URL, including a random identity fragment,
before DOM operations. Its page request filter permits only the owned root and
script; downloads are denied. It never attaches to casino or personal tabs.
The listener binds IPv4 loopback with Host/Origin checks, restrictive CSP,
8 KiB header limits, eight concurrent connections and five-second I/O limits.
Commands are serialized, consumed before dispatch and checked against the current
round. No action is replayed on reconnect. Browser debugging is local, but is not
a security boundary against other malicious processes on the same computer.
Temporary browser profiles contain no casino credentials; they may remain in the
OS temporary folder after closing. Safari is not a supported DOM driver.

Run desktop checks with `BACCARAT_DOM_CHECKS=1` to exercise an installed browser.
They verify DOM initial/auto-bet approval, settlement, result/pair controls, Undo,
shoe, settings, confirmed clear and inactive-session isolation. Core checks verify
the static-only listener and rejected application API routes, origin checks and
adapter idempotency. Physical macOS/installed-browser acceptance remains separate.

The dashboard and browser use the five-segment blue Player Pair/Player, green Tie,
and red Banker/Banker Pair reference layout. Labels do not enable side wagers:
Player/Banker/Tie record outcomes, pairs mark observations. No reference screenshot
account balances, player counts or percentages are fabricated.

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
