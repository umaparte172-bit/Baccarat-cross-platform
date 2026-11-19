# Electron and Selenium desktop verification

The desktop application uses Electron. Selenium WebDriver tests the actual desktop window through its visible controls. The test starts a fresh local practice session, records P/B/T results, checks history, pauses/resumes, stops, runs a replay, completes a same-seed Compare Settings sweep, exercises AME-AI and JJ, verifies the advanced IHG controls, and opens/closes the backtest panel. It also checks that the renderer cannot access Node.js.

This is desktop UI testing, not a migration of the existing live-table adapter. `src/live/` still contains the legacy direct DevTools connection. This change does not implement Selenium casino wagering or anti-detection features.

## Run

Use Node.js 22.23.4+, 24.22.1+, or 26.12.1+. Install the locked dependencies with `npm ci`, then run:

```sh
npm run electron:selenium
```

The test opens Electron and clicks its controls. A graphical desktop is needed. It uses a temporary data directory, never loads existing account settings, and removes its own test data afterwards. No casino or Telegram connection is started.

To check an already packaged executable:

```sh
# Windows
node tools/selenium-desktop.mjs --app release/desktop/win-unpacked/TRADE247.exe

# macOS Apple Silicon
node tools/selenium-desktop.mjs --app release/desktop/mac-arm64/TRADE247.app/Contents/MacOS/TRADE247
```

The desktop CI jobs run these checks on Windows and macOS after packaging. The driver and Selenium are development dependencies; end users do not install them. Electron and `electron-chromedriver` are pinned to the same release and must be updated together.

The test uses Selenium's standard WebDriver operations (`findElement`, `click`, `sendKeys`, waits, and a read-only script for the renderer isolation check). It does not call CDP endpoints. Electron's matching ChromeDriver is launched and stopped automatically.

Reference: [Selenium JavaScript API](https://www.selenium.dev/selenium/docs/api/javascript/).
