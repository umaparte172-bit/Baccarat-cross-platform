#!/usr/bin/env bash
set -euo pipefail

# Convert the existing product asset using Apple's icon tooling; no new branding.
source_image="${1:?PNG source required}"
resources="${2:?Bundle Resources directory required}"
iconset="$resources/Baccarat.iconset"
if [[ -e "$iconset" || -e "$resources/Baccarat.icns" ]]; then
  echo "Icon output already exists" >&2
  exit 1
fi
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$source_image" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  doubled=$((size * 2))
  sips -z "$doubled" "$doubled" "$source_image" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$resources/Baccarat.icns"
