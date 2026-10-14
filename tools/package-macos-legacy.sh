#!/usr/bin/env bash
set -euo pipefail

rid="${1:-}"
configuration="${2:-Release}"
output="${3:-artifacts/macos-legacy/${rid}}"

case "$rid" in
  osx-arm64|osx-x64) ;;
  *) echo "Usage: $0 <osx-arm64|osx-x64> [configuration] [output-directory]" >&2; exit 2 ;;
esac

if [[ -z "$output" || "$output" == "/" ]]; then
  echo "Refusing an unsafe output directory." >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$output" != /* ]]; then
  output="$root/$output"
fi
app="$output/Baccarat Workspace.app"
contents="$app/Contents"

if [[ -e "$app" ]]; then
  echo "Output already exists: $app" >&2
  exit 2
fi

mkdir -p "$contents/MacOS" "$contents/Resources"
cp "$root/packaging/macos/Info.plist" "$contents/Info.plist"
/usr/libexec/PlistBuddy -c 'Set :LSMinimumSystemVersion 11.0' "$contents/Info.plist"

dotnet publish "$root/src/Baccarat.Desktop/Baccarat.Desktop.csproj" \
  --configuration "$configuration" \
  --runtime "$rid" \
  --self-contained true \
  --output "$contents/MacOS" \
  -p:LegacyMacBuild=true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugSymbols=false \
  -p:DebugType=None

chmod 755 "$contents/MacOS/Baccarat.Desktop"
bash "$root/tools/validate-macos-app.sh" "$app" "$rid" 11.0
echo "$app"
