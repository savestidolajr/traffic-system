#!/usr/bin/env bash
# =============================================================================
# park.sh — Minimise an IBP environment's idle cost.
#
# Scales the App Service plan down to Basic (B1) — the dominant always-on cost —
# and STOPS the Function App so the scheduled poller doesn't run (no AI / HERE /
# ElevenLabs pay-per-use spend while parked). Best-effort: disables the env's
# alert rules + availability test so a parked env doesn't fire false alarms.
#
# Everything else (Static Web App, Key Vault, storage, private endpoints, app
# registration wiring) is left intact, so `resume.sh` brings it straight back.
#
# Usage:
#   SUBSCRIPTION=<sub-id> ENVIRONMENT=<dev|test|prod|sbx> ./deployment/scripts/park.sh
#
# Resume with: SUBSCRIPTION=<sub-id> ENVIRONMENT=<env> ./deployment/scripts/resume.sh
#
# NOTE: prod normally runs 24/7 — parking it takes the live service down.
# =============================================================================
set -euo pipefail

SUBSCRIPTION="${SUBSCRIPTION:?Set SUBSCRIPTION env var to your Azure subscription ID}"
ENVIRONMENT="${ENVIRONMENT:?Set ENVIRONMENT env var to dev, test, prod, or sbx}"
LOCATION="${LOCATION:-australiaeast}"
PARK_SKU="${PARK_SKU:-B1}"   # Basic still supports VNet integration + Always On.

case "$ENVIRONMENT" in
  dev) ENV_CODE="DEV" ;; test) ENV_CODE="TST" ;; prod) ENV_CODE="PRD" ;; sbx) ENV_CODE="SBX" ;;
  *) echo "Unknown ENVIRONMENT '$ENVIRONMENT' (expected dev|test|prod|sbx)"; exit 1 ;;
esac
case "$LOCATION" in
  australiaeast) REGION_CODE="AUE" ;; australiasoutheast) REGION_CODE="ASE" ;;
  *) echo "Unknown LOCATION '$LOCATION'"; exit 1 ;;
esac

STEM="IBP-${REGION_CODE}-${ENV_CODE}"
RG="${STEM}-RSG-APP01"
PLAN="${STEM}-PLN-APP01"
FNA="${STEM}-FNA-APP01"

az account set --subscription "$SUBSCRIPTION"
echo "Parking ${ENVIRONMENT} (${STEM}) in $(az account show --query name -o tsv)..."
if [ "$ENVIRONMENT" = "prod" ]; then
  read -r -p "  This will STOP the live PROD service. Type 'park prod' to continue: " c
  [ "$c" = "park prod" ] || { echo "Aborted."; exit 1; }
fi
# Silence FIRST, act second. Parking stops the app and downgrades the plan; both
# legitimately make a live-looking environment unhealthy, so the alert rules and
# the availability test have to be off BEFORE anything trips them. This used to
# be the last step, which meant any failure in between (verified 2026-07-30: the
# B1 downgrade returned "No available instances" six times over ten minutes, and
# step 4 exited non-zero) left a stopped app with its availability test still
# armed, firing alerts at the on-call inbox. resume.sh turns them back on.
toggle_alerts() {
  local enabled="$1"
  for a in $(az monitor metrics alert list -g "$RG" --query "[].name" -o tsv 2>/dev/null || true); do
    az monitor metrics alert update -g "$RG" -n "$a" --enabled "$enabled" -o none 2>/dev/null
  done
  # az CLI renamed the scheduled-query switch to --disabled (inverse polarity).
  local disabled="true"; [ "$enabled" = "true" ] && disabled="false"
  for a in $(az monitor scheduled-query list -g "$RG" --query "[].name" -o tsv 2>/dev/null || true); do
    az monitor scheduled-query update -g "$RG" -n "$a" --disabled "$disabled" -o none 2>/dev/null
  done
  # Activity-log alert update via CLI fails with ScopeIsInvalid for some categories;
  # a direct ARM PATCH on the resource id works for all of them.
  for a in $(az monitor activity-log alert list -g "$RG" --query "[].id" -o tsv 2>/dev/null || true); do
    az rest --method patch --url "https://management.azure.com${a}?api-version=2020-10-01" \
      --body "{\"properties\":{\"enabled\":${enabled}}}" -o none 2>/dev/null
  done
  for w in $(az resource list -g "$RG" --resource-type Microsoft.Insights/webtests --query "[].id" -o tsv 2>/dev/null || true); do
    az resource update --ids "$w" --set properties.Enabled="$enabled" -o none 2>/dev/null
  done
}
toggle_alerts false
echo "  alerts + availability test disabled"

# 1. Stop the Function App — halts the poller/timers (no pay-per-use spend).
az functionapp stop -g "$RG" -n "$FNA" -o none && echo "  stopped $FNA"

# 1b. Basic plans don't support deployment slots, so the staging slot must be
#     removed before scaling down. It's only used for blue-green deploys, not
#     normal running, and is recreated by the next full deploy (deploy.sh / pipeline).
if az functionapp deployment slot list -g "$RG" -n "$FNA" --query "[?name=='staging'] | [0].name" -o tsv 2>/dev/null | grep -q staging; then
  az functionapp deployment slot delete -g "$RG" -n "$FNA" --slot staging -o none 2>/dev/null \
    && echo "  removed staging slot (restored on next deploy)" || true
fi

# 2. Disable autoscale before scaling down
az monitor autoscale update -g "$RG" -n "${PLAN}-autoscale" --enabled false -o none 2>/dev/null \
  || echo "  (no autoscale setting to disable)"

# 3. Scale the plan down (the dominant idle cost). Azure returns a transient
#    "No available instances ... App Service is attempting to increase capacity"
#    for the target SKU in australiaeast; it cleared after six tries on
#    2026-07-30, so retry rather than leaving the plan on a premium SKU.
for attempt in 1 2 3 4 5 6; do
  if az appservice plan update -g "$RG" -n "$PLAN" --sku "$PARK_SKU" -o none 2>/dev/null; then
    echo "  plan ${PLAN} -> ${PARK_SKU}"
    break
  fi
  if [ "$attempt" = 6 ]; then
    echo "  ERROR: plan downgrade to ${PARK_SKU} failed after ${attempt} attempts (capacity)."
    echo "  App is stopped and alerts are silenced; retry the downgrade later."
    exit 1
  fi
  echo "  downgrade attempt ${attempt} failed (capacity); retrying in 90s..."
  sleep 90
done

# 4. Verify park state
echo "  verifying park state..."
_STATE=$(az functionapp show -g "$RG" -n "$FNA" --query state -o tsv 2>/dev/null || echo "Unknown")
_SKU=$(az appservice plan show -g "$RG" -n "$PLAN" --query sku.name -o tsv 2>/dev/null || echo "Unknown")
if [ "$_STATE" != "Stopped" ] || [ "$_SKU" != "$PARK_SKU" ]; then
  echo "  ERROR: park verification failed (state=$_STATE, sku=$_SKU)"
  exit 1
fi
echo "  verified: state=$_STATE, sku=$_SKU"

echo "Parked ${ENVIRONMENT}. Plan at ${PARK_SKU}, app stopped."
echo "Resume: SUBSCRIPTION=${SUBSCRIPTION} ENVIRONMENT=${ENVIRONMENT} ./deployment/scripts/resume.sh"
