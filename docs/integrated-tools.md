# Correction: one application, shared Tools

2026-10-16. The user clarified that the dashboard must reuse the previous
version's features through Tools, not fork them into another application.
Phase 3 additions were paused to correct this integration first.

## Implementation

`DashboardWindow` owns one navigation surface. Tools opens its menu in that
window; Strategies, Automation, Viewer, Account and confirmed session clearing
are reachable there. Back to Tools and Back to dashboard preserve state.
No separate MainWindow is opened. The old MainWindow remains in source for
existing regression coverage, not as an independent Tools workspace.

Each session owns its existing `StrategySessionEngine` after import. The retained
StrategyView binds to that session instead of owning another engine. Both entry
surfaces record each outcome once, preserving pairs. Undo on either surface removes
the same record and rolls back the matching engine snapshot. Protected PATTERN
rules still require manual decisions; dashboard entry cannot bypass them.

The dashboard displays the actual imported name, initial amount, rule descriptions,
next recommendation, settlements, calculated balance and profit. Imported rules
take precedence over video-settings drafts; draft controls cannot override an
active engine. This reuses existing supported behavior, not newly recovered rules.

`SessionTools` supplies the selected session with the existing simulation and
versioned viewer. Changes from either screen publish snapshots. Each session has
its own viewer channel and simulation state; another session cannot inherit old
snapshots. Switching sessions, loading/resetting a strategy, signing out or closing
cancels pending simulated approvals. Session switches also disable auto-bet.
Opening Tools alone does not reset data or authorize a simulated bet.

## History and reset contracts

- Loading/resetting begins a new strategy run without deleting session results.
- Captured settlements retain their run identity. Undo across a run boundary
  removes an old result without rolling back the new engine.
- Strategy tools show/export the current run. Dashboard profit/export covers the
  whole session, including prior runs and pairs. CSV preserves exact decimal values.
- Records created before importing a strategy remain observations, not retroactive bets.
- Starting bankroll cannot change after results exist. Balance is initial bankroll
  plus recorded strategy profits, not a casino account balance.
- Clearing requires in-window confirmation and affects only the selected session.
  Cancel preserves everything; confirmed clear retains the reusable definition.

Account, Sign In/Sign Up, Remember Me and Sign Out remain under Tools → Account.
The default original-server adapter is still unavailable; dashboard access never
requires it. Only local simulation is enabled. No backend or real bets were added.

## Verification and remaining work

Expanded desktop checks cover same-window navigation, feature-view reuse,
bidirectional record propagation, shared Undo, recommendations/profit, isolated
viewer snapshots, approval/session-switch cancellation, manual PATTERN gating,
reset/undo across runs, clear/cancel, signup fields and close-time cancellation.
Earlier account, Remember Me, strategy, viewer and automation checks remain.
Rendered previews are under ignored `artifacts/integrated-tools`, including
`tools-menu.png` and `dashboard-integrated.png`.

Durable storage, shoe transitions and remaining Phase 3 statistics were not
part of this correction; they are now covered in the subsequent
[Phase 3 implementation](video-phase-3-persistence.md). Roadmaps and unrecovered rules remain pending.
These checks do not establish original-app, native macOS or server parity.

Local verification: Release build passed with zero warnings/errors; Core and
Desktop checks, rendered headless UI checks, formatting verification, all 15 Python
analysis tests and `git diff --check` passed. At this correction checkpoint staging
failed with `unable to write new index file`; no commit, push or CI run was completed
then. The subsequent Phase 3 elevated staging attempt succeeded, allowing the pending
integration work to be included with Phase 3. Git permissions were not changed.
