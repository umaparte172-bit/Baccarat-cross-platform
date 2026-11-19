# TRADE247-V1

Local baccarat strategy bot. The UI is the client's `index.html`; this folder is the backend behind it.
Plan and milestones: [`../docs/trade247-v1-plan.md`](../docs/trade247-v1-plan.md).
**Installing, building and running 24/7 on a VPS: [`docs/deployment.md`](docs/deployment.md).**

## Status

Done: the Electron page runs against its private local server with Simulator, Manual and Replay feeds; the **IHG
engine** is verified against the client's document and includes the supplied project's entry filter, virtual single-wins,
Banker cover, cut/pause and list-progression controls; **AME-AI** implements the supplied momentum/confidence,
chaos-filter and bounded dynamic-stake formulas; **JJ** implements all five supplied cycle presets; the **seven classic strategies**
(Martingale, Grinding, Sequence, Odd Even, ODD Diff, ODD Looser, ODD is God) are verified against the client's written
cases and the C# reference engine; the **risk layer** (limits, pauses, keep-alive, end of shoe) is in place; **reports and
Telegram alerts** work; the **Backtest** window runs; and the program ships as a **desktop app** (Electron) and as a
single-file program for Windows and Apple-silicon macOS. Commit `ab6e016` passed Electron packaging, smoke tests and
Selenium on both platforms, including macOS ad-hoc signature verification. Follow-up fixes address mock-table timing
races and test-browser startup diagnostics. See [the handoff](docs/current-handoff.md) for verification details.

The supplied client build and user guide define the finished product as an offline paper-trading simulator. The older
experimental Live-table/CDP modules remain isolated behind explicit settings, but they are not part of the completed,
verified scope and have not been checked against a casino. Fingerprint spoofing and bot-detection evasion are not part
of this program.

## Guide-aligned desktop additions

- **P&L Chart:** hover to preview, click to pin, Escape to close. Includes cumulative profit/loss, highest/lowest points,
  drawdown, per-hand details, range presets, zoom, drag-to-pan and an overview. Pixel buckets retain both extrema.
- **Compare Settings:** up to 60 combinations and 500 trials per combination, limited to 600,000 combination/trial/shoe
  iterations. Capital is entered in dollars and converted separately for each base unit. Results are in dollars and sortable;
  **USE** copies the chosen settings to the form only. Press APPLY or START explicitly to use them.
- **Finite backtest capital:** an unaffordable next stake ends the trial as `short of stake`; capital never becomes negative.
  The reported capital-failure percentage includes both exhausted capital and unaffordable next stakes. Backtests ignore
  daily limits and keep-alive; session Take Profit / Stop Loss still apply.
- **Practice balance protection:** the optional Advanced setting stops Simulator, Manual and Replay before an unaffordable
  stake. It defaults off for existing practice workflows and does not change the experimental live adapter.
- **CSV format:** select English/comma or Vietnamese-style semicolon/decimal-comma exports in Advanced settings. The append-only
  history log stays in the canonical comma format so restart recovery remains compatible. CSV, Excel and dashboard money retain
  up to six decimal places, including fractional-cent Banker commission. Existing rounded logs cannot be reconstructed.
- **Desktop sleep protection:** a running session prevents automatic application suspension, but not display sleep. Stopping
  the session or closing the app releases the blocker. This cannot override a closed laptop lid or forced system sleep.

Verification and delivery limitations: [current handoff](docs/current-handoff.md).

## Classic strategies

`src/engine/classic.ts`, selected in the page's strategy list. They follow the decisive results (a tie changes nothing)
and share everything else with IHG: the risk layer, keep-alive, reports, the Backtest and live betting.

| Strategy | Pick | Stake |
|---|---|---|
| Martingale | first side, then the last winner | a loss multiplies the stake (default 2), a win returns to the Base Unit |
| Grinding | the last winner | a win adds one unit, a loss keeps it; once the run is one unit up, back to one unit |
| Sequence | the listed sides in order; a loss moves on, a win starts over | steps through the stake list with the sides |
| Odd Even | alternates between the odd side and its opposite | stake list: a loss steps up, a win returns to the first |
| ODD Diff | the side that won more of the odd-numbered results; no bet while level | stake list |
| ODD Looser | the side that won fewer of them | stake list |
| ODD is God | the winner of the latest odd-numbered result | stake list |

A stake list is the page's **Manual Sequence Input**. Hands with no pick are shown as **NO BET**. Checked by
`test/classic.reference.test.ts` (the 19 client cases, and 35,000 hands played through the original C# reference engine;
where those traces came from is in `test/fixtures/README.md`).

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
| New shoe | Default `continue`, matching the supplied reference application. The user can explicitly select `reset_pattern` or `reset_all`. |
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
  wait-for-pattern, end of shoe), not during a manual PAUSE. On a live table in Confirm mode it is placed
  without asking for approval: it is the anti-kick minimum bet, and waiting for a click would defeat it.
- **Near shoe end** (from hand #N of the shoe, needs a feed that reports shoe boundaries: Simulator, or `|` in
  Replay): `No new runs` lets an open run finish and then only watches; `Cut open run` drops the open run at hand N
  and then only watches. A new shoe resumes betting.
- A manual **PAUSE** freezes all of the above (countdowns, waiting, idle counts), so RESUME continues exactly
  where it left off.

Decisions where the spec is silent (none confirmed by the client yet): keep-alive and pauses count ties as hands;
keep-alive amounts round up to the minimum chip and are excluded from the win rate; "from scratch" after
WAIT PATTERN is a full reset including pattern history.

The *Virtual Bet - start after N losses* filter follows the supplied reference implementation: IHG rearms it after
every closed/cut run; the other strategies are gated by the shared risk layer and rearm after a closed run.

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
- **Compare Settings**: comma-separated parameter lists generate up to 60 combinations, all run with one shared
  seed and ranked by ruin rate, P10 and mean. Oversized sweeps are refused before work starts and can be cancelled.
- **Speed**: about 1.8 million hands per second per core. The default 100 sessions x 500 shoes takes about 2 s;
  the page's maximum (2,000 x 5,000) runs on worker threads, shows progress and can be cancelled. Worker threads
  keep a live session's event loop free while a backtest runs.
- A backtest never changes the running session or the saved settings, and the Telegram token is not handed to the
  workers.
- Results are simulation output, not a forecast. No betting system changes the house edge, and the figures
  (especially the "bad day" and "worst drawdown" ones) are the point of running it before real money.

## Live table and betting

The **Live table** feed reads the road of a baccarat table open in your own Chrome (started with the launcher script,
which opens a debugging port on this computer only). In the default **Paper** bet mode nothing is clicked or bet.
Code: `src/live/` (Chrome DevTools client, profile, page reader, road tracker, bettor), `src/feeds/live.ts`.

**Betting** (`src/betting.ts`, `src/live/bettor.ts`) is opt-in: **Confirm** (you press PLACE BET) or **Automatic**. Both need a
largest bet and a session limit; a bet is placed once, only while the table shows bets open and nothing is on the table,
from the fewest chips, with real mouse clicks, and counts only when the table's own total shows the stake. An unconfirmed bet,
or a placed bet that differs from what the strategy would bet, **stops the session** with an alert. It never touches deposits,
tips or withdrawals, and it does not change the browser's identity.

Tested against a real headless Chrome with a mock table whose betting controls sit in frames from another site
(`test/live.test.ts`, `test/bettor.test.ts`, `test/betting.session.test.ts`; skipped when no Chrome is found).
**Not yet run on a real Roobet/Evolution table**: `trade247 --inspect-table` and `--check-table` calibrate the selectors, and a
`live-profile.json` in the data folder supplies them without a new program. Details:
[`docs/deployment.md`](docs/deployment.md), sections 6 and 10.

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
npm run electron:start    # the desktop app, from source
npm run electron:dist     # the desktop app packaged for THIS operating system -> release/desktop/
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
ui/index.html     client-derived dashboard, backtest comparison and interactive P&L chart
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
build-resources/  application icon (from the original application): icon.ico, icon.icns, icon.png
packaging/        Windows scripts (start, VPS scheduled task), macOS launcher, run instructions
docs/             deployment guide
test/             Vitest
```
