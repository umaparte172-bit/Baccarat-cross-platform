#!/bin/bash
# =============================================================================
#  TRADE247-V1 - run the whole project from source on a Mac with one double-click.
#
#    ./Start-TRADE247.command           desktop app: local server + application window (default)
#    ./Start-TRADE247.command server    server only, page opens in your browser (same as "npm run dev")
#    ./Start-TRADE247.command check     install if needed, then build and self-test the app without a window
#
#  1. finds Node.js (22.23.4+ / 24.22.1+ / 26.12.1+): PATH, Homebrew, nvm, fnm, volta; offers Homebrew install if missing
#  2. downloads the project's packages (npm ci) the first time, and again whenever package-lock.json changes
#  3. builds and starts TRADE247. The server runs at http://127.0.0.1:8247 and the desktop window shows it.
#
#  If macOS refuses to open this file after a download, run it from Terminal:  bash Start-TRADE247.command
# =============================================================================
set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/trade247"
MODE="${1:-desktop}"

say() { printf '[TRADE247] %s\n' "$*"; }
fail() {
  printf '\n'; say "Stopped because of the error above."
  if [ "$MODE" != "check" ] && [ -t 0 ]; then read -n 1 -s -r -p "Press any key to close"; echo; fi
  exit 1
}

case "$MODE" in desktop|server|check) ;; *) say "Unknown option \"$MODE\". Use: desktop, server or check."; fail ;; esac
[ -f "$PROJECT/package.json" ] || { say "Cannot find the trade247 folder next to this file: $PROJECT"; fail; }
cd "$PROJECT" || fail

# ---------------------------------------------------------------- 1. Node.js
find_node() {
  command -v node >/dev/null 2>&1 && node -v >/dev/null 2>&1 && return 0
  # A double-clicked .command does not read every shell profile: look where Node is usually installed.
  for d in /opt/homebrew/bin /usr/local/bin "$HOME/.volta/bin" "$HOME/.local/share/fnm/aliases/default/bin"; do
    if [ -x "$d/node" ]; then PATH="$d:$PATH"; export PATH; node -v >/dev/null 2>&1 && return 0; fi
  done
  if [ -s "${NVM_DIR:-$HOME/.nvm}/nvm.sh" ]; then
    # shellcheck disable=SC1091
    . "${NVM_DIR:-$HOME/.nvm}/nvm.sh" >/dev/null 2>&1
    nvm use default >/dev/null 2>&1 || nvm use node >/dev/null 2>&1 || true
    command -v node >/dev/null 2>&1 && node -v >/dev/null 2>&1 && return 0
  fi
  return 1
}

install_node() {
  say "Node.js was not found on this Mac."
  local brew=""
  for b in /opt/homebrew/bin/brew /usr/local/bin/brew; do [ -x "$b" ] && brew="$b"; done
  if [ -n "$brew" ] && [ "$MODE" != "check" ] && [ -t 0 ]; then
    read -r -p "[TRADE247] Install Node.js now with Homebrew (brew install node@22)? [y/N] " answer
    if [ "${answer:-n}" = "y" ] || [ "${answer:-n}" = "Y" ]; then
      "$brew" install node@22 || return 1
      PATH="$("$brew" --prefix node@22)/bin:$PATH"; export PATH
      node -v >/dev/null 2>&1 && return 0
    fi
  fi
  say "Install Node.js 22 LTS or newer from https://nodejs.org/en/download , then run this file again."
  [ "$MODE" != "check" ] && open "https://nodejs.org/en/download" >/dev/null 2>&1
  return 1
}

find_node || install_node || fail
NODEVER="$(node -v)"
NMAJOR="$(echo "$NODEVER" | sed 's/^v//' | cut -d. -f1)"
say "Using Node.js $NODEVER ($(command -v node))"
if [ "$NMAJOR" -lt 22 ]; then
  say "Node.js $NODEVER is too old. Install Node.js 22 LTS or newer from https://nodejs.org and run this file again."
  fail
fi
[ "$(uname -m)" = "arm64" ] || say "Note: this Mac is not Apple silicon; the desktop app still runs from source."

# ---------------------------------------------------------------- 2. packages
STAMP="node_modules/.t247-installed-lock.json"
if [ ! -d node_modules ] || [ ! -f "$STAMP" ] || ! cmp -s package-lock.json "$STAMP"; then
  say "Downloading the project's packages (npm ci). The first time this takes a few minutes..."
  installed=0
  # A dropped connection is common on slow or busy networks: try up to three times.
  for attempt in 1 2 3; do
    if [ "$attempt" -gt 1 ]; then say "The download was interrupted. Trying again (attempt $attempt of 3)..."; sleep 5; fi
    if npm ci --no-audit --no-fund --fetch-retries=5 --fetch-retry-mintimeout=5000; then installed=1; break; fi
  done
  [ "$installed" = 1 ] || { say "Installing the packages failed 3 times. Check the internet connection and run this file again."; fail; }
  cp package-lock.json "$STAMP"
  say "Packages installed."
else
  say "Packages are up to date."
fi

# ---------------------------------------------------------------- 3. run
case "$MODE" in
  check)
    say "Building and self-testing the desktop app..."
    # The self-test plays a short replay: keep it out of your real data folder.
    export T247_DATA_DIR="${TMPDIR:-/tmp}/trade247-check"
    npm run electron:smoke || fail
    say "Check passed."
    exit 0
    ;;
  server)
    say "Starting the server (Ctrl+C to stop). The page opens in your browser."
    npm run dev -- --open
    ;;
  *)
    say "Building and starting the desktop app. Server: http://127.0.0.1:8247 (also works in a browser)"
    say "Quit TRADE247 (Cmd+Q) to stop it."
    npm run electron:start || fail
    ;;
esac
