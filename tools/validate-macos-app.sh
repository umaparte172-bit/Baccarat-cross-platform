#!/usr/bin/env bash
set -euo pipefail

app="${1:-}"
rid="${2:-}"

case "$rid" in
  osx-arm64) expected_architecture="arm64" ;;
  osx-x64) expected_architecture="x86_64" ;;
  *) echo "Usage: $0 <app-bundle> <osx-arm64|osx-x64>" >&2; exit 2 ;;
esac

if [[ ! -d "$app/Contents/MacOS" || ! -f "$app/Contents/Info.plist" ]]; then
  echo "Incomplete app-bundle layout: $app" >&2
  exit 1
fi

executable_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist")"
bundle_identifier="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist")"
executable="$app/Contents/MacOS/$executable_name"

plutil -lint "$app/Contents/Info.plist"
[[ "$bundle_identifier" == "com.baccarat.crossplatform" ]]
[[ -x "$executable" ]]
file "$executable" | grep -q "$expected_architecture"

if find "$app" -type f -name '*.exe' -print -quit | grep -q .; then
  echo "Windows executable found inside the macOS app bundle." >&2
  exit 1
fi

echo "Validated $rid app bundle: $app"
