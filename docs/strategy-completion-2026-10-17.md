# Strategy completion — 2026-10-17

## Implemented scope

The eight dashboard strategies use the supplied client acceptance cases:
Martingale, Grinding, Sequence, PATTERN, Odd Even, ODD Diff, ODD Looser,
and ODD is God. Empty settings select defined defaults; applying settings starts
the shared engine used by the ledger, recommendations and local automation.
This does not enable a live casino connection.

The latest PATTERN (IHG) cases and the user's direction to consult `IHG (1).pdf`
govern dashboard PATTERN. Previously imported protected strategy configurations
retain their existing interpretation; they are not silently migrated.

Behavior includes commission-aware settlement, tie preservation, stake
progression/reset, odd-result sampling, no-bet observations, PATTERN run limits
and virtual pause/resume. New shoes retain progression; Undo and saved-session
replay restore calculation state.

## Verification

- Core checks cover all 21 supplied cases, exact next side/stake, final balance,
  replay, Undo and reset.
- The optional private PDF fixture checks all 216 rows of Example 1, including
  wagers and cumulative profit. Set `BACCARAT_IHG_REFERENCE_PATH` to the locally
  extracted reference JSON to run it. The PDF is not redistributed.
- Desktop checks cover empty defaults for all eight strategies, cross-shoe
  progression, saved-session restoration, CSV preservation and Undo.
- A regression check covers whitespace in the first-side field and base-unit scaling.

## Remaining evidence boundary

The PDF contradicts itself when the high-bet run threshold and seventh single-win
pause threshold coincide. The engine stops with an explicit review message rather
than guessing. Client confirmation from the original app is required to remove
this guard. Run closure otherwise uses positive net profit or the settled-stake
threshold; recovery stake uses the documented deficit formula. Passing these
references does not prove parity for every undocumented original-app branch.

Live connectivity, signing/notarization and physical Mac acceptance are separate
delivery items, not implied by strategy-test completion.
