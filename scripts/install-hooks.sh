#!/usr/bin/env bash
# One-time wiring so git uses the tracked hooks in scripts/hooks/.
# Idempotent; also invoked by the root package.json `prepare` script.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
git -C "$REPO_ROOT" config core.hooksPath scripts/hooks
chmod +x "$REPO_ROOT"/scripts/hooks/* 2>/dev/null || true
echo "✓ git hooks wired to scripts/hooks (pre-push → verify.sh quick)"
