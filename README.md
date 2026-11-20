# TRADE247-V1

A local baccarat strategy bot with the client's page as its interface, shipped as a desktop app (Electron) and as a
single-file program for Windows and Apple-silicon macOS. Starting the app starts its own local server and opens the
window; the bot itself waits for START.

## Run it from source with one double-click

| | |
|---|---|
| Windows | double-click **`Start-TRADE247.bat`** |
| macOS | double-click **`Start-TRADE247.command`** (if macOS refuses a downloaded copy: `bash Start-TRADE247.command` in Terminal) |

The script finds Node.js 22+ (and offers to install it with winget or Homebrew if it is missing), downloads the
packages with `npm ci` the first time (and again whenever `package-lock.json` changes; it retries a dropped download),
then builds and starts the desktop app. The app runs the local server at **http://127.0.0.1:8247** and shows it in
its window; the same address also works in a browser (**File > Open in browser**). Close the window to stop it.

Options: `server` runs only the server and opens the page in your browser (the same as `npm run dev`);
`check` installs if needed, builds, and self-tests the app without a window (CI runs this on Windows and macOS from a
fresh checkout). The packaged app (installer, `.dmg`) behaves the same way: server and window in one program.

```
trade247/    The product: engine, strategies, risk layer, live table, betting, reports, Telegram, backtest, packaging.
docs/        The delivery plan, what is still needed from the client, what the original application tells us.
analysis/    Python tools, tests and evidence from studying the original Windows application (not part of the product).
.github/     CI: verify, package, desktop builds.
```

| Start here | |
|---|---|
| [`trade247/README.md`](trade247/README.md) | How to run it, build it, and what it does and does not do. |
| [`trade247/docs/deployment.md`](trade247/docs/deployment.md) | Install, desktop app, live table, betting, 24/7 on a VPS. |
| [`docs/trade247-v1-plan.md`](docs/trade247-v1-plan.md) | The delivery plan and its progress table. |
| [`docs/client-questions.md`](docs/client-questions.md) | What is still needed from the client, and why. |
| [`docs/original-app-reverse-engineering.md`](docs/original-app-reverse-engineering.md) | What the original application (Baccarat (Evolution) 2.0) tells us, from static analysis only. |
| [`analysis/README.md`](analysis/README.md) | The evidence behind it. |

## History

The first attempt was a native C# / .NET (Avalonia) application. Its logic (the strategies, the chip selection, the live
browser adapter) was ported into `trade247/`, and the project was removed. It remains in git history in the tag
**`legacy-csharp`**:

```bash
git worktree add ../legacy legacy-csharp
```

Not carried over, because the client's page and PRD do not use them: the old dashboard with sessions and undo, the road maps
(bead plate, big road, ...), the account and licence screens, and the import of the original `.txt` strategy files.

## Not in the repository

The supplied original application, the video and generated analysis output are local only (`Original Application/`,
`artifacts/`, `.tools/`) and are ignored by git. Never commit them, or any token, password or capture.
