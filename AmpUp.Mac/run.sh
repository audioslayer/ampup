#!/bin/bash
# run.sh — Build and launch AmpUp.Mac for development
#
# Usage:
#   ./run.sh              # build (Swift + dotnet) then launch
#   ./run.sh --no-swift   # skip Swift recompile, just rebuild C# + launch
#   ./run.sh --no-build   # launch last successful build without rebuilding
#   ./run.sh --bundle     # launch from dist/AmpUp.app (requires bundle.sh first)
#
# IMPORTANT: Must run from a Terminal session (not SSH) for audio TCC permission
# prompt to appear on first launch. SSH sessions cannot show GUI dialogs.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOTNET="${DOTNET:-/opt/homebrew/opt/dotnet@8/bin/dotnet}"
if [[ ! -x "$DOTNET" ]]; then
    DOTNET="$(which dotnet 2>/dev/null || echo dotnet)"
fi

NO_BUILD=false
USE_BUNDLE=false
BUILD_ARGS=()

for arg in "$@"; do
    case $arg in
        --no-build)  NO_BUILD=true ;;
        --bundle)    USE_BUNDLE=true ;;
        --no-swift)  BUILD_ARGS+=("--no-swift") ;;
    esac
done

# ── Launch from .app bundle ────────────────────────────────────────────────────
if [[ "$USE_BUNDLE" == "true" ]]; then
    APP="$SCRIPT_DIR/dist/AmpUp.app"
    if [[ ! -d "$APP" ]]; then
        echo "ERROR: $APP not found. Run ./bundle.sh first."
        exit 1
    fi
    echo "==> Launching from bundle: $APP"
    open "$APP"
    exit 0
fi

# ── Build ─────────────────────────────────────────────────────────────────────
if [[ "$NO_BUILD" == "false" ]]; then
    bash "$SCRIPT_DIR/build.sh" "${BUILD_ARGS[@]}"
fi

# ── Launch ────────────────────────────────────────────────────────────────────
BUILD_OUTPUT="$SCRIPT_DIR/bin/Debug/net8.0"
EXECUTABLE="$BUILD_OUTPUT/AmpUp.Mac"

if [[ ! -f "$EXECUTABLE" ]]; then
    echo "ERROR: Executable not found at $EXECUTABLE"
    echo "Run ./build.sh first."
    exit 1
fi

# Set DYLD_LIBRARY_PATH so the dotnet process finds libAmpUpAudio.dylib
export DYLD_LIBRARY_PATH="$SCRIPT_DIR/Native:${DYLD_LIBRARY_PATH:-}"

echo ""
echo "==> Launching AmpUp.Mac..."
echo "    (Audio TCC permission dialog will appear on first launch)"
echo ""

exec "$EXECUTABLE"
