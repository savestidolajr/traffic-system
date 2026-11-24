#!/usr/bin/env bash
set -euo pipefail

if [[ "${ARN_EXTERNAL_SMOKE:-}" != "1" ]]; then
  echo "Skipping external provider smoke tests. Set ARN_EXTERNAL_SMOKE=1 with AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_MODEL, HERE_API_KEY, and ELEVENLABS_API_KEY to run."
  exit 0
fi

missing=0
for name in AZURE_OPENAI_ENDPOINT AZURE_OPENAI_API_KEY AZURE_OPENAI_MODEL HERE_API_KEY ELEVENLABS_API_KEY; do
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required environment variable: $name"
    missing=1
  fi
 done
if [[ "$missing" -ne 0 ]]; then
  exit 1
fi

dotnet test test/ArnBulletinGenerator.ExternalSmoke.Tests/ArnBulletinGenerator.ExternalSmoke.Tests.csproj \
  -c Debug --logger "console;verbosity=normal"
