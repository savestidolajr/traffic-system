#!/usr/bin/env bash
set -euo pipefail

# Publishes the conversational workflow's three temporary agents so a human can read and edit
# their prompts in the Foundry portal and inspect the workflow traces there. The files in this
# repository remain the source of truth: re-running this script republishes them and overwrites
# any edits made in the portal.

API_VERSION="${API_VERSION:-2025-05-15-preview}"
RESOURCE_AUDIENCE="${RESOURCE_AUDIENCE:-https://ai.azure.com}"
PROJECT_ENDPOINT="${PROJECT_ENDPOINT:-https://ibp-aue-dev-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator}"
MODEL="${MODEL:-gpt-5.6-luna}"
# Every agent runs at none. A published agent carries its reasoning budget server-side, so a
# default of "low" here silently re-introduced a budget on every re-provision even after the
# agents had been corrected in the portal.
REASONING="${REASONING:-none}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKFLOW_DIR="${SCRIPT_DIR}/../../src/ArnBulletinGenerator.Workflow"
PROMPTS_DIR="${WORKFLOW_DIR}/Prompts"
SCHEMAS_DIR="${WORKFLOW_DIR}/Schemas"
AGENTS=(arn-wf-write-incidents arn-wf-write-flow arn-wf-compose arn-wf-edit)

usage() {
  cat >&2 <<'EOF'
Usage: PROJECT_ENDPOINT=<https://.../api/projects/<project>> deployment/scripts/provision-workflow-agents.sh [--delete]

  --delete  Delete the temporary conversational workflow agents instead of publishing them.
EOF
}

delete_agents=false
for arg in "$@"; do
  case "${arg}" in
    --delete) delete_agents=true ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: ${arg}" >&2; usage; exit 2 ;;
  esac
done

# Acquire the same project-scoped AI token used by the Foundry data-plane calls. `az rest` below
# uses the Azure CLI identity and resource audience as in provision-foundry-agents.sh; this
# explicit check turns an expired or absent login into a useful error before any mutation.
if ! TOKEN="$(az account get-access-token --resource "${RESOURCE_AUDIENCE}" --query accessToken -o tsv 2>/dev/null)" || [[ -z "${TOKEN}" ]]; then
  echo "Unable to acquire an Azure AI access token. Run 'az login' and try again." >&2
  exit 1
fi

# Fetching the collection once gives an unambiguous existence check: a missing agent is expected,
# while any other non-2xx response remains a hard failure under set -e.
list_agents() {
  az rest --method get --resource "${RESOURCE_AUDIENCE}" \
    --url "${PROJECT_ENDPOINT}/agents?api-version=${API_VERSION}" \
    -o json
}

existing_agents="$(list_agents)"
agent_exists() {
  local name="$1"
  jq -e --arg name "${name}" \
    'any(.data[]?; ((.id // "") == $name) or ((.name // "") == $name))' \
    <<<"${existing_agents}" >/dev/null
}

if [[ "${delete_agents}" == true ]]; then
  for name in "${AGENTS[@]}"; do
    if agent_exists "${name}"; then
      az rest --method delete --resource "${RESOURCE_AUDIENCE}" \
        --url "${PROJECT_ENDPOINT}/agents/${name}?api-version=${API_VERSION}" \
        --output none
      echo "${name}: deleted"
    else
      echo "${name}: absent"
    fi
  done
  exit 0
fi

declare -A AGENT_INSTRUCTIONS_FILE=(
  [arn-wf-write-incidents]="${PROMPTS_DIR}/writeIncidents.md"
  [arn-wf-write-flow]="${PROMPTS_DIR}/writeFlow.md"
  [arn-wf-compose]="${PROMPTS_DIR}/compose2.md"
  [arn-wf-edit]="${PROMPTS_DIR}/edit.md"
)

declare -A AGENT_SCHEMA_FILE=(
  [arn-wf-write-incidents]="${SCHEMAS_DIR}/writeIncidents.json"
  [arn-wf-write-flow]="${SCHEMAS_DIR}/writeFlow.json"
  [arn-wf-edit]="${SCHEMAS_DIR}/edit.json"
)

declare -A AGENT_FORMAT=(
  [arn-wf-compose]=""
)

create_or_update_agent() {
  local name="$1"
  local prompt_file="${AGENT_INSTRUCTIONS_FILE[$name]}"
  local schema_file="${AGENT_SCHEMA_FILE[$name]:-}"
  local format="${AGENT_FORMAT[$name]:-}"
  local instructions definition version action

  [[ -f "${prompt_file}" ]] || { echo "Missing prompt file: ${prompt_file}" >&2; exit 1; }
  # A marked-up prompt is authoring metadata plus body. The app sends the PARSED render: header
  # dropped, markers dropped, and one "## <heading>" emitted whenever the section changes. Mirror
  # that exactly. A sed that only deleted the markers left the header prose in and the headings
  # out, which is a 191-character lie in the one place someone goes to read the truth.
  #
  # The heading labels are duplicated from Playbook.Heading. That duplication is deliberate and
  # guarded: verify-workflow-agents diffs this render against the SystemInstructions in a live
  # capture, so the two cannot drift silently.
  # Only compose2 is decomposed into playbook blocks; write and edit are plain files the app
  # sends verbatim. Rendering an unmarked file through the block pass would emit nothing at all,
  # so branch on the markers rather than assuming every role is decomposed.
  if grep -q '^<!-- block:' "${prompt_file}"; then
    instructions="$(awk '
      /^<!-- block:/ {
        seen = 1
        section = $0
        sub(/.*section:[ ]*/, "", section)
        sub(/[ ]*(\|.*)?-->.*/, "", section)
        if (section != current) {
          current = section
          heading = section
          if (section == "Role")      heading = "Who you are"
          if (section == "Material")  heading = "What you may say"
          if (section == "Selection") heading = "What goes in"
          if (section == "Voice")     heading = "How it sounds"
          if (section == "Output")    heading = "How to reply"
          if (blocks++) print ""
          print "## " heading
          print ""
        }
        next
      }
      seen { print }
    ' "${prompt_file}" | sed -e :a -e '/^\n*$/{$d;N;};/\n$/ba')"
  else
    instructions="$(cat "${prompt_file}")"
  fi

  definition="$(jq -n \
    --arg model "${MODEL}" \
    --arg instructions "${instructions}" \
    --arg effort "${REASONING}" \
    '{
      kind: "prompt",
      model: $model,
      instructions: $instructions,
      reasoning: { effort: $effort },
      reasoning_effort: $effort,
      tools: []
    }')"

  if [[ -n "${schema_file}" ]]; then
    [[ -f "${schema_file}" ]] || { echo "Missing schema file: ${schema_file}" >&2; exit 1; }
    # The workflow keeps BARE JSON Schemas on disk, because that is what the schema actually is and
    # what AzureOpenAiWorkflowLlm sends after adding its own envelope. The Foundry agent API wants
    # that envelope baked in, so build it here rather than storing a Foundry-shaped file the C#
    # adapter would then have to unwrap. One artifact, two consumers, envelope added at the edge.
    jq -e 'type == "object" and has("properties")' "${schema_file}" >/dev/null \
      || { echo "Schema ${schema_file} is not a JSON Schema object with properties" >&2; exit 1; }
    definition="$(jq --slurpfile schema "${schema_file}" --arg name "arn_${name//-/_}" \
      '. + { text: { format: { type: "json_schema", name: $name, strict: true, schema: $schema[0] } } }' \
      <<<"${definition}")"
    format="json_schema"
  fi

  if agent_exists "${name}"; then
    action="updated"
    az rest --method post --resource "${RESOURCE_AUDIENCE}" \
      --url "${PROJECT_ENDPOINT}/agents/${name}/versions?api-version=${API_VERSION}" \
      --headers "Content-Type=application/json" \
      --body "$(jq -n --argjson definition "${definition}" \
        '{ description: "Provision-managed conversational workflow version", definition: $definition }')" \
      --output none
  else
    action="created"
    az rest --method post --resource "${RESOURCE_AUDIENCE}" \
      --url "${PROJECT_ENDPOINT}/agents?api-version=${API_VERSION}" \
      --headers "Content-Type=application/json" \
      --body "$(jq -n --arg name "${name}" --argjson definition "${definition}" \
        '{ name: $name, description: "Temporary conversational workflow agent; source is the repository prompt and schema files", definition: $definition }')" \
      --output none
  fi

  version="$(az rest --method get --resource "${RESOURCE_AUDIENCE}" \
    --url "${PROJECT_ENDPOINT}/agents/${name}?api-version=${API_VERSION}" \
    --query "versions.latest.version" -o tsv)"
  [[ -n "${version}" ]] || { echo "Agent ${name} was ${action} but its resulting version was empty" >&2; exit 1; }
  echo "${name}: ${action} (version ${version})"
}

for name in "${AGENTS[@]}"; do
  create_or_update_agent "${name}"
done
