# PATTERN: highest-priority strategy

Updated 2026-10-15 following the user's clarification: PATTERN is the most
important strategy. IHG is supporting description and is separate from PATTERN.
The IHG document's examples do not establish PATTERN's calculations. Earlier
discussion of Strategy 9 must not be used to label PATTERN or substitute IHG rules.

## What the supplied configuration establishes

The newly supplied `PATTERN.txt` matches the earlier client copy exactly:

| State | Side selector | Amount operation | Configured value |
| --- | --- | --- | --- |
| Initial | PATTERN | set | 100 |
| After win | PATTERN | Pattern | 2 |
| After loss | PATTERN | Pattern | 2 |

The rule-condition, stop-profit, stop-loss and consecutive-loss stop/pause values
are empty. This file supplies no tie behavior or algorithm for the PATTERN selector
or Pattern amount operation. The value 2 does not by itself establish doubling,
a two-hand lookback or a two-unit increment. Both outcome branches name the same
operation, but its internal calculation may still depend on outcomes and history.

The current loader recognizes the file. The engine requests a manual side for
PATTERN and a manual stake after a win or loss. It therefore does not yet reproduce
the original automatic PATTERN algorithm.

The no-login V1 workflow checks now cover this manual fallback end to end,
including approval only after both side and stake are resolved. See the
[workflow acceptance record](v1-workflow-acceptance.md). Passing these checks is
not evidence of automatic PATTERN parity or its original tie handling.

## Exact evidence needed

1. At a fresh reset, what history (if any) is required before the first bet, and
   how is Player or Banker selected?
2. After each win, loss or tie, how are the next side and amount selected? What
   does the amount value 2 control?
3. What causes progression to reset, pause or stop, and what carries across a
   shoe boundary?

A recording of PATTERN from a reset, showing settings and the next side and stake
around each result, can establish these rules without a new technical document.
Use its observed cases as reference tests; follow up only on uncovered branches.
No PATTERN demonstration video is currently available locally.

## Work order

1. Recover PATTERN selection and amount transitions from client demonstration or
   specific rule answers.
2. Implement and test those transitions, including ties, reset and undo.
3. Compare the same result sequence against the original application's output.
4. Continue the remaining priority strategies. Retain the IHG reference analysis;
   its open questions do not block independently evidenced PATTERN work.
