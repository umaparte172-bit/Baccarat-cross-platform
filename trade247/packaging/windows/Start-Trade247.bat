@echo off
rem Starts TRADE247 and keeps this window open if the program stops, so you can read why.
cd /d "%~dp0"
trade247-win-x64.exe %*
echo.
echo TRADE247 has stopped.
pause
