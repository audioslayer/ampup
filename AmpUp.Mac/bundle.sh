#!/bin/bash
# bundle.sh — Build AmpUp.Mac .app bundle and DMG installer
#
# Usage:
#   ./bundle.sh              # full build + bundle + DMG
#   ./bundle.sh --no-dmg     # build + bundle only (skip DMG creation)
#   ./bundle.sh --skip-swift # skip Swift recompile (use existing dylib)
#
# Output:
#   dist/AmpUp.app           macOS application bundle
#   dist/AmpUp-<version>.dmg drag-to-Applications installer
#
# Requirements (macOS only):
#   - Swift 5.9+ (Xcode CLI tools: xcode-select --install)
#   - .NET 8 SDK (brew install dotnet@8)
#   - hdiutil (built into macOS)
#   - codesign (built into macOS)
#   - iconutil (built into macOS, for icon generation)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Configuration ──────────────────────────────────────────────────────────────
BUNDLE_ID="com.wolfden.ampup"
APP_NAME="AmpUp"
EXECUTABLE_NAME="AmpUp.Mac"

# Read version from csproj
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$SCRIPT_DIR/AmpUp.Mac.csproj" 2>/dev/null | head -1)
VERSION="${VERSION:-0.1.0-alpha}"
VERSION_SHORT="${VERSION%%-*}"  # strip pre-release suffix for CFBundleVersion

DOTNET="${DOTNET:-/opt/homebrew/opt/dotnet@8/bin/dotnet}"
if [[ ! -x "$DOTNET" ]]; then
    DOTNET="$(which dotnet 2>/dev/null || echo dotnet)"
fi

DIST_DIR="$SCRIPT_DIR/dist"
APP_BUNDLE="$DIST_DIR/$APP_NAME.app"
CONTENTS="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"
FRAMEWORKS_DIR="$CONTENTS/Frameworks"

BUILD_SKIP_SWIFT=false
BUILD_NO_DMG=false
for arg in "$@"; do
    case $arg in
        --skip-swift) BUILD_SKIP_SWIFT=true ;;
        --no-dmg)     BUILD_NO_DMG=true ;;
    esac
done

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  AmpUp macOS Bundle Builder"
echo "  Version: $VERSION"
echo "  Output:  $DIST_DIR"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# ── Step 1: Compile Swift native bridge ───────────────────────────────────────
SWIFT_SRC="$SCRIPT_DIR/Native/AmpUpAudio.swift"
DYLIB_OUT="$SCRIPT_DIR/Native/libAmpUpAudio.dylib"

if [[ "$BUILD_SKIP_SWIFT" == "false" ]]; then
    echo ""
    echo "[1/5] Compiling Swift native bridge..."
    swiftc \
        -target arm64-apple-macos14.2 \
        -emit-library \
        -module-name AmpUpAudio \
        -o "$DYLIB_OUT" \
        "$SWIFT_SRC" \
        -framework CoreAudio \
        -framework AudioToolbox \
        -framework Foundation \
        -Onone
    echo "      → $DYLIB_OUT"
else
    echo "[1/5] Skipping Swift compile (--skip-swift)"
    if [[ ! -f "$DYLIB_OUT" ]]; then
        echo "ERROR: $DYLIB_OUT not found. Run without --skip-swift first."
        exit 1
    fi
fi

# ── Step 2: dotnet publish (self-contained, osx-arm64) ────────────────────────
echo ""
echo "[2/5] Publishing .NET app (osx-arm64, self-contained)..."
PUBLISH_DIR="$SCRIPT_DIR/publish"
rm -rf "$PUBLISH_DIR"

"$DOTNET" publish "$SCRIPT_DIR/AmpUp.Mac.csproj" \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained true \
    --output "$PUBLISH_DIR" \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:DebugSymbols=false \
    -p:DebugType=none \
    -nologo \
    -v minimal

echo "      → $PUBLISH_DIR"

# ── Step 3: Assemble .app bundle ──────────────────────────────────────────────
echo ""
echo "[3/5] Assembling .app bundle..."
rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$FRAMEWORKS_DIR"

# Copy published .NET output → MacOS/
cp -r "$PUBLISH_DIR/"* "$MACOS_DIR/"

# Rename main executable (dotnet publish names it after the csproj)
if [[ -f "$MACOS_DIR/$EXECUTABLE_NAME" ]]; then
    mv "$MACOS_DIR/$EXECUTABLE_NAME" "$MACOS_DIR/$APP_NAME"
fi

# Copy Swift dylib → Frameworks/
if [[ -f "$DYLIB_OUT" ]]; then
    cp "$DYLIB_OUT" "$FRAMEWORKS_DIR/libAmpUpAudio.dylib"
fi

# Copy knob-face.png asset → Resources/ (loaded at runtime)
if [[ -f "$REPO_ROOT/Assets/knob-face.png" ]]; then
    cp "$REPO_ROOT/Assets/knob-face.png" "$RESOURCES_DIR/"
fi

# Generate icon → Resources/AppIcon.icns
if command -v iconutil &>/dev/null; then
    bash "$SCRIPT_DIR/make-icns.sh" "$RESOURCES_DIR" || echo "WARNING: Icon generation failed, continuing without icon"
else
    echo "WARNING: iconutil not found, skipping icon generation"
fi

# Write Info.plist with version substitution
sed \
    -e "s|<string>0.1.0</string>|<string>$VERSION_SHORT</string>|" \
    -e "s|<string>0.1.0-alpha</string>|<string>$VERSION</string>|" \
    "$SCRIPT_DIR/Info.plist" > "$CONTENTS/Info.plist"

# PkgInfo (required by macOS loader)
echo -n "APPL????" > "$CONTENTS/PkgInfo"

# Fix dylib rpath so the app finds libAmpUpAudio.dylib at runtime
if [[ -f "$FRAMEWORKS_DIR/libAmpUpAudio.dylib" ]] && command -v install_name_tool &>/dev/null; then
    install_name_tool \
        -id "@rpath/libAmpUpAudio.dylib" \
        "$FRAMEWORKS_DIR/libAmpUpAudio.dylib" 2>/dev/null || true

    # Add @executable_path/../Frameworks to the main binary's rpath
    install_name_tool \
        -add_rpath "@executable_path/../Frameworks" \
        "$MACOS_DIR/$APP_NAME" 2>/dev/null || true
fi

echo "      → $APP_BUNDLE"

# ── Step 4: Ad-hoc code signing ───────────────────────────────────────────────
echo ""
echo "[4/5] Signing with ad-hoc signature..."

ENTITLEMENTS="$SCRIPT_DIR/AmpUp.entitlements"

# Sign dylib first (deepest components first)
if [[ -f "$FRAMEWORKS_DIR/libAmpUpAudio.dylib" ]]; then
    codesign \
        --force \
        --sign - \
        --entitlements "$ENTITLEMENTS" \
        "$FRAMEWORKS_DIR/libAmpUpAudio.dylib"
fi

# Sign all .dylib/.so files in MacOS/ (dotnet runtime)
find "$MACOS_DIR" -name "*.dylib" -o -name "*.so" | while read -r lib; do
    codesign --force --sign - "$lib" 2>/dev/null || true
done

# Sign the main bundle
codesign \
    --force \
    --sign - \
    --entitlements "$ENTITLEMENTS" \
    --deep \
    --options runtime \
    "$APP_BUNDLE"

echo "      → Signed (ad-hoc)"

# ── Step 5: Create DMG ────────────────────────────────────────────────────────
DMG_NAME="$APP_NAME-$VERSION"
DMG_PATH="$DIST_DIR/$DMG_NAME.dmg"

if [[ "$BUILD_NO_DMG" == "false" ]]; then
    echo ""
    echo "[5/5] Creating DMG installer..."

    # Create staging area for DMG contents
    DMG_STAGING="$DIST_DIR/dmg-staging"
    rm -rf "$DMG_STAGING"
    mkdir -p "$DMG_STAGING"

    # Copy .app to staging
    cp -r "$APP_BUNDLE" "$DMG_STAGING/"

    # Create symlink to /Applications for drag-to-install
    ln -s /Applications "$DMG_STAGING/Applications"

    # Remove old DMG if exists
    rm -f "$DMG_PATH"

    # Create read-write DMG from staging folder
    TEMP_DMG="$DIST_DIR/temp-rw.dmg"
    hdiutil create \
        -volname "$APP_NAME" \
        -srcfolder "$DMG_STAGING" \
        -ov \
        -format UDRW \
        -size 256m \
        "$TEMP_DMG"

    # Convert to compressed read-only DMG
    hdiutil convert "$TEMP_DMG" \
        -format UDZO \
        -imagekey zlib-level=9 \
        -o "$DMG_PATH"

    rm -f "$TEMP_DMG"
    rm -rf "$DMG_STAGING"

    echo "      → $DMG_PATH"
else
    echo "[5/5] Skipping DMG (--no-dmg)"
fi

# ── Done ──────────────────────────────────────────────────────────────────────
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Build complete!"
echo ""
echo "  App bundle: $APP_BUNDLE"
[[ "$BUILD_NO_DMG" == "false" ]] && echo "  DMG:        $DMG_PATH"
echo ""
echo "  To run the app:"
echo "    open $APP_BUNDLE"
echo ""
echo "  To install:"
[[ "$BUILD_NO_DMG" == "false" ]] && echo "    open $DMG_PATH"
echo "    cp -r $APP_BUNDLE /Applications/"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
