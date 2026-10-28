TRADE247-V1 {{VERSION}} - macOS, Apple Silicon (M1 / M2 / M3 ...)
================================================================

This build is for Macs with an Apple chip. It does not run on Intel Macs.

START
  1. Open Terminal, go to this folder and clear the download flag macOS puts on files from the internet:
         xattr -dr com.apple.quarantine .
  2. Double-click  Start-Trade247.command  (or run  ./{{EXE}}  in Terminal).
     A Terminal window opens and your browser shows the page. Close the window or press Ctrl+C to stop.
  Nothing is installed: delete the folder to remove it.

IF macOS SAYS THE PROGRAM "CANNOT BE OPENED" OR "COULD NOT VERIFY"
  The program is signed ad hoc, not by a registered Apple developer (no $99 account or notarization is
  needed for a program you run yourself). If step 1 above was skipped, macOS asks once instead:
    - macOS 14 or older: right-click the file, choose Open, then Open.
    - macOS 15 and newer: try to open it once, then System Settings > Privacy & Security >
      scroll down > "Open Anyway" next to the program's name, and confirm.
  If the file will not run at all, make it executable:  chmod +x {{EXE}} Start-Trade247.command

CHECK THE DOWNLOAD
  Compare its SHA-256 with SHA256SUMS.txt:   shasum -a 256 {{EXE}}

LIVE TABLE (read-only)
  1. Double-click  Start-Chrome-for-Trade247.command. It opens its own Chrome window.
  2. In that window sign in and open a baccarat table.
  3. In TRADE247 choose "Live table" as the result feed and press START.
  TRADE247 only reads the results shown on the table. It never clicks, types or bets there; bets are simulated.
  If the table is not found, the status says why. "Retry Table" reconnects without losing the session.
  To help check the table settings, run  {{EXE}} --inspect-table  with the table open; it saves a small file
  (element names, no balances, no page addresses beyond the site) that you can send for calibration.

WHERE YOUR DATA IS
  ~/Library/Application Support/Trade247   settings, session logs (CSV), reports, the day's profit/loss.
  Copy that folder to back it up. The Telegram bot token is kept there in secrets.json.

COMMAND LINE
  ./{{EXE}} --help     lists the options (--port, --no-open, --version ...)

The page listens on this computer only (127.0.0.1); other computers cannot reach it.
