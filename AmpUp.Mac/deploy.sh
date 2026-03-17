#!/bin/bash
# deploy.sh — Pull, build, and launch AmpUp on Mac (like deploy.bat on Windows)
#
# Usage:
#   ./deploy.sh           # pull + build + launch (for testing)
#   ./deploy.sh --release # pull + bundle .app + DMG (for release)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

MODE="dev"
[[ "${1:-}" == "--release" ]] && MODE="release"

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  AmpUp Mac Deploy ($MODE)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# ── Step 1: Pull latest ──────────────────────────
echo ""
echo "[1] Pulling latest from GitHub..."
cd "$REPO_ROOT"
git pull

# ── Step 2: Kill running AmpUp ───────────────────
echo ""
echo "[2] Killing AmpUp if running..."
pkill -f "AmpUp.Mac" 2>/dev/null || true
pkill -f "AmpUp" 2>/dev/null || true

if [[ "$MODE" == "release" ]]; then
    # ── Release: full bundle + DMG ───────────────
    echo ""
    echo "[3] Building .app bundle + DMG..."
    bash "$SCRIPT_DIR/bundle.sh"

    VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$SCRIPT_DIR/AmpUp.Mac.csproj" 2>/dev/null || echo "unknown")

    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  Release build complete!"
    echo ""
    echo "  App:  $SCRIPT_DIR/dist/AmpUp.app"
    echo "  DMG:  $SCRIPT_DIR/dist/AmpUp-$VERSION.dmg"
    echo ""
    echo "  To test:  open $SCRIPT_DIR/dist/AmpUp.app"
    echo "  To ship:  tell Howl to upload the DMG"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
else
    # ── Dev: quick build + launch ────────────────
    echo ""
    echo "[3] Building (dev)..."
    bash "$SCRIPT_DIR/build.sh"

    echo ""
    echo "[4] Launching AmpUp..."
    cd "$SCRIPT_DIR"
    DOTNET="${DOTNET:-/opt/homebrew/opt/dotnet@8/bin/dotnet}"
    [[ ! -x "$DOTNET" ]] && DOTNET="$(which dotnet 2>/dev/null || echo dotnet)"
    "$DOTNET" run --project AmpUp.Mac.csproj &

    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  AmpUp launched! Test away."
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
fi
