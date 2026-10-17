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

## Simultaneous limits — resolved for this application on 2026-10-18

The user approved the PDF's written rule rather than its conflicting Example 3
table. When the settled stake reaches the run limit and the isolated-win counter
reaches the pause limit on the same hand, settle that hand once, close the run,
and enter virtual play immediately. Continue evaluating the pattern without a
stake or cash change. Ties leave the virtual-win counter unchanged. Resume at one
base unit after two consecutive virtual wins, starting with the next hand.

Example 3 now pauses after shoe 146 hand 2. Hands 3–21 are virtual, and hand 22
resumes on Player at one unit. The table's two additional real bets on hands 3
and 4 are intentionally not reproduced. Tests cover this sequence, scaled units,
an exactly-equal run limit, ties, Undo, archive replay and a fresh post-pause run.

New PATTERN definitions are tagged `client-cases:v2:PATTERN`. Existing v1 saved
runs retain their old behavior so their financial histories are not rewritten.
To use the approved rule in an existing session, choose **Return to strategy
drafts**, then **Apply settings**; earlier settled hands remain in the ledger.
New sessions use v2 when PATTERN settings are applied.

This is an explicit application rule approved by the user, not newly recovered
proof of the original executable's behavior. Other run closure and recovery-stake
rules remain unchanged.

Live connectivity, signing/notarization and physical Mac acceptance are separate
delivery items, not implied by strategy-test completion.
