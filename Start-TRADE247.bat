@echo off
rem ============================================================================
rem  TRADE247-V1 - run the whole project from source with one double-click.
rem
rem    Start-TRADE247.bat            desktop app: local server + application window (default)
rem    Start-TRADE247.bat server     server only, page opens in your browser (same as "npm run dev")
rem    Start-TRADE247.bat check      install if needed, then build and self-test the app without a window
rem
rem  What it does, in order:
rem    1. finds Node.js (22.23.4+ / 24.22.1+ / 26.12.1+); offers to install it with winget if it is missing
rem    2. downloads the project's packages (npm ci) the first time, and again whenever package-lock.json changes
rem    3. builds and starts TRADE247. The server runs at http://127.0.0.1:8247 and the desktop window shows it.
rem ============================================================================
setlocal EnableExtensions EnableDelayedExpansion
title TRADE247-V1
set "ROOT=%~dp0"
set "PROJECT=%ROOT%trade247"
if not exist "%PROJECT%\package.json" (
  echo [TRADE247] Cannot find the trade247 folder next to this file: "%PROJECT%"
  goto :fail
)
cd /d "%PROJECT%"

set "MODE=%~1"
if "%MODE%"=="" set "MODE=desktop"
if /i not "%MODE%"=="desktop" if /i not "%MODE%"=="server" if /i not "%MODE%"=="check" (
  echo [TRADE247] Unknown option "%MODE%". Use: desktop, server or check.
  goto :fail
)

rem ---------------------------------------------------------------- 1. Node.js
call :find_node
if errorlevel 1 call :install_node
if errorlevel 1 goto :fail
for /f "delims=" %%v in ('node -v') do set "NODEVER=%%v"
set "NODENUM=%NODEVER:v=%"
for /f "tokens=1,2 delims=." %%a in ("%NODENUM%") do (set "NMAJOR=%%a" & set "NMINOR=%%b")
echo [TRADE247] Using Node.js %NODEVER%
if %NMAJOR% LSS 22 (
  echo [TRADE247] Node.js %NODEVER% is too old. Install Node.js 22 LTS or newer from https://nodejs.org and run this file again.
  goto :fail
)

rem ---------------------------------------------------------------- 2. packages
rem A copy of package-lock.json is kept in node_modules after a successful install; if they differ, install again.
set "NEEDINSTALL=0"
if not exist "node_modules\" set "NEEDINSTALL=1"
if not exist "node_modules\.t247-installed-lock.json" set "NEEDINSTALL=1"
if "%NEEDINSTALL%"=="0" (
  fc /b "package-lock.json" "node_modules\.t247-installed-lock.json" >nul 2>&1
  if errorlevel 1 set "NEEDINSTALL=1"
)
if "%NEEDINSTALL%"=="1" (
  echo [TRADE247] Downloading the project's packages ^(npm ci^). The first time this takes a few minutes...
  call :npm_install
  if errorlevel 1 (
    echo [TRADE247] Installing the packages failed 3 times. Check the internet connection and run this file again.
    goto :fail
  )
  copy /y "package-lock.json" "node_modules\.t247-installed-lock.json" >nul
  echo [TRADE247] Packages installed.
) else (
  echo [TRADE247] Packages are up to date.
)

rem ---------------------------------------------------------------- 3. run
if /i "%MODE%"=="check" (
  echo [TRADE247] Building and self-testing the desktop app...
  rem The self-test plays a short replay: keep it out of your real data folder.
  set "T247_DATA_DIR=%TEMP%\trade247-check"
  call npm run electron:smoke
  if errorlevel 1 goto :fail
  echo [TRADE247] Check passed.
  exit /b 0
)
if /i "%MODE%"=="server" (
  echo [TRADE247] Starting the server ^(Ctrl+C to stop^). The page opens in your browser.
  call npm run dev -- --open
  goto :end
)
echo [TRADE247] Building and starting the desktop app. Server: http://127.0.0.1:8247  ^(also works in a browser^)
echo [TRADE247] Close the TRADE247 window to stop it.
call npm run electron:start
if errorlevel 1 goto :fail
goto :end

rem ============================================================================
:npm_install
rem A dropped connection is common on slow or busy networks: try up to three times.
for /l %%i in (1,1,3) do (
  if %%i GTR 1 (
    echo [TRADE247] The download was interrupted. Trying again ^(attempt %%i of 3^)...
    timeout /t 5 /nobreak >nul
  )
  call npm ci --no-audit --no-fund --fetch-retries=5 --fetch-retry-mintimeout=5000
  if not errorlevel 1 exit /b 0
)
exit /b 1

:find_node
node -v >nul 2>&1 && exit /b 0
rem Not on PATH (or a version manager points at a version that is not installed): look in the usual places.
for %%d in ("%ProgramFiles%\nodejs" "%LOCALAPPDATA%\Programs\nodejs" "%ProgramFiles(x86)%\nodejs") do (
  if exist "%%~d\node.exe" (
    set "PATH=%%~d;!PATH!"
    node -v >nul 2>&1 && exit /b 0
  )
)
for %%r in ("%NVM_HOME%" "%APPDATA%\nvm" "%LOCALAPPDATA%\nvm" "%LOCALAPPDATA%\Author Software\nvm\installs") do (
  if not "%%~r"=="" if exist "%%~r\" (
    for /f "delims=" %%v in ('dir /b /ad /o-d "%%~r\v*" 2^>nul') do (
      if exist "%%~r\%%v\node.exe" (
        set "PATH=%%~r\%%v;!PATH!"
        node -v >nul 2>&1 && exit /b 0
      )
    )
  )
)
exit /b 1

:install_node
echo [TRADE247] Node.js was not found on this computer.
where winget >nul 2>&1
if errorlevel 1 goto :manual_node
if /i "%MODE%"=="check" goto :manual_node
choice /c YN /m "[TRADE247] Install Node.js LTS now with winget"
if errorlevel 2 goto :manual_node
winget install -e --id OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements
set "PATH=%ProgramFiles%\nodejs;%PATH%"
node -v >nul 2>&1 && exit /b 0
echo [TRADE247] Node.js was installed but is not visible yet. Close this window and run the file again.
exit /b 1

:manual_node
echo [TRADE247] Install Node.js 22 LTS or newer from https://nodejs.org/en/download , then run this file again.
if /i not "%MODE%"=="check" start "" "https://nodejs.org/en/download"
exit /b 1

:fail
echo.
echo [TRADE247] Stopped because of the error above.
if /i not "%MODE%"=="check" pause
exit /b 1

:end
endlocal
exit /b 0
