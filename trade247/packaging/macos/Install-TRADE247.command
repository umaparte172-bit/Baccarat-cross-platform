#!/bin/bash
# Installs TRADE247 on an Apple-silicon Mac WITHOUT an Apple developer account, notarization or Apple ID.
#
# The app is signed ad hoc (codesign -s -), which is all Apple silicon needs to run a program. What stops a downloaded
# copy is only the "downloaded from the internet" flag (com.apple.quarantine) that the browser puts on it. This script
# copies the app to Applications, removes that flag, checks (and if needed repairs) the ad-hoc signature, and opens it.
#
# Run it from Terminal (this works even when macOS refuses to open a downloaded script by double-click):
#     bash "/Volumes/TRADE247 0.1.0-arm64/Install-TRADE247.command"
# or drag this file into a Terminal window after typing  bash  and a space, then press Return.
#
# Options (environment): TRADE247_INSTALL_DIR=<folder>  install somewhere else;  TRADE247_NO_OPEN=1  do not open it.
# The first argument may name the TRADE247.app to install (default: the one next to this script).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
src="${1:-$here/TRADE247.app}"
dest_dir="${TRADE247_INSTALL_DIR:-/Applications}"

say() { printf '\n%s\n' "$*"; }
fail() { say "ERROR: $*"; [ -t 0 ] && read -n 1 -s -r -p "Press any key to close"; exit 1; }

[ "$(uname -s)" = "Darwin" ] || fail "This installer is for macOS."
[ "$(uname -m)" = "arm64" ] || say "Note: this build is for Apple-silicon Macs (M1/M2/M3...). It will not run on an Intel Mac."
[ -d "$src" ] || fail "Cannot find TRADE247.app next to this script ($src). Run it from the opened disk image or the unzipped folder."

if [ ! -w "$dest_dir" ]; then
  say "$dest_dir is not writable for this user; installing into ~/Applications instead."
  dest_dir="$HOME/Applications"
fi
mkdir -p "$dest_dir"
dest="$dest_dir/TRADE247.app"

# Quit a running copy so it can be replaced.
osascript -e 'tell application "TRADE247" to quit' >/dev/null 2>&1 || true

say "Copying TRADE247 to $dest_dir ..."
rm -rf "$dest"
ditto "$src" "$dest"

say "Removing the 'downloaded from the internet' flag ..."
xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true

if ! codesign --verify --deep --strict "$dest" >/dev/null 2>&1; then
  say "Re-applying the ad-hoc signature (no Apple ID involved) ..."
  codesign --force --deep --sign - "$dest"
fi
codesign --verify --deep --strict "$dest" || fail "The app's signature does not verify; download it again."

if xattr -p com.apple.quarantine "$dest" >/dev/null 2>&1; then
  fail "The quarantine flag could not be removed. Try: sudo xattr -dr com.apple.quarantine \"$dest\""
fi

say "TRADE247 is installed in $dest"
if [ -z "${TRADE247_NO_OPEN:-}" ]; then
  open "$dest"
fi
