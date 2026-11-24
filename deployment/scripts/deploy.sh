#!/usr/bin/env bash
# =============================================================================
# deploy.sh — Full deployment script for ArnBulletinGenerator
# Deploys infrastructure (Bicep), secrets, function app code, and SWA frontend.
#
# Usage:
#   SUBSCRIPTION=<sub-id> ENVIRONMENT=<dev|test|prod> \
#     ./deployment/scripts/deploy.sh [--skip-infra] [--skip-code] [--skip-web]
#
# Deploys at SUBSCRIPTION scope across the ARN-confirmed multi-RG layout
# (IBP-AUE-{ENV}-RSG-{APP01,NTW01,KYV01,STA01} — see
# docs/reference/azure-naming-convention.md).
#
# Prerequisites:
#   - az CLI logged in with rights to create RGs / deploy at subscription scope
#     (or Contributor + User Access Administrator on pre-created IBP RGs)
#   - .NET SDK installed (dotnet)
#   - Node.js / npm installed (for SWA CLI + web build)
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DEPLOY_DIR="$REPO_ROOT/deployment"

# ─── Configuration (edit these or override via env vars) ─────────────────────
SUBSCRIPTION="${SUBSCRIPTION:?Set SUBSCRIPTION env var to your Azure subscription ID}"
ENVIRONMENT="${ENVIRONMENT:?Set ENVIRONMENT env var to dev, test, or prod}"
LOCATION="${LOCATION:-australiaeast}"

# Environment → naming segment + default parameter file
case "$ENVIRONMENT" in
  dev)  ENV_CODE="DEV"; DEFAULT_PARAMS="main.dev.bicepparam" ;;
  test) ENV_CODE="TST"; DEFAULT_PARAMS="main.test.bicepparam" ;;
  prod) ENV_CODE="PRD"; DEFAULT_PARAMS="main.prod.bicepparam" ;;
  *) echo "Unknown ENVIRONMENT '$ENVIRONMENT' (expected dev|test|prod)"; exit 1 ;;
esac
BICEP_PARAMS_FILE="${BICEP_PARAMS_FILE:-$DEFAULT_PARAMS}"

case "$LOCATION" in
  australiaeast)      REGION_CODE="AUE" ;;
  australiasoutheast) REGION_CODE="ASE" ;;
  *) echo "Unknown LOCATION '$LOCATION'"; exit 1 ;;
esac

# ARN naming convention: IBP-AUE-{ENV}-RSG-{GROUP}01
NAME_STEM="IBP-${REGION_CODE}-${ENV_CODE}"
RG_APP="${NAME_STEM}-RSG-APP01"
RG_KYV="${NAME_STEM}-RSG-KYV01"
# Function App, SWA, AI Services, and Logic App all live in the app RG.
RESOURCE_GROUP="$RG_APP"

# External API keys — set via env vars or prompted
ELEVENLABS_API_KEY="${ELEVENLABS_API_KEY:-}"
HERE_API_KEY="${HERE_API_KEY:-}"
# QLDTraffic is optional corroboration. When no dedicated TMR key is supplied,
# the script seeds the published demo key only if the environment vault does
# not already contain a key, preserving current behaviour without overwriting a
# later per-environment replacement.
QLDTRAFFIC_API_KEY="${QLDTRAFFIC_API_KEY:-}"
HERE_APP_ID="${HERE_APP_ID:-}"
# ─────────────────────────────────────────────────────────────────────────────

# Parse flags
SKIP_INFRA=false
SKIP_CODE=false
SKIP_WEB=false
SLOT_SWAP=false
for arg in "$@"; do
  case $arg in
    --skip-infra) SKIP_INFRA=true ;;
    --skip-code)  SKIP_CODE=true ;;
    --skip-web)   SKIP_WEB=true ;;
    --slot-swap) SLOT_SWAP=true ;;
    --help|-h)
      echo "Usage: $0 [--skip-infra] [--skip-code] [--skip-web] [--slot-swap]"
      echo "  --skip-infra  Skip Bicep infrastructure deployment"
      echo "  --skip-code   Skip function app code deployment"
      echo "  --skip-web    Skip SWA frontend deployment"
      echo "  --slot-swap   Use staging-slot swap deploy (blue-green) instead of direct zipdeploy"
      ;;
    *) echo "Unknown flag: $arg"; exit 1 ;;
  esac
done

# ─── Helpers ─────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
info()  { echo -e "${GREEN}[INFO]${NC}  $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
fail()  { echo -e "${RED}[FAIL]${NC}  $*"; exit 1; }

check_command() {
  command -v "$1" &>/dev/null || fail "'$1' is required but not found in PATH"
}

# ─── Preflight checks ───────────────────────────────────────────────────────
info "Running preflight checks..."
check_command az
check_command dotnet
check_command node
check_command npm
check_command zip

az account show &>/dev/null || fail "Not logged into Azure CLI. Run: az login"
az account set --subscription "$SUBSCRIPTION" 2>/dev/null || fail "Cannot set subscription $SUBSCRIPTION"
info "Subscription: $(az account show --query '{name:name, id:id}' -o tsv)"

# ─── Parked-environment guard ────────────────────────────────────────────────
# Detect a parked env BEFORE the Bicep deploy. park.sh stops the Function App
# and disables the alert rules; a Bicep deploy re-creates and re-enables every
# alert rule (alerts.bicep creates them enabled: true), so deploying into a
# parked env leaves alerts armed against a stopped app, which then fires false
# Sev1s. MEASURED 2026-08-10: a 14:30 deploy/alert-rule write against parked
# prod fired -no-executions (14:32:16Z), -health-availability (14:34:54Z) and
# -kv-access-denied (14:35:46Z) while the app was deliberately stopped.
# Choice: REFUSE by default rather than deploy-then-re-park. Re-parking means
# running `functionapp stop` and a plan downgrade from inside the deploy script,
# on a code path that would almost never execute — and if its detection were ever
# wrong it would take a LIVE environment off air. Refusing cannot. An operator who
# genuinely wants to deploy into a parked env sets ALLOW_PARKED_DEPLOY=1 and
# re-parks afterwards with park.sh, which is the tested path for that.
# The Function App name is deterministic under the naming convention
# (main.bicep: functionAppName = '${nameStem}-FNA-APP01').
PARKED_FNA="${NAME_STEM}-FNA-APP01"
FNA_STATE=$(az webapp show -n "$PARKED_FNA" -g "$RESOURCE_GROUP" \
  --subscription "$SUBSCRIPTION" --query state -o tsv 2>/dev/null || echo "Unknown")
if [ "$FNA_STATE" = "Unknown" ]; then
  # az webapp show can fail with a publishxml/action authorization error for
  # principals without site publish rights; a plain ARM GET on the site works.
  FNA_STATE=$(az rest --method get \
    --url "https://management.azure.com/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$PARKED_FNA?api-version=2022-09-01" \
    --query properties.state -o tsv 2>/dev/null || echo "Unknown")
fi
if [ "$FNA_STATE" = "Stopped" ]; then
  if [ "${ALLOW_PARKED_DEPLOY:-}" = "1" ]; then
    warn "$PARKED_FNA is Stopped (parked) and ALLOW_PARKED_DEPLOY=1 — proceeding."
    warn "This deploy WILL re-arm the alert rules against a stopped app. Re-run park.sh afterwards."
  else
    fail "$PARKED_FNA is Stopped — this environment is parked. A deploy re-arms every alert
  rule against a stopped app, which then fires false Sev1s (measured on prod 2026-08-10).
  Resume it first with deployment/scripts/resume.sh, or set ALLOW_PARKED_DEPLOY=1 to
  deploy anyway and re-park with deployment/scripts/park.sh afterwards."
  fi
fi

# =============================================================================
# STEP 1: Deploy Infrastructure (Bicep)
# =============================================================================
if [ "$SKIP_INFRA" = false ]; then
  info "═══ Step 1: Deploying Bicep infrastructure ═══"
  DEPLOY_NAME="deploy-$(date +%Y%m%d-%H%M%S)"

  echo "Running what-if deployment preview..."
  az deployment sub what-if \
    --subscription "$SUBSCRIPTION" \
    --location "$LOCATION" \
    --template-file "$DEPLOY_DIR/main.bicep" \
    --parameters "$DEPLOY_DIR/$BICEP_PARAMS_FILE" \
    --output table

  echo ""
  read -p "Proceed with deployment? (y/N) " confirm
  if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
      echo "Deployment cancelled."
      exit 0
  fi

  az deployment sub create \
    --subscription "$SUBSCRIPTION" \
    --location "$LOCATION" \
    --template-file "$DEPLOY_DIR/main.bicep" \
    --parameters "$DEPLOY_DIR/$BICEP_PARAMS_FILE" \
    --name "$DEPLOY_NAME" \
    -o json > /tmp/bicep_deploy_output.json 2>&1 \
    || fail "Bicep deployment failed. Check /tmp/bicep_deploy_output.json"

  info "Bicep deployment succeeded: $DEPLOY_NAME"
else
  info "═══ Step 1: Skipping infrastructure (--skip-infra) ═══"
fi

# ─── Read deployment outputs ────────────────────────────────────────────────
# Only read Bicep outputs from the deployment THIS run just created. Grabbing
# "the newest deploy-* in the subscription" leaks another environment's outputs
# (2026-07-19: --skip-infra on test picked up a dev deployment and zip-deployed
# toward IBP-AUE-DEV-FNA-APP01 inside the TST resource group).
if [ "$SKIP_INFRA" = false ]; then
  info "Reading deployment outputs from $DEPLOY_NAME..."
  OUTPUTS=$(az deployment sub show \
    --subscription "$SUBSCRIPTION" \
    --name "$DEPLOY_NAME" \
    --query 'properties.outputs' -o json 2>/dev/null || echo '{}')
else
  OUTPUTS='{}'
fi

FUNC_APP_NAME=$(echo "$OUTPUTS" | python3 -c "import json,sys; print(json.load(sys.stdin).get('functionAppName',{}).get('value',''))" 2>/dev/null || echo "")
KV_NAME=$(echo "$OUTPUTS" | python3 -c "import json,sys; print(json.load(sys.stdin).get('keyVaultName',{}).get('value',''))" 2>/dev/null || echo "")
SWA_HOSTNAME=$(echo "$OUTPUTS" | python3 -c "import json,sys; print(json.load(sys.stdin).get('staticWebAppHostName',{}).get('value',''))" 2>/dev/null || echo "")
AI_ENDPOINT=$(echo "$OUTPUTS" | python3 -c "import json,sys; print(json.load(sys.stdin).get('aiServicesEndpoint',{}).get('value',''))" 2>/dev/null || echo "")
STORAGE_NAME=$(echo "$OUTPUTS" | python3 -c "import json,sys; print(json.load(sys.stdin).get('storageAccountName',{}).get('value',''))" 2>/dev/null || echo "")

# Resolve from this environment's resource groups when outputs are unavailable
# (--skip-infra) — RGs are derived from ENVIRONMENT, so cross-env leakage is impossible.
if [ -z "$FUNC_APP_NAME" ]; then
  info "Resolving app names from $RESOURCE_GROUP..."
  FUNC_APP_NAME=$(az functionapp list -g "$RESOURCE_GROUP" --subscription "$SUBSCRIPTION" --query '[0].name' -o tsv 2>/dev/null || echo "")
  KV_NAME=$(az keyvault list -g "$RG_KYV" --subscription "$SUBSCRIPTION" --query '[0].name' -o tsv 2>/dev/null || echo "")
fi

[ -z "$FUNC_APP_NAME" ] && fail "Cannot determine function app name from deployment outputs"
[ -z "$KV_NAME" ] && fail "Cannot determine key vault name from deployment outputs"

info "Function App: $FUNC_APP_NAME"
info "Key Vault:    $KV_NAME"
info "SWA:          $SWA_HOSTNAME"

# ─── Configure autoscale (only when infra was deployed) ─────────────────
if [ "$SKIP_INFRA" = false ] && [ -n "$STORAGE_NAME" ]; then
  info "Configuring autoscale for plan ${NAME_STEM}-PLN-APP01..."
  "$SCRIPT_DIR/setup-autoscale.sh" "$RESOURCE_GROUP" "${NAME_STEM}-PLN-APP01" "$STORAGE_NAME" \
    || warn "autoscale setup failed -- run manually: $SCRIPT_DIR/setup-autoscale.sh $RESOURCE_GROUP ${NAME_STEM}-PLN-APP01 $STORAGE_NAME"
fi

# =============================================================================
# STEP 2: Populate Key Vault Secrets
# =============================================================================
info "═══ Step 2: Populating Key Vault secrets ═══"

# 2a. Azure AI API key — sourced directly from the deployed Cognitive Services resource
AI_SERVICES_NAME=$(az cognitiveservices account list \
  -g "$RESOURCE_GROUP" --subscription "$SUBSCRIPTION" \
  --query '[0].name' -o tsv 2>/dev/null || echo "")

if [ -n "$AI_SERVICES_NAME" ]; then
  AI_KEY=$(az cognitiveservices account keys list \
    --name "$AI_SERVICES_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --subscription "$SUBSCRIPTION" \
    --query 'key1' -o tsv 2>/dev/null || echo "")

  if [ -n "$AI_KEY" ]; then
    # Non-fatal: on a locked KV (pna=Disabled, e.g. test/prod) the write is Forbidden
    # from outside the VNet; the secret already exists on any previously-deployed env.
    if az keyvault secret set --vault-name "$KV_NAME" \
      --name "AiSettings--AzureAI--ApiKey" --value "$AI_KEY" \
      --subscription "$SUBSCRIPTION" -o none 2>/dev/null; then
      info "✓ AiSettings--AzureAI--ApiKey set from $AI_SERVICES_NAME (key1)"
    else
      warn "Could not write AiSettings--AzureAI--ApiKey (locked KV?) — keeping existing value"
    fi
  else
    warn "Could not retrieve AI Services key — set AiSettings--AzureAI--ApiKey manually"
  fi
else
  warn "No Cognitive Services account found — set AiSettings--AzureAI--ApiKey manually"
fi

# 2b. ElevenLabs API key
if [ -n "$ELEVENLABS_API_KEY" ]; then
  if az keyvault secret set --vault-name "$KV_NAME" \
    --name "AiSettings--ElevenLabs--ApiKey" --value "$ELEVENLABS_API_KEY" \
    --subscription "$SUBSCRIPTION" -o none 2>/dev/null; then
    info "✓ AiSettings--ElevenLabs--ApiKey set"
  else
    warn "Could not write AiSettings--ElevenLabs--ApiKey (locked KV?) — keeping existing value"
  fi
else
  # Check if it already exists — distinguish "absent" from "unreachable".
  # On test/prod the vault has publicNetworkAccess: Disabled, so a read from
  # outside the VNet fails with Forbidden + "Public network access is disabled
  # and request is not from a trusted service nor via an approved private link"
  # (MEASURED during today's prod deploy: three such warnings, with matching Key
  # Vault audit rows). These denied reads also trip the kv-access-denied alert —
  # the parent is separately scoping that rule to application identities.
  # Behaviour is unchanged: on any read failure we keep the existing value and
  # continue; only the diagnosis and message differ.
  KV_SHOW_OUT=""; KV_SHOW_RC=0
  KV_SHOW_OUT=$(az keyvault secret show --vault-name "$KV_NAME" \
    --name "AiSettings--ElevenLabs--ApiKey" --subscription "$SUBSCRIPTION" \
    --query 'value' -o tsv 2>&1) || KV_SHOW_RC=$?
  if [ "$KV_SHOW_RC" -eq 0 ] && [ -n "$KV_SHOW_OUT" ]; then
    info "✓ AiSettings--ElevenLabs--ApiKey already exists (keeping current value)"
  elif [ "$KV_SHOW_RC" -ne 0 ] && grep -qi "public network access" <<< "$KV_SHOW_OUT"; then
    warn "ELEVENLABS_API_KEY not set and Key Vault is unreachable from here (public network access disabled) — keeping existing value"
  else
    warn "ELEVENLABS_API_KEY not set and secret doesn't exist. Set it: export ELEVENLABS_API_KEY=... and re-run, or set manually."
  fi
fi

# 2c. HERE API key
if [ -n "$HERE_API_KEY" ]; then
  if az keyvault secret set --vault-name "$KV_NAME" \
    --name "Here--ApiKey" --value "$HERE_API_KEY" \
    --subscription "$SUBSCRIPTION" -o none 2>/dev/null; then
    info "✓ Here--ApiKey set"
  else
    warn "Could not write Here--ApiKey (locked KV?) — keeping existing value"
  fi
else
  # Same absent-vs-unreachable distinction as 2b (see comment there).
  KV_SHOW_OUT=""; KV_SHOW_RC=0
  KV_SHOW_OUT=$(az keyvault secret show --vault-name "$KV_NAME" \
    --name "Here--ApiKey" --subscription "$SUBSCRIPTION" \
    --query 'value' -o tsv 2>&1) || KV_SHOW_RC=$?
  if [ "$KV_SHOW_RC" -eq 0 ] && [ -n "$KV_SHOW_OUT" ]; then
    HERE_API_KEY="$KV_SHOW_OUT"
    info "✓ Here--ApiKey read from Key Vault for frontend build"
  elif [ "$KV_SHOW_RC" -ne 0 ] && grep -qi "public network access" <<< "$KV_SHOW_OUT"; then
    warn "HERE_API_KEY not set and Here--ApiKey is unreachable from here (vault denies public network access) — keeping existing value"
  else
    warn "HERE_API_KEY not set and secret doesn't exist."
  fi
fi

# 2d. QLDTraffic API key
# Keep a behaviour-neutral fallback for environments that have not yet received
# a dedicated TMR key, but never overwrite an existing per-environment secret.
QLDTRAFFIC_DEMO_KEY='3e83add325cbb69ac4d8e5bf433d770b'
if [ -n "$QLDTRAFFIC_API_KEY" ]; then
  if az keyvault secret set --vault-name "$KV_NAME" \
    --name "OfficialFeeds--QldApiKey" --value "$QLDTRAFFIC_API_KEY" \
    --subscription "$SUBSCRIPTION" -o none 2>/dev/null; then
    info "✓ OfficialFeeds--QldApiKey set from QLDTTRAFFIC_API_KEY"
  else
    warn "Could not write OfficialFeeds--QldApiKey (locked KV?) — keeping existing value"
  fi
else
  KV_SHOW_OUT=""; KV_SHOW_RC=0
  KV_SHOW_OUT=$(az keyvault secret show --vault-name "$KV_NAME" \
    --name "OfficialFeeds--QldApiKey" --subscription "$SUBSCRIPTION" \
    --query 'value' -o tsv 2>&1) || KV_SHOW_RC=$?
  if [ "$KV_SHOW_RC" -eq 0 ] && [ -n "$KV_SHOW_OUT" ]; then
    info "✓ OfficialFeeds--QldApiKey already exists (keeping current value)"
  elif [ "$KV_SHOW_RC" -ne 0 ] && grep -qi "public network access" <<< "$KV_SHOW_OUT"; then
    warn "QLDTRAFFIC_API_KEY not set and OfficialFeeds--QldApiKey is unreachable from here (vault denies public network access) — keeping existing value"
  elif az keyvault secret set --vault-name "$KV_NAME" \
    --name "OfficialFeeds--QldApiKey" --value "$QLDTRAFFIC_DEMO_KEY" \
    --subscription "$SUBSCRIPTION" -o none 2>/dev/null; then
    warn "OfficialFeeds--QldApiKey was absent; seeded the published demo key. Replace it with a dedicated TMR key when available (RISK-36)."
  else
    warn "Could not seed OfficialFeeds--QldApiKey (locked KV?) — QLDTraffic will be unavailable until the secret is set"
  fi
fi


# =============================================================================
# STEP 3: Deploy Function App Code
# =============================================================================
if [ "$SKIP_CODE" = false ]; then
  info "═══ Step 3: Building and deploying function app code ═══"

  # 3a. Build
  info "Building .NET project..."
  PUBLISH_DIR="$REPO_ROOT/publish"
  rm -rf "$PUBLISH_DIR"
  dotnet publish "$REPO_ROOT/src/ArnBulletinGenerator.Functions/ArnBulletinGenerator.Functions.csproj" \
    -c Release -o "$PUBLISH_DIR" --no-self-contained 2>&1 | tail -5

  # 3b. Create zip
  DEPLOY_ZIP="/tmp/arn_deploy_$(date +%s).zip"
  (cd "$PUBLISH_DIR" && zip -r "$DEPLOY_ZIP" . -q)
  info "Created deployment package: $(du -h "$DEPLOY_ZIP" | cut -f1)"

  # 3c. Deploy — direct zipdeploy or staging-slot swap
  if [ "$SLOT_SWAP" = true ]; then
    # Check staging slot exists
    SLOT_EXISTS=$(az functionapp deployment slot list -g "$RESOURCE_GROUP" -n "$FUNC_APP_NAME" \
      --query "[?name=='staging'].name" -o tsv 2>/dev/null || echo "")
    if [ -z "$SLOT_EXISTS" ]; then
      info "Staging slot not found -- falling back to direct zipdeploy"
      SLOT_SWAP_MODE=false
    else
      SLOT_SWAP_MODE=true
    fi
  else
    SLOT_SWAP_MODE=false
  fi

  if [ "$SLOT_SWAP_MODE" = true ]; then
    info "Deploying to staging slot of $FUNC_APP_NAME..."
    az functionapp deployment source config-zip \
      --subscription "$SUBSCRIPTION" \
      --resource-group "$RESOURCE_GROUP" \
      --name "$FUNC_APP_NAME" \
      --slot staging \
      --src "$DEPLOY_ZIP" \
      -o none 2>&1 || fail "Staging slot zip deployment failed"
    info "✓ Staging slot code deployed"

    # Poll staging slot health
    info "Waiting for staging slot to respond..."
    SLOT_HOSTNAME=$(az functionapp show -g "$RESOURCE_GROUP" -n "$FUNC_APP_NAME" --slot staging \
      --query defaultHostName -o tsv 2>/dev/null || echo "")
    if [ -n "$SLOT_HOSTNAME" ]; then
      for _i in $(seq 1 12); do
        SLOT_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://${SLOT_HOSTNAME}/api/health" 2>/dev/null || echo "000")
        if [ "$SLOT_STATUS" = "200" ] || [ "$SLOT_STATUS" = "503" ]; then break; fi
        sleep 10
      done
      if [ "$SLOT_STATUS" != "200" ] && [ "$SLOT_STATUS" != "503" ]; then
        warn "Staging slot health check returned $SLOT_STATUS -- swapping anyway"
      fi
    fi

    # Swap
    info "Swapping staging slot to production..."
    az functionapp deployment slot swap \
      -g "$RESOURCE_GROUP" -n "$FUNC_APP_NAME" \
      --slot staging \
      -o none 2>&1 || fail "Slot swap failed"
    info "✓ Slot swap complete"

    # Cleanup — remove the staging slot to save cost (Bicep recreates on next deploy)
    az functionapp deployment slot delete -g "$RESOURCE_GROUP" -n "$FUNC_APP_NAME" \
      --slot staging -o none 2>/dev/null || true
  else
    info "Deploying to $FUNC_APP_NAME via zipdeploy..."
    az functionapp deployment source config-zip \
      --subscription "$SUBSCRIPTION" \
      --resource-group "$RESOURCE_GROUP" \
      --name "$FUNC_APP_NAME" \
      --src "$DEPLOY_ZIP" \
      -o none 2>&1 || fail "Zip deployment failed"
    info "✓ Code deployed successfully"

    # Restart to pick up fresh KV references
    info "Restarting function app to resolve KV references..."
    az functionapp restart \
      -n "$FUNC_APP_NAME" \
      -g "$RESOURCE_GROUP" \
      --subscription "$SUBSCRIPTION" \
      -o none 2>/dev/null || true

    # Wait for host to start
    info "Waiting for function app to start..."
    sleep 15
  fi

  # Verify app is running (not stuck in Stopped state)
  APP_STATE=$(az webapp show -n "$FUNC_APP_NAME" -g "$RESOURCE_GROUP" \
    --subscription "$SUBSCRIPTION" --query "state" -o tsv 2>/dev/null || echo "Unknown")

  if [ "$APP_STATE" != "Running" ]; then
    warn "Function app state is '$APP_STATE' -- starting it..."
    az functionapp start -n "$FUNC_APP_NAME" -g "$RESOURCE_GROUP" \
      --subscription "$SUBSCRIPTION" -o none 2>/dev/null
    sleep 10
  fi
  info "✓ Function app state: Running"

  # Cleanup
  rm -f "$DEPLOY_ZIP"
else
  info "═══ Step 3: Skipping code deployment (--skip-code) ═══"
fi

# =============================================================================
# STEP 4: Deploy SWA Frontend
# =============================================================================
if [ "$SKIP_WEB" = false ]; then
  info "═══ Step 4: Building and deploying SWA frontend ═══"

  SWA_NAME=$(az staticwebapp list -g "$RESOURCE_GROUP" --subscription "$SUBSCRIPTION" \
    --query '[0].name' -o tsv 2>/dev/null || echo "")
  [ -z "$SWA_NAME" ] && fail "No Static Web App found in $RESOURCE_GROUP"


  # Export HERE key as VITE_ var for the Vite build so the evidence map
  # renders on HERE's basemap (with live traffic overlay) instead of the
  # fallback Leaflet map. When the key is empty the build still succeeds;
  # the fallback map is used instead.
  if [ -z "$HERE_API_KEY" ]; then
    warn "HERE_API_KEY not available — web build will use the fallback Leaflet map (no HERE basemap)"
  else
    info "Exporting VITE_HERE_API_KEY for web build"
  fi
  export VITE_HERE_API_KEY="${HERE_API_KEY}"
  # 4a. Build frontend
  info "Building web app..."
  (cd "$REPO_ROOT/web" && npm ci --silent && npm run build 2>&1 | tail -3)

  # Full prod-style RBAC: overlay the custom-Entra-provider SWA config (token
  # carries Operator/Admin roles) onto the built artifact for test/prod,
  # replacing the built-in-provider config the build ships by default. Mirrors
  # deploy-stage.yml's applyRbacConfig step. Without this, a manual redeploy
  # here silently reverts test/prod to the built-in provider — bit prod once
  # already (2026-07-07, decisions.md) and had to be patched by hand.
  if [ "$ENVIRONMENT" = "test" ] || [ "$ENVIRONMENT" = "prod" ]; then
    info "Rendering full prod-style RBAC SWA auth config ($ENVIRONMENT)..."
    # Rendered, never copied: the renderer injects this environment's rolesSource token
    # audience (`<clientId>/.default`). A plain cp ships OIDC-only scopes, Entra mints a
    # Graph-audience access token, SwaRolesSourceTokenValidator cannot validate it, and no
    # operator is ever registered at sign-in (2026-08-13).
    python3 "$REPO_ROOT/deployment/static-web-app/render-rbac-config.py" \
      --env "$ENVIRONMENT" "$REPO_ROOT/web/dist/staticwebapp.config.json"
  fi

  # 4b. Deploy
  info "Deploying to SWA: $SWA_NAME..."
  SWA_TOKEN=$(az staticwebapp secrets list -n "$SWA_NAME" -g "$RESOURCE_GROUP" \
    --subscription "$SUBSCRIPTION" --query "properties.apiKey" -o tsv)

  (cd "$REPO_ROOT/web" && npx @azure/static-web-apps-cli deploy ./dist \
    --deployment-token "$SWA_TOKEN" --env production 2>&1 | tail -5)

  info "✓ SWA frontend deployed"
else
  info "═══ Step 4: Skipping web deployment (--skip-web) ═══"
fi

# =============================================================================
# STEP 5: Post-deployment Validation
# =============================================================================
info "═══ Step 5: Post-deployment validation ═══"

# 5a. Verify function app is running
APP_STATE=$(az webapp show -n "$FUNC_APP_NAME" -g "$RESOURCE_GROUP" \
  --subscription "$SUBSCRIPTION" --query "state" -o tsv 2>/dev/null || echo "Unknown")
if [ "$APP_STATE" = "Running" ]; then
  info "✓ Function app is Running"
else
  fail "Function app is $APP_STATE — not Running!"
fi

# 5b. Verify all functions loaded
# Expected count derives from the source tree, not a hardcoded floor: the old
# `-ge 15` check passed with 15 of the 120 real functions loaded. 120 = distinct
# [Function("...")] attribute names under src/ArnBulletinGenerator.Functions/
# (MEASURED: grep -rhoE '\[Function\("[^"]+"\)\]' → 120; the Functions host's own
# "Found the following functions:" startup log agrees). If the grep yields 0
# (e.g. run from an unexpected cwd / renamed source dir) skip the check rather
# than assert nonsense. The `|| EXPECTED_FUNC_COUNT=0` also absorbs grep's
# non-zero exit (no match / missing dir) under `set -o pipefail`.
EXPECTED_FUNC_COUNT=$(grep -rhoE '\[Function\("[^"]+"\)\]' \
  "$REPO_ROOT/src/ArnBulletinGenerator.Functions/" 2>/dev/null | sort -u | wc -l) || EXPECTED_FUNC_COUNT=0
FUNC_COUNT=$(az functionapp function list \
  -g "$RESOURCE_GROUP" -n "$FUNC_APP_NAME" \
  --subscription "$SUBSCRIPTION" \
  --query 'length(@)' -o tsv 2>/dev/null || echo "0")
if [ "$EXPECTED_FUNC_COUNT" -eq 0 ]; then
  warn "Could not derive expected function count from source — skipping load check"
elif [ "$FUNC_COUNT" -ge "$EXPECTED_FUNC_COUNT" ]; then
  info "✓ $FUNC_COUNT functions loaded (expected $EXPECTED_FUNC_COUNT)"
else
  warn "Only $FUNC_COUNT of $EXPECTED_FUNC_COUNT functions loaded. Host may still be starting — check in 30s."
fi

# 5c. Verify Key Vault references
info "Checking Key Vault references..."
KV_REF_STATUS=$(az rest --method GET \
  --url "https://management.azure.com/subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Web/sites/$FUNC_APP_NAME/config/configreferences/appsettings?api-version=2022-09-01" \
  -o json 2>/dev/null || echo '{"value":[]}')

UNRESOLVED=$(echo "$KV_REF_STATUS" | python3 -c "
import json, sys
data = json.load(sys.stdin)
items = data.get('value', [])
bad = [i['name'] for i in items if i.get('properties',{}).get('status','') != 'Resolved']
if bad: print(', '.join(bad))
" 2>/dev/null || echo "")

if [ -z "$UNRESOLVED" ]; then
  info "✓ All Key Vault references resolved"
else
  warn "Unresolved KV references: $UNRESOLVED"
  warn "Try: az functionapp restart -n $FUNC_APP_NAME -g $RESOURCE_GROUP --subscription $SUBSCRIPTION"
fi

# 5d. Summary
echo ""
info "═══════════════════════════════════════════════════"
info "  Deployment Complete!"
info "═══════════════════════════════════════════════════"
info "  Function App:  https://$FUNC_APP_NAME.azurewebsites.net"
[ -n "$SWA_HOSTNAME" ] && info "  Frontend:      https://$SWA_HOSTNAME"
info "  Key Vault:     $KV_NAME"
info ""
info "  Manual steps remaining:"
info "    1. Authorize Teams API connection in Azure Portal (first deploy only)"
info "       Portal → Logic Apps → ${NAME_STEM}-LOA-APP01 → API connections → teams → Authorize"
info "═══════════════════════════════════════════════════"
