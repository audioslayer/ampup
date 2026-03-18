#!/bin/bash
# make-icns.sh — Generate AppIcon.icns from existing PNG assets
# Uses macOS iconutil. Run from AmpUp.Mac/ directory.
# Requires: ../Assets/icon/ampup-*.png (already in repo)
#
# Usage: ./make-icns.sh [output_dir]
#   output_dir defaults to Resources/ (used by bundle.sh)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ASSETS_DIR="$SCRIPT_DIR/../Assets/icon"
OUTPUT_DIR="${1:-$SCRIPT_DIR/Resources}"
ICONSET="$SCRIPT_DIR/AppIcon.iconset"

echo "==> Generating AppIcon.icns from $ASSETS_DIR"

# Verify source PNGs exist
if [[ ! -f "$ASSETS_DIR/ampup-16.png" ]]; then
    echo "ERROR: Missing icon assets at $ASSETS_DIR"
    echo "Expected: ampup-16.png, ampup-32.png, ampup-64.png, ampup-128.png, ampup-256.png, ampup-512.png"
    exit 1
fi

# Create iconset directory (macOS iconutil format)
rm -rf "$ICONSET"
mkdir -p "$ICONSET"

# Copy/scale icons into iconset structure
# iconutil expects exact filenames: icon_16x16.png, icon_16x16@2x.png, etc.
copy_icon() {
    local src="$1"
    local dst="$2"
    if [[ -f "$src" ]]; then
        cp "$src" "$ICONSET/$dst"
    else
        echo "WARNING: Missing $src, skipping $dst"
    fi
}

copy_icon "$ASSETS_DIR/ampup-16.png"   "icon_16x16.png"
copy_icon "$ASSETS_DIR/ampup-32.png"   "icon_16x16@2x.png"   # 32px = 16@2x
copy_icon "$ASSETS_DIR/ampup-32.png"   "icon_32x32.png"
copy_icon "$ASSETS_DIR/ampup-64.png"   "icon_32x32@2x.png"   # 64px = 32@2x
copy_icon "$ASSETS_DIR/ampup-128.png"  "icon_128x128.png"
copy_icon "$ASSETS_DIR/ampup-256.png"  "icon_128x128@2x.png" # 256px = 128@2x
copy_icon "$ASSETS_DIR/ampup-256.png"  "icon_256x256.png"
copy_icon "$ASSETS_DIR/ampup-512.png"  "icon_256x256@2x.png" # 512px = 256@2x
copy_icon "$ASSETS_DIR/ampup-512.png"  "icon_512x512.png"

# Scale 512→1024 for 512@2x if sips is available
if command -v sips &>/dev/null && [[ -f "$ASSETS_DIR/ampup-512.png" ]]; then
    sips -z 1024 1024 "$ASSETS_DIR/ampup-512.png" --out "$ICONSET/icon_512x512@2x.png" &>/dev/null || \
        copy_icon "$ASSETS_DIR/ampup-512.png" "icon_512x512@2x.png"
else
    copy_icon "$ASSETS_DIR/ampup-512.png" "icon_512x512@2x.png"
fi

# Convert iconset → .icns
mkdir -p "$OUTPUT_DIR"
iconutil -c icns "$ICONSET" -o "$OUTPUT_DIR/AppIcon.icns"
rm -rf "$ICONSET"

echo "==> Icon created: $OUTPUT_DIR/AppIcon.icns"
