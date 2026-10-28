#!/bin/bash
# Opens Chrome the way TRADE247's "Live table" feed needs it: with a debugging port on this computer only and its own
# profile (so your normal Chrome is untouched). Sign in and open a baccarat table in THIS window, then choose
# "Live table" in TRADE247 and press Start. TRADE247 only reads the table; it never clicks or bets.
PORT=9014
PROFILE="$HOME/Library/Application Support/Trade247/chrome-profile"
if [ ! -d "/Applications/Google Chrome.app" ]; then
  echo "Google Chrome was not found in /Applications. Install it from https://www.google.com/chrome/ and run this again."
  read -n 1 -s -r -p "Press any key to close"
  exit 1
fi
open -na "Google Chrome" --args --remote-debugging-port=$PORT --remote-debugging-address=127.0.0.1 --user-data-dir="$PROFILE"
echo "Chrome was opened with debugging port $PORT (this computer only)."
echo "Open your table in that window. Use the same port in TRADE247 (the default is $PORT)."
sleep 6
