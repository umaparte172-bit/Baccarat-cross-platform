# Video-led Phase 3: shared ledger and local recovery

2026-10-16. This extends the corrected single-window Tools architecture. It does
not replace the existing strategy engine or add old-server communication.

## Implemented behavior

- Each session retains timestamped hands, pairs, captured settlements, strategy-run
  identity, settings drafts, selected strategy and shoe boundaries.
- New Shoe starts fresh shoe counts and clears pair-entry flags. It retains the
  session ledger, bankroll and strategy progression. An empty shoe cannot be
  followed by another empty shoe. Undo on an empty new shoe reverses the boundary;
  otherwise it removes the last hand and its matching engine snapshot.
- New Shoe and reversing a shoe boundary cancel pending simulation and pause it.
- Current-shoe B/P/T counts and percentages include ties. Streak and chop use
  decisive results only: ties do not break either; chop is the length of the
  alternating suffix, or zero if fewer than two alternating outcomes exist.
- Session statistics show recorded bets, wins/losses/pushes, total stake, peak
  balance, current drawdown and maximum drawdown. Peak starts at initial bankroll;
  drawdown is peak minus balance. Observations and paused rounds are not bets.
- Payouts remain exact decimal Player 1:1, Banker 0.95:1, tie push, using the
  existing engine. Prior-run settlements survive strategy resets. CSV adds shoe
  and shoe-hand numbers and preserves the full session, not only visible rows.

**Evidence boundary:** New Shoe reset semantics and shoe-versus-session financial
scope are not conclusively demonstrated by the video. The policies above are
explicit, reversible implementation choices, not claims of recovered parity.
Automatic PATTERN and other protected rules remain Phase 4 work; no rule was guessed.

## Persistence and safety

The default shell uses `BaccaratWorkspace/workspace-v1.json` under
`Environment.SpecialFolder.LocalApplicationData`. Its exact path appears in the
footer. No login is required. Tests inject isolated temporary files or disable storage.

The version-1 document contains sessions, selected session, all strategy drafts,
applied settings, pairs, shoe boundaries, ledger, active strategy definition and
pre-hand decisions. The Core engine replays these decisions through its existing
calculations to rebuild the current recommendation and Undo snapshots. Deserialization
also recalculates ledger settlements and compares active-run rounds against replay.
Manual side/stake choices, pending manual resolution, pauses, stops and Undo survive restart.

Autosave is debounced by 750 ms. An immutable capture is made on the UI thread;
file writes run on a worker. Later edits schedule a subsequent save. Normal closing
flushes pending changes. A failed save leaves changes in memory and presents a
return-or-explicit-discard choice; it does not silently close. Navigation-only
changes do not rewrite identical archive bytes.

The store bounds files to 32 MiB, supports at most 100 sessions and 10,000 hands per
session, rejects unknown versions, duplicate/unknown/missing fields, malformed rules,
inconsistent money totals and bad boundaries. A single-writer file lease prevents
another app instance from replacing this workspace. Saves flush a unique temporary
file and atomically replace the primary, retaining the previous save as `.bak`.

If primary loading fails, a valid backup can be viewed immediately but saving
requires **Tools → Save recovered workspace**. This copies the rejected original
to a unique `.rejected-...` file before restoration, retaining the backup. If no
valid copy exists, writes are blocked and a warning identifies the affected path.
No empty workspace silently replaces corrupt or unsupported files.

These are local plaintext records, not tamper-proof financial/account records.
Do not share them if their session history is private. Passwords, account tokens,
simulation connections, pending approvals and auto-bet enablement are excluded.
Simulation always starts disconnected after restart. Forced termination can lose
changes since the last completed save; ordinary closing flushes first.

## Verification

Automated checks cover exact decimal settlement/statistics, reversible shoe
boundaries, ties in chop/streaks, archive/CSV roundtrips, selected drafts/pairs,
manual-choice Undo after restart, strategy reset boundaries, paused/stopped/manual
states, invalid schemas/properties/values, changed totals, capacity rejection,
backup recovery and retained rejected files, concurrent-writer rejection, real
replacement failure preserving the last save, and close-time flush/discard guards.
Existing account, Remember Me, shared Tools, viewer and simulation checks remain.

Local Windows build and checks use a separate ignored output folder because a
running app locks the normal build folder. Native macOS installation/runtime checks
remain Phase 7; this is not a macOS acceptance claim. Repository commit/push status
is reported separately; local verification does not imply a new CI run.

Release build: zero warnings/errors. Core behavioral checks, headless/Skia desktop
checks, all 15 Python static-analysis checks and whitespace checks passed. Visual
inspection of `artifacts/phase3-previews/dashboard-history.png` confirmed readable
ledger/statistics layout. Enum validation uses the older overload compatible with
the legacy target; native/legacy-runtime execution was not performed in this phase.

Git delivery: elevated staging succeeded, but the commit attempt failed with
`fatal: unable to write new index file`. No Phase 3 commit/push or new CI result is
claimed. The changes remain local; repository access controls were not modified.
