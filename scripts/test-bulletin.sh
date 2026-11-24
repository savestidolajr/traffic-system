#!/usr/bin/env bash
set -euo pipefail
# SUPERSEDED (2026-07-15): prefer `python3 tools/arn/arn.py trigger --env local`, which
#   wraps this script. Kept as the underlying local engine. See MASTER-CLI-DESIGN.md §6.

# ── Generate a single traffic bulletin and output structured results ──
#
# Usage:
#   ./scripts/test-bulletin.sh                          # Sydney, 30s, default voice (direct HTTP)
#   ./scripts/test-bulletin.sh --market melbourne       # Melbourne, 30s
#   ./scripts/test-bulletin.sh --duration 45            # Sydney, 45s
#   ./scripts/test-bulletin.sh --voice-id JBFqnCBsd6RMkjVDRZzb --voice-name George
#   ./scripts/test-bulletin.sh --market brisbane --duration 20 --voice-id abc123
#   ./scripts/test-bulletin.sh --list-voices            # List available ElevenLabs voices
#   ./scripts/test-bulletin.sh --script-only            # Script generation only (no TTS, ~8s)
#   ./scripts/test-bulletin.sh --audio-only             # Synthesize default 30s script (no traffic/LLM)
#   ./scripts/test-bulletin.sh --audio-only --script "Your custom script text here"
#   ./scripts/test-bulletin.sh --lat -33.85 --lng 151.21 --radius 10  # Custom coverage
#   ./scripts/test-bulletin.sh --station "2GB"          # Custom station name
#   ./scripts/test-bulletin.sh --timeout 180            # Custom poll timeout
#   ./scripts/test-bulletin.sh --model eleven_turbo_v2_5  # Different TTS model
#   ./scripts/test-bulletin.sh --schedule               # Use schedule poller (slower, legacy)
#
# Outputs JSON to stdout. Files saved to output/bulletins/<jobId>.*
#
# Requires: curl, jq, local environment running (./scripts/local-start.sh)

# ── Defaults ──
MARKET="sydney"
DURATION=30
VOICE_ID=""
VOICE_PROFILE_ID=""
VOICE_NAME=""
VOICE_MODEL=""
STATION=""
SCRIPT_ONLY=false
AUDIO_ONLY=false
CUSTOM_SCRIPT=""
USE_SCHEDULE=false
TRANSCRIBE=false
TIMEOUT=120
CUSTOM_LAT=""
CUSTOM_LNG=""
CUSTOM_RADIUS=""
OUTPUT_DIR="output/bulletins"
API_BASE="http://localhost:7071/api"
LIST_VOICES=false

# Mock admin principal (local dev only — AadClientId="local-dev")
PRINCIPAL="eyJhdXRoX3R5cCI6ImFhZCIsIm5hbWVfdHlwIjoibmFtZSIsInJvbGVfdHlwIjoicm9sZXMiLCJjbGFpbXMiOlt7InR5cCI6Im5hbWUiLCJ2YWwiOiJMb2NhbCBEZXYifSx7InR5cCI6InJvbGVzIiwidmFsIjoiQWRtaW4ifSx7InR5cCI6Imh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vaWRlbnRpdHkvY2xhaW1zL29iamVjdGlkZW50aWZpZXIiLCJ2YWwiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifV19"

AZURITE_CONN="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1"

# ── Market presets (lat, lng, radiusKm) ──
declare -A MARKET_LAT MARKET_LNG MARKET_RADIUS MARKET_STATION
MARKET_LAT[sydney]=-33.8688;   MARKET_LNG[sydney]=151.2093;  MARKET_RADIUS[sydney]=25;  MARKET_STATION[sydney]="2GB"
MARKET_LAT[melbourne]=-37.8136; MARKET_LNG[melbourne]=144.9631; MARKET_RADIUS[melbourne]=25; MARKET_STATION[melbourne]="3AW"
MARKET_LAT[brisbane]=-27.4698; MARKET_LNG[brisbane]=153.0251; MARKET_RADIUS[brisbane]=25; MARKET_STATION[brisbane]="4BC"
MARKET_LAT[perth]=-31.9505;   MARKET_LNG[perth]=115.8605;   MARKET_RADIUS[perth]=25;   MARKET_STATION[perth]="6PR"
MARKET_LAT[adelaide]=-34.9285; MARKET_LNG[adelaide]=138.6007; MARKET_RADIUS[adelaide]=25; MARKET_STATION[adelaide]="5AA"
MARKET_LAT[goldcoast]=-28.0167; MARKET_LNG[goldcoast]=153.4000; MARKET_RADIUS[goldcoast]=20; MARKET_STATION[goldcoast]="Gold FM"

# ── Parse args ──
while [[ $# -gt 0 ]]; do
    case "$1" in
        --market)       MARKET="$2"; shift 2 ;;
        --duration)     DURATION="$2"; shift 2 ;;
        --voice-id)     VOICE_ID="$2"; shift 2 ;;
        --voice)        VOICE_PROFILE_ID="$2"; shift 2 ;;
        --voice-name)   VOICE_NAME="$2"; shift 2 ;;
        --model)        VOICE_MODEL="$2"; shift 2 ;;
        --station)      STATION="$2"; shift 2 ;;
        --lat)          CUSTOM_LAT="$2"; shift 2 ;;
        --lng)          CUSTOM_LNG="$2"; shift 2 ;;
        --radius)       CUSTOM_RADIUS="$2"; shift 2 ;;
        --timeout)      TIMEOUT="$2"; shift 2 ;;
        --output-dir)   OUTPUT_DIR="$2"; shift 2 ;;
        --script-only)  SCRIPT_ONLY=true; shift ;;
        --audio-only)   AUDIO_ONLY=true; shift ;;
        --script)       CUSTOM_SCRIPT="$2"; shift 2 ;;
        --schedule)     USE_SCHEDULE=true; shift ;;
        --transcribe)   TRANSCRIBE=true; shift ;;
        --list-voices)  LIST_VOICES=true; shift ;;
        --help|-h)
            sed -n '3,15p' "$0" | sed 's/^# *//'
            exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

# ── Dependency check ──
for cmd in curl jq; do
    command -v "$cmd" > /dev/null 2>&1 || { echo "{\"status\":\"error\",\"error\":\"$cmd is required but not installed\"}" ; exit 1; }
done

# ── Helper: API call with auth header ──
api() {
    local method="$1" path="$2"
    shift 2
    curl -sf -X "$method" "${API_BASE}${path}" \
        -H "X-MS-CLIENT-PRINCIPAL: $PRINCIPAL" \
        -H "Content-Type: application/json" \
        "$@"
}

# ── List voices mode ──
if $LIST_VOICES; then
    echo "── Available ElevenLabs Voices ──" >&2
    api GET /voices | jq -r '.Voices[] | "\(.VoiceId)  \(.Name)  [\(.Labels.accent // "unknown")]  models: \(.ModelIds[:3] | join(", "))"' 2>/dev/null \
        || api GET /voices | jq '.' 2>/dev/null \
        || echo "Failed to fetch voices. Is the local environment running?" >&2
    exit 0
fi

# ── Verify host is up ──
if ! curl -s -o /dev/null -w '%{http_code}' "${API_BASE}/health" 2>/dev/null | grep -qE '^(200|503)'; then
    echo '{"status":"error","error":"Functions host not running. Run ./scripts/local-start.sh first"}' 
    exit 1
fi
# ── Audio-only mode: skip traffic/LLM, go straight to TTS ──
if $AUDIO_ONLY; then
    # Resolve voice
    SETTINGS_FILE="$(cd "$(dirname "$0")/.." && pwd)/src/ArnBulletinGenerator.Functions/local.settings.json"
    if [[ -z "$VOICE_ID" && -f "$SETTINGS_FILE" ]]; then
        VOICE_ID=$(jq -r '.Values.Broadcast__DefaultVoiceId // ""' "$SETTINGS_FILE" 2>/dev/null || echo "")
        [[ -z "$VOICE_NAME" ]] && VOICE_NAME=$(jq -r '.Values.Broadcast__DefaultVoiceName // ""' "$SETTINGS_FILE" 2>/dev/null || echo "")
    fi

    # Default script if none provided
    if [[ -z "$CUSTOM_SCRIPT" ]]; then
        CUSTOM_SCRIPT="Good morning Sydney. On the M1 Pacific Motorway inbound, expect delays of around twelve minutes between Wahroonga and the Harbour Bridge after an earlier incident cleared one lane. The Warringah Freeway is crawling through North Sydney with roadworks near the off-ramp. If you're heading west, the M4 is moving well past Parramatta but there's a breakdown near Strathfield slowing the run in. Give yourself a bit of extra time this morning."
    fi

    echo "── Audio-only synthesis ──" >&2
    echo "  Script:   ${#CUSTOM_SCRIPT} chars, $(echo "$CUSTOM_SCRIPT" | wc -w | tr -d ' ') words" >&2
    echo "  Duration: ${DURATION}s target" >&2
    echo "  Voice:    ${VOICE_ID:-<default>} ${VOICE_NAME:+($VOICE_NAME)}" >&2
    echo "" >&2

    SYNTH_JSON=$(jq -n \
        --arg script "$CUSTOM_SCRIPT" \
        --argjson duration "$DURATION" \
        --arg voiceId "$VOICE_ID" \
        '{script: $script, targetDurationSeconds: $duration}
        + (if $voiceId != "" then {voiceId: $voiceId} else {} end)')

    SYNTH_RESULT=""
    for attempt in 1 2 3; do
        SYNTH_RESULT=$(api POST "/synthesize" -d "$SYNTH_JSON" 2>&1) && break
        echo "  API not ready (attempt $attempt/3), retrying..." >&2
        sleep 10
    done

    SYNTH_STATUS=$(echo "$SYNTH_RESULT" | jq -r '.status // "error"' 2>/dev/null)
    if [[ "$SYNTH_STATUS" != "success" ]]; then
        echo "{\"status\":\"error\",\"error\":\"Synthesis failed\",\"detail\":$(echo "$SYNTH_RESULT" | jq '.' 2>/dev/null || echo '""')}"
        exit 1
    fi

    AUDIO_URL=$(echo "$SYNTH_RESULT" | jq -r '.audioUrl // ""')
    SYNTH_DURATION=$(echo "$SYNTH_RESULT" | jq -r '.durationSec // 0')
    AUDIO_BYTES=$(echo "$SYNTH_RESULT" | jq -r '.audioSizeBytes // 0')

    # Download audio
    mkdir -p "$OUTPUT_DIR"
    AUDIO_FILE=""
    JOB_ID="synth-$(date +%s)"
    if [[ -n "$AUDIO_URL" && "$AUDIO_URL" != "null" ]]; then
        AUDIO_FILE="${OUTPUT_DIR}/${JOB_ID}.wav"
        BLOB_NAME="${AUDIO_URL%%\?*}"
        BLOB_NAME="${BLOB_NAME##*/}"
        az storage blob download --container-name broadcasts --name "$BLOB_NAME" --file "$AUDIO_FILE" \
            --connection-string "$AZURITE_CONN" -o none 2>/dev/null || {
            curl -sf "$AUDIO_URL" -o "$AUDIO_FILE" 2>/dev/null || AUDIO_FILE=""
        }
    fi
    AUDIO_SIZE=0
    [[ -n "$AUDIO_FILE" && -f "$AUDIO_FILE" ]] && AUDIO_SIZE=$(stat -c%s "$AUDIO_FILE" 2>/dev/null || stat -f%z "$AUDIO_FILE" 2>/dev/null || echo "0")
    [[ "$AUDIO_SIZE" -gt 0 ]] && echo "  Audio saved: $AUDIO_FILE (${AUDIO_SIZE} bytes)" >&2

    # Output JSON
    RESULT=$(jq -n \
        --arg status "success" \
        --arg mode "audio-only" \
        --arg jobId "$JOB_ID" \
        --arg script "$CUSTOM_SCRIPT" \
        --argjson durationSec "$SYNTH_DURATION" \
        --argjson targetDurationSec "$DURATION" \
        --arg audioFile "${AUDIO_FILE:-}" \
        --argjson audioSizeBytes "$AUDIO_SIZE" \
        --arg voiceId "$VOICE_ID" \
        --arg voiceName "$VOICE_NAME" \
        '{status: $status, mode: $mode, jobId: $jobId, script: $script, durationSec: $durationSec, targetDurationSec: $targetDurationSec, audioFile: $audioFile, audioSizeBytes: $audioSizeBytes, voice: {id: (if $voiceId == "" then "default" else $voiceId end), name: (if $voiceName == "" then "default" else $voiceName end)}}')
    echo "$RESULT" | tee "${OUTPUT_DIR}/${JOB_ID}.json"
    exit 0
fi

# ── Resolve market parameters ──
LAT="${CUSTOM_LAT:-${MARKET_LAT[$MARKET]:-}}"
LNG="${CUSTOM_LNG:-${MARKET_LNG[$MARKET]:-}}"
RADIUS="${CUSTOM_RADIUS:-${MARKET_RADIUS[$MARKET]:-25}}"
STATION="${STATION:-${MARKET_STATION[$MARKET]:-$MARKET}}"

if [[ -z "$LAT" || -z "$LNG" ]]; then
    echo "{\"status\":\"error\",\"error\":\"Unknown market '$MARKET'. Use --lat/--lng or one of: sydney melbourne brisbane perth adelaide goldcoast\"}"
    exit 1
fi
# ── Resolve voice profile ──
if [[ -n "$VOICE_PROFILE_ID" && "$VOICE_PROFILE_ID" != vp-* ]]; then
    PROFILES=$(api GET /voices/profiles 2>/dev/null || echo "{}")
    VOICE_PROFILE_ID=$(echo "$PROFILES" | jq -r --arg n "$VOICE_PROFILE_ID" '[.Items // .Profiles // .VoiceProfiles // .profiles // .[]] | .[] | select((.Name // .name // "") | ascii_downcase == ($n | ascii_downcase)) | (.Id // .id // .VoiceProfileId) ' | head -n1)
fi
if [[ -z "$VOICE_PROFILE_ID" ]]; then
    SCHEDULE_LOOKUP=$(api GET "/schedules/${MARKET}" 2>/dev/null || echo "{}")
    VOICE_PROFILE_ID=$(echo "$SCHEDULE_LOOKUP" | jq -r '.VoiceProfileId // .voiceProfileId // empty')
fi
if [[ -z "$VOICE_PROFILE_ID" ]]; then
    echo "{\"status\":\"error\",\"error\":\"no voice: market '$MARKET' has no schedule; pass --voice <voice-profile-id>\"}"
    exit 1
fi

SCHEDULE_ID="test-${MARKET}"
SCHEDULE_JSON=$(jq -n \
    --arg name "Test ${MARKET}" \
    --arg station "$STATION" \
    --arg voiceId "$VOICE_ID" \
    --arg voiceName "$VOICE_NAME" \
    --arg cronExpr "*/2 * * * *" \
    --argjson lat "$LAT" \
    --argjson lng "$LNG" \
    --argjson radius "$RADIUS" \
    --argjson duration "$DURATION" \
    '{
        name: $name,
        station: $station,
        cronExpression: $cronExpr,
        timezone: "Australia/Sydney",
        enabled: true,
        targetDurationSeconds: $duration,
        coverage: { latitude: $lat, longitude: $lng, radiusKm: $radius }
    }
    + (if $voiceId != "" then { voiceId: $voiceId } else {} end)
    + (if $voiceName != "" then { voiceName: $voiceName } else {} end)')

# Add voice model to schedule if specified (via voice settings on the job)
# Note: model is applied via the ModelSelector based on voiceId; explicit model override
# requires setting it on the schedule's voice config. For now, model selection is automatic.

BEFORE_TS=$(date -u +%Y-%m-%dT%H:%M:%S)

echo "── Generating bulletin ──" >&2
echo "  Market:   $MARKET ($STATION)" >&2
echo "  Coverage: $LAT, $LNG (${RADIUS}km)" >&2
echo "  Duration: ${DURATION}s" >&2
echo "  Voice:    ${VOICE_ID:-<default>} ${VOICE_NAME:+($VOICE_NAME)}" >&2
echo "  Mode:     $(if $USE_SCHEDULE; then echo "schedule poller"; else echo "direct HTTP"; fi)" >&2
if [[ -n "$VOICE_MODEL" ]]; then
    echo "  Model:    $VOICE_MODEL" >&2
fi
echo "" >&2

JOB_ID=""

if $USE_SCHEDULE; then
    # ── Schedule poller path (slower, but doesn't require dedup fix) ──
    SCHEDULE_RESULT=""
    for attempt in 1 2 3; do
        SCHEDULE_RESULT=$(api PUT "/schedules/${SCHEDULE_ID}" -d "$SCHEDULE_JSON" 2>&1) && break
        echo "  Schedule API not ready (attempt $attempt/3), retrying..." >&2
        sleep 10
    done
    if [[ -z "$SCHEDULE_RESULT" ]] || echo "$SCHEDULE_RESULT" | jq -e '.Error // .error' > /dev/null 2>&1; then
        echo "{\"status\":\"error\",\"error\":\"Failed to create schedule\",\"detail\":$(echo "$SCHEDULE_RESULT" | jq '.' 2>/dev/null || echo '""')}"
        exit 1
    fi
    echo "  Schedule created, waiting for poller..." >&2

    POLL_START=$(date +%s)
    while true; do
        ELAPSED=$(( $(date +%s) - POLL_START ))
        if (( ELAPSED > TIMEOUT )); then
            api PUT "/schedules/${SCHEDULE_ID}" -d '{"enabled":false}' > /dev/null 2>&1 || true
            echo "{\"status\":\"error\",\"error\":\"Timeout after ${TIMEOUT}s waiting for bulletin generation\"}"
            exit 1
        fi
        JOBS=$(api GET "/jobs" 2>/dev/null || echo "{}")
        CANDIDATE=$(echo "$JOBS" | jq -r --arg sid "$SCHEDULE_ID" --arg station "$STATION" --arg before "$BEFORE_TS" '
            def items: if type == "object" and has("Items") then .Items
                       elif type == "object" and has("Jobs") then .Jobs
                       elif type == "array" then .
                       else [] end;
            def matches: (.Market // .market // "") as $m | (.Station // .station // "") as $s |
                ($m == $sid or $s == $station) and
                ((.CreatedAt // .createdAt // "") > $before) and
                ((.Status // .status) | test("WaitingApproval|WAITING_APPROVAL|Failed|FAILED|FallbackUsed|FALLBACK_USED"));
            [ items[] | select(matches) ] | sort_by(.CreatedAt // .createdAt) | last // empty
        ' 2>/dev/null || echo "")
        if [[ -n "$CANDIDATE" && "$CANDIDATE" != "null" ]]; then
            JOB_ID=$(echo "$CANDIDATE" | jq -r '.JobId // .jobId // empty')
            [[ -n "$JOB_ID" ]] && break
        fi
        echo -n "." >&2
        sleep 5
    done
    BROADCAST_JSON=$(jq -n \
        --arg location "$STATION" \
        --argjson duration "$DURATION" \
        --argjson lat "$LAT" \
        --argjson lng "$LNG" \
        --argjson radius "$RADIUS" \
        --arg voiceProfileId "$VOICE_PROFILE_ID" \
        --arg voiceId "$VOICE_ID" \
        --arg voiceName "$VOICE_NAME" \
        --argjson scriptOnly "$SCRIPT_ONLY" \
        '{
            location: $location,
            targetDurationSeconds: $duration,
            coverage: { latitude: $lat, longitude: $lng, radiusKm: $radius },
            scriptOnly: $scriptOnly,
            VoiceProfileId: $voiceProfileId
        }
        + (if $voiceId != "" then { voiceId: $voiceId } else {} end)
        + (if $voiceName != "" then { voiceName: $voiceName } else {} end)')

    TRIGGER_RESULT=""
    for attempt in 1 2 3; do
        TRIGGER_RESULT=$(api POST "/broadcast" -d "$BROADCAST_JSON" 2>&1) && break
        echo "  API not ready (attempt $attempt/3), retrying..." >&2
        sleep 10
    done
    JOB_ID=$(echo "$TRIGGER_RESULT" | jq -r '.JobId // .jobId // empty' 2>/dev/null)
    if [[ -z "$JOB_ID" ]]; then
        echo "{\"status\":\"error\",\"error\":\"Failed to trigger broadcast\",\"detail\":$(echo "$TRIGGER_RESULT" | jq '.' 2>/dev/null || echo '""')}"
        exit 1
    fi
    echo "  Triggered job: $JOB_ID — waiting for pipeline..." >&2

    # Poll until the job reaches a terminal state
    POLL_START=$(date +%s)
    while true; do
        ELAPSED=$(( $(date +%s) - POLL_START ))
        if (( ELAPSED > TIMEOUT )); then
            echo "{\"status\":\"error\",\"error\":\"Timeout after ${TIMEOUT}s waiting for job $JOB_ID\"}"
            exit 1
        fi
        JOB_CHECK=$(api GET "/jobs/${JOB_ID}" 2>/dev/null || echo "{}")
        CHECK_STATUS=$(echo "$JOB_CHECK" | jq -r '.Status // .status // ""')
        if echo "$CHECK_STATUS" | grep -qiE 'WAITING_APPROVAL|WaitingApproval|APPROVED|Approved|FAILED|Failed|FALLBACK_USED|FallbackUsed'; then
            break
        fi
        echo -n "." >&2
        sleep 3
    done
fi

echo "" >&2
echo "  Job found: $JOB_ID" >&2

# ── Disable schedule ──
api PUT "/schedules/${SCHEDULE_ID}" -d '{"enabled":false}' > /dev/null 2>&1 || true
JOB_DETAIL=$(api GET "/jobs/${JOB_ID}" 2>/dev/null || echo "{}")

JOB_STATUS=$(echo "$JOB_DETAIL" | jq -r '.Status // .status // "unknown"')
SCRIPT_TEXT=$(echo "$JOB_DETAIL" | jq -r '.Script.Data.Text // .scriptText // ""')
MEASURED_DUR=$(echo "$JOB_DETAIL" | jq -r '.Audio.MeasuredDurationSec // .measuredDurationSec // 0')
AUDIO_URL=$(echo "$JOB_DETAIL" | jq -r '.Audio.Data.Url // .audioUrl // ""')
INCIDENT_COUNT=$(echo "$JOB_DETAIL" | jq -r '.SelectedIncidents | length // 0' 2>/dev/null || echo "0")
CORRIDOR_COUNT=$(echo "$JOB_DETAIL" | jq -r '.SelectedCorridors | length // 0' 2>/dev/null || echo "0")

# ── Extract diagnostics from API response ──
STATUS_HISTORY=$(echo "$JOB_DETAIL" | jq '[.StatusHistory // [] | .[] | {status: .Status, at: .At, by: .By, note: (.Note // "")}]' 2>/dev/null || echo "[]")
JOB_LOGS=$(echo "$JOB_DETAIL" | jq '[.Logs // [] | .[] | {at: .At, level: .Level, message: .Message}]' 2>/dev/null || echo "[]")
ATTEMPT_HISTORY=$(echo "$JOB_DETAIL" | jq '[.AttemptHistory // [] | .[] | {attempt: .AttemptNumber, succeeded: .Succeeded, at: .At, syllablesPerSecond: .SyllablesPerSecond, estimatedDurationSec: .EstimatedDurationSec, actualDurationSec: .ActualDurationSec, failureReason: .FailureReason}]' 2>/dev/null || echo "[]")
ATTEMPT_COUNT=$(echo "$ATTEMPT_HISTORY" | jq 'length' 2>/dev/null || echo "0")
REJECTION_COUNT=$(echo "$JOB_DETAIL" | jq '.RejectionHistory | length // 0' 2>/dev/null || echo "0")

# ── Ensure output directory exists ──
mkdir -p "$OUTPUT_DIR"

# ── Dump filtered func log for this job ID ──
FUNC_LOG="/tmp/arn-func.log"
LOG_FILE="${OUTPUT_DIR}/${JOB_ID}.log"
if [[ -f "$FUNC_LOG" ]]; then
    strings "$FUNC_LOG" | grep -F "$JOB_ID" | grep -v 'User-Agent' | grep -v 'x-ms-' | grep -v 'Authorization' > "$LOG_FILE" 2>/dev/null || true
    LOG_LINES=$(wc -l < "$LOG_FILE" 2>/dev/null || echo "0")
    echo "  Logs saved: $LOG_FILE ($LOG_LINES lines)" >&2
else
    echo "" > "$LOG_FILE"
    LOG_LINES=0
fi

# ── Extract stage timings from func log ──
extract_timing() {
    local pattern="$1"
    grep -oP "$pattern"' completed in \K[0-9]+' "$LOG_FILE" 2>/dev/null | tail -1 || echo ""
}
TRAFFIC_FETCH_MS=$(grep -oP '\[TrafficFetch\].*traffic fetch completed in \K[0-9]+' "$LOG_FILE" 2>/dev/null | tail -1 || true)
GENERATION_MS=$(grep -oP '\[GenerateBroadcast\].*generation loop finished in \K[0-9]+' "$LOG_FILE" 2>/dev/null | tail -1 || true)
SYNTHESIS_MS=$(grep -oP '\[AudioSynthesize\].*synthesis (succeeded|FAILED) in \K[0-9]+' "$LOG_FILE" 2>/dev/null | tail -1 || true)
ORCHESTRATION_MS=$(grep -oP '\[Orchestrate\].*completed in \K[0-9]+' "$LOG_FILE" 2>/dev/null | tail -1 || true)
GEN_ATTEMPTS=$(grep -c '\[AudioSynthesize\].*starting synthesis' "$LOG_FILE" 2>/dev/null || true)
WORD_CORRECTIONS=$(grep -c '\[WordTarget\].*correcting word count' "$LOG_FILE" 2>/dev/null || true)
STALENESS_DROPPED=$(grep -oP '\[TrafficStaleness\].*dropped \K[0-9]+' "$LOG_FILE" 2>/dev/null | head -1 || true)
CONTENT_SAFETY_BLOCKED=$(grep -c '\[ContentSafety\].*blocked' "$LOG_FILE" 2>/dev/null || true)

# Default empty values to "null" for --arg or "0" for --argjson
TRAFFIC_FETCH_MS="${TRAFFIC_FETCH_MS:-null}"
GENERATION_MS="${GENERATION_MS:-null}"
SYNTHESIS_MS="${SYNTHESIS_MS:-null}"
ORCHESTRATION_MS="${ORCHESTRATION_MS:-null}"
GEN_ATTEMPTS="${GEN_ATTEMPTS:-0}"
WORD_CORRECTIONS="${WORD_CORRECTIONS:-0}"
STALENESS_DROPPED="${STALENESS_DROPPED:-0}"
CONTENT_SAFETY_BLOCKED="${CONTENT_SAFETY_BLOCKED:-0}"

# ── Handle failure ──
if [[ "$JOB_STATUS" == "Failed" || "$JOB_STATUS" == "FAILED" ]]; then
    ERROR_MSG=$(echo "$JOB_DETAIL" | jq -r '.ErrorMessage // .errorMessage // "unknown error"')
    FAIL_JSON=$(jq -n \
        --arg status "failed" \
        --arg jobId "$JOB_ID" \
        --arg market "$MARKET" \
        --arg station "$STATION" \
        --arg jobStatus "$JOB_STATUS" \
        --arg error "$ERROR_MSG" \
        --arg logFile "$LOG_FILE" \
        --argjson statusHistory "$STATUS_HISTORY" \
        --argjson logs "$JOB_LOGS" \
        --argjson attempts "$ATTEMPT_HISTORY" \
        '{status: $status, jobId: $jobId, market: $market, station: $station, jobStatus: $jobStatus, error: $error, logFile: $logFile, diagnostics: {statusHistory: $statusHistory, logs: $logs, attempts: $attempts}}')
    echo "$FAIL_JSON" | tee "${OUTPUT_DIR}/${JOB_ID}.json"
    exit 1
fi

# ── Download audio ──
AUDIO_FILE=""
AUDIO_SIZE=0

if [[ -n "$AUDIO_URL" && "$AUDIO_URL" != "null" ]]; then
    AUDIO_FILE="${OUTPUT_DIR}/${JOB_ID}.wav"
    
    # Extract blob name from various URL formats
    BLOB_NAME="$AUDIO_URL"
    BLOB_NAME="${BLOB_NAME%%\?*}"                          # strip query string
    BLOB_NAME="${BLOB_NAME##*/}"                            # keep only filename
    BLOB_NAME="${BLOB_NAME#local://}"                       # strip local:// prefix
    
    if [[ -n "$BLOB_NAME" ]]; then
        az storage blob download \
            --container-name broadcasts \
            --name "$BLOB_NAME" \
            --file "$AUDIO_FILE" \
            --connection-string "$AZURITE_CONN" \
            -o none 2>/dev/null || {
            curl -sf "$AUDIO_URL" -o "$AUDIO_FILE" 2>/dev/null || {
                AUDIO_FILE=""
                echo "  Warning: could not download audio" >&2
            }
        }
    fi
    
    if [[ -n "$AUDIO_FILE" && -f "$AUDIO_FILE" ]]; then
        AUDIO_SIZE=$(stat -c%s "$AUDIO_FILE" 2>/dev/null || stat -f%z "$AUDIO_FILE" 2>/dev/null || echo "0")
        echo "  Audio saved: $AUDIO_FILE (${AUDIO_SIZE} bytes)" >&2
    fi
fi
# ── Transcribe audio (optional) ──
TRANSCRIPT=""
TRANSCRIPT_FILE=""
PRONUNCIATION_ISSUES=""
if $TRANSCRIBE && [[ -n "$AUDIO_FILE" && -f "$AUDIO_FILE" ]]; then
    REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
    WHISPER_BIN="${REPO_ROOT}/.venv/bin/whisper"
    if [[ -x "$WHISPER_BIN" ]]; then
        echo "  Transcribing audio..." >&2
        TRANSCRIPT_FILE="${OUTPUT_DIR}/${JOB_ID}.transcript.txt"

        # Prompt Whisper with Australian place names so it transcribes them correctly
        WHISPER_PROMPT="Warringah Freeway, Wahroonga, Woolloongabba, Toowoomba, Parramatta, Strathfield, Coomera, Mooloolaba, Pacific Motorway, WestConnex, NorthConnex, EastLink, CityLink, Tullamarine, Calder Freeway, Monash Freeway, Bolte Bridge, Ipswich Motorway, Logan Motorway, Gateway Motorway, Bruce Highway, Hume Highway, Princes Highway, Great Western Highway, Anzac Bridge, Sydney Harbour Bridge, Eastern Distributor, Cross City Tunnel, West Gate Freeway, Wahroonga, Hornsby, Chatswood, Epping, Bankstown, Liverpool, Penrith, Campbelltown, Blacktown, Hurstville, Kogarah, Sutherland, Cronulla, Manly, Mosman, Balgowlah, Brookvale, Dee Why, Narrabeen, Avalon, Kingsford Smith, Mascot, Botany, Maroubra, Coogee, Bondi, Paddington, Surry Hills, Redfern, Newtown, Marrickville, Burwood, Ashfield, Homebush, Ryde, Gladesville, Drummoyne"

        "$WHISPER_BIN" "$AUDIO_FILE" \
            --model base --language en \
            --output_format txt \
            --output_dir "$OUTPUT_DIR" \
            --fp16 False \
            --initial_prompt "$WHISPER_PROMPT" \
            2>/dev/null
        # Whisper names output after the input file
        WHISPER_OUT="${OUTPUT_DIR}/$(basename "$AUDIO_FILE" .wav).txt"
        if [[ -f "$WHISPER_OUT" ]]; then
            mv "$WHISPER_OUT" "$TRANSCRIPT_FILE"
            TRANSCRIPT=$(cat "$TRANSCRIPT_FILE")
            echo "  Transcript saved: $TRANSCRIPT_FILE" >&2

            # Simple word-level diff to find pronunciation issues
            if [[ -n "$SCRIPT_TEXT" ]]; then
                # Normalize both texts: lowercase, strip punctuation, one word per line
                SCRIPT_WORDS=$(echo "$SCRIPT_TEXT" | tr '[:upper:]' '[:lower:]' | sed "s/[^a-z0-9' ]//g" | tr ' ' '\n' | grep -v '^$')
                TRANSCRIPT_WORDS=$(echo "$TRANSCRIPT" | tr '[:upper:]' '[:lower:]' | sed "s/[^a-z0-9' ]//g" | tr ' ' '\n' | grep -v '^$')
                # Words in script but not in transcript (potential mispronunciations)
                MISSING=$(comm -23 <(echo "$SCRIPT_WORDS" | sort -u) <(echo "$TRANSCRIPT_WORDS" | sort -u) | tr '\n' ', ' | sed 's/, $//')
                EXTRA=$(comm -13 <(echo "$SCRIPT_WORDS" | sort -u) <(echo "$TRANSCRIPT_WORDS" | sort -u) | tr '\n' ', ' | sed 's/, $//')
                if [[ -n "$MISSING" || -n "$EXTRA" ]]; then
                    PRONUNCIATION_ISSUES=$(jq -n \
                        --arg missing "$MISSING" \
                        --arg extra "$EXTRA" \
                        '{wordsInScriptNotInTranscript: ($missing | split(", ")), wordsInTranscriptNotInScript: ($extra | split(", "))}')
                    echo "  Pronunciation diff: missing=[$MISSING] extra=[$EXTRA]" >&2
                else
                    PRONUNCIATION_ISSUES="{}"
                    echo "  Pronunciation: all words match ✓" >&2
                fi
            fi
        else
            echo "  Warning: transcription produced no output" >&2
        fi
    else
        echo "  Warning: Whisper not installed (.venv/bin/whisper). Skipping transcription." >&2
    fi
fi

# ── Build and output structured JSON ──
RESULT_JSON=$(jq -n \
    --arg status "success" \
    --arg jobId "$JOB_ID" \
    --arg market "$MARKET" \
    --arg station "$STATION" \
    --arg jobStatus "$JOB_STATUS" \
    --arg script "$SCRIPT_TEXT" \
    --argjson durationSec "$MEASURED_DUR" \
    --argjson targetDurationSec "$DURATION" \
    --argjson incidentCount "$INCIDENT_COUNT" \
    --argjson corridorCount "$CORRIDOR_COUNT" \
    --arg audioFile "${AUDIO_FILE:-}" \
    --argjson audioSizeBytes "$AUDIO_SIZE" \
    --arg logFile "$LOG_FILE" \
    --arg voiceId "$VOICE_ID" \
    --arg voiceName "$VOICE_NAME" \
    --argjson lat "$LAT" \
    --argjson lng "$LNG" \
    --argjson radiusKm "$RADIUS" \
    --argjson statusHistory "$STATUS_HISTORY" \
    --argjson logs "$JOB_LOGS" \
    --argjson attempts "$ATTEMPT_HISTORY" \
    --argjson attemptCount "$ATTEMPT_COUNT" \
    --argjson rejectionCount "$REJECTION_COUNT" \
    --arg trafficFetchMs "${TRAFFIC_FETCH_MS:-null}" \
    --arg generationMs "${GENERATION_MS:-null}" \
    --arg synthesisMs "${SYNTHESIS_MS:-null}" \
    --arg orchestrationMs "${ORCHESTRATION_MS:-null}" \
    --argjson wordCorrections "$WORD_CORRECTIONS" \
    --argjson stalenessDropped "$STALENESS_DROPPED" \
    --argjson contentSafetyBlocked "$CONTENT_SAFETY_BLOCKED" \
    --argjson synthAttempts "$GEN_ATTEMPTS" \
    --arg transcript "${TRANSCRIPT:-}" \
    --arg transcriptFile "${TRANSCRIPT_FILE:-}" \
    --arg pronIssues "${PRONUNCIATION_ISSUES:-}" \
    '{
        status: $status,
        jobId: $jobId,
        market: $market,
        station: $station,
        jobStatus: $jobStatus,
        script: $script,
        durationSec: $durationSec,
        targetDurationSec: $targetDurationSec,
        incidentCount: $incidentCount,
        corridorCount: $corridorCount,
        audioFile: $audioFile,
        audioSizeBytes: $audioSizeBytes,
        logFile: $logFile,
        coverage: { latitude: $lat, longitude: $lng, radiusKm: $radiusKm },
        voice: {
            id: (if $voiceId == "" then "default" else $voiceId end),
            name: (if $voiceName == "" then "default" else $voiceName end)
        },
        transcription: {
            transcript: $transcript,
            transcriptFile: $transcriptFile,
            pronunciationDiff: (if $pronIssues == "" then null else ($pronIssues | fromjson) end)
        },
        diagnostics: {
            pipelineMs: ($orchestrationMs | if . == "null" then null else tonumber end),
            stages: {
                trafficFetchMs: ($trafficFetchMs | if . == "null" then null else tonumber end),
                generationLoopMs: ($generationMs | if . == "null" then null else tonumber end),
                audioSynthesisMs: ($synthesisMs | if . == "null" then null else tonumber end)
            },
            attempts: {
                synthesisAttempts: $synthAttempts,
                wordTargetCorrections: $wordCorrections,
                totalAttempts: $attemptCount,
                rejections: $rejectionCount,
                contentSafetyBlocked: $contentSafetyBlocked,
                stalenessDropped: $stalenessDropped
            },
            statusHistory: $statusHistory,
            logs: $logs,
            attemptHistory: $attempts
        }
    }')

# Save JSON result to file and output to stdout
echo "$RESULT_JSON" | tee "${OUTPUT_DIR}/${JOB_ID}.json"