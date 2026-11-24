#!/usr/bin/env bash
# Fails when a live Foundry role agent's instructions or baked response format no longer match git.
#
# WHY THIS EXISTS (2026-07-30): role-agent instructions and response formats are managed in git,
# rather than hand-tuned in the portal, so every live role has a diffable, reproducible definition.
# The check prevents portal edits from silently becoming the real behaviour.
#
#
#

#
# CONTRACT: git is the source of truth. Foundry is a deployment target. If they disagree, either
# re-run provision-foundry-agents.sh (git wins) or export the portal change into git deliberately.
#
# Usage:
#   PROJECT_ENDPOINT=https://<res>.services.ai.azure.com/api/projects/<project> \
#     deployment/scripts/check-foundry-agent-drift.sh
#
# Exit 0 = every agent matches. Exit 1 = drift (diff printed). Exit 2 = usage/auth problem.
set -uo pipefail

PROJECT_ENDPOINT="${PROJECT_ENDPOINT:-${1:-}}"
API_VERSION="${API_VERSION:-2025-05-15-preview}"
RESOURCE_AUDIENCE="${RESOURCE_AUDIENCE:-https://ai.azure.com}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROMPTS_DIR="${PROMPTS_DIR:-${SCRIPT_DIR}/../../prompts}"

if [[ -z "${PROJECT_ENDPOINT}" ]]; then
  echo "Usage: PROJECT_ENDPOINT=<https://.../api/projects/<project>> $0" >&2
  exit 2
fi

NEUTRAL_INSTRUCTIONS="Follow the instructions in the user message exactly. Produce only the output it asks for, in the exact format it specifies. Do not add commentary, preamble, or labels."

# Must mirror provision-foundry-agents.sh exactly — an agent missing from BOTH tables would be
# silently unchecked, which is the failure mode this script exists to prevent.
#
# That failure mode ACTUALLY HAPPENED. Until 2026-09-03 this file listed only arn-safety and
# arn-director, so the eight arn-wf-* agents — the ones carrying the load-bearing response
# schemas — were never checked. Test's arn-wf-write-incidents and arn-wf-write-flow silently
# drifted onto an older schema that omitted `stories[].condition`, and because those schemas set
# additionalProperties:false the desks were structurally forbidden from emitting the one field
# the prompt calls "the only field of yours that is read out". A drift check that covers a subset
# reports "All role agents match git" while the real contract rots, which is worse than no check.
declare -A AGENT_INSTRUCTIONS_FILE=()
declare -A AGENT_FORMAT=(
  ["arn-safety"]="json_object"
  ["arn-director"]="json_object"
  ["arn-wf-write-incidents"]="json_schema"
  ["arn-wf-write-incidents-terra"]="json_object"
  ["arn-wf-write-flow"]="json_schema"
  ["arn-wf-write-flow-terra"]="json_object"
  ["arn-wf-compose"]=""
  ["arn-wf-compose-terra"]=""
  ["arn-wf-edit"]="json_schema"
  ["arn-wf-edit-terra"]="json_object"
)
# The schema itself is the contract, so a matching format TYPE is not enough: compare the schema
# document too, against the same files the raw-model route passes per call.
declare -A AGENT_SCHEMA_FILE=(
  ["arn-wf-write-incidents"]="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow/Schemas/writeIncidents.json"
  ["arn-wf-write-flow"]="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow/Schemas/writeFlow.json"
  ["arn-wf-edit"]="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow/Schemas/edit.json"
)
ALL_AGENTS=(arn-safety arn-director
            arn-wf-write-incidents arn-wf-write-incidents-terra
            arn-wf-write-flow arn-wf-write-flow-terra
            arn-wf-compose arn-wf-compose-terra
            arn-wf-edit arn-wf-edit-terra)
tmp="$(mktemp -d)"
trap 'rm -rf "${tmp}"' EXIT

drifted=0
missing=0

for name in "${ALL_AGENTS[@]}"; do
  live_json="${tmp}/${name}.json"
  if ! az rest --method get --resource "${RESOURCE_AUDIENCE}" \
      --url "${PROJECT_ENDPOINT}/agents/${name}?api-version=${API_VERSION}" \
      -o json > "${live_json}" 2>/dev/null; then
    echo "MISSING  ${name} — not deployed to this project"
    missing=$((missing + 1))
    continue
  fi

  agent_drift=0
  expected_format="${AGENT_FORMAT[$name]}"
  live_format="$(jq -r '.versions.latest.definition.text.format.type // ""' "${live_json}")"
  if [[ "${expected_format}" != "${live_format}" ]]; then
    echo "DRIFT    ${name} — live response format differs from git"
    echo "         git:  ${expected_format:-text}"
    echo "         live: ${live_format:-text}"
    agent_drift=1
  fi

  # A matching format TYPE proves nothing about the shape being enforced. Compare the schema
  # document, and the strict flag, against git — this is the check that would have caught the
  # missing `stories[].condition` described above.
  if [[ "${expected_format}" == "json_schema" ]]; then
    local_schema="${AGENT_SCHEMA_FILE[$name]:-}"
    if [[ -z "${local_schema}" || ! -f "${local_schema}" ]]; then
      echo "MISSING  ${name} — git schema file absent: ${local_schema:-<unset>}"
      missing=$((missing + 1))
      continue
    fi
    live_strict="$(jq -r '.versions.latest.definition.text.format.strict // false' "${live_json}")"
    if [[ "${live_strict}" != "true" ]]; then
      echo "DRIFT    ${name} — live schema is not strict (git requires strict: true)"
      agent_drift=1
    fi
    if ! diff -q <(jq -S '.' "${local_schema}") \
                 <(jq -S '.versions.latest.definition.text.format.schema' "${live_json}") >/dev/null; then
      echo "DRIFT    ${name} — live response SCHEMA differs from git"
      echo "         git:  ${local_schema}"
      diff -u <(jq -S '.' "${local_schema}") \
              <(jq -S '.versions.latest.definition.text.format.schema' "${live_json}") \
        --label "git/${name}.schema" --label "live/${name}.schema" | head -40 | sed 's/^/         /'
      agent_drift=1
    fi
  fi

  live="${tmp}/${name}.live.txt"
  jq -r '.versions.latest.definition.instructions // ""' "${live_json}" > "${live}"

  expected="${tmp}/${name}.expected.txt"
  ifile="${AGENT_INSTRUCTIONS_FILE[$name]:-}"
  if [[ -n "${ifile}" ]]; then
    if [[ ! -f "${ifile}" ]]; then
      echo "MISSING  ${name} — git file absent: ${ifile}"
      missing=$((missing + 1))
      continue
    fi
    cat "${ifile}" > "${expected}"
  else
    printf '%s' "${NEUTRAL_INSTRUCTIONS}" > "${expected}"
  fi

  # Command substitution strips trailing newlines from BOTH sides, which is the only normalisation
  # wanted here: `jq -r` always emits a trailing newline and a text file may or may not end with one,
  # so comparing raw bytes reports every agent as drifted. Everything else must match exactly.
  if [[ "$(cat "${expected}")" == "$(cat "${live}")" ]]; then
    printf 'OK       %-15s %6d chars (format=%s)\n' "${name}" "$(wc -c < "${expected}")" \
      "${live_format:-text}"
  else
    echo "DRIFT    ${name} — live instructions differ from git"
    echo "         git:  ${ifile:-<neutral shim>}"
    diff -u "${expected}" "${live}" \
      --label "git/${name}" --label "live/${name}" | head -40 | sed 's/^/         /'
    agent_drift=1
  fi
  if (( agent_drift )); then
    drifted=$((drifted + 1))
  fi
done

echo
if (( drifted > 0 || missing > 0 )); then
  echo "FAILED: ${drifted} agent(s) drifted, ${missing} missing."
  echo "Git is the source of truth. Re-run provision-foundry-agents.sh to push git over the live"
  echo "agent, or copy the portal change into the git file deliberately and commit it."
  exit 1
fi

echo "All ${#ALL_AGENTS[@]} role agents match git."
