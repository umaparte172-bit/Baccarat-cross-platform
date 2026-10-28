# TRADE247-V1 delivery plan

Status: draft for client sign-off. Supersedes the Wetalkbet clone direction in `README.md` and `TODO.md`
for new work. The existing .NET code under `src/` stays as an archive; nothing there is deleted.

Sources: the client PRD, the client's `Original Application/index.html` (the UI reference and the de-facto
API contract), the IHG evidence in `docs/strategy-9-ihg-evidence.md`, and the 315 extracted IHG hands in
`artifacts/ihg-review/reference.json` (local, git-ignored).

## 1. Scope

### In scope
- Local bot for macOS Apple Silicon and Windows (including a Windows VPS running 24/7).
- The client's `index.html` as the UI, unchanged apart from number format (English `1,234.56`, not `1.234,56`).
- Strategies: IHG, AME-AI, JJ (all three are in the HTML selector).
- Risk and money management, shoe guard, keep-alive, virtual bets, take profit and stop loss.
- Feeds: Simulator (8-deck), Manual (P/B/T buttons), Replay (letters or CSV), then Live table.
- Backtest window, Excel/CSV log (`trade247_session_history.csv` plus `.xlsx` on demand), Telegram alerts.
- Native executables for macOS arm64 and Windows, full source, README.

### Out of scope (stated to the client in writing)
- Anti-detection: fingerprint spoofing (WebGL, canvas, user-agent), DataDome/Cloudflare bypass, stealth
  plugins, and mouse movement designed to evade bot checks. The live layer uses the real browser as it is.
- Casino credential handling. The user logs in by hand in the browser the bot controls.
- Apple Developer certificate and notarization (ad-hoc signing only).
- Any login, license, activation or online authentication of the app itself.

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Language | Node 22 + TypeScript | Node 22 is installed here, Python is not on PATH. The UI is already web, so one language end to end. Playwright is first-class in Node. The client allowed Python or Node. |
| HTTP server | Fastify, bound to `127.0.0.1` | Implements the API that `index.html` already calls. |
| Browser automation | `playwright-core` driving the user's installed Chrome or Edge | No bundled browser to ship. Reuses the idea of the earlier C# `BrowserLocator` (now in git tag `legacy-csharp`). |
| Tests | Vitest, plus Playwright for UI end-to-end | |
| Report | `exceljs` for `.xlsx`, hand-written CSV | |
| Packaging | esbuild bundle, Node single-executable (SEA) per OS, built by a GitHub Actions matrix (`macos-14`, `windows-latest`) | A SEA binary must be built on its own OS. `.github/` already exists. |
| New code location | `trade247/` at the repo root | Keeps the archived .NET work intact. |

If the client insists on Python, only the backend changes; the engine spec, tests and UI do not.

## 3. Architecture

```
trade247/
  ui/index.html            client's page (copied, number format changed)
  src/
    server/                Fastify routes = the /api contract (section 4)
    engine/                PURE, deterministic, no I/O
      types.ts  ihg.ts  ame.ts  jj.ts  stakes.ts  risk.ts  shoe.ts  session.ts
    feeds/                 simulator.ts  manual.ts  replay.ts  live.ts   (one interface)
    live/                  browser.ts  profile.ts  reader.ts  bettor.ts  guard.ts
    backtest/              worker.ts  stats.ts
    report/                csv.ts  xlsx.ts
    notify/                telegram.ts
    store/                 settings.ts  secrets.ts  sessions.ts  paths.ts
  test/                    unit, golden, e2e, fixtures/
```

Key rule: `engine.step(state, hand) -> { state, row }` is a pure function. The live bot, the simulator
and the backtest all call the same function. This is what makes the backtest trustworthy and the strategies
testable without a casino.

Data lives in the OS app-data folder (`~/Library/Application Support/Trade247`, `%APPDATA%\Trade247`):
settings, sessions, logs, and `secrets.json` (Telegram token, file mode 0600, never logged, masked in the UI).

## 4. API contract (read from `index.html`)

All requests carry `X-T247: 1`. The server also checks the `Host` and `Origin` headers (defence against
DNS rebinding and cross-site requests).

| Endpoint | Used for |
|---|---|
| `GET /api/state?since=N` | Polled every 700 ms. Returns state plus only the history rows after hand N. |
| `POST /api/start` `{settings}` | Start a session. Response `{ok, status}`. |
| `POST /api/apply` `{...settings}` | Apply to a running session. Response `{live}`. Strategy switch takes effect from the next hand. |
| `POST /api/stop`, `/api/pause`, `/api/resume` | Pause places no real bets but keeps watching. |
| `POST /api/retry` | Reconnect the result feed without losing the session. |
| `POST /api/reset` | Fails while running; returns `{ok, saved}` with the saved report path. |
| `POST /api/report/open` `{fmt}` and `GET /api/report?fmt=` | Open in the default app, or download the same snapshot. |
| `POST /api/settings`, `POST /api/telegram_test` | Save settings; send a test message. |
| `POST /api/manual` `{result}` | Manual feed: P, B or T. |
| `POST /api/backtest/start`, `GET /status?id=`, `POST /cancel` | Backtest job. |

`state` fields the page reads: `settings, running, paused, status, error, next{mode, pick, units, stake,
final, pattern, reason}, hands, virtual_hands, win_rate, day_pnl, max_drawdown, max_stake_units,
strategy_state, balance, pnl, session, session_no, started_ts, stopped_ts, now_ts, logs_dir, rows,
total_rows`.

`rows[]` fields: `n, time, pick, mode (REAL | VIRTUAL | MIN), stake, result, outcome (WIN | LOSS | TIE |
V-WIN | V-LOSS | V-TIE), pattern, note, single_wins, run_net_units, session_pnl, cards, session`.

Settings keys: everything with `data-key` in the page, plus `strategy, unit_value, chain, progression
(recovery | list | flat | dynamic), keepalive_enabled, keepalive_amount, entry_loss_filter, take_profit,
stop_loss`.

## 5. Strategy specifications

### 5.1 IHG (well specified, testable now)
- Ties are ignored for selection.
- Selection: no data, bet Player. One result, repeat it. Otherwise look at the last two results: a streak
  (PP or BB) is followed, a chop (PB or BP) is opposed.
- 2-2 rule: if the last four results are XXYY, follow the 2-2 pattern, keeping its phase until the prediction
  loses, then return to the two-result rule. Pattern labels describe the state after the row's outcome.
- Money (linear recovery, not Martingale): first bet of a run is 1 unit. While a run is open and a bet wins,
  next stake is `ceil(max(0, -run_net)) + 1` units. The rounding step is configurable (`round_step`, 1 by
  default). Banker wins pay 0.95, Player wins pay 1. Decimal arithmetic only.
- A run closes and resets to 1 unit when its net profit is above 0. Total profit is not erased.
- Single wins (a win between two losses): count them; at the limit (default 7, custom allowed) switch to
  virtual bets. Resume real betting after 2 consecutive virtual wins.
- Max unit has two distinct meanings in the HTML and both are implemented:
  - Run-end stake (document value 45): the stake is placed in full, then the run is closed win or lose.
    The IHG document places 51, 60 and 81 unit bets, so this is not a strict cap.
  - Hard cap: a stake above it is never placed; the run is dropped and the stake returns to base.
- Simultaneous run-limit and single-wins-limit: immediate virtual pause (approved by the client on
  2026-10-18, recorded in `strategy-completion-2026-10-17.md`).
- Min chip: stakes round up to `min_chip`.

Acceptance oracle: replay the 315 hands (`artifacts/ihg-review/reference.json`) with the "IHG document
settings" preset (`round_step 1`, run-end 45, single wins 7, shoe change = continue). Every row's pick,
stake, P&L, run net and balance must match. The two known source conflicts (shoe 146 hands 3 and 4) are
asserted explicitly, not hidden.

### 5.2 AME-AI (needs the client's formulas)
Known from the PRD and HTML: scan window (default 12, minimum 6), four rhythm periods (streak, chop,
3-cycle, 2-2), confidence from frequency, lag (recency-weighted) and inertia, entry at confidence >= 70%
otherwise virtual, chaos filter when entropy >= 0.92 and fit < 0.70, freeze 4 hands with virtual probes.
Settings: stable units, recovery rate, max step, flat vs dynamic.

Unknown: the formulas for frequency, lag, inertia, fit and entropy, how "period p" maps to a prediction,
and how recovery rate and max step shape the stake. Plan: ask the client first. If he cannot supply them,
write a one-page interpretation spec with worked examples and get his written approval before coding.
We do not invent the algorithm silently.

### 5.3 JJ (needs the client's meaning)
Selection follows IHG patterns; stakes follow a cycle. Presets in the HTML are tuples
`[hands per cycle, increase from %, up to %, stake cap x base, reset after hands]`:
JJ20 `[10,5,20,2,60]`, JJ30 `[10,10,350,10,100]`, JJ50 `[10,10,98,12,250]`, JJ70 `[10,10,10,0,0]`,
JJ80 `[100,20,20,0,0]`. Settings also include a ramp (cycles). The exact stake formula is unknown; same
procedure as AME-AI (ask, otherwise spec and approval).

## 6. Cross-cutting behaviour

- Shoe change guard: PRD says cut the losing sequence and reset to base on a new shoe. The IHG document says
  continue across shoes. The HTML has a policy selector (reset pattern / reset pattern + run / continue).
  Default to the PRD behaviour (reset pattern + run); the "IHG document settings" button selects continue.
- Near-shoe-end policy: no new runs / cut open run / off, from a configurable hand number.
- Keep-alive: minimum bet on the Nth consecutive hand without a real bet (setting `keepalive_idle`).
- Virtual bet entry filter: real stakes start only after N consecutive virtual losses.
- Consecutive losses: stop (end session, or wait for pattern: virtual until 2 wins) and pause for N hands.
- Take profit and stop loss per session; daily stop loss across all of the day's sessions (persisted per
  local calendar day). Daily take profit from the PRD is added as a daily setting.
- Telegram on: daily stop loss, daily take profit, disconnection, shoe change.
- Logging: every hand appended to `trade247_session_history.csv` immediately (Time, Strategy, Bet, Result,
  PnL and the other row columns); `.xlsx` generated on demand.

## 7. Live table layer (read first, bet later)

Principles: passive reading, the user's own logged-in browser, no stealth, every action bounded.

1. Browser: launch the installed Chrome/Edge with a persistent profile folder; the user logs in manually.
   Headed by default. A `headless` option exists but is documented as unsupported by some sites. Playwright
   input is sent through the debugging protocol, so it does not take over the physical mouse.
2. Table profile (JSON per site): frame URL pattern, detection text/selector, result selectors, chip
   selectors, bet area selectors, shoe-change signal. Plan reuses the idea in the existing
   `docs/live-table-integration.md`.
3. Reader: subscribe to the page's WebSocket frames (`page.on('websocket')`) where the profile allows,
   fall back to DOM reading. De-duplicate results, detect round boundaries and shoe changes, surface
   "feed lost" to the UI (status pill) and to Telegram, and reconnect with back-off (Retry Table does this
   on demand).
4. Bettor (only after the reader is proven against real tables): select chips, click the bet area, verify
   the bet from the table (balance change or confirmation), never retry blindly. An unverifiable bet is
   `Indeterminate`: stop betting and alert, so there is never a double bet.
5. Guard rails (mandatory in live mode): max stake, max session stake, max chip clicks per bet, covered-control
   check (never click through a pop-up), auto-bet toggle off by default, kill switch (STOP) always works.
6. Timing: a fixed, configurable settle delay before clicking (default 500 ms) so the UI is ready. No
   randomised human-mimicry.

Blocker: the target site, table URL, and a logged-in session to inspect. Without them this stage cannot be
calibrated, and any estimate for it is a guess.

## 8. Milestones

Estimates are working days of focused work, rounded; they assume the client answers within 2 days.

| # | Milestone | Days | Exit criteria |
|---|---|---|---|
| M0 | Alignment and setup | 1-2 | Client answers the open questions; repo scaffold, CI skeleton, plan signed off. |
| M1 | Skeleton and UI wiring | 3-4 | `index.html` loads from the local server; Start/Stop/Pause/Retry/Reset work with the Manual and Simulator feeds; table, timer, cards and balance render; API contract tests pass. |
| M2 | IHG engine | 4-6 | The 315-hand oracle passes; property tests pass (balance = start + sum of P&L, runs close at net > 0, virtual rows have zero P&L). Demo 1 to the client: IHG running in his own UI on the simulator. |
| M3 | Risk and operations layer | 4-5 | All section 6 behaviours covered by tests; Apply-while-running works; daily limits survive a restart. |
| M4 | Persistence, reports, Telegram | 3 | CSV written per hand; `.xlsx` and CSV open from the Excel Log button; Telegram test and the four alert triggers verified against a fake API. |
| M5 | Backtest | 3 | Worker-thread backtest returns every KPI, histogram and curve the page renders; cancel works; seeded runs are reproducible. Demo 2. |
| M6 | AME-AI and JJ | 5-8 | Starts only after the client's formulas or an approved interpretation spec. Golden vectors approved by the client; chaos filter and presets covered by tests. |
| M7 | Live reader (no betting) | 6-10 | Results from a real table match what is on screen for 200+ consecutive hands; shoe change detected; feed loss and recovery verified. |
| M8 | Live betting | 4-6 | Bets placed and verified on the client's chosen table with minimum stakes; kill switch tested; no double bets under forced faults. |
| M9 | Packaging and acceptance | 4-5 | Mac arm64 binary and Windows exe from CI; Windows VPS runbook (auto-login, scheduled task at logon, crash recovery); README; source delivered; client acceptance checklist signed. |

Rough totals: paper-trading product (M0-M5) about 3 weeks; with AME-AI/JJ and live, about 6-8 weeks
total, depending mostly on the client's answers and table access.

### Progress

| Milestone | Status |
|---|---|
| M0 Alignment and setup | Done (scaffold, tests, README). The client's questions in section 11 are still unanswered. |
| M1 Skeleton and UI wiring | Done. |
| M2 IHG engine | Done. 315-hand oracle passes; 88 tests in total; six deliberate engine mutations are all caught. |
| M9 Packaging and acceptance | Mostly done. Single-file Windows program built and smoke-tested (16 checks) here, including from a zip extracted elsewhere; embedded page and embedded worker thread verified; scheduled-task scripts for a Windows VPS with dry-run and guards; deployment guide; CI workflow for Windows and Apple-silicon macOS. **Not verified here:** the macOS build (needs a Mac or the CI run), the scheduled task actually registering (only dry-run tested), and Excel opening the .xlsx. 254 tests. Left for later: signing (optional), auto-resume of a session after a restart (to be decided with live trading). |
| M5 Backtest | Done. One shared `PlayCore` runs live and simulated sessions; ten risk setups replayed through both paths give identical results. Worker threads, progress, cancel, reproducible by seed whatever the thread count. About 1.8M hands/s per core; the default 100 x 500 run takes ~2 s. 242 tests; 16 deliberate mutations of the backtest code are all caught (two initially survived and were closed). Packaging note for M9: the worker file must ship beside the program, and a single-file executable needs the worker handled specially. |
| M4 Persistence, reports, Telegram | Done. Excel and CSV reports (from the live session or the history file), report snapshots, four Telegram alert triggers verified against a fake API, plus one live call to the real Telegram API that correctly returned "Unauthorized" for a made-up token. 165 tests; 16 deliberate mutations all caught. The .xlsx passes Microsoft's Open XML validator but has not been opened in real Excel: please confirm on a machine that has it. |
| M3 Risk and operations layer | Done except the Virtual Bet entry filter, which waits for the client's answer (once per session or before every run?). 110 tests; ten deliberate mutations of the risk layer are all caught. |
| M7 Live table, read-only | Done in its read-only form (no betting). Reads the road of a table open in the user's own Chrome through its debugging port, including tables inside frames from another site; new hands only, new-shoe detection with a two-reading confirmation, lost / restored table wired to the Telegram alert, Retry Table, `--inspect-table` calibration capture, `live-profile.json` override. Tested against a real headless Chrome and, packaged, by the smoke test. **Not checked against a real Roobet/Evolution table**: the selectors come from the original app's 2023 strings and an earlier calibration, and need the client's capture. 278 tests; 12 deliberate mutations caught. Findings from the original app: [`original-app-reverse-engineering.md`](original-app-reverse-engineering.md). Everything still needed from the client: [`client-questions.md`](client-questions.md). |
| M6a Classic strategies | Done. Martingale, Grinding, Sequence, Odd Even, ODD Diff, ODD Looser and ODD is God, ported from the C# project: the 19 written client cases pass, and 35,000 hands (280 scenarios) played through the C# reference engine (the C# runner is in git tag `legacy-csharp`, see `trade247/test/fixtures/README.md`) match pick for pick, stake for stake, balance for balance. Hands with no pick are "NO BET". 13 deliberate mutations of the engine caught. AME-AI and JJ (the rest of M6) still wait for the client's formulas. |
| Desktop app (Electron) | Done for Windows: the app window, keychain-sealed Telegram token, close-while-running warning, one instance, same data folder; the packaged app (asar) passes its own smoke test (page, API, replay, backtest on worker threads), and the NSIS installer and portable exe build. The Mac `.dmg` is built by the CI job and has not been run on a Mac. |
| M8 Live betting | Built, tested on a mock table in a real Chrome (real mouse clicks through frames from another site), **not run on a real casino table**. Modes Paper (default) / Confirm / Automatic; largest-bet and session limits are mandatory; one bet, placed once, confirmed only by the table's own total; unconfirmed bets and placed-versus-strategy mismatches stop the session with an alert; pause waits for the result; `--check-table` calibrates the profile without clicking. The anti-detect items of the PRD are not built. Needs the client's table capture and a first supervised test. |

Section 11 updates from M2: question 5 is answered by the document itself (Example 4 keeps the run's debt after
a virtual pause and resumes at one unit; Example 3's text says a fresh run starts after a run-end pause).
Questions 4 and 6 are still open; the engine uses the defaults listed in `trade247/README.md`.

## 9. Test strategy

- Unit and golden tests on the pure engine: the IHG oracle, boundary cases (rounding of 1.05 to 3 units,
  45-unit run end, hard cap, 7th single win, 2-win resume).
- Property tests with seeded random shoes: accounting invariants hold for every strategy and setting.
- API contract tests: every endpoint and field the page reads.
- UI end-to-end with Playwright: load the page, run a manual feed, check rows, balance, pill state, report menu.
- Live reader: record real frames once, replay them as fixtures so reader tests need no casino.
- Packaging smoke test in CI on both OSes: binary starts, serves the page, runs a 50-hand simulation.

## 10. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| AME-AI and JJ formulas never arrive | M6 blocked | Interpretation spec plus written approval; ship IHG first. |
| Live table is brittle (markup/protocol changes) | Reader breaks | Profile files, recorded-frame tests, loud failure plus Telegram alert. |
| Operator bans bots even without stealth | Account loss | Stated to the client in writing; paper mode is the default; low default limits; auto-bet off by default. |
| PRD vs HTML vs IHG document disagree | Rework | Section 11 questions answered in writing before M2/M3. |
| Headless unsupported by the site | VPS plan fails | Headed browser in a VPS desktop session is the supported path; headless is best-effort. |
| Unsigned Mac app shows a Gatekeeper warning | Install friction | README shows right-click Open, or `xattr -d com.apple.quarantine`. |

## 11. Questions to settle in writing

1. AME-AI formulas (frequency, lag, inertia, fit, entropy; meaning of period p; recovery rate; max step).
2. JJ stake formula behind the five presets.
3. Live target: site, table URL, and a way to inspect a logged-in session.
4. Shoe change: PRD reset (default) or IHG-document continue?
5. After virtual mode ends: PRD says reset the sequence; the document keeps the unfinished run debt. Which?
6. Single wins: confirm the HTML meaning (a win between two losses) over the PRD's "L-W-L" wording.
7. Take profit and stop loss: per session, daily, or both (default: both, as above).
8. Python or Node (default: Node).
9. Is V1 paper-trading plus read-only live, with betting as a separate sign-off?

## 12. First week

1. Day 1: send the reply and the questions; create `trade247/` scaffold, lint, Vitest, CI skeleton.
2. Day 2: copy `index.html`, switch number format, implement `/api/state` and `/api/start|stop` with a stub engine.
3. Day 3: Simulator and Manual feeds, row model, session timer, reset/retry/pause.
4. Day 4: contract tests plus Playwright end-to-end for the above.
5. Days 5-6: IHG selection and staking, first pass at the 315-hand oracle.
