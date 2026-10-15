# Strategy 9: IHG evidence and outstanding decisions

Reviewed 2026-10-15. The user explicitly confirmed that Strategy 9 means IHG
(Izak's Holy Grail). This is an evidence review, not a claim that IHG is already
implemented or that the original application has been verified against it.

Subsequent clarification on 2026-10-15: **PATTERN is the highest priority**.
The user described IHG as description and PATTERN as a separate strategy. Keep
this audit as supporting reference; it does not define PATTERN's algorithm or
make IHG implementation the next product milestone. See the
[PATTERN evidence review](pattern-strategy-evidence.md). Its open rules are
independent of the IHG document's unresolved cases below.

## Inputs and scope

The supplied `IHG (1).pdf` has 15 pages and SHA-256
`72e74729d565210612e3d9b7cefba26df7f6e423d1f5823a0d01b32fc6261abd`.
The eight newly supplied TXT files are byte-identical to the earlier client copies:
Grinding, Martingale, ODD Diff, Odd Even, ODD is God, ODD Looser, PATTERN and
Sequence. Their named selectors remain configuration references rather than
algorithm descriptions. The PDF adds a distinct, documented IHG strategy; it does
not establish that IHG is equivalent to Grinding or PATTERN.

The installed application also supplies Ladder, which is absent from the eight
client files. Its amount type is currently rejected by the replacement loader.
The correspondence between numbered Strategies 1-6 and the named files remains
unconfirmed. Strategy 9's identity is now confirmed.

The latest client scope requires V1 to open directly into the workspace, without
login, credentials, licensing, activation, online authentication or commercial
features. The demo login gate has now been removed. Authentication recovery
is no longer a prerequisite for local strategy work. Any table connectivity or
external execution requirement must be established separately.

## Recoverable IHG behavior

- Start with one unit on Player. After the first decision, follow that decision.
- Normally use the last two decisions: follow a repeat, or oppose the latest
  decision when the two alternate.
- Detect PPBB or BBPP in the latest four decisions and follow the two-by-two
  pattern, maintaining its phase until the prediction loses; then return to the
  two-decision rule. Pattern labels in the worked tables describe the state AFTER
  the outcome on that row, not the prediction before it.
- After a loss, the next real stake is one unit. After a win in an unfinished run,
  calculate `ceil(max(0, -run_net)) + 1` units. Decimal accounting is required;
  round the deficit before adding one. A deficit of 1.05 therefore produces 3.
- Player wins earn the stake; Banker wins earn 95% of it in these examples.
  A positive completed run starts again at one unit without erasing total profit.
- Count an isolated win when a win is followed by a loss. The described pause
  threshold is seven such wins. Continue evaluating predictions during virtual
  rounds; virtual stakes and cash changes are zero. Resume real play at one unit
  after two consecutive virtual wins. Example 4 retains the unfinished run debt.
- Continue history and unfinished run state across shoe boundaries; a new shoe is
  not an automatic reset (pages 5-7 and 11-12).
- The nominal 45-unit threshold is applied to a bet that is actually placed and
  settled. It is not a strict pre-bet cap: the document explicitly places a 51-unit
  bet and then closes that run whether it wins or loses (page 10). Examples also
  contain terminal bets of 60 and 81. Do not silently clamp those stakes to 45.

These are the document's mechanics, not evidence of predictive accuracy or future
profit. Its performance claims have not been independently validated. Separate
user exposure limits in the application must remain distinct from the IHG rule.

## Reference audit

`tools/audit_ihg_reference.py` extracts example rows, keeps their page/shoe/hand
identity, and uses Decimal arithmetic to check payouts, cumulative net, run net,
adjacent stake changes and adjacent selection consistency. Selection checks use
the preceding table pattern label; they are not an independent full strategy
implementation. Excerpt opening states are incomplete, so not every first-row
transition can be checked. This audit does not exercise the original executable.

| Example | Pages | Extracted hands |
| --- | --- | ---: |
| 1 | 3-7 | 216 |
| 2 | 10 | 20 |
| 3 | 11 | 45 |
| 4 | 12 | 34 |
| Total | | 315 |

The supplied PDF produced no arithmetic, adjacent-stake or adjacent-selection
discrepancies under these checks. Boundary rows and complete examples 2-4 were
visually checked against rendered pages. The audit flags two real bets following
a count of at least seven isolated wins: shoe 146, hands 3 and 4.

Reproduce with Python and `requirements-strategy-evidence.txt` installed:

```sh
python tools/audit_ihg_reference.py "/path/to/IHG (1).pdf"
```

Optionally use `--output artifacts/ihg-review/reference.json` to save all extracted
rows to an existing local directory. Output creation refuses overwrite. The local
reference JSON and rendered pages are under ignored `artifacts/ihg-review/`.
The supplied PDF, raw client files and full tables are not committed.

## Decisions still needed from the client

1. **Simultaneous limits.** Page 12 says virtual play begins after shoe 146,
   hand 2, where the high-bet threshold and seventh single win coincide. The page
   11 table instead places real one-unit bets on hands 3 and 4, and starts virtual
   play on hand 5. On 2026-10-15 the user said the client must confirm the original
   application's behavior. Do not choose either interpretation silently.
2. **Ties.** The PDF contains no tie rows or explicit tie rule. Confirm whether
   ties push the stake and leave selection history, streak counters, pause state
   and next stake unchanged. The user said this needs client confirmation.
3. **Exactly 45.** Examples cover values above 45, but none equals 45. Confirm
   whether a settled stake of exactly 45 closes the run, or only a stake above 45.
4. **Consecutive wins with nonpositive run net.** The prose describes both positive
   run profit and consecutive wins as closure criteria. These can diverge after
   commission: a run at -40 followed by a 41-unit Banker win remains at -1.05.
   Does it continue with 3 units, or close and reset to 1? No supplied row resolves
   that case.

Further evidence is still required for the other strategies: the meanings of
DOMINOR, AI SELECTION, ODD selectors, Grinding/Pattern amount calculations,
Sequence's W5/W1/W3/W4 tokens and Odd Even's `5,3,5,1` rule condition.
The newly supplied TXT files do not resolve these behaviors.

## Acceptance if IHG is separately implemented

- Implement IHG as explicit state transitions for selection phase, run accounting,
  real/virtual play, isolated-win count and resets; do not alias another strategy.
- Turn approved examples and boundary decisions into independent regression
  fixtures. Record source inconsistencies separately from intended behavior.
- Check recommendations before each outcome to prevent using future outcomes.
- Validate unit scaling, commission, ties, virtual rounds, shoe continuation,
  undo and reset against expected results and original-app demonstrations.
- Only claim original-app parity after a client-confirmed comparison. Passing
  source-table arithmetic checks does not satisfy this acceptance criterion.
