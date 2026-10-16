# New application — Phase 1: video specification recovery

Date: 2026-10-16. Status: evidence baseline complete; application behavior unchanged.

This starts the **new seven-phase video-led plan**, not a claim that the earlier
ten-stage migration delivered this reference application. The latest direction
is to build an independent application with the demonstrated functionality,
without integration with the old Wetalkbet server. PATTERN remains the priority.
The recording is the client's friend's application, not the original Windows EXE.
Its behavior is reference evidence, not proof of original-app equivalence.

## Deliverables and evidence provenance

- [Machine-readable cases](video-reference-cases.json): 21 test-note cases and
  10 separately transcribed screen histories, with timestamps and conflicts.
- [Frame extractor](../tools/extract_video_reference.py): reproducible, read-only
  video decoding; refuses to overwrite an existing evidence directory.
- [Arithmetic auditor](../tools/audit_video_reference.py) and
  [regression checks](../tests/test_video_reference.py).
- The screen inventory, implementation gaps and Phase 2 acceptance criteria below.

Source: `Original Application/VID-20261016-WA0002.mp4`, 67,285,178 bytes.
SHA-256: `9f3a68fad62f9c225220ddc2226e41e1ca82e1c812db6ff16cb8113333d80f9e`.
FFmpeg reports 11:04.13 duration, 1338×552, H.264, 30 fps, and no audio stream.
There is therefore no spoken discussion to transcribe from this file.
Neither the original EXE nor the friend's app was executed during this phase.

Exact-position frame sets and SHA-256 manifests are retained locally in
`artifacts/video-phase1` (24 frames) and `artifacts/video-phase1-detail` (14 frames).
Video and generated screenshots remain ignored, not uploaded to GitHub.
The absent `Test case.txt` was recovered only where readable inside the recording;
we do not possess the original file or its omitted sections.

Evidence levels used here:

1. **Observed**: visible fields, recommendations or history rows at a timestamp.
2. **Written reference**: expectations in the on-screen text document, not
   automatically verified by the adjacent application screen.
3. **Inference/proposal**: useful implementation direction requiring tests or
   further evidence; never silently promoted to observed behavior.

## Recording index

Times identify useful frames, not exact boundaries between demonstrations.

| Time | Evidence recovered |
| --- | --- |
| 00:00 | Empty state: “No Session”, New Session; test setup and Martingale notes |
| 01:05 | Martingale four-hand tie case, balances and next Banker 1 |
| 02:05 | Grinding notes G1–G3; explicit grind unit 2, initial Player 2 |
| 03:00 | Grinding P/B/B history, final balance 1001.90, next Banker 4 |
| 03:40 | Sequence S1 actual P/B/B/B history conflicts with written P/P/B/B |
| 03:50 | Sequence B/T history and 1000.95 balance |
| 04:20 | PATTERN initial state; UI run limit 1 versus written limit 2 |
| 04:40 | PATTERN at hand 12, roadmaps and history visible |
| 04:50 | PATTERN at hand 14, balance 1001.95, next Player 1 |
| 05:10 | PATTERN P/T/P/B history: balance 1001, next Player 1, Chop |
| 05:30 | Odd Even E1–E3 test notes |
| 06:30 | Odd Even UI history differs from E2 despite same final balance |
| 07:10 | Odd Even custom stakes mid-example; ODD Diff D1–D3 notes |
| 08:10 | ODD Diff B/T/B history, balance 1000.95 |
| 09:10 | ODD Looser P/B/P history, balance 999.95; L1–L3 notes |
| 09:50 | ODD Looser B/T/B history; all ODD is God notes readable |
| 11:03 | ODD is God custom stakes 5/10; B/P/B, balance 1004.50 |

## Screen and interaction specification

The target is a single light desktop dashboard, not the current navy collection
of separate workspace screens. Native title-bar appearance will differ between
Windows and macOS; reproduce content hierarchy and workflow rather than fake OS chrome.

| Region | Visible content | Required behavior / evidence limit |
| --- | --- | --- |
| Empty state | No Session, explanation, New Session | Create a session before entering results; no sign-in shown in this recording |
| Left sidebar | Toggle, plus button, dated session entries with strategy subtitles | Select/create sessions; cross-process persistence is not demonstrated |
| Session heading | Session date/time; shoe and hand numbers | Update as results are recorded |
| Enter result | Red Banker/B, blue Player/P, green Tie/T | Manual outcome entry; letters are visible, keyboard shortcuts not verified |
| Pair flags | Banker pair and Player pair checkboxes | Capture per-hand flags; do not infer side-bet wagers or payouts |
| Session actions | Undo, New Shoe | Controls observed; exact reset/carry-over semantics remain open |
| Strategy settings | Dropdown, base unit, starting bankroll, strategy-specific fields | Editable settings; text notes request Enter to commit values |
| Next hand | Colored side/stake or No bet, unit/step, explanatory text | Recommendation must be computed before the next recorded result |
| Financial summary | Balance and Session P/L | Decimal ledger; retain stake actually used for each historical hand |
| Roadmaps | Bead Plate, Big Road, Big Eye Boy, Small Road, Cockroach Pig | Derived from results, not recommendations; exact layout edge cases need fixtures |
| Ask | Banker/Player hypothetical indicators | Preview next derived-road colors without recording a real result |
| History | Descending #, Result, Suggested, Stake, Net, Balance | Preserve no-bet rows and ties; changing settings must not rewrite settled stakes |
| Current shoe | B/P/T counts and percentages; streak/chop; bets W/L/P; wagered, peak, max drawdown | Separate observed results from actual wagers; terminology/calculation scope below |
| Stop and pause | Collapsed settings disclosure | Presence observed, detailed inputs and state transitions not recovered |

The video visibly labels the application “Baccarat Assistant”. This is a reference
label, not an approved product rename. Existing branding need not be copied from
the friend's app without a product decision.

### Strategy-specific evidence

Blank settings can show grey placeholders. A placeholder is not proof of a saved
value or a user-entered parameter. Preserve this distinction in the implementation.

| Strategy | Settings/behavior supported by examples | Not yet established |
| --- | --- | --- |
| Martingale | Starts Player 1 in notes; follows previous decisive winner; doubles after loss; resets after win; demonstrated ties push | Caps, fractional units, long loss runs, shoe/reset semantics |
| Grinding | First-bet side and grind unit; examples include unit 2; some wins increase stake, profitable completion returns to base | Full run-profit/reset formula cannot be reduced to “increase after every win” |
| Sequence | Sides and stake lists; displayed side placeholder B,P,P,B,B,P,B,P; sequence position text | Different-length lists, wrap boundaries and all reset combinations |
| PATTERN | First-bet side, run-end stake limit, single wins before pause; First bet/Follow/Streak/Chop labels; P/T/P/B example | Complete progression, high-bet/pause collision, absent P2/P3, mapping to old PATTERN.txt value 2 |
| Odd Even | Odd-results side, stake list; custom 2/4/6 list; E1 tie leaves next B3 | Settings edits mid-shoe, full default list semantics |
| ODD Diff | No bet initially; UI explicitly compares sides on odd-numbered results; equal counts can mean no bet | Progression across repeated no-bet spans, all stop conditions |
| ODD Looser | No bet initially; UI trails on odd-numbered results; ties/no-bet rows shown | Full edge-case progression; keep supplied “Looser” spelling unless renamed intentionally |
| ODD is God | UI follows last odd-numbered result; no initial wager; custom 5/10 example | Exhaustive wrap/reset behavior and ties at every boundary |

The notes call PATTERN “PATTERN (IHG)”, while the user's prior clarification says
IHG is supporting description and PATTERN is separate. Do not merge the PDF's
state machine into PATTERN solely because this new heading resembles it.

## Ledger and statistics evidence

Observed histories support Player wins paying +stake, Banker wins +0.95×stake,
losses −stake, and ties returning stake with zero net. No-bet hands have zero stake
and unchanged balance. These are evidence-backed example calculations, not a
claim about every configurable payout or side bet.

- At 08:10, B/T/B has three observed hands but two bets (W1/L0/P1), wagered 2,
  peak 1000.95 and max drawdown 0. The opening no-bet Banker is not counted as a bet.
- At 09:10, P/B/P has two bets, wagered 2, peak 1000.95 and drawdown 1.
- At 11:03, custom 5/10 has wagered 15, peak 1004.50 and drawdown 5.
- Ties count as observed hands. At 08:10 Banker is 66.7% and Tie 33.3%.
  Banker streak is ×2 despite the intervening tie: ties do not break this example's streak.
- Proposed general drawdown model: running peak minus balance, retaining the
  maximum. It fits these examples but must be validated over longer histories.
- Shoe-versus-session scope for profits, wagered, peak and drawdown is not proven
  because the recovered examples do not establish New Shoe carry-over behavior.

Future strategy tests must compare each hand's prior recommendation, recorded
stake, net, balance, next recommendation and state. Final balance alone is insufficient.

## Discrepancies and unresolved branches

| ID | Evidence | Treatment |
| --- | --- | --- |
| SEQ-01 | Written S1 P/P/B/B gives 998.95 with its listed bets, not stated 994.95. Actual UI at 03:40 is P/B/B/B and correctly gives 994.95. | Keep both; use S1-UI as the demonstrated history, not a silently edited note |
| PAT-01 | P1 notes specify run limit 2 and reference absent P2; displayed limit is 1 at 04:20–04:50 | Do not claim run-limit-2 acceptance; recover missing branch before final algorithm sign-off |
| PAT-02 | New “PATTERN (IHG)” heading conflicts with earlier separation | Keep provenance distinct; no automatic rule substitution |
| ODD-01 | At 06:30 the UI records P/P/P/B; E2 notes specify P/P/B with odd side P. Both end at 998 | Store separate histories; same balance does not establish identical execution |
| UI-01 | Session sidebar subtitles sometimes retain the previous strategy; e.g. Sequence selected while subtitle says Grinding | Fix state synchronization in our app, not reproduce an apparent reference defect |
| UI-02 | At 11:03 first row says Suggested Banker ×1 but stake is 0 with no net | Preserve no-wager ledger semantics; make future labels consistent, not copy the contradiction |
| GAP-01 | Stop/pause, New Shoe carry-over, settings validation, persistence/restart, pair settlement, long-road overflow not sufficiently demonstrated | Design explicit contracts and tests in their implementation phases; do not call them recovered |

No request for the unavailable Test case.txt is needed. The first specific client
comparison, when possible, should be PATTERN with run limit **2**, showing every
stake for `P P P B B P P P B P P B P B`, then the pause boundary. This is not a
blocker for Phase 2's dashboard work. Until confirmed, unsupported branches must
be visible as unverified/manual rather than presenting guessed recommendations.

## Current repository gap assessment

Read-only inspection of the current source, not a new complete runtime audit:

| Component | Existing foundation | Work required for new reference |
| --- | --- | --- |
| Desktop shell | MainWindow routes account/home/separate workspaces | Unified sidebar/dashboard and empty state; latest scope must not require old-server login |
| Session | Manual P/B/T records, undo, counts, CSV | Per-hand pairs, shoes, bankroll ledger, selectable independent sessions |
| Strategies | File loader, generic amount transitions, manual fallbacks | Dedicated tested strategy states; PATTERN/Grinding currently request manual values |
| Recommendations | StrategyView displays engine output | Integrated settings/editor and next-hand panel; explicit no-bet observation state |
| Roadmaps | No matching dashboard roadmap implementation found | Pure layout models plus all five views and Ask preview |
| Data | Existing settings/secret boundaries are not a session repository | Versioned local session storage, atomic saves, recovery and migration tests |
| Communication | Local versioned viewer and controlled simulation | Reuse where useful; no legacy server/API work required by new direction |

Do not remove existing account code or prior work in an evidence-only phase.
The new offline dashboard must not depend on authentication. No new backend,
live-casino integration, real wager placement, licensing or activation is inferred
from this video. Preserve useful existing functionality without pretending it
already matches the reference.

## Phase 2 handoff: dashboard implementation order

1. Introduce the unified session shell and empty state; retain a route back to
   existing functionality during migration. Main tools must work without old-server access.
2. Add session sidebar, selected-session heading, entry controls and settings layout.
3. Place Next hand, balance/P&L, five roadmap regions, history and statistics in
   the demonstrated hierarchy. Use explicit empty/unavailable states, never fake calculations.
4. Wire existing verified actions and separate view models from deterministic
   strategy/session/roadmap calculations. Keep existing core/infrastructure/UI boundaries.
5. Check resize/scroll behavior, centered button text, focus/keyboard access,
   session selection and synchronized labels with headless UI tests and screenshots.

Phase 2 completion requires a coherent navigable shell, no old-server dependency
for local work, no dead controls presented as working, and regression checks.
Full ledger/persistence is Phase 3, strategy correctness Phase 4 (PATTERN first),
roadmaps Phase 5, integration Phase 6, and packaging/client acceptance Phase 7.
Structural completion is not a claim that those later functions are already done.

## Verification and reproduction

Completed locally: both extraction runs (38 frames total), evidence arithmetic
audit, four new evidence regression tests, and all **15 Python analysis tests**.
The broader suite uses the existing `.tools/analysis-deps` dependency directory
via `PYTHONPATH`; the bundled Python alone initially lacked `dnfile`.
`git diff --check` passed. Application builds/runtime tests were not rerun for this
documentation/tooling-only phase; no new claim of application parity is made.
Pre-existing Remember Me changes were preserved and kept outside this phase's
staging list. Commit/push is pending: ordinary and elevated staging attempts
failed to write `.git/index`; read-only ACL inspection found write-deny entries.
No repository permissions were modified, and no commit or push was completed.

```powershell
python tools/extract_video_reference.py `
  'Original Application/VID-20261016-WA0002.mp4' artifacts/video-phase1 `
  --ffmpeg '<path-to-ffmpeg.exe>' `
  --seconds 0 30 65 95 125 180 210 250 270 290 310 330 350 390 410 430 450 490 510 550 590 610 650 663
python tools/extract_video_reference.py `
  'Original Application/VID-20261016-WA0002.mp4' artifacts/video-phase1-detail `
  --ffmpeg '<path-to-ffmpeg.exe>' `
  --seconds 190 200 220 230 240 260 280 360 440 460 470 520 620 640
python tools/audit_video_reference.py
python -m unittest discover -s tests -p test_video_reference.py
```

Use fresh output directories if already extracted. Python auditing requires only
the standard library; frame extraction additionally requires FFmpeg.
Expected evidence audit: **19 internally consistent written cases, 1 conflicting
written case (S1), 1 incomplete written case (P1), 10 arithmetically consistent
observed histories**. This deliberately preserves the conflict; it does not
certify any strategy algorithm, safety, profitability or application parity.
