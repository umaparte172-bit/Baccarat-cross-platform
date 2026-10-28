TRADE247-V1 {{VERSION}} - Windows (64-bit)
==========================================

START
  Double-click  Start-Trade247.bat  (or  {{EXE}}).
  A window opens and your browser shows the page. Keep the window open while you use the bot;
  close it (or press Ctrl+C) to stop it. Nothing is installed: delete the folder to remove it.

FIRST TIME: "Windows protected your PC" (SmartScreen)
  The program is not code-signed (signing needs a paid certificate), so Windows asks once.
  Click "More info", then "Run anyway". Some antivirus tools are also wary of unsigned programs;
  if yours blocks it, add this folder to its exclusions.
  You can check the file is the one that was built: compare its SHA-256 with SHA256SUMS.txt
  (PowerShell:  Get-FileHash {{EXE}} -Algorithm SHA256).

LIVE TABLE (read-only)
  1. Double-click  Start-Chrome-for-Trade247.bat. It opens its own Chrome window.
  2. In that window sign in and open a baccarat table.
  3. In TRADE247 choose "Live table" as the result feed and press START.
  TRADE247 only reads the results shown on the table. It never clicks, types or bets there; bets are simulated.
  If the table is not found, the status says why. "Retry Table" reconnects without losing the session.
  To help check the table settings, run  {{EXE}} --inspect-table  with the table open; it saves a small file
  (element names, no balances, no page addresses beyond the site) that you can send for calibration.

WHERE YOUR DATA IS
  %APPDATA%\Trade247   settings, session logs (CSV), reports, the day's profit/loss.
  Copy that folder to back it up. The Telegram bot token is kept there in secrets.json.

COMMAND LINE
  {{EXE}} --help       lists the options (--port, --no-open, --version ...)

24/7 ON A VPS
  See docs\deployment.md, section "Windows VPS". Short version, in PowerShell, in this folder:
      .\install-vps-task.ps1 -DryRun       (shows what it would do)
      .\install-vps-task.ps1 -StartNow     (starts at logon and restarts after a crash)
  It never places bets by itself: after a restart the bot is idle until you press START.

The page listens on this computer only (127.0.0.1); other computers cannot reach it.
