#!/usr/bin/env bash
set -euo pipefail

# ── Stop local Arn environment ──
# Usage: ./scripts/local-stop.sh [--clean]
#   --clean: also remove Azurite data directory

echo "── Stopping Arn Local Environment ──"

pkill -f "func start" 2>/dev/null && echo "✓ Functions host stopped" || echo "  Functions host not running"
pkill -f "dotnet.*ArnBulletinGenerator.Functions" 2>/dev/null || true
pkill -f "azurite" 2>/dev/null && echo "✓ Azurite stopped" || echo "  Azurite not running"

if [[ "${1:-}" == "--clean" ]]; then
    rm -rf /tmp/azurite-arn
    rm -f /tmp/arn-func.log
    echo "✓ Cleaned temp files"
fi

echo "Done."
