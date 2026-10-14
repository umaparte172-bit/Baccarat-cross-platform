#!/usr/bin/env bash
set -euo pipefail

app="${1:-}"
rid="${2:-}"
expected_minimum="${3:-14.0}"

case "$rid" in
  osx-arm64) expected_architecture="arm64" ;;
  osx-x64) expected_architecture="x86_64" ;;
  *) echo "Usage: $0 <app-bundle> <osx-arm64|osx-x64> [minimum-macos-version]" >&2; exit 2 ;;
esac

case "$expected_minimum" in
  11.0|14.0) ;;
  *) echo "Unsupported minimum macOS version: $expected_minimum" >&2; exit 2 ;;
esac

if [[ ! -d "$app/Contents/MacOS" || ! -f "$app/Contents/Info.plist" ]]; then
  echo "Incomplete app-bundle layout: $app" >&2
  exit 1
fi

executable_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist")"
bundle_identifier="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist")"
minimum_system_version="$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$app/Contents/Info.plist")"
executable="$app/Contents/MacOS/$executable_name"

plutil -lint "$app/Contents/Info.plist"
[[ "$bundle_identifier" == "com.baccarat.crossplatform" ]]
[[ "$minimum_system_version" == "$expected_minimum" ]]
[[ -x "$executable" ]]
file "$executable" | grep -q "$expected_architecture"

validate_deployment_target() {
  local candidate="$1"
  local deployment_version deployment_major deployment_minor expected_major expected_minor
  deployment_version="$(otool -l "$candidate" | awk '
    $1 == "cmd" { build = ($2 == "LC_BUILD_VERSION"); legacy = ($2 == "LC_VERSION_MIN_MACOSX"); next }
    build && $1 == "minos" { print $2; exit }
    legacy && $1 == "version" { print $2; exit }
  ')"
  if [[ -z "$deployment_version" ]]; then
    echo "Unable to determine the minimum macOS deployment version: $candidate" >&2
    exit 1
  fi

  IFS=. read -r deployment_major deployment_minor _ <<< "$deployment_version"
  IFS=. read -r expected_major expected_minor _ <<< "$expected_minimum"
  deployment_minor="${deployment_minor:-0}"
  expected_minor="${expected_minor:-0}"
  if (( deployment_major > expected_major ||
        (deployment_major == expected_major && deployment_minor > expected_minor) )); then
    echo "$candidate requires macOS $deployment_version, above bundle minimum $expected_minimum." >&2
    exit 1
  fi
}

while IFS= read -r -d '' candidate; do
  if file "$candidate" | grep -q 'Mach-O'; then
    validate_deployment_target "$candidate"
  fi
done < <(find "$app/Contents" -type f -print0)

if find "$app" -type f -name '*.exe' -print -quit | grep -q .; then
  echo "Windows executable found inside the macOS app bundle." >&2
  exit 1
fi

echo "Validated $rid app bundle for macOS $expected_minimum+: $app"
