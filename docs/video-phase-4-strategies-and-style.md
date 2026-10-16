# Phase 4 strategy and presentation checkpoint

The replacement stays a single offline application. The dashboard's Tools
button opens the existing Strategies, Automation, Viewer and Account features
inside the same window. They use the selected dashboard session and its shared
strategy engine; historical hand settlements remain when a new run starts.

## Implemented from the available demonstration

- Martingale: the observed first side, stake doubling after a loss, winner-side
  continuation, reset after a win and unchanged recommendation after a tie.
- Sequence: user-entered side and stake lists, advancing after a loss and
  resetting after a win. The recorded UI vectors S1-UI and S2-UI match, including
  the tie and banker commission. A list is explicit input, not a claim that the
  original's entire Sequence algorithm has been recovered.
- PATTERN: a user-selected first Player or Banker stake can be played; later
  decisions require explicit manual side/stake resolution in Tools. The
  run-limit and pause fields are rejected while their transitions are unknown.
  The demonstration does not establish a general PATTERN selector or the
  meaning of the protected Pattern amount rule.
- Switching a generated strategy or returning to drafts starts a new run
  without rewriting earlier results. Reapplying unchanged settings does not
  reset a running strategy. Undo and local persistence retain run boundaries.

The reference comparisons are executable checks using the hand-by-hand cases
in `video-reference-cases.json`. Martingale M1/M2 and Sequence S1-UI/S2-UI
verify next side, stake and final profit; PATTERN checks opening, tie, guarded
continuation, reset and Undo. The broader strategy engines remain incomplete:
the video lacks enough observations to generalize PATTERN, Grinding and the
Odd variants. No missing rule is silently invented.

## Presentation review

Dashboard, Tools menu, Strategies, Automation and Viewer now share a light
surface, white bordered cards, compact section labels, dark text and centered
button labels. Account keeps its form geometry but uses the same palette and
control treatment. Headless rendered screenshots were reviewed at 1280×860;
automated alignment and compact-window checks pass. Native macOS visual and
behavioral validation is still required in Phase 7.

This is a partial Phase 4 checkpoint, not feature parity with the original app.
