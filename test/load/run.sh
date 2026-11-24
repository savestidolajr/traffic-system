#!/usr/bin/env bash
# SUPERSEDED (2026-07-15): prefer `python3 tools/arn/arn.py perf load`. Kept as the
#   underlying k6 runner.
# =============================================================================
# run.sh — Arn load/performance test runner
#
# Usage:
#   ./test/load/run.sh <scenario> <env> [extra args passed to the scenario]
#
#   scenario: health | api-read | pipeline-burst | soak
#   env:      dev | test | prod        (prod: NEVER without explicit sign-off)
#
# Examples:
#   ./test/load/run.sh health test                    # anonymous, safe anywhere
#   ./test/load/run.sh api-read dev --vus 10 --duration 60s
#   ./test/load/run.sh pipeline-burst dev --jobs 11   # one per market, real spend
#   SMOKE=1 ./test/load/run.sh pipeline-burst dev     # 1-job harness smoke
#
# Design notes:
#   - k6 is auto-downloaded to test/load/.bin (pinned version, no global install).
#   - Auth: dev accepts the Azure CLI ARM-audience token (dev-only Easy Auth
#     lever). Test/prod API scenarios need an ARN-granted automation identity
#     (app-role-assigned service principal) — see docs/operations/load-testing.md
#     "Blockers". health works everywhere unauthenticated.
#   - Cost: pipeline-burst hits real HERE/Azure OpenAI/ElevenLabs. See the
#     runbook for per-run cost estimates BEFORE raising --jobs.
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_DIR="$SCRIPT_DIR/.bin"
K6_VERSION="v0.57.0"

SCENARIO="${1:?Usage: run.sh <health|api-read|pipeline-burst|soak> <dev|test|prod>}"
ENVIRONMENT="${2:?Usage: run.sh <scenario> <dev|test|prod>}"
shift 2

case "$ENVIRONMENT" in
  dev)  BASE_URL="https://ibp-aue-dev-fna-app01.azurewebsites.net" ;;
  test) BASE_URL="https://ibp-aue-tst-fna-app01.azurewebsites.net" ;;
  prod) BASE_URL="https://ibp-aue-prd-fna-app01.azurewebsites.net"
        echo "!! prod target — confirm an approved load-test window exists." >&2
        read -p "Type PROD to continue: " confirm
        [ "$confirm" = "PROD" ] || exit 1 ;;
  *) echo "Unknown env '$ENVIRONMENT'"; exit 1 ;;
esac

ensure_k6() {
  if command -v k6 &>/dev/null; then K6=k6; return; fi
  K6="$BIN_DIR/k6"
  if [ ! -x "$K6" ]; then
    echo "Downloading k6 $K6_VERSION..."
    mkdir -p "$BIN_DIR"
    curl -sSL "https://github.com/grafana/k6/releases/download/$K6_VERSION/k6-$K6_VERSION-linux-amd64.tar.gz" \
      | tar -xz -C "$BIN_DIR" --strip-components=1 "k6-$K6_VERSION-linux-amd64/k6"
    chmod +x "$K6"
  fi
}

arm_token() {
  az account get-access-token --resource "https://management.azure.com" --query accessToken -o tsv
}

RESULTS_DIR="$SCRIPT_DIR/results"
mkdir -p "$RESULTS_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"

case "$SCENARIO" in
  health)
    ensure_k6
    "$K6" run --summary-export "$RESULTS_DIR/health-$ENVIRONMENT-$STAMP.json" \
      -e BASE_URL="$BASE_URL" "$@" "$SCRIPT_DIR/health.js"
    ;;
  api-read)
    ensure_k6
    TOKEN="$(arm_token)"
    "$K6" run --summary-export "$RESULTS_DIR/api-read-$ENVIRONMENT-$STAMP.json" \
      -e BASE_URL="$BASE_URL" -e BEARER="$TOKEN" "$@" "$SCRIPT_DIR/api-read.js"
    ;;
  soak)
    ensure_k6
    TOKEN="$(arm_token)"
    "$K6" run --summary-export "$RESULTS_DIR/soak-$ENVIRONMENT-$STAMP.json" \
      -e BASE_URL="$BASE_URL" -e BEARER="$TOKEN" -e SOAK=1 "$@" "$SCRIPT_DIR/api-read.js"
    ;;
  pipeline-burst)
    python3 "$SCRIPT_DIR/pipeline_burst.py" --base-url "$BASE_URL" \
      --out "$RESULTS_DIR/pipeline-$ENVIRONMENT-$STAMP.json" ${SMOKE:+--jobs 1} "$@"
    ;;
  *) echo "Unknown scenario '$SCENARIO'"; exit 1 ;;
esac

echo "Results: $RESULTS_DIR"
