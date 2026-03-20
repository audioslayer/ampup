#!/bin/bash
# build.sh — Compile Swift native bridge + dotnet build (debug, for development)
#
# Usage:
#   ./build.sh              # build Swift dylib + dotnet build
#   ./build.sh --no-swift   # skip Swift recompile (faster, use when only C# changed)
#
# For a full .app bundle + DMG release, use bundle.sh instead.
# For running immediately after build, use run.sh.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DOTNET="${DOTNET:-/opt/homebrew/opt/dotnet@8/bin/dotnet}"
if [[ ! -x "$DOTNET" ]]; then
    DOTNET="$(which dotnet 2>/dev/null || echo dotnet)"
fi

SWIFT_SRC="$SCRIPT_DIR/Native/AmpUpAudio.swift"
DYLIB_OUT="$SCRIPT_DIR/Native/libAmpUpAudio.dylib"

SKIP_SWIFT=false
for arg in "$@"; do
    [[ "$arg" == "--no-swift" ]] && SKIP_SWIFT=true
done

# ── Swift bridge ──────────────────────────────────────────────────────────────
if [[ "$SKIP_SWIFT" == "false" ]]; then
    echo "==> Compiling Swift native bridge..."
    swiftc \
        -target arm64-apple-macos14.2 \
        -emit-library \
        -module-name AmpUpAudio \
        -o "$DYLIB_OUT" \
        "$SWIFT_SRC" \
        -framework CoreAudio \
        -framework AudioToolbox \
        -framework Foundation \
        -framework Cocoa \
        -Onone
    echo "    → $DYLIB_OUT"
else
    echo "==> Skipping Swift compile (--no-swift)"
fi

# ── dotnet build ──────────────────────────────────────────────────────────────
echo "==> Building AmpUp.Mac (Debug)..."
"$DOTNET" build "$SCRIPT_DIR/AmpUp.Mac.csproj" \
    --configuration Debug \
    -nologo \
    -v minimal

BUILD_OUTPUT="$SCRIPT_DIR/bin/Debug/net8.0"
echo "    → $BUILD_OUTPUT/AmpUp.Mac"

# Copy dylib next to the executable so it's found at runtime during dev
if [[ -f "$DYLIB_OUT" ]]; then
    [[ -d "$BUILD_OUTPUT" ]] && cp "$DYLIB_OUT" "$BUILD_OUTPUT/"
    # Also copy to RID-specific folder (dotnet run may output there)
    RID_OUTPUT="$BUILD_OUTPUT/osx-arm64"
    [[ -d "$RID_OUTPUT" ]] && cp "$DYLIB_OUT" "$RID_OUTPUT/"
    echo "    → Copied libAmpUpAudio.dylib to build output"
fi

echo "==> Build complete. Run with: ./run.sh"
