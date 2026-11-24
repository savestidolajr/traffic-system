#!/usr/bin/env bash
set -euo pipefail

# ── One-command local dev: backend + frontend together ──
# Brings up Azurite + the Azure Functions host (via local-start.sh), then runs
# the Vite dev server in the foreground so you can create bulletins in the UI at
# http://localhost:5173 (it proxies /api → http://localhost:7071 and injects the
# local Admin principal automatically).
#
# Usage: ./scripts/dev.sh
#
# Ctrl+C stops the frontend. The backend (Azurite + Functions host) keeps running
# for fast iteration — fully tear it down with ./scripts/local-stop.sh.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# 1. Backend (idempotent: Azurite, queues, blob container, Functions host on :7071).
"$REPO_ROOT/scripts/local-start.sh"

# 2. Frontend (foreground).
echo ""
echo "── Frontend ──"
echo "  UI:       http://localhost:5173"
echo "  Backend:  http://localhost:7071   (logs: tail -f /tmp/arn-func.log)"
echo "  Stop all: ./scripts/local-stop.sh"
echo ""

cd "$REPO_ROOT/web"
[ -d node_modules ] || npm install
exec npm run dev
