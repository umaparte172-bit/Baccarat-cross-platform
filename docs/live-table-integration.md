# Live table (real browser) integration

Reproduces the original app's Selenium-style behaviour (Chrome with remote debugging + dedicated profile,
read the table, place bets) without ChromeDriver, accounts or licences.

## How it works
- **Automation tool → "Live table (real browser)"**: choose a profile, *Open live browser* starts Chrome/Edge on
  `127.0.0.1:9014` with its own profile (`%LOCALAPPDATA%/BaccaratWorkspace/live-browser-profile`).
  You sign in and open the table yourself; the app never handles credentials.
- *Connect live table* attaches over the DevTools protocol to the tab whose host matches the profile
  (tables inside same-origin or cross-origin iframes are supported), polls the status banner, results and balance,
  and records each new result in the selected session (with pair flags if the profile maps them).
- The strategy engine then recommends the next bet exactly as for manual entry. Placement still uses the existing
  controller: **approval for every bet**, stake/session limits, exact chip combination, no retry after a failure.
- Placement clicks the chip then the Player/Banker spot with real mouse events, **refuses when the betting window is
  closed or a stake is already on the table**, and reports *Accepted* only when the table's bet-total element shows the
  requested stake. Anything else is *Indeterminate* and is never repeated.
- Every read/click re-checks that the page host still matches the profile.

## Profiles
JSON files in `%LOCALAPPDATA%/BaccaratWorkspace/live-profiles` (a `template-calibrate-me.json` is created).
Selectors are CSS, or `xpath:...`. Fields: `StatusSelector`/`OpenText`/`ClosedText`, `ChipSelector` + `ChipValueSource`
(`text` or an attribute such as `data-value`), `PlayerSelector`, `BankerSelector`, `BetTotalSelector`,
`ResultSelector` + `NewestResult` (`first`/`last`) + `ResultRules` (text/class/aria/data-* substring → outcome),
`PairRules`, `BalanceSelector`, `FrameUrlContains`, `HostContains` (required).
Use **Inspect page** after connecting: it writes `inspection-*.json` (frames, ids, data-role/aria/class/text candidates)
next to the profiles to calibrate selectors.

## Verified / not verified
- Verified in automated checks (`BACCARAT_DOM_CHECKS=1`) against a local mock table in real Chrome: iframe reading,
  chips, closed-window refusal, confirmed placement, duplicate-operation guard, result detection, host guard, and the
  whole path through the desktop app.
- **Not verified against any real casino table.** The original app's selectors (absolute XPaths under
  `//*[@id='root']`, status texts `PLACE YOUR BETS` / `WAIT FOR NEXT GAME`) live in its protected resources and the
  provider's page has surely changed; a profile must be calibrated on the real page before use.
