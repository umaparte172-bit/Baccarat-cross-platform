# Live table (real browser) integration

Reproduces the original app's Selenium-style behaviour (Chrome with remote debugging + dedicated profile,
read the table, place bets) without ChromeDriver, accounts or licences.

## How it works
- **Automation tool -> "Live table (real browser)"**: choose a profile, press *Open live browser* (Chrome/Edge on
  `127.0.0.1:9014`, own profile under `%LOCALAPPDATA%/BaccaratWorkspace/live-browser-profile`), then *Watch for table*.
  The app is **not tied to any URL**: sign in and open any live table yourself, on any site. While watching it only reads
  tab text; as soon as a tab shows a table (profile `DetectText`, default PLAYER + BANKER, optionally `DetectSelector`;
  works inside same-origin or cross-origin iframes) it locks to that tab's host and connects automatically.
  An optional `HostContains` in a profile can still restrict which sites are eligible. Credentials are never handled.
- Once connected it polls the status banner, results and balance and records each new result in the selected session
  (with pair flags if the profile maps them).
- The strategy engine then recommends the next bet as for manual entry. Placement uses the existing controller:
  **approval for every bet**, stake/session limits, exact chip combination, no retry after a failure.
- Placement clicks the chip then the Player/Banker spot with real mouse events, **refuses when the betting window is
  closed or a stake is already on the table**, and reports *Accepted* only when the table's bet-total element shows the
  requested stake. Anything else is *Indeterminate* and is never repeated.
- Every read/click re-checks that the page host still equals the host it was detected on.

## Frames at any depth
The table is found however deeply it is nested. Cross-process frames (different sites) are attached recursively as
separate DevTools sessions; same-process frames, including cross-origin ones, are read through isolated execution
worlds. All frames of a tab are checked every second, so late-loading frames are picked up, and the deepest frame
that matches the detection rules wins. Clicks use real mouse input at top-level coordinates, summing each ancestor
iframe's position, border and padding. If the table frame reloads, it is re-found inside the same tab.

## Profiles
JSON files in `%LOCALAPPDATA%/BaccaratWorkspace/live-profiles` (a `template-calibrate-me.json` is created).
Selectors are CSS, or `xpath:...`. Fields: `StatusSelector`/`OpenText`/`ClosedText`, `ChipSelector` + `ChipValueSource`
(`text` or an attribute such as `data-value`), `PlayerSelector`, `BankerSelector`, `BetTotalSelector`,
`ResultSelector` + `NewestResult` (`first`/`last`) + `ResultRules` (text/class/aria/data-* substring → outcome),
`PairRules`, `BalanceSelector`, `FrameUrlContains`, `DetectText`/`DetectSelector` (how a table is recognised), optional `HostContains`.
Use **Inspect page** after connecting: it writes `inspection-*.json` (frames, ids, data-role/aria/class/text candidates)
next to the profiles to calibrate selectors.

## Verified / not verified
- Verified in automated checks (`BACCARAT_DOM_CHECKS=1`) against a local mock table in real Chrome: iframe reading,
  chips, closed-window refusal, confirmed placement, duplicate-operation guard, result detection, host guard, and the
  whole path through the desktop app.
- **Not verified against any real casino table.** The original app's selectors (absolute XPaths under
  `//*[@id='root']`, status texts `PLACE YOUR BETS` / `WAIT FOR NEXT GAME`) live in its protected resources and the
  provider's page has surely changed; a profile must be calibrated on the real page before use.


## Calibration status (Evolution Speed Baccarat on a Roobet table)
- From the first inspection: the table is in the third frame level; the bead road is `[data-role="Bead-road"] [data-role="roadItem"]`
  (text `P`/`B`/`T`, oldest first) and counters are `gameCount`, `playerWins`, `bankerWins`, `ties`, `playerPairs`, `bankerPairs`.
  The shipped profile *Evolution baccarat (results only)* uses this to record results automatically.
- Not yet known: bet spots, chips, betting-open indicator and bet total. Run **Inspect page** while a round is open for betting
  (the report now skips chat/winners noise, lists up to 1500 elements with visibility, size, disabled state and data attributes),
  then send the newest `inspection-*.json` to finish the betting selectors.

## Dashboard buttons while connected
With a live table connected, **Player/Banker** in Enter result ask to place that bet on the real table (stake = strategy stake,
or one base unit), confirm in a dialog, then go through the same stake limits; Tie/pair buttons are ignored (read from the table).

## Canvas tables (no buttons or text in the page)
Some tables draw everything on one `<canvas>`. The app supports them without a DOM to read:
- **Detection:** `DetectSelector: "canvas"` with no `DetectText`.
- **Clicks:** `point:<css>@x,y` selectors target a spot as fractions of the canvas box (e.g. `point:canvas@0.45,0.926`);
  chips that cannot be read are listed in `StaticChips` with their value and click point. The overlay check still applies.
- **State from the table's WebSocket:** traffic is captured (Inspect page saves the last 200 messages), Socket.io frames are
  normalised to `EVENT {json}`, and profile regexes decide the betting window (`SocketOpenPattern`/`SocketClosedPattern`),
  results (`SocketResultRules`, pair patterns) and placement confirmation (`SocketBetAckPattern` + `SocketBetAmountPattern`).
  A canvas bet is only reported *Accepted* when the table's own confirmation shows the exact stake on that side.
- **Calibrated profile:** *Multiplier Baccarat (demo canvas)* for the free play-money table at
  `games.ntcc.massivegaming.io/mb-table/?operator=demo` (winner codes seen: 0 Player, 3 Banker, 2 Tie). Verified end to end
  on that demo: results read, two 6-unit bets placed and confirmed by the table.
