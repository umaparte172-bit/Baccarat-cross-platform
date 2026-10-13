# Stage 8 controlled automation report

## Outcome

Stage 8 implements a complete controlled automation workflow using a local, idempotent simulation adapter. It covers recovered `InitialBet`, `AutoBet`, `PlaceBet`, `DummyBet` and `GetChips` feature roles without claiming recovery of their protected algorithms.

No original executable was run. No browser is launched, no credentials are used, and no real bet or monetary action can occur through the default application.

## Workflow

1. Connect the simulation through bounded reconnect logic.
2. Receive a ready recommendation from the strategy engine.
3. Prepare an initial bet explicitly, or prepare subsequent recommendations when auto-bet is enabled.
4. Validate per-bet and cumulative-session exposure limits.
5. Wait for explicit operator approval.
6. Build an exact minimum-click combination from the available chip denominations.
7. Clear prior selection and select each chip.
8. Submit one Player or Banker placement with a unique operation identifier.
9. Accept only a verified adapter acknowledgement.
10. Publish the new automation state through `automation.snapshot` document version 1.0.

## Safety and recovery

- Operations are serialized so concurrent UI or strategy events cannot overlap placements.
- Auto-bet cannot enable while disconnected or faulted.
- Strategy changes invalidate stale pending approval.
- Stakes are limited to two decimal places and exact available-chip composition.
- Per-bet, cumulative-session, chip-click and reconnect limits are mandatory.
- Reconnect uses bounded exponential backoff.
- A disconnect before placement reconnects into a paused review state and requires a new decision.
- A disconnect, cancellation or unexpected failure during placement produces an indeterminate result.
- Indeterminate operations are never automatically replayed.
- Recovery reconnects into a paused state and clears pending work; it never assumes whether the table accepted an uncertain action.

## Components

- `AutomationController`: state machine, approval, safety limits, serialization and recovery.
- `ChipSelection`: bounded exact chip composition with minimum clicks.
- `IAutomationTableAdapter`: replaceable table boundary.
- `SimulatedAutomationTableAdapter`: local chip, side, idempotency and acknowledgement simulation.
- `AutomationView`: operator controls and state presentation.
- `automation.snapshot` 1.0: Viewer integration for state, mode, accepted count, session stake and messages.

## Verification

Automated checks cover exact and impossible chip combinations, disconnected enable rejection, explicit approval, initial and automatic Player/Banker actions, cumulative exposure stop, bounded reconnect success, stale-approval cancellation, indeterminate placement, recovery without replay, Viewer delivery and the complete headless Automation workspace flow.

## Live adapter boundary

A live browser/table adapter is not implemented because static evidence does not reveal trustworthy selectors, chip mappings, table readiness signals, click confirmation, acknowledgement behavior or reconnect reconciliation. Supplying those details through sanitized VMware evidence is required before that adapter can be designed or enabled. The simulator is feature-complete for the controlled workflow; it is not represented as compatibility with a live casino table.
