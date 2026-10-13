# Stage 6 strategy and session engine report

Status date: 2026-10-13. The original application was not executed. The eight supplied strategy files were read as data and validated locally; they remain excluded from Git.

## Recovered schema

Each strategy contains a name, optional rule condition, initial/win/loss conditions, optional profit/loss limits, a consecutive-loss stop rule and a consecutive-loss pause rule. Each condition contains a `bet_on` selector and an amount object with `type` and comma-separated `value` fields.

Observed amount types are `set`, `Reset Amount`, `Multiply Amount`, `Reset Sequence`, `Next Sequence`, `Grinding` and `Pattern`. Observed selectors include direct `PLAYER`, dynamic `WINNER`, and protected domain-specific labels such as `AI SELECTION`, `DOMINOR`, `ODD Diff`, `ODD EVEN`, `ODD is God`, `ODD Looser` and `PATTERN`.

## Implemented behavior

- Strict, case-preserving JSON import with a 64 KiB file limit and rejection of unknown fields.
- Invariant positive-number and bounded-sequence validation.
- Direct Player/Banker selection and previous non-tie winner resolution.
- Explicit manual Player/Banker resolution for protected selector labels.
- Set, reset amount, multiply amount, reset sequence and next sequence progression.
- Explicit manual stake entry for protected Grinding and Pattern algorithms.
- Configurable payout rules; the desktop currently uses standard Player 1:1 and Banker 0.95:1 win profit, with Player/Banker wagers pushed on ties.
- Stop profit, stop loss, consecutive-loss stop and pause-after-loss behavior.
- Observed no-bet rounds during pauses.
- Exact-state undo, full reset and invariant CSV export.
- A Strategies desktop tab showing the next recommendation, totals and recent history.

The engine is an advisory/offline session tool. It never places a wager, controls a browser or connects to the original service.

## Compatibility boundary

The supplied data names several algorithms but does not define them. Protected executable bodies prevent static recovery of their calculations. Automatically inventing those behaviors would make the replacement unreliable. The engine therefore pauses for an explicit side or stake while retaining the original selector/rule label in its state and export.

This makes all supplied files loadable and operational for manual sessions while clearly separating verified behavior from unrecovered automation. Exact automatic parity for the protected labels requires additional algorithm evidence or authoritative expected-result examples.

## Verification

- All eight supplied strategy files pass the strict loader locally.
- Fixed, dynamic and opaque selectors are covered.
- Wins, losses, Banker commission and tie pushes are covered.
- Amount multiplication, reset and sequence exhaustion are covered.
- Profit stops, consecutive-loss pauses and paused observations are covered.
- Undo, reset and CSV output are covered.
- Headless desktop checks exercise recommendation, settlement, totals and undo.
- Authentication, manual-session and architecture checks continue to pass.
