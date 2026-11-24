#!/usr/bin/env bash
# =============================================================================
# resume.sh — Bring a parked IBP environment back to its normal running state.
#
# Reverses park.sh: scales the App Service plan back up to the normal SKU,
# starts the Function App (prod + staging slots), and re-enables the alert
# rules + availability test.
#
# Usage:
#   SUBSCRIPTION=<sub-id> ENVIRONMENT=<dev|test|prod|sbx> ./deployment/scripts/resume.sh
# =============================================================================
set -euo pipefail

SUBSCRIPTION="${SUBSCRIPTION:?Set SUBSCRIPTION env var to your Azure subscription ID}"
ENVIRONMENT="${ENVIRONMENT:?Set ENVIRONMENT env var to dev, test, prod, or sbx}"
LOCATION="${LOCATION:-australiaeast}"
RESUME_SKU="${RESUME_SKU:-P0V3}"   # matches the template default (PremiumV3 P0v3).

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
echo "Resuming ${ENVIRONMENT} (${STEM}) in $(az account show --query name -o tsv)..."

# 1. Scale the plan back up.
az appservice plan update -g "$RG" -n "$PLAN" --sku "$RESUME_SKU" -o none && echo "  plan ${PLAN} -> ${RESUME_SKU}"

# 2. Start the Function App. (The staging slot is not recreated here — park
#    removed it to allow Basic scaling; the next full deploy restores it. It's
#    not needed for normal running.)
az functionapp start -g "$RG" -n "$FNA" -o none && echo "  started $FNA"

# 3. Wait for the app to start and verify health
echo "  waiting for $FNA to reach Running state..."
for _i in $(seq 1 24); do
  STATE=$(az functionapp show -g "$RG" -n "$FNA" --query state -o tsv 2>/dev/null || echo "Unknown")
  if [ "$STATE" = "Running" ]; then break; fi
  sleep 5
done
if [ "$STATE" != "Running" ]; then
  echo "  ERROR: $FNA did not reach Running after 120s (state=$STATE)"
  exit 1
fi
echo "  $FNA is Running"
HOSTNAME=$(az functionapp show -g "$RG" -n "$FNA" --query defaultHostName -o tsv 2>/dev/null || echo "")
if [ -n "$HOSTNAME" ]; then
  HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://${HOSTNAME}/api/health" 2>/dev/null || echo "000")
  if [ "$HTTP_STATUS" != "200" ] && [ "$HTTP_STATUS" != "503" ]; then
    echo "  ERROR: /api/health returned HTTP $HTTP_STATUS (expected 200 or 503)"
    exit 1
  fi
  echo "  health endpoint returned $HTTP_STATUS"
fi

# 4. Re-enable autoscale
az monitor autoscale update -g "$RG" -n "${PLAN}-autoscale" --enabled true -o none 2>/dev/null \
  || echo "  (no autoscale setting -- run setup-autoscale.sh)"

# 5. Re-enable alerts + availability test.
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
toggle_alerts true
echo "  alerts + availability test re-enabled"

echo "Resumed ${ENVIRONMENT}. Plan at ${RESUME_SKU}, app started, health verified."
echo "Check:  curl -s -o /dev/null -w '%{http_code}\\n' https://${FNA,,}.azurewebsites.net/api/health"
