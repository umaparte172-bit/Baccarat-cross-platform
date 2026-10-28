#!/bin/bash
# Starts TRADE247 in a Terminal window. Close the window or press Ctrl+C to stop it.
cd "$(dirname "$0")" || exit 1
BIN="$(ls trade247-macos-* 2>/dev/null | head -n 1)"
if [ -z "$BIN" ]; then
  echo "Cannot find the trade247-macos-* program next to this file."
  read -n 1 -s -r -p "Press any key to close"
  exit 1
fi
./"$BIN" "$@"
echo
read -n 1 -s -r -p "TRADE247 has stopped. Press any key to close"
echo
