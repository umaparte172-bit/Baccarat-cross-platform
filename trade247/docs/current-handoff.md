# Telegram control, Selenium live driver, Mac install without Apple ID - 2026-11-20 (later)

## Implemented

- **Telegram alerts** have a fixed layout: icon and CATEGORY title, then *Category*, *Action* (what the bot did),
  *Purpose* (why), an optional *Detail*, then session figures, state, time and suggested commands
  (`src/notify/messages.ts`). New alerts: connection restored, session started/stopped, bet waiting for approval.
  The shoe-change *Action* states what the configured shoe-change policy actually does.
- **Telegram remote control** (`src/notify/control.ts`, setting *Commands from Telegram*, off by default):
  /status /history /settings /start (confirm button) /stop /pause /resume /approve /skip /retry /report /help, with
  inline buttons. Only the configured numeric chat ID is obeyed; commands older than the program start are ignored;
  approvals carry the bet's id so an old button cannot approve a newer bet; settings cannot be changed remotely.
- **Selenium live-table driver** (`src/live/selenium.ts`, setting *Browser driver*): ChromeDriver attached to the
  table Chrome via `debuggerAddress`; frames walked with WebDriver frame switching; clicks with WebDriver Actions.
  The live feed and bettor now use a driver-neutral `TablePage` interface; DevTools remains the default.
  `selenium-webdriver` is a runtime dependency; `selenium-manager` ships in the desktop app (asar-unpacked) and in
  the single-file release folder.
- **macOS without Apple ID**: `packaging/macos/Install-TRADE247.command` (inside the DMG) copies the app, clears the
  quarantine flag, verifies or re-applies the ad-hoc signature, and opens it. A new CI step simulates a downloaded,
  quarantined copy, installs it with the script, and runs the smoke test and selenium-manager from the installed app.

## Verified locally (Windows, Node 24.21)

- Typecheck passes. New tests: `test/control.test.ts` (10, fake Telegram server), approval-id test in
  `betting.unit.test.ts`, updated alert-format tests in `telegram.test.ts`.
- `test/selenium.test.ts`: 8/8 against the mock table in headless Chrome 157, using Electron's ChromeDriver 156 with
  `--disable-build-check` (the matching ChromeDriver could not be downloaded here: network blocked).
- `npm run electron:build` bundles Selenium and copies selenium-manager.

## Not verified

- The macOS install step and DMG contents run only on the macOS CI runner; nothing was pushed, so CI has not run them.
- Delivery to the client's real Telegram chat; the Selenium driver on a real casino table; client Mac acceptance.

# Completion and verification — 2026-11-20

## Implemented in the current working tree

- Session P&L chart: hover preview, click-to-pin, Escape/Close, statistics, per-hand details, range presets, zoom/pan and overview.
- Compare Settings: common dollar capital converted per base unit, same seed, sortable dollar results, bounded workloads, validation and USE-to-form without applying.
- Finite-capital backtests stop before an unaffordable stake; daily limits and keep-alive are excluded while session limits remain active.
- Optional practice balance guard for Simulator, Manual and Replay (off by default).
- Selectable CSV locale; exact fractional-cent payouts in state, dashboard, history, CSV and Excel. Old rounded historical entries cannot be recovered.
- Desktop application-suspension blocker while sessions run, released on stop/quit; display sleep is still allowed.
- Fixed the CI contract test that still required `macos-14` after the workflow moved to `macos-15`.
- Updated Selenium desktop checks for the chart and comparison workflow.

No strategy formulas were changed in this pass. The new tests exercise reporting, simulation capital accounting and desktop controls.

## Verified locally

- `npm run typecheck`: passed.
- `npm run build`: passed.
- `npm run electron:build`: passed; generated files are under `build/electron`.
- Full local test suite after the CI fixture fixes: 447 checks across 28 files, including all local Chrome mock-table suites.
- Updated source Electron Selenium checks: 13/13 passed.
- Rebuilt Windows installer and portable executable; packaged smoke checks and 13/13 packaged Selenium checks passed.
- Windows and Apple Silicon desktop CI jobs passed at `ab6e016`, including macOS packaging, signature verification, smoke checks and Selenium.
- The Windows mock-session suite was rerun after replacing click-arrival waits with application-confirmation waits: 12/12 passed.
- All four affected browser/startup suites rerun after the fixes: 51/51 passed. No production adapter changes were needed.
- Includes reference strategy traces, exact-money exports, backtest worker consistency, API validation, chart interaction model,
  comparison logic, sleep-blocker lifecycle, and the supported runner assertion.

```sh
npm test
```

No suites were excluded in this run. Browser tests use local mock tables, not casino services.
The UI logic tests use test doubles; the Selenium tests additionally verify native-window controls.

## Delivery

### Latest follow-up

GitHub access was restored and commits through `f8bf9a3` were pushed. In
[run 44560401434](https://github.com/umaparte172-bit/Baccarat-cross-platform/actions/runs/44560401434),
Linux verification and both Electron desktop jobs passed, including macOS signing verification, smoke and Selenium.
The separate command-line package jobs still failed: macOS at the mock Banker-click assertion, and Windows at an
obsolete smoke assertion for a removed English-format label. The latter was reproduced locally and fixed to execute
the actual number formatter; the rebuilt Windows command-line package now passes all 20 smoke checks. Its mock-table
acknowledgement race was also corrected. The macOS assertion now includes click/result diagnostics to identify its cause.

The workspace permission problem subsequently cleared without changing index permissions; those fixes were pushed
in `bd5ed79`. In run 44576516790 the Windows command-line package passed all checks. Additional diagnostics exposed
a missed chip-selection event in the fast (5–15 ms gap) mock test and a test starting before the browser rendered its
closed status. The test fixture now acknowledges rendered state explicitly, and the direct-click suite uses a fixed
60 ms gap (the production minimum) instead of sub-frame input timings. Production adapters and strategy rules are
unchanged. A new full CI run must verify these follow-up changes before the overall release can be called green.

### Earlier investigation

The application changes and original CI assertion fix were pushed in `ab6e016`.
Both desktop packages were produced and tested in [run 44542811417](https://github.com/umaparte172-bit/Baccarat-cross-platform/actions/runs/44542811417).
That run exposed additional timing races in the local mock fixture used by the full test jobs; the fixture now serializes
click processing, renders the result before returning, and ignores stale poll responses. This changes tests only.
Test-browser startup now waits for a responding endpoint for up to 20 seconds and includes process errors/stderr
in failures, instead of reporting only a generic ten-second timeout. A regression test covers a missing executable.
The follow-up commit must pass all five jobs; consult [the workflow](https://github.com/umaparte172-bit/Baccarat-cross-platform/actions/workflows/trade247.yml)
for the latest status rather than treating successful desktop jobs as a successful overall workflow.

The follow-up fixes are saved in local commit `b08d68b`, but its push was rejected: the active GitHub account
`newsfriend` lacks write access to this repository. The user is restoring access. No new CI run has verified that
commit yet. Downloading the earlier macOS artifact also failed (network timeout, then authentication failure);
no new local Mac installer is claimed delivered.

The old run's macOS failure was an obsolete assertion requiring
`macos-14` after the workflow moved to `macos-15`; the fix is included here. Windows mock tests had a race between
the test's table click and the application's acknowledgement; only the test synchronization was changed.

The local `release/desktop` Windows installer and portable executable now contain the application updates.
Mac packages are built on the Apple Silicon CI runner, not on this Windows workstation. Client-machine first launch
still needs confirmation; CI verification is not a substitute for testing the client's exact macOS environment.

## Previously verified delivery

Commits through `aec3037` were pushed. In [run 44465501565](https://github.com/umaparte172-bit/Baccarat-cross-platform/actions/runs/44465501565),
the Apple Silicon Electron desktop job passed packaging, signature checking and its earlier Selenium checks.
Other jobs were failing or still in progress at the last successful status read; the whole workflow is not claimed green.
The current source changes are newer than that run.

## Boundaries

- The verified product is a local practice/replay/backtest application.
- Selenium verifies the Electron desktop. The legacy `src/live` adapter still uses CDP; it has not been migrated or validated against a casino.
- Real-money casino automation, fingerprint spoofing, and bot-detection evasion are not implemented as part of this work.
- Telegram tests use a controlled test endpoint; delivery to the client's actual Telegram account remains unverified.
- Ad-hoc signing is not Apple notarization. A downloaded, unnotarized app may require macOS first-open approval; zero-intervention deployment is not promised.

No unrelated user files were changed.
