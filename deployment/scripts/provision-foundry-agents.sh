#!/usr/bin/env bash
set -euo pipefail

# Provisions the Foundry agents used by the non-workflow pipeline roles.
#
# Every configured role agent is managed here so provisioning remains reproducible and
# reviewable. The role definitions and prompt files are the source of truth for the live
# Foundry project.
#
# The agent endpoint rejects a request-side `text` field when an agent is specified, so
# response formats are baked into each agent definition below.
#
# Reasoning effort is baked per role. All roles use "none" for speed, cost and consistency.








PROJECT_ENDPOINT="${PROJECT_ENDPOINT:-${1:-}}"
API_VERSION="${API_VERSION:-2025-05-15-preview}"
RESOURCE_AUDIENCE="${RESOURCE_AUDIENCE:-https://ai.azure.com}"

# The model deployment every role agent uses. MUST match the deployed shared
# AiSettings:AzureAI:Model (the per-role ModelFor fallback) so an agent call is
# behaviourally identical to the raw-model call it replaces.
AGENT_MODEL="${AGENT_MODEL:-gpt-5.6-luna}"

# gpt-5.6-luna's shared pool is intermittently saturated (503 `no healthy upstream`,
# ~60s stalls, and 429s while our own quota headers read 149/150 requests remaining),
# while gpt-5.6-terra measured 0 throttles and 0 stalls over the same window.
FALLBACK_AGENT_MODEL="${FALLBACK_AGENT_MODEL:-gpt-5.6-terra}"

if [[ -z "${PROJECT_ENDPOINT}" ]]; then
  echo "Usage: PROJECT_ENDPOINT=<https://.../api/projects/<project>> [AGENT_MODEL=<deployment>] [FALLBACK_AGENT_MODEL=<deployment>] $0"
  exit 1
fi


SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Agent instructions are supplied by the request templates; all managed roles use the neutral shim.
NEUTRAL_INSTRUCTIONS="Follow the instructions in the user message exactly. Produce only the output it asks for, in the exact format it specifies. Do not add commentary, preamble, or labels."

# The four arn-wf-* agents already existed in the dev project as unmanaged drift; they are
# managed here now so every role has one reproducible provisioning path.
# The -terra siblings use the alternate deployment because the luna shared pool is
# intermittently saturated; see FALLBACK_AGENT_MODEL above.
# role agent -> reasoning effort
declare -A AGENT_REASONING=(
  ["arn-safety"]="none"
  ["arn-director"]="none"
  ["arn-wf-write-incidents"]="none"
  ["arn-wf-write-incidents-terra"]="none"
  ["arn-wf-write-flow"]="none"
  ["arn-wf-write-flow-terra"]="none"
  ["arn-wf-compose"]="none"
  ["arn-wf-compose-terra"]="none"
  ["arn-wf-edit"]="none"
  ["arn-wf-edit-terra"]="none"
)

# role agent -> baked response format. EMPTY means no `text` is baked, i.e. the agent replies in
# whatever form the prompt asks for.
#
# arn-wf-compose is deliberately empty. BulletinComposer calls the LLM with jsonSchema: null
# because, in its own words, "prose is intentionally unwrapped — a JSON envelope costs tokens and
# creates escaping failure modes without adding anything the downstream workflow needs". A baked
# json_object OVERRIDES that and forces an envelope the composer never asked for: on 2026-08-31,
# with json_object baked, dev aired a bulletin whose script text was literally
# `{ "bulletin": "Punt Road has taken a theatrical turn at Union Street, ..." }` — braces, key and
# quotes read out on air, and the audio synthesised from it. The desks and the editor DO request
# JSON, so json_object is correct for them.
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

# role agent -> the git schema baked into a json_schema format. THE SCHEMA IS THE CONTRACT, and
# these files are its only source of truth: the same files the raw-model route passes per call
# (PromptLibrary.Schema), so both routes enforce byte-identical shapes.
#
# This table exists because the three primary desks previously carried "json_object" here while
# the LIVE agents carried json_schema, applied by hand and never written back to git. The drift
# was invisible (see check-foundry-agent-drift.sh, which covered only 2 of the 10 agents) and it
# caused a real defect: on 2026-09-03 test's arn-wf-write-incidents and arn-wf-write-flow were
# found enforcing an OLDER schema that omitted `stories[].condition`. Because these schemas set
# additionalProperties:false, the desks were structurally FORBIDDEN from emitting the one field
# the prompt calls "the only field of yours that is read out" — so every test bulletin lost its
# condition wording silently. Running this script as it was would have re-broken it by replacing
# json_schema with json_object.
#
# The -terra fallback siblings stay json_object deliberately: they are the emergency path and are
# not schema-pinned, matching what dev has always run.
declare -A AGENT_SCHEMA_FILE=(
  ["arn-wf-write-incidents"]="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow/Schemas/writeIncidents.json"
  ["arn-wf-write-flow"]="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow/Schemas/writeFlow.json"
  ["arn-wf-edit"]="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow/Schemas/edit.json"
)

# role agent -> model deployment; agent routes cannot override model per request, so
# each fallback role has a separately provisioned sibling bound to terra.
declare -A AGENT_MODEL_BY_NAME=(
  ["arn-safety"]="${AGENT_MODEL}"
  ["arn-director"]="${AGENT_MODEL}"
  ["arn-wf-write-incidents"]="${AGENT_MODEL}"
  ["arn-wf-write-incidents-terra"]="${FALLBACK_AGENT_MODEL}"
  ["arn-wf-write-flow"]="${AGENT_MODEL}"
  ["arn-wf-write-flow-terra"]="${FALLBACK_AGENT_MODEL}"
  ["arn-wf-compose"]="${AGENT_MODEL}"
  ["arn-wf-compose-terra"]="${FALLBACK_AGENT_MODEL}"
  ["arn-wf-edit"]="${AGENT_MODEL}"
  ["arn-wf-edit-terra"]="${FALLBACK_AGENT_MODEL}"
)

# Creates the agent if absent, then publishes a new version. The arrays above are intentionally
# explicit: missing a live role here would make provisioning appear successful while leaving it stale.
create_or_update_agent() {
  local name="$1"
  local model="$2"
  local effort="$3"
  local fmt="$4"

  local agent_url="${PROJECT_ENDPOINT}/agents/${name}?api-version=${API_VERSION}"
  local list_url="${PROJECT_ENDPOINT}/agents?api-version=${API_VERSION}"
  local version_url="${PROJECT_ENDPOINT}/agents/${name}/versions?api-version=${API_VERSION}"

  local exists
  exists="$(az rest --method get --resource "${RESOURCE_AUDIENCE}" --url "${agent_url}" --query "id" -o tsv 2>/dev/null || true)"

  local instructions="${NEUTRAL_INSTRUCTIONS}"

  # Base definition; add a baked text.format only when the role needs JSON.
  local definition
  definition="$(jq -n \
    --arg model "${model}" \
    --arg instructions "${instructions}" \
    --arg effort "${effort}" \
    '{
      kind: "prompt",
      model: $model,
      instructions: $instructions,
      reasoning: { effort: $effort },
      reasoning_effort: $effort,
      tools: []
    }')"
  if [[ "${fmt}" == "json_schema" ]]; then
    # json_schema carries the schema itself, so the shape is enforced provider-side rather than
    # merely "some JSON". The schema name mirrors what the live agents already carry
    # (arn_<agent name with dashes as underscores>) so republishing is a no-op on a correct project.
    local schema_file="${AGENT_SCHEMA_FILE[$name]:-}"
    if [[ -z "${schema_file}" || ! -f "${schema_file}" ]]; then
      echo "FATAL ${name}: format is json_schema but its schema file is missing: ${schema_file:-<unset>}" >&2
      exit 2
    fi
    local schema_name="arn_${name//-/_}"
    # `strict: true` is stated rather than left to the service default, and it MUST match the raw
    # route's ResponseTextFormat.CreateJsonSchemaFormat(..., jsonSchemaIsStrict: true). Omitting it
    # still lands as true today, which makes a republish look identical while depending on a
    # provider default we do not control.
    definition="$(jq --arg n "${schema_name}" --slurpfile s "${schema_file}" \
      '. + { text: { format: { type: "json_schema", name: $n, strict: true, schema: $s[0] } } }' <<<"${definition}")"
  elif [[ -n "${fmt}" ]]; then
    definition="$(jq --arg t "${fmt}" '. + { text: { format: { type: $t } } }' <<<"${definition}")"
  fi

  # Hash only the complete definition, including the baked response format. Metadata is attached
  # after this point so the timestamp does not make the definition hash change on every run, and
  # the hash never contains itself.
  local definition_sha
  definition_sha="$(printf '%s' "${definition}" | sha256sum)"
  definition_sha="${definition_sha%% *}"
  definition_sha="${definition_sha:0:12}"
  local provisioned_utc
  provisioned_utc="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
  local metadata
  metadata="$(jq -n \
    --arg sha "${definition_sha}" \
    --arg provisioned "${provisioned_utc}" \
    '{ arn_definition_sha: $sha, arn_provisioned_utc: $provisioned }')"

  if [[ -z "${exists}" ]]; then
    echo "Creating agent ${name} (${model}, reasoning=${effort}, format=${fmt:-text})"
    az rest --method post --resource "${RESOURCE_AUDIENCE}" --url "${list_url}" \
      --headers "Content-Type=application/json" \
      --body "$(jq -n --arg name "${name}" --argjson definition "${definition}" --argjson metadata "${metadata}" \
        '{ name: $name, description: ("Arn pipeline role agent (" + $name + ") — provisioned by provision-foundry-agents.sh; see docs/reference/foundry-role-agents.md"), metadata: $metadata, definition: $definition }')" \
      --output none
  else
    echo "Publishing new version for agent ${name} (${model}, reasoning=${effort}, format=${fmt:-text})"
    az rest --method post --resource "${RESOURCE_AUDIENCE}" --url "${version_url}" \
      --headers "Content-Type=application/json" \
      --body "$(jq -n --argjson definition "${definition}" --argjson metadata "${metadata}" \
        '{ description: "Provision-managed version update", metadata: $metadata, definition: $definition }')" \
      --output none
  fi
  echo "${name}: ${model} reasoning=${effort} format=${fmt:-text} definition_sha=${definition_sha}"
}
for name in arn-safety arn-director arn-wf-write-incidents arn-wf-write-incidents-terra arn-wf-write-flow arn-wf-write-flow-terra arn-wf-compose arn-wf-compose-terra arn-wf-edit arn-wf-edit-terra; do
  create_or_update_agent "${name}" "${AGENT_MODEL_BY_NAME[$name]}" "${AGENT_REASONING[$name]}" "${AGENT_FORMAT[$name]}"
done

echo
echo "Done. Current role-agent versions:"
az rest --method get --resource "${RESOURCE_AUDIENCE}" \
  --url "${PROJECT_ENDPOINT}/agents?api-version=${API_VERSION}" \
  --query "data[?starts_with(id,'arn-')].{id:id,model:versions.latest.definition.model,effort:versions.latest.definition.reasoning_effort,format:versions.latest.definition.text.format.type,version:versions.latest.version}" \
  -o table
