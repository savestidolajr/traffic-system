#!/usr/bin/env bash
set -euo pipefail

# ── Run the bulletin quality eval and print a scorecard ──
#
# Drives the local eval harness: replayed (deterministic, free) HERE traffic →
# live Azure OpenAI script generation → live ElevenLabs synthesis → fabrication,
# measured-duration, style, safety and pronunciation scorers. Use it to baseline,
# then re-run after a change, and diff the scorecards.
#
# Usage:
#   ./scripts/eval.sh                       # run + print scorecard, save to output/eval/
#   ./scripts/eval.sh --label before        # tag the saved scorecard (for before/after)
#   ./scripts/eval.sh --label after
#   ./scripts/eval.sh --no-build            # skip the build (use existing binaries)
#   ./scripts/eval.sh --live-here         # use live HERE APIs instead of replay corpus
#   ./scripts/eval.sh --no-save           # don't write the scorecard to disk
#   ./scripts/eval.sh --script-only       # skip ElevenLabs + Whisper (no TTS quota; duration is an estimate)
#   ./scripts/eval.sh --model gpt-5.1     # override the model deployment (default: from local.settings.json)
#   ./scripts/eval.sh --review            # write an agent-judged review pack (source data + script + scores)
#   ./scripts/eval.sh --full-pipeline     # judge the SHIPPED script: draft → review → refine (not the raw draft)
#
# Keys are read from src/ArnBulletinGenerator.Functions/local.settings.json.
# Azure OpenAI is required. HERE is replayed by default; --live-here requires
# Here__ApiKey. ElevenLabs is optional (without it, duration falls back to an
# estimate and pronunciation is skipped). Pronunciation also needs local Whisper
# at .venv/bin/whisper, which is confined to 2 low-priority cores so it won't hog
# the machine.
#
# Requires: dotnet, jq.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

LOCAL_SETTINGS="src/ArnBulletinGenerator.Functions/local.settings.json"
TEST_PROJECT="test/ArnBulletinGenerator.Infrastructure.Tests"
FILTER="FullyQualifiedName~BulletinEvalTests"
OUTPUT_DIR="output/eval"

LABEL=""
DO_BUILD=true
DO_SAVE=true
SCRIPT_ONLY=false
MODEL_OVERRIDE=""
LIVE_HERE=false
REVIEW=false
FULL_PIPELINE=false

# ── Parse args ──
while [[ $# -gt 0 ]]; do
    case "$1" in
        --label)    LABEL="$2"; shift 2 ;;
        --no-build) DO_BUILD=false; shift ;;
        --no-save)  DO_SAVE=false; shift ;;
        --script-only) SCRIPT_ONLY=true; shift ;;
        --model)    MODEL_OVERRIDE="$2"; shift 2 ;;
        --live-here) LIVE_HERE=true; shift ;;
        --review)   REVIEW=true; shift ;;
        --full-pipeline) FULL_PIPELINE=true; shift ;;
        -h|--help)  awk 'NR>=4{if(/^#/){sub(/^# ?/,"");print}else exit}' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

# ── Preconditions ──
command -v dotnet >/dev/null || { echo "error: dotnet not found" >&2; exit 1; }
command -v jq >/dev/null     || { echo "error: jq not found" >&2; exit 1; }
[[ -f "$LOCAL_SETTINGS" ]]    || { echo "error: $LOCAL_SETTINGS not found" >&2; exit 1; }

read_key() { jq -r ".Values[\"$1\"] // empty" "$LOCAL_SETTINGS"; }

AZURE_OPENAI_ENDPOINT="$(read_key AiSettings__AzureAI__Endpoint)"
AZURE_OPENAI_API_KEY="$(read_key AiSettings__AzureAI__ApiKey)"
AZURE_OPENAI_MODEL="$(read_key AiSettings__AzureAI__Model)"
AZURE_OPENAI_MODEL="${MODEL_OVERRIDE:-$AZURE_OPENAI_MODEL}"
ELEVENLABS_API_KEY="$(read_key AiSettings__ElevenLabs__ApiKey)"
HERE_API_KEY="$(read_key Here__ApiKey)"

if [[ -z "$AZURE_OPENAI_ENDPOINT" || -z "$AZURE_OPENAI_API_KEY" || "$AZURE_OPENAI_API_KEY" == "test-key" ]]; then
    echo "error: Azure OpenAI credentials missing in $LOCAL_SETTINGS (AiSettings__AzureAI__*)" >&2
    exit 1
fi

export ARN_EVAL_LIVE=1
if $LIVE_HERE; then
    if [[ -z "$HERE_API_KEY" || "$HERE_API_KEY" == "test-key" ]]; then
        echo "error: HERE credentials missing in $LOCAL_SETTINGS (Here__ApiKey)" >&2
        exit 1
    fi
    export ARN_EVAL_HERE_LIVE=1 HERE_API_KEY
fi
if $REVIEW; then
    export ARN_EVAL_REVIEW=1
    echo "  review:      enabled (review pack written to output/eval/ for manual judgement)"
fi
if $FULL_PIPELINE; then
    export ARN_EVAL_FULL_PIPELINE=1
    echo "  pipeline:    full (draft → review → refine; judges the shipped script)"
fi
export AZURE_OPENAI_ENDPOINT AZURE_OPENAI_API_KEY AZURE_OPENAI_MODEL

echo "── Bulletin eval ──"
echo "  model:       ${AZURE_OPENAI_MODEL:-<default>}"
echo "  HERE:        $($LIVE_HERE && echo live || echo replay)"
if $SCRIPT_ONLY; then
    export ARN_EVAL_SCRIPT_ONLY=1
    echo "  mode:        script-only (no ElevenLabs, no Whisper — no TTS quota)"
    echo "  duration:    estimate (word-rate)"
    echo "  pronunciation: skipped"
else
    if [[ -n "$ELEVENLABS_API_KEY" && "$ELEVENLABS_API_KEY" != "test-key" ]]; then
        export ELEVENLABS_API_KEY
        echo "  duration:    measured (ElevenLabs — uses TTS quota)"
    else
        echo "  duration:    estimate only (no ElevenLabs key)"
    fi
    if [[ -x "$REPO_ROOT/.venv/bin/whisper" ]]; then
        echo "  pronunciation: enabled (Whisper, 2 cores)"
    else
        echo "  pronunciation: skipped (.venv/bin/whisper not found)"
    fi
fi
echo ""

# ── Build (unless skipped) ──
if $DO_BUILD; then
    echo "Building…"
    dotnet build "$TEST_PROJECT" -c Debug --nologo -v q >/dev/null
fi

# ── Run ──
LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT

set +e
dotnet test "$TEST_PROJECT" -c Debug --no-build \
    --filter "$FILTER" \
    --logger "console;verbosity=detailed" >"$LOG" 2>&1
RESULT=$?
set -e

# ── Extract the scripts + scorecard from the test output ──
SCORECARD="$(sed -n '/Standard Output Messages:/,/^Test Run /p' "$LOG" \
    | grep -vE '^\[xUnit|Standard Output Messages:|^Test Run ' \
    | sed 's/^ //')"

if [[ -z "$SCORECARD" ]]; then
    echo "No scorecard produced. Test output:" >&2
    grep -vE '^\[xUnit' "$LOG" | tail -30 >&2
    exit "${RESULT:-1}"
fi

echo "$SCORECARD"

# ── Save (unless skipped) ──
if $DO_SAVE; then
    mkdir -p "$OUTPUT_DIR"
    TS="$(date +%Y%m%dT%H%M%SZ)"
    SLUG="${LABEL:+${LABEL}-}"
    DEST="$OUTPUT_DIR/${SLUG}${TS}.txt"
    {
        echo "# bulletin eval — ${LABEL:-unlabelled} — $TS"
        echo "# model: ${AZURE_OPENAI_MODEL:-<default>}  commit: $(git rev-parse --short HEAD 2>/dev/null || echo n/a)"
        echo ""
        echo "$SCORECARD"
    } >"$DEST"
    echo ""
    echo "Saved → $DEST"
fi

exit "$RESULT"
