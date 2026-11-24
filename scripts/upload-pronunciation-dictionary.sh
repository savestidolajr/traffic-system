#!/usr/bin/env bash
set -euo pipefail

# ── Upload a pronunciation dictionary to ElevenLabs ──
#
# Usage: ./scripts/upload-pronunciation-dictionary.sh <name> <rules.pls|rules.json>
#
# Environment:
#   ELEVENLABS_API_KEY   (required) — your ElevenLabs API key
#
# Outputs the dictionary ID and version ID on success, plus an appsettings
# JSON snippet you can paste into your configuration.

if [ $# -ne 2 ]; then
    echo "Usage: $0 <name> <rules.pls|rules.json>" >&2
    exit 1
fi

NAME="$1"
RULES_FILE="$2"

if [ ! -f "$RULES_FILE" ]; then
    echo "Error: rules file not found: $RULES_FILE" >&2
    exit 1
fi

if [ -z "${ELEVENLABS_API_KEY:-}" ]; then
    echo "Error: ELEVENLABS_API_KEY is not set." >&2
    exit 1
fi

echo "── Uploading pronunciation dictionary ──"
echo "  Name:  $NAME"
echo "  File:  $RULES_FILE"
echo ""

# Determine content type from file extension
EXT="${RULES_FILE##*.}"
case "$EXT" in
    pls)    CONTENT_TYPE="application/octet-stream" ;;
    json)   CONTENT_TYPE="application/json" ;;
    *)      echo "Error: unsupported file extension '$EXT'. Use .pls or .json." >&2
            exit 1 ;;
esac

RESPONSE=$(curl -s -X POST "https://api.elevenlabs.io/v1/pronunciation-dictionaries/add-from-file" \
    -H "xi-api-key: $ELEVENLABS_API_KEY" \
    -F "name=$NAME" \
    -F "file=@$RULES_FILE;type=$CONTENT_TYPE")

# Extract id and version_id from response
DICT_ID=$(echo "$RESPONSE" | grep -o '"id"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
VERSION_ID=$(echo "$RESPONSE" | grep -o '"version_id"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/.*"version_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')
ERROR=$(echo "$RESPONSE" | grep -o '"detail"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/.*"detail"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')

if [ -n "$ERROR" ]; then
    echo "Error from ElevenLabs API: $ERROR" >&2
    echo "Full response: $RESPONSE" >&2
    exit 1
fi

if [ -z "$DICT_ID" ] || [ -z "$VERSION_ID" ]; then
    echo "Error: could not parse ID or version_id from response." >&2
    echo "Response: $RESPONSE" >&2
    exit 1
fi

echo "── Success ──"
echo "  Dictionary ID:  $DICT_ID"
echo "  Version ID:     $VERSION_ID"
echo ""
echo "── appsettings snippet (add to AiSettings:ElevenLabs) ──"
echo ""
echo '"PronunciationDictionaries": {'
echo '    "<market-id>": {'
echo "        \"DictionaryId\": \"$DICT_ID\","
echo "        \"VersionId\": \"$VERSION_ID\""
echo '    }'
echo '}'
echo ""
echo "Or for the default (non-market-specific) pair, set:"
echo "  PronunciationDictionaryId: $DICT_ID"
echo "  PronunciationDictionaryVersionId: $VERSION_ID"
