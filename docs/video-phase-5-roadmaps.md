# Phase 5: current-shoe roadmaps

Implemented Bead Plate, Big Road, Big Eye Boy, Small Road, Cockroach Pig and
Banker/Player Ask previews. They use recorded outcomes and pair flags from the
selected session's current shoe, independently of any strategy or server.

`RoadmapCalculator` is a pure Core calculation. `RoadmapsView` renders its
read-only snapshot using the dashboard's existing light cards and colors.
There is no second history to save or synchronize. Undo, shoe changes, session
switches and restored workspaces rebuild the roads from their source records.

## Rules and display

Beads include every hand and fill six rows before advancing a column. Big Road
groups decisive results into streaks, adds ties to the preceding mark, and
retains opening ties at the first cell until a decisive result arrives. Pair
flags remain available per hand on the Bead Plate; Big Road combines the pair
flags of hands sharing its cell. Hover details show hand numbers and markers.

The three derived roads compare logical streaks at offsets 1, 2 and 3.
Comparisons begin only when sufficient prior streaks exist. On a new streak,
compare completed streak lengths; within a streak, compare occupancy at the
current depth and the preceding depth in the reference streak. Thus both empty
positions produce red. Ties do not generate derived entries. Visual tails do
not truncate logical streak lengths.

Six-row layout turns right at the bottom or an occupied cell and remains
horizontal for the rest of that streak. A new streak starts from the next
available top-row column. Each road scrolls horizontally, showing its newest
entries after a result change. Rendering limits drawing to visible columns.

Ask displays the hypothetical next derived colors and shapes for each decisive
outcome. It neither changes records nor places a bet. A dash means the derived
road has not reached its starting point. These indicators describe history,
not the probability of the next winner.

The reference video establishes the five chart names and Ask workflow. Standard
notation and comparison rules were cross-checked against the published
[Live Baccarat game help](https://tables.kasoom.com/assets/help/baccarat/en/rules.html).
Long-tail and leading-tie edge cases are explicit implementation contracts;
the supplied recording does not demonstrate every boundary.

## Verification

- Hand-written fixtures cover six-row wrapping, collisions, alternation,
  leading/attached ties, pair markers, each derived road's start boundary,
  mixed streak lengths, and explicit Ask colors.
- 2,048 hypothetical outcome comparisons agree with the corresponding next
  recorded mark. Ten-thousand-hand streak and repeated-long-streak cases have
  no overlapping or out-of-range cells.
- Desktop checks cover record, Undo, New Shoe and reversal, plus all five roads
  and Ask after a saved workspace is restored.
- Rendered populated roads were inspected. The dashboard's width now follows
  the window so roadmap content cannot expand the right-hand panels off screen.

Native client macOS acceptance remains part of Phase 7. Server integration and
the unresolved Phase 4 strategy rules remain separate outstanding work.
