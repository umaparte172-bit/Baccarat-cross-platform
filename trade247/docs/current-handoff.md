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
- Full local test suite: 446 checks across 27 files, including all local Chrome mock-table suites.
- Updated source Electron Selenium checks: 13/13 passed.
- Rebuilt Windows installer and portable executable; packaged smoke checks and 13/13 packaged Selenium checks passed.
- Windows and Apple Silicon desktop CI jobs passed at `ab6e016`, including macOS packaging, signature verification, smoke checks and Selenium.
- The Windows mock-session suite was rerun after replacing click-arrival waits with application-confirmation waits: 12/12 passed.
- Direct browser fixture and session suites rerun after synchronizing mock click updates: 29/29 passed. No production adapter changes were needed.
- Includes reference strategy traces, exact-money exports, backtest worker consistency, API validation, chart interaction model,
  comparison logic, sleep-blocker lifecycle, and the supported runner assertion.

```sh
npm test
```

No suites were excluded in this run. Browser tests use local mock tables, not casino services.
The UI logic tests use test doubles; the Selenium tests additionally verify native-window controls.

## Delivery

The application changes and original CI assertion fix were pushed in `ab6e016`.
Both desktop packages were produced and tested in [run 44542811417](https://github.com/umaparte172-bit/Baccarat-cross-platform/actions/runs/44542811417).
That run exposed additional timing races in the local mock fixture used by the full test jobs; the fixture now serializes
click processing, renders the result before returning, and ignores stale poll responses. This changes tests only.
Test-browser startup now waits for a responding endpoint for up to 20 seconds and includes process errors/stderr
in failures, instead of reporting only a generic ten-second timeout. A regression test covers a missing executable.
The follow-up commit must pass all five jobs; consult [the workflow](https://github.com/umaparte172-bit/Baccarat-cross-platform/actions/workflows/trade247.yml)
for the latest status rather than treating successful desktop jobs as a successful overall workflow.

GitHub access and approval review are now available. The old run's macOS failure was an obsolete assertion requiring
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
