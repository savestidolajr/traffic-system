#!/usr/bin/env bash
# Fast backend-E2E loop. Keeps Azurite + the Functions host alive between runs so a
# single test costs ~5s and the full suite ~30s, against ~7min for scripts/e2e-backend.sh
# (which rebuilds and tears the host down on every invocation via its EXIT trap).
#
#   scripts/e2e-fast.sh up               start/refresh Azurite + host (once)
#   scripts/e2e-fast.sh reset            wipe storage clean (needed between FULL runs)
#   scripts/e2e-fast.sh test [filter]    run the suite, or one test by name substring
#   scripts/e2e-fast.sh down             stop everything
#
# Gotchas this encodes, all learned the hard way:
#   - Azurite needs --skipApiVersionCheck or every `az storage` call 400s.
#   - The host MUST write /tmp/arn-e2e-func.log: PoisonLifecycleE2ETests greps it for
#     dequeue counts and silently reports 0 if it is missing.
#   - Do NOT delete tables to reset; the host caches TableClients and 500s afterwards
#     (and Azure table delete has a recreate cooldown). Wipe the Azurite data dir instead.
#   - Tests seed named entities (voice profiles) and 409 on a second run, so a full-suite
#     pass needs `reset` first. Single-test iteration usually does not.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DIR=/tmp/azurite-arn-e2e
LOG=/tmp/arn-e2e-func.log
CONN="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"

seed() {
  for q in broadcast-queue regeneration-queue broadcast-queue-poison regeneration-queue-poison; do
    az storage queue create --name "$q" --connection-string "$CONN" -o none
  done
  for c in broadcasts audit-exports; do
    az storage container create --name "$c" --connection-string "$CONN" -o none
  done
  mkdir -p /tmp/arn-e2e-zetta
}

start_azurite() {
  pkill -f "azurite.*$DIR" 2>/dev/null || true; sleep 1
  # Redirect AND disown: a background child that inherits the caller's stdout keeps the
  # pipe open, so `scripts/e2e-fast.sh up | tail` would hang forever instead of returning.
  nohup npx --yes azurite --silent --location "$DIR" \
    --blobPort 10000 --queuePort 10001 --tablePort 10002 --skipApiVersionCheck \
    > /tmp/arn-e2e-azurite.log 2>&1 &
  disown
  for _ in $(seq 1 40); do (echo > /dev/tcp/127.0.0.1/10002) >/dev/null 2>&1 && break; sleep 0.5; done
}

start_host() {
  pkill -f "func start --port 7071" 2>/dev/null || true; sleep 1
  ( cd "$REPO_ROOT/src/ArnBulletinGenerator.Functions" && exec env \
      AzureWebJobsStorage="$CONN" FUNCTIONS_WORKER_RUNTIME=dotnet-isolated \
      AadClientId=00000000-0000-0000-0000-000000000000 Auth__DevFallbackRole="" \
      Providers__OutputProvider=Console Providers__ExternalProviderMode=Fake \
      Storage__AccountName=devstoreaccount1 Storage__ContainerName=broadcasts \
      AiSettings__AzureAI__Endpoint=https://fake.invalid AiSettings__AzureAI__Model=fake-model \
      AiSettings__AzureAI__ApiKey=fake AiSettings__ElevenLabs__ApiKey=fake \
      AiSettings__ElevenLabs__BaseUrl=https://fake.invalid AiSettings__ElevenLabs__ModelId=eleven_v3 \
      AiSettings__ElevenLabs__UseTimestampsEndpoint=false Here__ApiKey=fake \
      Here__TrafficBaseUrl=https://fake.invalid Here__GeocodingBaseUrl=https://fake.invalid \
      Here__RoutingBaseUrl=https://fake.invalid \
      Broadcast__FillerCartAudioUrl=https://fake.invalid/filler-cart.wav \
      Webhook__Enabled=false Zetta__Enabled=false E2E__ZettaOutputPath=/tmp/arn-e2e-zetta \
      AzureFunctionsJobHost__extensions__queues__maxDequeueCount=3 \
      AzureFunctionsJobHost__extensions__queues__visibilityTimeout=00:00:01 \
      func start --port 7071 > "$LOG" 2>&1 ) </dev/null >/dev/null 2>&1 &
  disown
  for _ in $(seq 1 120); do
    s="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:7071/api/health 2>/dev/null || true)"
    [[ "$s" == 200 || "$s" == 503 ]] && { echo "✓ host ready ($s)"; return; }
    sleep 1
  done
  echo "✗ host not ready; see $LOG"; exit 1
}

case "${1:-test}" in
  up)    start_azurite; seed; start_host ;;
  reset) pkill -f "azurite.*$DIR" 2>/dev/null || true; sleep 1
         rm -rf "$DIR"; mkdir -p "$DIR"; start_azurite; seed; start_host ;;
  down)  pkill -f "func start --port 7071" 2>/dev/null || true
         pkill -f "azurite.*$DIR" 2>/dev/null || true; echo "stopped" ;;
  test)  FILTER="${2:-}"
         dotnet build "$REPO_ROOT/test/ArnBulletinGenerator.E2E.Tests/ArnBulletinGenerator.E2E.Tests.csproj" -v q --nologo
         ARN_E2E_FUNCTIONS_BASE_URL="http://127.0.0.1:7071/api" ARN_E2E_FUNC_LOG_PATH="$LOG" \
           dotnet test "$REPO_ROOT/test/ArnBulletinGenerator.E2E.Tests" --no-build --nologo -v q \
           ${FILTER:+--filter "FullyQualifiedName~$FILTER"} ;;
  *)     echo "usage: $0 up|reset|test [filter]|down"; exit 2 ;;
esac
