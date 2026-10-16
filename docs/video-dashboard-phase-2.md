# Video-led Phase 2 — unified dashboard

Date: 2026-10-16. This report covers Phase 2 of the new seven-phase plan, not the
older static-analysis Phase 2. Reference: [Phase 1 specification](video-reference-phase-1.md).

**Correction after client review:** the separate-window/separate-data approach
described below has been replaced. [Tools integration](integrated-tools.md) now
reuses the existing feature views and selected-session engine inside one window.
This report retains the original delivery history, not the current Tools design.

## Implemented

- The application starts in a light dashboard with an empty state and New Session.
  No account, credential-store lookup or server connection is required.
- A toggleable session sidebar, synchronized strategy subtitles and a selected
  session/shoe/hand heading replace the old navigation-card entry point.
- Independent in-memory sessions use the existing Core recorder for manual
  Banker, Player and Tie results. Per-hand pair flags are captured and clear after
  entry. Undo updates history/counts. CSV export snapshots the chosen session
  before opening the picker, and includes all outcomes, pairs and timestamps.
- All eight strategy editors are present, with PATTERN initially selected.
  Common values and strategy-specific drafts survive session/strategy switches.
  Enter or Apply settings validates fields without fabricating recommendations.
  Blank optional settings remain unspecified; placeholders are not algorithm defaults.
- Counts and outcome percentages update immediately. History is newest-first and
  displays the latest 100 rows; full records remain available in CSV.
- Next hand, balance/P&L, five roadmap regions, Ask, history and statistics follow
  the reference hierarchy, with unavailable calculations explicitly labeled.
- Desktop settings and next-hand panels sit side by side; narrow layouts stack
  them. The content scrolls vertically and, when needed, horizontally. Buttons
  center text on both axes. Inputs have automation labels and keyboard focus.
- Existing tools opens a single owned window directly into offline Home. Existing
  account screens, Remember Me, strategy import/manual fallbacks, simulation and
  viewer are preserved. Closing/reopening this window works; closing the dashboard
  closes it and invokes its existing simulation cancellation behavior.

## Deliberate boundaries

| Available now | Deferred, not represented as working |
| --- | --- |
| Session creation/switching, manual results, pair metadata, Undo, CSV | Phase 3: durable storage, New Shoe carry-over, bankroll ledger and remaining statistics |
| Validated per-session strategy drafts | Phase 4: dedicated automatic strategies, verified PATTERN branches, stop/pause rules |
| Five named roadmap regions and Ask area | Phase 5: actual plotted roads and prediction indicators |
| Access to preserved existing tools | Phase 6: shared data/integration with the new dashboard |
| Local Windows build and headless rendering | Phase 7: final macOS packaging and client-device validation |

New Shoe is disabled with its reason visible; Stop and pause expands an explanation.
An em dash means unimplemented, not a zero financial result. Manual records are
not wagers. There is no live betting or new backend. Data is in memory only,
limited to 100 sessions and 10,000 hands per session; closing loses that data.
The persistent-session work remains Phase 3, not hidden behind an implied save.

The new dashboard and the existing tools intentionally keep separate state during
migration. No automatic import of the old account or strategy session occurs.
PATTERN/IHG and run-limit ambiguities from Phase 1 are still unresolved; this
phase does not choose a financial rule based on the conflicting video notes.

## Code map

- `src/Baccarat.Desktop/DashboardWindow.cs`: startup, owned tools-window lifecycle.
- `src/Baccarat.Desktop/Views/DashboardView.cs`: layout and control wiring.
- `src/Baccarat.Desktop/ViewModels/DashboardWorkspace.cs`: session ownership,
  drafts/validation, pair metadata and export, without Avalonia dependencies.
- `src/Baccarat.Desktop/DashboardUi.cs`: light dashboard presentation helpers.
- `tests/Baccarat.Desktop.Checks/DashboardChecks.cs`: behavior/layout regression checks.

Core → Infrastructure → Desktop dependency direction is unchanged. Future financial
and roadmap algorithms belong in Core, not in control event handlers. Local save
adapters belong in Infrastructure, not in this presentation draft model.

## Verification

Local results: Release solution build **0 warnings / 0 errors**; Core checks,
Desktop checks (including both headless behavior and Skia-rendered mode), formatting
verification, all **15 Python analysis tests**, and `git diff --check` passed.
An early dashboard test exposed deferred text-change handling; field drafts now
update synchronously on the Text property before Apply/Enter reads them.

The dashboard checks cover no-login entry, independent sessions, pair capture and
clearing, Undo, CSV pair fields, counts, rejected invalid settings, Enter-to-apply,
per-strategy drafts, un-applied draft preservation, all eight selectors, synchronized
labels, sidebar toggle, 800×600 layout, existing-tools reuse/reopen/close, foreign
session rejection, amount/list validation, and session/hand capacity boundaries.
Existing account, Remember Me, strategy, simulation and viewer checks are retained.

Rendered headless screenshots are generated with the real Skia renderer (not
blank headless drawing) and kept under ignored `artifacts/dashboard-phase2`:
`dashboard-empty.png`, `dashboard-pattern.png`, `dashboard-odd-is-god.png`,
`dashboard-compact.png`, and `dashboard-history.png`. These are Windows-rendered previews, not proof of native
macOS rendering or client acceptance. The original supplied EXE is never executed.

Reproduce the desktop checks with:

```powershell
dotnet run --project tests/Baccarat.Desktop.Checks -c Release
# Optional: enable rendered previews in addition to behavioral checks.
$env:BACCARAT_SCREENSHOT_DIR = 'F:\New folder\artifacts\dashboard-phase2'
dotnet run --project tests/Baccarat.Desktop.Checks -c Release
```

Use an output directory appropriate to your machine. The existing CI desktop-check
step automatically includes DashboardChecks. CI execution and macOS acceptance
must not be inferred from local test success.

## Delivery status

Implementation and checks are saved locally. An elevated staging attempt still
fails with `fatal: unable to write new index file`. Repository permissions were
not altered. No commit, push or new CI run was completed for this phase.
Earlier Phase 1 and Remember Me work remains preserved in the working tree.
