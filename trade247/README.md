# TRADE247-V1

Local baccarat strategy bot. The UI is the client's `index.html`; this folder is the backend behind it.
Plan and milestones: [`../docs/trade247-v1-plan.md`](../docs/trade247-v1-plan.md).

## Status

M0 to M3 are done: the page runs against the local server with the Simulator, Manual and Replay feeds, the
**IHG engine** is verified against the client's IHG document, and the **risk layer** (limits, pauses,
keep-alive, end of shoe) is in place. See the two sections below.

Not built yet, and each says so in the UI: AME-AI, JJ, Manual Sequence Input, the Virtual Bet entry filter,
Telegram, Excel (.xlsx), Backtest and the live table. **Start refuses** the first four rather than ignoring
them, so a setting can never be silently dropped.

## IHG engine

`src/engine/ihg.ts`. Rules come from the IHG document and the client's PRD; the numbers below are checked by
`test/ihg.oracle.test.ts`, which replays all 315 worked hands of the document (Example 1: 216 hands, exact
match including cumulative net; Examples 2 and 4 exact; Example 3 per the approved rule).

- Selection ignores ties: first bet Player, then streak = follow, chop = oppose; 2-2 (PPBB/BBPP) is followed
  until a prediction loses.
- Stakes: after a loss, 1 unit. After a win inside an open run, `ceil(deficit) + 1` units (`round_step` changes
  the rounding). A run closes when its net is above zero.
- Run-end stake (document: 45): placed in full, then the run closes win or lose. Hard cap: a stake above it is
  never placed, the run is dropped.
- Single wins (a loss that follows a win inside a run): at the limit (document: 7) betting goes virtual until
  two consecutive virtual wins, then resumes at 1 unit with the run's debt kept.
- Run-end stake and single-win limit on the same hand: the run closes and virtual play starts at once
  (approved by the client on 2026-10-18; the document's Example 3 table shows two more real bets, its text does not).

Decisions made where the document is silent (all easy to change, none confirmed by the client yet):

| Situation | What the engine does |
|---|---|
| New shoe | Default `reset_all` (PRD: cut the losing sequence). The "IHG document settings" button selects `continue`. |
| Single-win counter after a run-end stake that loses | Reset to 0 for the next run; the closing row still shows the count it reached (as the document does). |
| Shoe change while virtual | Virtual mode is kept (it is a table safety valve, not a deficit). |
| PAUSE | Watch only: the pattern keeps following the table; money, run, counters and virtual wins do not move. |
| Chip rounding | Stakes round up to the minimum chip and the run is accounted in the money actually staked. |

## Risk layer

`src/engine/risk.ts`, a pure state machine between the feed and the strategy. It never changes IHG's rules;
it decides whether a hand is bet, only watched, or ends the session.

- **Take Profit / Stop Loss** (per session, $): the session stops on the hand that reaches the limit.
- **Daily stop loss / take profit** (all sessions of the local calendar day, $; the daily take profit is the
  PRD's addition to the page): the session stops with status `LOCKED`, and Start is refused until the limit is
  raised or the next day begins. The day's total survives restarts (`day.json`).
- **Stop after N losses** (real bets lost in a row; a win resets, a tie does not): `STOP BET` ends the session.
  `WAIT PATTERN` watches with virtual picks and starts again from scratch (new pattern, no run) after two
  consecutive winning picks.
- **Pause after N losses for M hands**: no real bets for M hands (ties count), then betting resumes with the
  run exactly as it was.
- **Keep-Alive**: on every Nth consecutive hand without a bet the strategy's own pick is bet at the minimum
  amount (real money, shown as `MIN`). Applies while the strategy sits out (single-win pause, loss pause,
  wait-for-pattern, end of shoe), not during a manual PAUSE.
- **Near shoe end** (from hand #N of the shoe, needs a feed that reports shoe boundaries: Simulator, or `|` in
  Replay): `No new runs` lets an open run finish and then only watches; `Cut open run` drops the open run at hand N
  and then only watches. A new shoe resumes betting.
- A manual **PAUSE** freezes all of the above (countdowns, waiting, idle counts), so RESUME continues exactly
  where it left off.

Decisions where the spec is silent (none confirmed by the client yet): keep-alive and pauses count ties as hands;
keep-alive amounts round up to the minimum chip and are excluded from the win rate; "from scratch" after
WAIT PATTERN is a full reset including pattern history.

**Open question for the client**: the *Virtual Bet - start after N losses* filter is still refused at Start. Does
it apply once at the start of a session, or before every run (wait for a cold streak, bet one run, wait again)?
The two readings behave very differently, so it is not guessed.

## Run

Requires Node 22 or newer.

```bash
cd trade247
npm install
npm start            # http://127.0.0.1:8247/   (add -- --open to open the browser)
npm test             # unit and API tests
npm run typecheck
```

Environment variables: `T247_PORT` (default 8247), `T247_DATA_DIR` (default: `%APPDATA%\Trade247` on Windows,
`~/Library/Application Support/Trade247` on macOS).

## Data folder

| File | Contents |
|---|---|
| `settings.json` | Last-used settings (no secrets) |
| `secrets.json` | Telegram bot token, mode 0600, never sent to the page (the page sees `********`) |
| `meta.json` | Session counter |
| `day.json` | Today's profit/loss across all sessions (for the daily limits) |
| `logs/trade247_session_history.csv` | Every hand, appended as it happens |

## Security

The server listens on `127.0.0.1` only. API calls need the `X-T247: 1` header, and the `Host` and `Origin`
headers must match the local address, which blocks other web pages and DNS rebinding.

## Layout

```
ui/index.html     the client's page (only change: English number format)
src/engine/       pure logic: settings, money, dealer, strategy interface, IHG engine, factory + safety gate
src/feeds/        simulator, manual, replay
src/session.ts    session state the page polls
src/server/       HTTP routes (the /api contract the page calls)
src/report/       CSV log
src/store/        settings, secrets, session counter
test/             Vitest
```
