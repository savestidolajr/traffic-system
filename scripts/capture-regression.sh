#!/usr/bin/env bash
set -euo pipefail
# SUPERSEDED (2026-07-15): prefer `python3 tools/arn/arn.py fixtures capture --as regression`.
#   Kept as the underlying implementation.
#
# Capture a deployed bulletin job we were unhappy with into the version-controlled regression corpus,
# so its script can be REPRODUCED and re-scored after a prompt/code change (data held constant).
#
# It reads the job's GeneratorInputJson — the complete writer-input snapshot persisted per job by the
# orchestrator — from Table Storage and writes test/corpus/regressions/<label>.json. Replay with:
#   ARN_EVAL_LIVE=1 dotnet test test/ArnBulletinGenerator.Infrastructure.Tests \
#     --filter RegressionCorpus_Replay_RegeneratesAndScores
#
# Usage: scripts/capture-regression.sh <jobId> <label> [reason]
#   - <jobId>  RowKey from the operator console URL (/jobs/<jobId>)
#   - <label>  fixture filename (kebab-case), e.g. mildura-flowlist
#   - [reason] why it was flagged (free text)
# Prereq: `az account show` reports the IBP subscription (az login otherwise).
# Env: ARN_STORAGE_ACCOUNT overrides the storage account (default dev: ibpauedevstaapp01).

JOB_ID="${1:?usage: capture-regression.sh <jobId> <label> [reason]}"
LABEL="${2:?usage: capture-regression.sh <jobId> <label> [reason]}"
REASON="${3:-flagged via capture-regression.sh}"
ACCOUNT="${ARN_STORAGE_ACCOUNT:-ibpauedevstaapp01}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/test/corpus/regressions/$LABEL.json"

# RowKey alone also matches the JobIndex stub row (PK=JobIndex, no snapshot columns)
# since the Job_{market} partition change; exclude it to read the real data row.
ENTITY=$(az storage entity query --account-name "$ACCOUNT" --auth-mode login --table-name radioJobs \
  --filter "RowKey eq '$JOB_ID' and PartitionKey ne 'JobIndex'" \
  --select RowKey ScriptText CreatedAt GeneratorInputJson -o json)

SNAP=$(echo "$ENTITY" | jq -r '.items[0].GeneratorInputJson // empty')
if [[ -z "$SNAP" ]]; then
  echo "ERROR: job $JOB_ID has no GeneratorInputJson (predates snapshot persistence, or not found)." >&2
  echo "       Old jobs must be reconstructed by hand — see" >&2
  echo "       test/ArnBulletinGenerator.Infrastructure.Tests/Regression/MilduraFixtureBuilder.cs" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT")"
echo "$ENTITY" | jq \
  --arg label "$LABEL" --arg reason "$REASON" --arg job "$JOB_ID" \
  --arg captured "$(date -u +%Y-%m-%dT%H:%M:%SZ)" '
  .items[0] as $j
  | { Label: $label,
      Reason: $reason,
      SourceJobId: $job,
      OriginalScript: ($j.ScriptText // ""),
      CapturedAtUtc: $captured,
      Snapshot: ($j.GeneratorInputJson | fromjson) }' > "$OUT"

echo "Wrote regression fixture: $OUT"
echo "Replay it: ARN_EVAL_LIVE=1 dotnet test test/ArnBulletinGenerator.Infrastructure.Tests --filter RegressionCorpus_Replay_RegeneratesAndScores"
