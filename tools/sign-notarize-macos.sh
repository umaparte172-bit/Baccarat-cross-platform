#!/usr/bin/env bash
set -euo pipefail

app="${1:-}"
rid="${2:-}"
archive="${3:-}"
identity="${APPLE_SIGNING_IDENTITY:-}"
notary_profile="${APPLE_NOTARY_PROFILE:-}"

if [[ -z "$app" || -z "$archive" ]]; then
  echo "Usage: $0 <app-bundle> <osx-arm64|osx-x64> <output-zip>" >&2
  exit 2
fi
if [[ -z "$identity" || -z "$notary_profile" ]]; then
  echo "APPLE_SIGNING_IDENTITY and APPLE_NOTARY_PROFILE must be configured." >&2
  exit 2
fi
if [[ ! -d "$app" || -e "$archive" ]]; then
  echo "The app must exist and the output archive must not already exist." >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
entitlements="$root/packaging/macos/Baccarat.entitlements"
main_executable="$app/Contents/MacOS/Baccarat.Desktop"

bash "$root/tools/validate-macos-app.sh" "$app" "$rid"
security find-identity -v -p codesigning | grep -F -- "$identity" >/dev/null

# Sign nested Mach-O files first. The main executable and outer bundle are signed last.
while IFS= read -r -d '' candidate; do
  if [[ "$candidate" != "$main_executable" ]] && file "$candidate" | grep -q 'Mach-O'; then
    codesign --force --options runtime --timestamp --sign "$identity" "$candidate"
  fi
done < <(find "$app/Contents" -type f -print0)

codesign --force --options runtime --timestamp --entitlements "$entitlements" \
  --sign "$identity" "$main_executable"
codesign --force --options runtime --timestamp --entitlements "$entitlements" \
  --sign "$identity" "$app"
codesign --verify --deep --strict --verbose=2 "$app"

ditto -c -k --sequesterRsrc --keepParent "$app" "$archive"
xcrun notarytool submit "$archive" --keychain-profile "$notary_profile" --wait
xcrun stapler staple "$app"
xcrun stapler validate "$app"
spctl --assess --type execute --verbose=2 "$app"

echo "Signed, notarized and stapled: $app"
echo "Distribution archive: $archive"
