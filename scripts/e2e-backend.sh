#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FUNC_DIR="$REPO_ROOT/src/ArnBulletinGenerator.Functions"
AZURITE_DIR="${ARN_E2E_AZURITE_DIR:-/tmp/azurite-arn-e2e}"
FUNC_LOG="${ARN_E2E_FUNC_LOG:-/tmp/arn-e2e-func.log}"
ZETTA_OUT="${E2E__ZettaOutputPath:-/tmp/arn-e2e-zetta}"
AZURITE_CONN="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"
FUNC_PID=""
# CI reuses the job's Release build for the E2E test project (--no-build) to skip
# a redundant Debug rebuild; local dev defaults to a Debug build. (func start
# builds the host itself — it cannot --no-build a foreign-config output.)
TEST_CONFIG="${ARN_E2E_TEST_CONFIG:-Debug}"
TEST_NOBUILD="${ARN_E2E_TEST_NOBUILD:-}"

cleanup() {
  if [[ -n "$FUNC_PID" ]]; then
    pkill -TERM -P "$FUNC_PID" >/dev/null 2>&1 || true
    kill "$FUNC_PID" >/dev/null 2>&1 || true
    sleep 1
    pkill -KILL -P "$FUNC_PID" >/dev/null 2>&1 || true
    kill -KILL "$FUNC_PID" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

host_healthy() {
  local s
  s="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:7071/api/health 2>/dev/null || true)"
  [[ "$s" == "200" || "$s" == "503" ]]
}

# Opt-in fast path for the dev loop: reuse an already-running, healthy host on
# :7071 and skip Azurite setup + build + `func start`. Assumes the existing host
# and its storage are healthy; run without the flag for a clean slate.
if [[ "${ARN_E2E_REUSE_HOST:-0}" == "1" ]] && host_healthy; then
  echo "✓ Reusing existing Functions host on :7071 (ARN_E2E_REUSE_HOST=1); skipping Azurite + build + start"
else

# Fresh slate: a previous run leaves seeded entities (e.g. "E2E Hudson" voice
# profiles) that 409 the next run's creates. The wipe below used to sit only in
# the "not running" branch, so an Azurite still up from the previous run — the
# exact situation described — silently skipped it and the next run failed on a
# 409. CI agents are always clean; make local match.
#
# Only ever restart an Azurite serving OUR data directory. One serving a
# different location is somebody else's (e.g. a local dev stack) yet still owns
# ports 10000-10002, so reusing it would read foreign storage: fail loudly.
mapfile -t OUR_AZURITE < <(pgrep -f "azurite.*${AZURITE_DIR}" 2>/dev/null || true)
mapfile -t ANY_AZURITE < <(pgrep -f "azurite" 2>/dev/null || true)

if [[ "${ARN_E2E_REUSE_AZURITE:-0}" == "1" && ${#ANY_AZURITE[@]} -gt 0 ]]; then
  echo "✓ Reusing running Azurite (ARN_E2E_REUSE_AZURITE=1) — state NOT wiped"
elif [[ ${#ANY_AZURITE[@]} -gt 0 && ${#OUR_AZURITE[@]} -eq 0 ]]; then
  echo "x An Azurite not serving $AZURITE_DIR already owns ports 10000-10002."
  echo "  Stop it, or set ARN_E2E_REUSE_AZURITE=1 to accept its storage as-is."
  exit 1
else
  if [[ ${#OUR_AZURITE[@]} -gt 0 ]]; then
    echo "Restarting Azurite for a clean slate (pids: ${OUR_AZURITE[*]})"
    kill "${OUR_AZURITE[@]}" 2>/dev/null || true
    for _ in $(seq 1 20); do
      pgrep -f "azurite.*${AZURITE_DIR}" >/dev/null 2>&1 || break
      sleep 0.5
    done
  fi
  echo "Starting Azurite at $AZURITE_DIR"
  rm -rf "$AZURITE_DIR"
  mkdir -p "$AZURITE_DIR"
  npx azurite --silent \
    --location "$AZURITE_DIR" \
    --blobPort 10000 --queuePort 10001 --tablePort 10002 \
    --skipApiVersionCheck &
  # `npx azurite` DOWNLOADS azurite when the agent's npm cache is cold: measured 85
  # seconds on CI build 3422, where a fixed 3-second sleep meant the queue creation
  # below hit a closed port and the whole E2E lane failed with "Connection refused".
  # Wait for all three service ports instead, and say which one never opened.
  for port in 10000 10001 10002; do
    for _ in $(seq 1 300); do
      (echo > "/dev/tcp/127.0.0.1/$port") >/dev/null 2>&1 && break
      sleep 0.5
    done
    if ! (echo > "/dev/tcp/127.0.0.1/$port") >/dev/null 2>&1; then
      echo "x Azurite did not open port $port within 150s (npx may still be fetching it)."
      exit 1
    fi
  done
fi

for q in broadcast-queue regeneration-queue broadcast-queue-poison regeneration-queue-poison; do
  az storage queue create --name "$q" --connection-string "$AZURITE_CONN" -o none >/dev/null
  az storage message clear --queue-name "$q" --connection-string "$AZURITE_CONN" -o none >/dev/null
 done
for c in broadcasts audit-exports; do
  az storage container create --name "$c" --connection-string "$AZURITE_CONN" -o none >/dev/null
 done
mkdir -p "$ZETTA_OUT"
echo "✓ Azurite queues and containers ready"

if (echo > /dev/tcp/127.0.0.1/7071) >/dev/null 2>&1; then
  echo "x Port 7071 is already in use. Stop the existing Functions host before running backend E2E."
  exit 1
fi

# The additive job read models are OFF in product defaults and in every deployed
# environment until that environment's backfill has run and compared. This harness
# starts from an empty emulator, so no history exists for an index to miss: it turns
# them ON below, which is the only place their read paths are exercised.
rm -f "$FUNC_LOG"
echo "Starting Functions host (log: $FUNC_LOG)"
(
  cd "$FUNC_DIR"
  exec env \
    AzureWebJobsStorage="UseDevelopmentStorage=true" \
    FUNCTIONS_WORKER_RUNTIME="dotnet-isolated" \
    AadClientId="00000000-0000-0000-0000-000000000000" \
    Auth__DevFallbackRole="" \
    Providers__OutputProvider="Console" \
    Providers__ExternalProviderMode="Fake" \
    Storage__AccountName="devstoreaccount1" \
    Storage__ContainerName="broadcasts" \
    AiSettings__AzureAI__Endpoint="https://fake.invalid" \
    AiSettings__AzureAI__Model="fake-model" \
    AiSettings__AzureAI__ApiKey="fake" \
    AiSettings__ElevenLabs__ApiKey="fake" \
    AiSettings__ElevenLabs__BaseUrl="https://fake.invalid" \
    AiSettings__ElevenLabs__ModelId="eleven_v3" \
    AiSettings__ElevenLabs__UseTimestampsEndpoint="false" \
    Here__ApiKey="fake" \
    Here__TrafficBaseUrl="https://fake.invalid" \
    Here__GeocodingBaseUrl="https://fake.invalid" \
    Here__RoutingBaseUrl="https://fake.invalid" \
    Broadcast__FillerCartAudioUrl="https://fake.invalid/filler-cart.wav" \
    Webhook__Enabled="false" \
    Zetta__Enabled="false" \
    Jobs__Persistence__TimelineWriteEnabled="true" \
    Jobs__Persistence__TimelineReadEnabled="true" \
    Jobs__Persistence__PayloadWriteEnabled="true" \
    Jobs__Persistence__PayloadReadEnabled="true" \
    Jobs__Persistence__ReadIndexWriteEnabled="true" \
    Jobs__Persistence__ReadIndexReadEnabled="true" \
    Jobs__Persistence__LastSuccessfulWriteEnabled="true" \
    Jobs__Persistence__LastSuccessfulReadEnabled="true" \
    Jobs__Persistence__LedgerWriteEnabled="true" \
    Jobs__Persistence__LedgerReadEnabled="true" \
    Jobs__Ledger__WriteEnabled="true" \
    Jobs__Ledger__ReadEnabled="true" \
    E2E__ZettaOutputPath="$ZETTA_OUT" \
    AzureFunctionsJobHost__extensions__queues__maxDequeueCount=3 \
    AzureFunctionsJobHost__extensions__queues__visibilityTimeout=00:00:01 \
    func start --port 7071 > "$FUNC_LOG" 2>&1
) &
FUNC_PID=$!

echo -n "Waiting for Functions health"
for _ in $(seq 1 240); do
  status="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:7071/api/health 2>/dev/null || true)"
  if [[ "$status" == "200" || "$status" == "503" ]]; then
    echo ""
    echo "✓ Functions host ready with status $status"
    break
  fi
  if ! kill -0 "$FUNC_PID" >/dev/null 2>&1; then
    echo ""
    echo "✗ Functions host exited early. Log: $FUNC_LOG"
    echo "----- func host log -----"; cat "$FUNC_LOG" 2>/dev/null || true; echo "----- end func host log -----"
    exit 1
  fi
  echo -n "."
  sleep 0.5
 done

status="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:7071/api/health 2>/dev/null || true)"
if [[ "$status" != "200" && "$status" != "503" ]]; then
  echo ""
  echo "✗ Functions host failed readiness. Log: $FUNC_LOG"
  echo "----- func host log -----"; cat "$FUNC_LOG" 2>/dev/null || true; echo "----- end func host log -----"
  exit 1
fi
fi

ARN_E2E_FUNCTIONS_BASE_URL="http://127.0.0.1:7071/api" \
  ARN_E2E_FUNC_LOG_PATH="$FUNC_LOG" \
  dotnet test "$REPO_ROOT/test/ArnBulletinGenerator.E2E.Tests/ArnBulletinGenerator.E2E.Tests.csproj" \
  -c "$TEST_CONFIG" ${TEST_NOBUILD} --logger "console;verbosity=normal"
