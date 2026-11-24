#!/usr/bin/env bash
set -euo pipefail

# ── Local environment startup for Arn bulletin generation testing ──
# Starts Azurite (storage emulator) and Azure Functions host.
# Idempotent — safe to run if already running.
#
# Usage: ./scripts/local-start.sh

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AZURITE_DIR="/tmp/azurite-arn"
FUNC_LOG="/tmp/arn-func.log"
FUNC_DIR="$REPO_ROOT/src/ArnBulletinGenerator.Functions"
AZURITE_CONN="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1"

echo "── Arn Local Environment ──"

# ── Azurite ──
if pgrep -f "azurite" > /dev/null 2>&1; then
    echo "✓ Azurite already running"
else
    echo "Starting Azurite..."
    mkdir -p "$AZURITE_DIR"
    npx azurite --silent \
        --location "$AZURITE_DIR" \
        --blobPort 10000 --queuePort 10001 --tablePort 10002 \
        --skipApiVersionCheck &
    sleep 3
    echo "✓ Azurite started"
fi

# ── Create queues ──
for q in broadcast-queue regeneration-queue broadcast-queue-poison; do
    az storage queue create --name "$q" --connection-string "$AZURITE_CONN" -o none 2>/dev/null || true
done
echo "✓ Queues ready"

# ── Create blob container ──
az storage container create --name broadcasts --connection-string "$AZURITE_CONN" -o none 2>/dev/null || true
echo "✓ Blob container ready"

# ── Functions host ──
if pgrep -f "func start" > /dev/null 2>&1 || pgrep -f "dotnet.*ArnBulletinGenerator.Functions" > /dev/null 2>&1; then
    echo "✓ Functions host already running"
else
    echo "Starting Functions host (log: $FUNC_LOG)..."
    cd "$FUNC_DIR"
    nohup func start > "$FUNC_LOG" 2>&1 &
    cd "$REPO_ROOT"

    # Wait for health endpoint
    echo -n "  Waiting for health"
    for i in $(seq 1 60); do
        if curl -s -o /dev/null -w '%{http_code}' http://localhost:7071/api/health 2>/dev/null | grep -qE '^(200|503)'; then
            echo ""
            echo "✓ Functions host ready (http://localhost:7071)"
            break
        fi
        echo -n "."
        sleep 3
    done

    if ! curl -s -o /dev/null -w '%{http_code}' http://localhost:7071/api/health 2>/dev/null | grep -qE '^(200|503)'; then
        echo ""
        echo "✗ Functions host failed to start. Check $FUNC_LOG"
        exit 1
    fi
fi

# ── Seed voice calibration ──
SETTINGS_FILE="$REPO_ROOT/src/ArnBulletinGenerator.Functions/local.settings.json"
PRINCIPAL="eyJhdXRoX3R5cCI6ImFhZCIsIm5hbWVfdHlwIjoibmFtZSIsInJvbGVfdHlwIjoicm9sZXMiLCJjbGFpbXMiOlt7InR5cCI6Im5hbWUiLCJ2YWwiOiJMb2NhbCBEZXYifSx7InR5cCI6InJvbGVzIiwidmFsIjoiQWRtaW4ifSx7InR5cCI6Imh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vaWRlbnRpdHkvY2xhaW1zL29iamVjdGlkZW50aWZpZXIiLCJ2YWwiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifV19"
if [[ -f "$SETTINGS_FILE" ]]; then
    DEFAULT_VOICE=$(jq -r '.Values.Broadcast__DefaultVoiceId // ""' "$SETTINGS_FILE" 2>/dev/null || echo "")
    if [[ -n "$DEFAULT_VOICE" ]]; then
        echo -n "Calibrating default voice ($DEFAULT_VOICE)..."
        CALIB_RESULT=$(curl -sf -X POST "http://localhost:7071/api/voices/$DEFAULT_VOICE/calibrate" \
            -H "X-MS-CLIENT-PRINCIPAL: $PRINCIPAL" 2>/dev/null || echo "")
        if echo "$CALIB_RESULT" | jq -e '.status == "success"' > /dev/null 2>&1; then
            WPM=$(echo "$CALIB_RESULT" | jq -r '.wordsPerMinute | floor')
            SPS=$(echo "$CALIB_RESULT" | jq -r '.syllablesPerSecond | (. * 100 | floor) / 100')
            echo " ✓ ${WPM} WPM, ${SPS} syll/s"
        else
            echo " skipped (will use defaults)"
        fi
    fi
fi

echo ""
echo "Ready. Use ./scripts/test-bulletin.sh to generate a bulletin."
echo "Use ./scripts/local-stop.sh to shut down."
