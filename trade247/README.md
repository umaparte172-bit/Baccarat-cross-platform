# TRADE247-V1

Local baccarat strategy bot. The UI is the client's `index.html`; this folder is the backend behind it.
Plan and milestones: [`../docs/trade247-v1-plan.md`](../docs/trade247-v1-plan.md).
**Installing, building and running 24/7 on a VPS: [`docs/deployment.md`](docs/deployment.md).**

## Status

M0 to M5 and M9 are done, and M7 in its read-only form: the page runs against the local server with the Simulator, Manual, Replay and read-only Live table
feeds, the **IHG engine** is verified against the client's IHG document, the **risk layer** (limits, pauses,
keep-alive, end of shoe) is in place, **reports and Telegram alerts** work, the **Backtest** window runs, and the
program is **packaged** as a single file for Windows and Apple-silicon macOS (the Windows package is built and
smoke-tested here; the macOS package is built by CI and has not been run on a Mac yet). See the sections below.

Not built yet, and each says so in the UI: AME-AI, JJ, Manual Sequence Input, the Virtual Bet entry filter and
betting on a live table. **Start refuses** the first four rather than ignoring them, so a setting can never be silently
dropped.

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

## Backtest

The **BACKTEST** button runs the strategy and every setting on the screen over thousands of simulated sessions
and shows the spread of results (all in units, with a dollar figure beside the average).

- **A session** is a run of N simulated 8-deck shoes (a shoe ends at the cut card, about 70 hands; Banker pays
  95%). Take profit, stop loss, the daily limits (a session counts as one day), pauses, wait-for-pattern, shoe
  policy, hard cap and run-end stake all apply. **Keep-alive is ignored**, as the window says.
- **Bankroll**: with a bankroll set, a session that loses that many units is "wiped out" and stops there; `0` is
  an unlimited bankroll.
- **Source**: simulated shoes, or pasted results (`P`/`B`/`T`, `|` between shoes) as one session.
- **One engine**: the live session and the backtest share one `PlayCore` (strategy engine, risk layer, money). The
  tests replay the same hands through the live Replay path and the backtest for ten different risk setups and
  require identical profit/loss, drawdown, largest stake, money staked, virtual hands and stop reason.
- **Reproducible**: the same seed gives the same result, whatever the number of CPU threads. Seed `0` picks a
  random seed and the result reports it.
- **Speed**: about 1.8 million hands per second per core. The default 100 sessions x 500 shoes takes about 2 s;
  the page's maximum (2,000 x 5,000) runs on worker threads, shows progress and can be cancelled. Worker threads
  keep a live session's event loop free while a backtest runs.
- A backtest never changes the running session or the saved settings, and the Telegram token is not handed to the
  workers.
- Results are simulation output, not a forecast. No betting system changes the house edge, and the figures
  (especially the "bad day" and "worst drawdown" ones) are the point of running it before real money.

## Live table (read-only)

The **Live table** feed reads the road of a baccarat table open in your own Chrome (started with the launcher script,
which opens a debugging port on this computer only). It only reads: nothing is clicked or bet, bets stay simulated.
Code: `src/live/` (Chrome DevTools client, profile, page reader, road tracker) and `src/feeds/live.ts`. Tested against a
real headless Chrome with a table inside a frame from another site (`test/live.test.ts`, skipped when no Chrome is found).
**Not yet checked against a real Roobet/Evolution table**: `trade247 --inspect-table` captures what is needed to calibrate
the selectors, and a `live-profile.json` in the data folder replaces them without a new program.
Details: [`docs/deployment.md`](docs/deployment.md), section 6.

## Reports and alerts

**Excel Log button.** Excel (.xlsx) or CSV, complete up to the current hand; the session keeps running.
- Each press saves a snapshot to `reports/` (newest 20 kept, each with its own name so a file still open in
  Excel is never overwritten) and opens it in the default application. With `T247_OPEN_REPORTS=0` (servers,
  VPS) nothing is launched and the page downloads the file instead.
- The report is the live session, or after a restart or Reset the latest session rebuilt from the history file
  (`logs/trade247_session_history.csv`, appended on every hand).
- The workbook has one `Trades` sheet: bold frozen header, filter, money and counts as real numbers (so Excel can
  sum and chart them), everything else as text (text that looks like a formula stays text).
- The `.xlsx` is written by a small built-in writer (no Excel library at runtime). It is read back by ExcelJS in
  the tests, and was checked with Microsoft's Open XML SDK validator (0 schema errors on a live session and on a
  5,000-row session; the same validator flags deliberately broken files). **No copy of Excel was available while
  building this, so please open one in real Excel once and confirm.**

**Telegram alerts** (bot token and chat ID in Advanced settings; "Send test" shows the exact result):
- Triggers: daily stop loss hit, daily take profit hit, table connection lost, shoe change. Each message carries
  the session, strategy, hands, session profit/loss and balance. The first shoe of a session does not alert; a
  fast simulator sends at most one pending shoe-change alert; connection loss alerts once per outage.
- The token goes only into the request URL to `api.telegram.org` (HTTPS, no redirects, 8 s timeout). Its format is
  checked first so it cannot change the request path, and it is stripped from any error message. It is stored in
  `secrets.json`, never in the settings file, the page, the logs or the reports.
- A failed alert never stops the session; the reason appears in the page ("Alert not delivered - ..."). If the
  token or chat ID is half set up, Start still works and says "Telegram alerts are off: ...".
- Connection-loss alerts are called by the Live table feed (`SessionManager.feedLost()` / `feedRestored()`). The
  simulator, Manual and Replay feeds cannot disconnect.

## Run

Requires Node 22 or newer.

```bash
cd trade247
npm ci               # install the exact dependencies
npm start            # http://127.0.0.1:8247/   (add -- --open to open the browser)
npm test             # unit and API tests (about 40 s)
npm run typecheck
npm run package      # single-file program for THIS operating system -> release/
npm run smoke        # start the built program and check it end to end (16 checks)
npm run release      # package + a ready-to-zip folder with scripts, README and SHA256SUMS.txt
```

`trade247 --help` lists the command-line options (`--port`, `--no-open`, `--version`).
A single-file program contains the Node runtime of the machine that built it, so the Windows and macOS packages are
built on their own systems; `.github/workflows/trade247.yml` does that in CI. Details, the macOS first-run steps and
the Windows VPS runbook are in [`docs/deployment.md`](docs/deployment.md).

Environment variables: `T247_PORT` (default 8247), `T247_DATA_DIR` (default: `%APPDATA%\Trade247` on Windows,
`~/Library/Application Support/Trade247` on macOS), `T247_OPEN_REPORTS=0` (do not launch applications).

## Data folder

| File | Contents |
|---|---|
| `settings.json` | Last-used settings (no secrets) |
| `secrets.json` | Telegram bot token, mode 0600, never sent to the page (the page sees `********`) |
| `meta.json` | Session counter |
| `reports/` | Report snapshots opened from the Excel Log button (newest 20) |
| `day.json` | Today's profit/loss across all sessions (for the daily limits) |
| `logs/trade247_session_history.csv` | Every hand, appended as it happens |

## Security

The server listens on `127.0.0.1` only. API calls need the `X-T247: 1` header, and the `Host` and `Origin`
headers must match the local address, which blocks other web pages and DNS rebinding.

## Layout

```
ui/index.html     the client's page (only change: English number format)
src/engine/       pure logic: settings, money, dealer, strategy interface, IHG engine, risk layer, PlayCore
src/backtest/     trial runner, statistics, job manager, worker thread
src/feeds/        simulator, manual, replay
src/session.ts    session state the page polls
src/server/       HTTP routes (the /api contract the page calls)
src/report/       CSV history log, report table, .xlsx writer, snapshots
src/notify/       Telegram client and alert queue
src/store/        settings, secrets, session counter
src/cli.ts        command-line options
src/runtime.ts    version, embedded page and worker when packaged
tools/            build-app (single-file program), assemble-release, smoke-app
packaging/        Windows scripts (start, VPS scheduled task), macOS launcher, run instructions
docs/             deployment guide
test/             Vitest
```
