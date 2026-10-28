# TRADE247-V1: building, installing and running

This guide covers the three things you do with the program: **build** it from source, **run** it on a Mac or a
Windows PC, and keep it **running 24/7** on a Windows VPS.

TRADE247 is a local program. It serves its page on `127.0.0.1` (this computer only) and never accepts connections
from other computers. It needs no account, licence, login or Apple Developer membership.

## 1. What you get

| File | For |
|---|---|
| `trade247-win-x64-<version>.zip` | Windows 10/11 and Windows Server, 64-bit |
| `trade247-macos-arm64-<version>.tar.gz` | Macs with an Apple chip (M1, M2, M3 ...) |
| source code (`trade247/` in the repository) | building it yourself |

Each package contains one program file (about 80 MB, it carries its own copy of the Node.js runtime, so nothing
else has to be installed), start scripts, `README-RUN.txt`, `SHA256SUMS.txt` and this guide.

## 2. Build from source

Requirements: **Node.js 22 or newer** (https://nodejs.org). Nothing else.

```bash
cd trade247
npm ci                 # install the exact dependencies
npm run typecheck      # TypeScript check
npm test               # the test suite (about 40 seconds)
npm start              # run from source: http://127.0.0.1:8247/
```

To build the single-file program **for the computer you are on**:

```bash
npm run package        # writes release/trade247-<os>-<arch>[.exe]
npm run smoke          # starts the built program and checks it end to end (16 checks)
npm run release        # package + a ready-to-zip folder with scripts, README and SHA256SUMS.txt
```

A single-file program contains the Node.js binary of the computer that built it, so **each package must be built
on its own operating system**: build the Windows one on Windows and the Mac one on an Apple-silicon Mac.
The repository's GitHub Actions workflow (`.github/workflows/trade247.yml`) does exactly that on a Windows runner
and an Apple-silicon macOS runner, runs the tests and the smoke test on each, and attaches the packages to the run.

## 3. Run on a Mac (Apple Silicon)

1. Unpack `trade247-macos-arm64-<version>.tar.gz` (double-click it, or `tar -xzf` in Terminal).
2. In Terminal, in the unpacked folder, remove the "downloaded from the internet" flag:
   ```bash
   xattr -dr com.apple.quarantine .
   ```
3. Double-click `Start-Trade247.command`. A Terminal window opens and the page opens in your browser.
   Close the window (or press Ctrl+C) to stop the bot.

The program is signed **ad hoc** (Apple Silicon refuses unsigned programs, but an ad-hoc signature is enough for a
program you run yourself). It is not notarized, which is why step 2 is needed. If macOS still refuses:

* macOS 14 and older: right-click the file, **Open**, then **Open**.
* macOS 15 and newer: try to open it once, then **System Settings > Privacy & Security**, scroll down, **Open Anyway**.
* If it will not execute at all: `chmod +x trade247-macos-arm64 Start-Trade247.command`.

Your data is in `~/Library/Application Support/Trade247`.

## 4. Run on Windows

1. Unzip `trade247-win-x64-<version>.zip` anywhere (for example `C:\Trade247`).
2. Double-click `Start-Trade247.bat` (or the `.exe`). A window opens and the page opens in your browser.
   Keep the window open while the bot is in use; close it, or press Ctrl+C, to stop it.

The first time, Windows SmartScreen says **"Windows protected your PC"**: click **More info**, then **Run anyway**.
The program is not code-signed (a signing certificate costs money and is not needed to run it). Some antivirus
programs distrust unsigned programs; if one blocks it, add the folder to its exclusions. You can confirm the file is
the one that was built by comparing `Get-FileHash trade247-win-x64.exe -Algorithm SHA256` with `SHA256SUMS.txt`.

Your data is in `%APPDATA%\Trade247`.

## 5. Windows VPS, 24/7

### Which machine

Any Windows 10/11 or Windows Server VPS with a desktop (RDP) session. Two vCPUs and 4 GB of RAM are plenty for
the bot; more CPU only makes backtests faster. The program makes outbound connections only (Telegram, and the casino
page once live trading exists) and needs **no inbound firewall rule**.

### One-time setup

1. **Copy and unzip** the package, for example to `C:\Trade247`.
2. **Stop the VPS from sleeping.** In an administrator PowerShell:
   ```powershell
   powercfg /change standby-timeout-ac 0
   powercfg /hibernate off
   ```
3. **Log in automatically after a reboot**, so the bot comes back without you. Run `netplwiz`, untick *Users must
   enter a user name and password*, and enter the account's password (or use Sysinternals *Autologon*, which stores it
   more safely). Anyone with console access to the VPS then gets that session, which is normally protected by your
   hosting provider's own login.
4. **Make Windows Update restart at a time you choose** (Settings > Windows Update > Advanced options > Active
   hours), or the machine may reboot in the middle of a session.
5. **Register the bot** (normal PowerShell, in the program folder):
   ```powershell
   .\install-vps-task.ps1 -DryRun        # shows exactly what it would register
   .\install-vps-task.ps1 -StartNow
   ```
   This creates a scheduled task named `Trade247` that **starts the bot at logon and restarts it every minute if it
   crashes**. Use `-Port` and `-DataDir` to change the port or the data folder. Remove it again with
   `.\uninstall-vps-task.ps1`.
6. **Disconnect from RDP, do not sign out.** Closing the Remote Desktop window disconnects and leaves the session
   (and the bot) running; *Sign out* ends it.

### Using it from your own computer

The page is only reachable on the VPS itself. Either open a browser inside the RDP session, or forward the port over
SSH (Windows has an OpenSSH server) and browse on your own machine:

```bash
ssh -L 8247:127.0.0.1:8247 <user>@<vps-address>      # then open http://127.0.0.1:8247/ locally
```

Never expose the port to the internet: the page has no login.

### After a restart or a crash

The bot comes back **idle**. It does **not** resume a session or place a bet by itself; you press START. This is
deliberate: restoring a half-finished run (its open debt, its pause counters) after an unplanned stop is a decision
that belongs with the person who owns the money, and it will be settled together with live trading. Settings, the
history log and the day's profit/loss survive a restart, so the daily limits still hold.

### Updating

1. Stop the task (`Stop-ScheduledTask -TaskName Trade247`) or close the bot's window.
2. Replace the program file with the new one. Keep the data folder as it is.
3. Start the task again (`Start-ScheduledTask -TaskName Trade247`). `trade247-win-x64.exe --version` shows the version.

### Backups and logs

Copy the data folder (`%APPDATA%\Trade247`): `settings.json`, `secrets.json` (the Telegram token), `day.json`,
`logs\trade247_session_history.csv` (every hand), and `reports\`. The Excel Log button on the page downloads the
latest session as .xlsx or CSV. (The scheduled task sets `T247_OPEN_REPORTS=0`, so on the VPS reports are downloaded
by your browser instead of the program trying to launch Excel, which a server may not have.)

### Watching it from your phone

Set the Telegram bot token and chat ID under **Advanced settings** and press **Send test**. You are then told
about the daily stop loss, the daily take profit, shoe changes and lost table connections. There is no "program
started" message yet, so after a reboot check the page (or the scheduled task's *Last Run Result*).

## 6. Live table

The **Live table** result feed reads the results of a baccarat table you have open in your own Chrome. In the default
**Paper** bet mode it never clicks, types or bets there: bets stay simulated, and the page says so. Real betting is a
separate, opt-in step (section 10).

1. In the app, press **Open Chrome for the table** (Result feed > Live table). It opens a Chrome window with its own profile
   (in the data folder) and a debugging port on this computer only (default 9014), or tells you why it could not. The
   launcher scripts `Start-Chrome-for-Trade247.bat` (Windows) and `.command` (Mac) do the same thing without the app.
2. In that window sign in and open a baccarat table.
3. In TRADE247 choose **Live table (read-only, from your Chrome)**, check the port, press **START**.

How it behaves:

* The road already on screen when you press START is history and is **not played**. Every hand added afterwards is a result.
* A shorter road is a new shoe (the shoe-change policy and the Telegram "shoe change" alert apply). It is acted on only
  after two readings in a row agree, so a half-drawn page cannot cause a false shoe change.
* If the table disappears (you left it, Chrome closed, the page navigated) for 15 seconds, the status shows
  `FEED LOST` and the Telegram "disconnected" alert is sent once. When a table is found again, the road is read afresh:
  the hands dealt in between are **not** replayed. **Retry Table** reconnects at once without losing the session.
* A cell that is not recognised stops that reading from being used, rather than guessing.
* Tables inside frames from another site (the usual setup) are supported.

**Evolution has not been checked against a live table yet; Pragmatic Play has.** Without a `live-profile.json` in the data folder the program tries both built-in profiles on every open tab: Pragmatic Play (the road grid, checked on a real Roobet table on 2026-10-28) and Evolution's
bead road (`[data-role="Bead-road"]` with `[data-role="roadItem"]` cells showing P / B / T). If a table is not found, or
the hands are wrong, run this with the table open and send the file it saves:

```bash
trade247 --inspect-table            # --live-port 9014 is the default
```

The file lists element names and short labels and the site of each tab. It leaves out page addresses beyond the site and
balances and names. A corrected profile can then be dropped into the data folder as `live-profile.json` (no new program
needed):

```json
{
  "name": "Evolution baccarat (bead road)",
  "containerSelector": "[data-role=\"Bead-road\"]",
  "itemSelector": "[data-role=\"roadItem\"]",
  "rules": [ { "exact": "P", "result": "P" }, { "exact": "B", "result": "B" }, { "exact": "T", "result": "T" } ],
  "hostContains": ""
}
```

A rule uses `exact` (the cell's text) or `contains` (text, aria-label or class). A broken file makes START refuse, with the reason.
Chrome 136 and newer refuse a debugging port on the default profile, which is why the launcher uses its own.

## 7. Options and environment variables

```
trade247 --help
  --open / --no-open   open the page in the browser (default: yes when packaged, no from source)
  --port <number>      port to listen on (default 8247)
  --inspect-table      save a description of the table open in Chrome (calibration), then exit
  --check-table        show what the table profile can read from the open table (clicks nothing), then exit
  --live-port <n>      Chrome debugging port for --inspect-table and --check-table (default 9014)
  --version, -v        print the version
```

| Variable | Meaning |
|---|---|
| `T247_PORT` | Port (same as `--port`) |
| `T247_DATA_DIR` | Where settings, logs and reports are kept |
| `T247_NO_OPEN=1` | Same as `--no-open` |
| `T247_OPEN_REPORTS=0` | Never launch Excel or another desktop application; the page downloads reports instead |

## 8. Troubleshooting

| Symptom | What to do |
|---|---|
| "Port 8247 is already in use" | The bot is already running (look for its window or task), or use `--port 8300` |
| Windows: "Windows protected your PC" | **More info > Run anyway** (the program is not code-signed) |
| Windows: the program disappears or is quarantined | Your antivirus distrusts unsigned programs; add the folder to its exclusions |
| macOS: "cannot be opened" / "could not verify" | See section 3 (`xattr -dr com.apple.quarantine .`, then **Open Anyway**) |
| The page does not open by itself | Open `http://127.0.0.1:8247/` yourself; on a VPS it is started with `--no-open` |
| "Alert not delivered - ..." in the page | Telegram refused or could not be reached; press **Send test** to see why |
| "Could not write the session log" | The data folder is read-only or the disk is full; set `T247_DATA_DIR` to a writable folder |
| A backtest says "Could not start the backtest" | Report it with the version from `--version`; backtests use worker threads |

## 9. Desktop app (Electron)

The same program as a normal application window: **TRADE247** for Windows (installer and portable `.exe`) and for Apple-silicon
Macs (`.dmg`). It runs the same local server inside the app and shows the page in its own window, so it behaves exactly like
the command-line program, with these differences:

* The Telegram token is stored in the system keychain (Keychain on macOS, DPAPI on Windows), not as a readable file.
* Closing the window while a session is running asks first, because closing stops the bot.
* Only one copy runs at a time; starting a second brings the first forward.
* Settings, logs and reports use the same data folder as the command-line program (`File > Open data folder`).

```bash
npm run electron:start     # build and open the app from source
npm run electron:smoke     # build and check it end to end (page, API, replay, backtest), without a window
npm run electron:dist      # package it for THIS operating system into release/desktop/
```

A Mac app has to be built on a Mac (CI does that) and a Windows app on Windows. The app is not code-signed: Windows
SmartScreen asks once ("More info > Run anyway"); on a Mac the app is signed ad hoc and not notarized, so the first start
needs **System Settings > Privacy & Security > Open Anyway** (or `xattr -dr com.apple.quarantine /Applications/TRADE247.app`).
There is no auto-update. For a Windows VPS that runs 24/7 without a signed-in desktop, keep using the single-file
program with the scheduled task (section 5): the desktop app needs a desktop session.

## 10. Betting on a live table

**Off by default.** With the **Live table** feed you can choose a **Bet mode** (Advanced settings > Result feed):

| Mode | What happens |
|---|---|
| **Paper** (default) | Every bet is simulated. Nothing is clicked on the table. |
| **Confirm** | When bets open, the bet the strategy wants appears in the page ("BET WAITING: PLAYER $2.00") and is placed only when you press **PLACE BET**. Not approved within the time you set: skipped. |
| **Automatic** | The program places the bet after a short random pause (default 0.5 to 2.5 seconds). |

It is **real money**, and a casino may close an account that bets automatically; that risk is yours. What the program does to protect you:

* **Limits come first.** You must set the **largest bet** and a **session limit**. A bet above either is never placed and ends the session.
* **One bet, placed once.** It starts only when the table says bets are open and nothing is on the table. It builds the stake from the
  fewest chips, clicks them with a real mouse, and waits for the table's **own total** to show exactly the stake. If the table does not
  show it (or shows a different amount), the session **stops** with an alert; nothing is retried.
* **The books match the table.** When the result arrives, what was placed must equal what the strategy would have bet; if not, the
  session stops. A bet the strategy wanted but that could not be placed is recorded as a hand that was only watched.
* **PAUSE** while a bet is on the table starts after its result. **STOP** ends everything immediately.
* It never goes near deposits, tips, withdrawals or any account page.
* It does not change your browser's identity or imitate a person's mouse movements; the pause before a bet is only a delay.

### Ready-made profiles

Copy one into the data folder as `live-profile.json`, then run `trade247 --check-table` with the table open.

| File | Table | Status |
|---|---|---|
| `docs/profiles/pragmatic-play-roobet.json` | Pragmatic Play baccarat (for example "Baccarat 5") on Roobet | **Road only** (Paper mode). Checked against a real open table on 2026-10-28: it read the road. No betting settings yet. Its selectors are generated class names (`wC_wK`), which Pragmatic may change; `--check-table` shows when they stop matching. |
| built-in | Pragmatic Play and Evolution baccarat | Both are tried automatically when there is no `live-profile.json`, so the Pragmatic file above is only needed to pin or change it. Road only (Paper mode); Evolution is unverified. |

Roobet hosts tables from more than one provider (Evolution, Pragmatic Play, ...), so the profile must match the provider of the table you open. If the status says "no baccarat table is visible" while a table is open, the profile is for another provider.

### The table does not connect: check in this order

The status line under the title says what the program saw. In order of likelihood:

1. **"Cannot reach Chrome on port 9014"**: the table is open in your usual Chrome, which cannot be read. Press **Open Chrome for the table**, sign in to the casino in the window that opens and open the table there (a second Chrome with its own profile can run beside your usual one).
2. **"The Chrome on port 9014 has no page open" / "No baccarat table is open in it"**: Chrome is the right one but the table is not in it yet, or is still loading. Wait until the road next to the video shows, then **Retry Table**.
3. **"A Pragmatic Play table is open there, but its road was not recognised"**: the casino changed its page. Run `trade247 --check-table` and `trade247 --inspect-table` and send the result.
4. The port in the page differs from the one Chrome uses. Both default to 9014.
5. Another program already uses port 9014: set another port in the page and in the launcher.

To see the same thing from a terminal: `trade247 --check-table` prints, for each tab, the profile that matched and the hands it read, and clicks nothing.

### Calibrating a table

Betting needs a **table profile with a `betting` block**, because the page's selectors differ by table and change over time.
The built-in profile only reads the road. In the data folder, `live-profile.json`:

```json
{
  "name": "My table",
  "containerSelector": "[data-role=\"Bead-road\"]",
  "itemSelector": "[data-role=\"roadItem\"]",
  "rules": [ { "exact": "P", "result": "P" }, { "exact": "B", "result": "B" }, { "exact": "T", "result": "T" } ],
  "hostContains": "",
  "betting": {
    "statusSelector": "...CSS of the element that says PLACE YOUR BETS...",
    "openText": "PLACE YOUR BETS",
    "chipSelector": "...CSS of the chips...",
    "chipValueFrom": "text",
    "playerSelector": "...CSS of the Player spot...",
    "bankerSelector": "...CSS of the Banker spot...",
    "betTotalSelector": "...CSS of the element showing the total staked...",
    "balanceSelector": ""
  }
}
```

1. Open the table in the launcher's Chrome and run `trade247 --inspect-table`; the file lists the element names on the page.
2. Fill in the profile, then run `trade247 --check-table` with the table open. It **clicks nothing**; it prints what it can read:
   the road, whether bets are open, the chips and their values, whether both spots are found, and the total on the table.
3. Fix whatever says "NOT found" or "NOT readable" and run it again, until every line is right in both a betting round and a closed one.
4. Start with **Paper**, then **Confirm** with a tiny bet and a small balance (or a demo table), and watch the first bets yourself.
   Move to **Automatic** only after that.

The click logic is tested against a mock table in a real Chrome, including a table inside frames from another site. It has **not** been
run on a real casino table, which needs your account.

## 11. What is not included yet

* AME-AI and JJ (waiting for the formulas), and the *Virtual Bet - start after N losses* filter (waiting for one answer).
* Fingerprint spoofing and bot-detection evasion (not built; see the client questions document).
* Reconciling the table's balance with the bot's books (the profile may name a balance element, but it is not compared yet).
* Automatic resume of a session after a restart (see section 5).
* Code signing and notarization. They are optional and cost money; the packages work without them, with the one-time
  prompts described above. If you later buy a Windows code-signing certificate or join the Apple Developer Program,
  signing is one extra step in the build.
* Intel Macs (the macOS package is Apple-silicon only) and 32-bit Windows.
