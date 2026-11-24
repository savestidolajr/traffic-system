#!/usr/bin/env bash
# ============================================================================
# setup-autoscale.sh
# ============================================================================
# Assert the declarative generation-demand autoscale configuration.
#
# Microsoft.Insights/autoscaleSettings in modules/function-app.bicep is the
# source of truth. This script deliberately does not create or mutate rules:
# deploy.sh invokes it after every deployment so a provider-side failure or a
# hand-edited setting is visible immediately. In subscriptions where Azure
# rejects App Insights custom metrics during the Bicep deployment, operators
# may use this script's assertion output to drive an attended, provider-specific
# fallback; that fallback must keep the same generation-only metric contract.
#
# The previous script configured account-wide QueueMessageCount rules. Those
# rules counted lesson-learning, audit, and poison work and could scale the
# generation plan for unrelated queues; they are intentionally gone.
#
# Usage:
#   ./deployment/scripts/setup-autoscale.sh <resource-group> <plan-name> <storage-account-name>
#
# The storage-account argument is retained for deploy.sh compatibility and is
# echoed only as deployment context. It is never used as an autoscale metric
# source.
# ============================================================================

set -euo pipefail

RG="${1:?Usage: $0 <resource-group> <plan-name> <storage-account-name>}"
PLAN="${2:?Usage: $0 <resource-group> <plan-name> <storage-account-name>}"
STORAGE="${3:?Usage: $0 <resource-group> <plan-name> <storage-account-name>}"
AUTOSCALE_NAME="${PLAN}-autoscale"

echo "Asserting declarative autoscale for plan: ${PLAN}"
echo "Storage context (not an autoscale source): ${STORAGE}"

if ! az monitor autoscale show \
  --resource-group "${RG}" \
  --name "${AUTOSCALE_NAME}" \
  --only-show-errors \
  -o none; then
  echo "ERROR: ${AUTOSCALE_NAME} is missing. Deploy modules/function-app.bicep before using a fallback." >&2
  exit 1
fi

ENABLED=$(az monitor autoscale show \
  --resource-group "${RG}" \
  --name "${AUTOSCALE_NAME}" \
  --query enabled -o tsv)
MINIMUM=$(az monitor autoscale show \
  --resource-group "${RG}" \
  --name "${AUTOSCALE_NAME}" \
  --query 'profiles[0].capacity.minimum' -o tsv)
MAXIMUM=$(az monitor autoscale show \
  --resource-group "${RG}" \
  --name "${AUTOSCALE_NAME}" \
  --query 'profiles[0].capacity.maximum' -o tsv)
RULE_METRICS=$(az monitor autoscale show \
  --resource-group "${RG}" \
  --name "${AUTOSCALE_NAME}" \
  --query 'profiles[0].rules[].metricTrigger.metricName' -o tsv)

[[ "${ENABLED}" == "true" ]] || {
  echo "ERROR: ${AUTOSCALE_NAME} exists but is disabled." >&2
  exit 1
}
[[ "${MINIMUM}" == "1" || "${MINIMUM}" == "2" ]] || {
  echo "ERROR: expected declarative minimum 1 (non-prod) or 2 (prod), got ${MINIMUM:-<missing>}." >&2
  exit 1
}
[[ "${MAXIMUM}" == "6" ]] || {
  echo "ERROR: expected declarative maximum 6, got ${MAXIMUM:-<missing>}." >&2
  exit 1
}

# Account-wide storage metrics and non-generation queues are forbidden in the
# active rule set. Bash matching avoids depending on a host grep implementation.
if [[ "${RULE_METRICS}" == *QueueMessageCount* \
  || "${RULE_METRICS}" == *ApproximateMessageCount* \
  || "${RULE_METRICS}" == *lesson-learning* \
  || "${RULE_METRICS}" == *activity-audit* \
  || "${RULE_METRICS}" == *poison* ]]; then
  echo "ERROR: autoscale contains an account-wide, lesson, audit, or poison metric." >&2
  exit 1
fi

for metric in \
  broadcast.queue.ready.depth \
  broadcast.queue.oldest_age_seconds \
  broadcast.queue.scale_in.eligible; do
  [[ "${RULE_METRICS}" == *"${metric}"* ]] || {
    echo "ERROR: declarative autoscale is missing ${metric}." >&2
    exit 1
  }
done

echo "Autoscale assertion passed: enabled=${ENABLED}, min=${MINIMUM}, max=${MAXIMUM}."
echo "Active rules use only generation ready-depth/oldest-age and positive scale-in eligibility."
