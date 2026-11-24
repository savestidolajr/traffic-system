#!/usr/bin/env bash
# ============================================================================
# smoke-test.sh
# ============================================================================
# Production smoke test executed post-swap by the Azure DevOps pipeline.
# Returns non-zero on any failure so the pipeline fails the deploy.
#
# Tests:
#   1. /api/health returns 200
#   2. /api/schedules requires auth (401/403 without bearer, 200 with)
#   3. /api/jobs requires auth (401/403 without bearer, 200 with)
#   4. If ARN_SMOKE_BEARER + ARN_SMOKE_TRIGGER=1: POST /api/broadcast against
#      a "test" market and assert 200/202 with a job id.
#
# Usage:
#   ARN_SMOKE_BASE_URL=https://ibp-aue-prd-fna-app01.azurewebsites.net \
#     [ARN_SMOKE_BEARER=<aad-token>] \
#     [ARN_SMOKE_TRIGGER=1] \
#     ./deployment/scripts/smoke-test.sh
# ============================================================================

set -euo pipefail

BASE_URL="${ARN_SMOKE_BASE_URL:?ARN_SMOKE_BASE_URL is required}"
BEARER="${ARN_SMOKE_BEARER:-}"
TRIGGER="${ARN_SMOKE_TRIGGER:-0}"

fail=0
ok()   { echo "  ✓ $*"; }
warn() { echo "  ! $*"; }
err()  { echo "  ✗ $*"; fail=1; }

curl_status() {
  local url="$1"; shift
  local code
  code=$(curl --max-time 15 -s -o /dev/null -w "%{http_code}" "$@" "$url" 2>/dev/null || echo "000")
  echo "$code"
}

# ── 1. health ───────────────────────────────────────────────────────────────
echo "[1/4] GET ${BASE_URL}/api/health"
HEALTH=$(curl --max-time 15 -s "${BASE_URL}/api/health" || echo "")
HEALTH_CODE=$(curl_status "${BASE_URL}/api/health")
if [ "${HEALTH_CODE}" = "200" ]; then
  ok "health 200"
elif [ "${HEALTH_CODE}" = "503" ]; then
  warn "health 503 (degraded — non-critical downstream may be unreachable)"
  echo "${HEALTH}" | head -40
else
  err "health returned ${HEALTH_CODE}"
fi

# ── 2. schedules requires auth ──────────────────────────────────────────────
echo "[2/4] GET ${BASE_URL}/api/schedules"
NO_AUTH_CODE=$(curl_status "${BASE_URL}/api/schedules")
if [ "${NO_AUTH_CODE}" = "401" ] || [ "${NO_AUTH_CODE}" = "403" ]; then
  ok "schedules requires auth (${NO_AUTH_CODE})"
elif [ "${NO_AUTH_CODE}" = "200" ] && [ -z "${BEARER}" ]; then
  err "schedules returned 200 without auth — Easy Auth not enforcing"
else
  warn "schedules returned ${NO_AUTH_CODE} without auth"
fi

if [ -n "${BEARER}" ]; then
  AUTH_CODE=$(curl_status "${BASE_URL}/api/schedules" -H "Authorization: Bearer ${BEARER}")
  if [ "${AUTH_CODE}" = "200" ]; then
    ok "schedules 200 with bearer"
  else
    err "schedules returned ${AUTH_CODE} WITH bearer — token may be invalid or RBAC misconfigured"
  fi
fi

# ── 3. jobs ─────────────────────────────────────────────────────────────────
echo "[3/4] GET ${BASE_URL}/api/jobs"
JOBS_NOAUTH=$(curl_status "${BASE_URL}/api/jobs")
if [ "${JOBS_NOAUTH}" = "401" ] || [ "${JOBS_NOAUTH}" = "403" ]; then
  ok "jobs requires auth (${JOBS_NOAUTH})"
elif [ "${JOBS_NOAUTH}" = "200" ] && [ -z "${BEARER}" ]; then
  err "jobs returned 200 without auth"
fi
if [ -n "${BEARER}" ]; then
  JOBS_AUTH=$(curl_status "${BASE_URL}/api/jobs" -H "Authorization: Bearer ${BEARER}")
  if [ "${JOBS_AUTH}" = "200" ]; then
    ok "jobs 200 with bearer"
  else
    err "jobs returned ${JOBS_AUTH} with bearer"
  fi
fi

# ── 4. optional: trigger a synthetic broadcast ──────────────────────────────
if [ "${TRIGGER}" = "1" ] && [ -n "${BEARER}" ]; then
  echo "[4/4] POST ${BASE_URL}/api/broadcast (synthetic)"
  RESP=$(curl --max-time 30 -s -w "\n%{http_code}" -X POST \
    -H "Authorization: Bearer ${BEARER}" \
    -H "Content-Type: application/json" \
    -d '{
      "station": "smoke-test",
      "market": "smoke-test",
      "targetDurationSec": 15,
      "voiceProvider": "ElevenLabs",
      "voiceId": "smoke",
      "voiceName": "Smoke",
      "voiceModel": "eleven_v3",
      "voiceSettings": {
        "stability": 0.5, "similarityBoost": 0.75, "style": 0.15,
        "useSpeakerBoost": true, "speed": 1.0
      },
      "latitude": -33.87, "longitude": 151.21, "radiusKm": 10
    }' \
    "${BASE_URL}/api/broadcast" 2>/dev/null || echo "000")
  BODY=$(echo "${RESP}" | head -n -1)
  CODE=$(echo "${RESP}" | tail -n 1)
  if [ "${CODE}" = "200" ] || [ "${CODE}" = "202" ]; then
    ok "broadcast accepted (${CODE}): ${BODY}"
  else
    err "broadcast returned ${CODE}: ${BODY}"
  fi
else
  echo "[4/4] synthetic broadcast skipped (set ARN_SMOKE_TRIGGER=1 + ARN_SMOKE_BEARER to enable)"
fi

if [ "${fail}" -ne 0 ]; then
  echo "smoke-test: FAIL"
  exit 1
fi
echo "smoke-test: OK"
