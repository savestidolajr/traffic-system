#!/usr/bin/env bash
# SUPERSEDED (2026-07-15): prefer `python3 tools/arn/arn.py` — `arn job`, `arn env`,
#   `arn settings`, `arn health` cover this natively and typed. This raw client is kept
#   only for routes `arn` does not wrap yet.
#
# arn-api.sh — thin client for the DEPLOYED Arn API.
#
# Acquires an AAD bearer token via the current Azure CLI identity and calls the
# IBP-AUE-DEV Function App directly (bypassing the SWA). The Function App's Easy
# Auth validates the token's audience + issuer; the in-app AuthorizationMiddleware
# enforces Operator/Admin, and in DEV `Auth__DevFallbackRole=Admin` grants Admin to
# any authenticated caller.
#
# AUTH MODES
#   Dev (current): the dev Function App's Easy Auth `allowedAudiences` was widened to
#     include the ARM audience, so the Azure CLI's own token is accepted as-is. No app
#     registration needed. This is a deliberate, reversible DEV-only lever.
#       => default ARN_API_RESOURCE=https://management.azure.com/  (just run `az login`)
#
#   Proper (test/prod-safe): a dedicated automation service principal granted the Admin
#     app role on the API app. Then nothing here changes except two env vars:
#       az login --service-principal -u <appId> -p <secret> --tenant <arn-tenant>
#       export ARN_API_RESOURCE=api://4671da9f-93b0-49bd-9310-366ede758810
#
# USAGE
#   scripts/arn-api.sh GET  /api/schedules
#   scripts/arn-api.sh GET  /api/jobs
#   scripts/arn-api.sh POST /api/broadcast '{"MarketId":"adelaide","TargetDurationSeconds":30}'
#
set -euo pipefail

HOST="${ARN_API_HOST:-https://ibp-aue-dev-fna-app01.azurewebsites.net}"
RESOURCE="${ARN_API_RESOURCE:-https://management.azure.com/}"

method="${1:-GET}"
path="${2:?usage: arn-api.sh [METHOD] /api/<path> [json-body]}"
body="${3:-}"

token="$(az account get-access-token --resource "$RESOURCE" --query accessToken -o tsv)"

args=(-sS -X "$method" -H "Authorization: Bearer ${token}")
if [[ -n "$body" ]]; then
    args+=(-H "Content-Type: application/json" -d "$body")
fi

curl "${args[@]}" "${HOST}${path}"
echo
