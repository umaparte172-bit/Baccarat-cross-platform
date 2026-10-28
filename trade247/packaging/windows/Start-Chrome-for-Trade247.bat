@echo off
rem Opens Chrome the way TRADE247's "Live table" feed needs it: with a debugging port on this computer only and
rem its own profile (so your normal Chrome is untouched). Sign in and open a baccarat table in THIS window,
rem then choose "Live table" in TRADE247 and press Start. TRADE247 only reads the table; it never clicks or bets.
setlocal
set "PORT=9014"
set "PROFILE=%LOCALAPPDATA%\Trade247\chrome-profile"
set "CHROME="
for %%P in ("%ProgramFiles%\Google\Chrome\Application\chrome.exe" "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe" "%LocalAppData%\Google\Chrome\Application\chrome.exe") do (
  if exist %%P set "CHROME=%%~P"
)
if not defined CHROME (
  echo Google Chrome was not found. Install it from https://www.google.com/chrome/ and run this again.
  pause
  exit /b 1
)
start "" "%CHROME%" --remote-debugging-port=%PORT% --remote-debugging-address=127.0.0.1 --user-data-dir="%PROFILE%"
echo Chrome was opened with debugging port %PORT% (this computer only).
echo Open your table in that window. Use the same port in TRADE247 (the default is %PORT%).
timeout /t 8 >nul
