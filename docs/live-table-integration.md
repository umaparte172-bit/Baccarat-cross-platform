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
