---
title: "Foundry role agents"
description: "How Arn pipeline roles route through Foundry prompt agents or the raw model, including the conversational workflow agents, Content Rewrite, and where their prompts and reasoning effort come from"
tags: [foundry, agents, llm, pipeline, transparency]
purpose: "reference"
status: current
last_verified: 2026-08-30
review_after: 2026-09-18
volatility: medium
source_of_truth: "AzureAIOptions.AgentNameFor + ResponsesTransport.DispatchLlmAsync (code); prompts/ (instructions); deployment/scripts/provision-foundry-agents.sh (Safety/Director) + deployment/scripts/provision-workflow-agents.sh (workflow agents) + check-foundry-agent-drift.sh (deployment parity)"
verified_by:
  - "Source review of AzureAIOptions, ResponsesTransport, and both provisioning scripts 2026-08-30"
  - "Live dev Foundry agent inventory and app-setting verification 2026-08-30"
aliases:
  - role agents
  - foundry agents
  - agent routing
common_queries:
  - "Which pipeline roles call a Foundry agent vs the raw model?"
  - "Where is the source of truth for the safety agent prompt?"
  - "Which role agents does the provisioner manage?"
---

# Foundry role agents

Non-workflow production bulletin-generation roles use either the two configured **Foundry prompt agents**
(`Safety` and `Director`) or the raw model. `DeliveryWriter`, `Rewrite`, and `Lesson` are raw-model
roles with no Foundry agent. `DeliveryWriter` and `Rewrite` are still dispatched as roles, but they
always take the raw-model path. Foundry routing produces a full trace; raw routing uses the same
transport and captures the rendered request, but has no Foundry agent span.

## The seam

`ResponsesTransport.DispatchLlmAsync(role, …)` is the single routing point. For each role it
calls `AzureAIOptions.AgentNameFor(role)`:

- **agent name configured** → `SendToAgentAsync` (Foundry agent endpoint,
  `…/agents/{name}/endpoint/protocols/openai/responses`, `ai.azure.com` token scope).
- **not configured** → `SendToModelAsync` (raw model, `cognitiveservices` scope).

Both paths share the same rendered prompt, the same `ILlmCallCapture` (so agent calls appear in
`job-diagnostics/*/llm_calls` exactly like raw-model calls — without this, routing a role to an
agent would silently drop it from the diagnostics tooling), the same token-exhaustion retry, and
the same API-key auth fallback. **Response format and reasoning effort differ in delivery, not
effect:** the raw path sends them per-request; the agent endpoint REJECTS a request-side `text`
field ("Not allowed when agent is specified"), so both the JSON/text format AND the reasoning
effort are baked into each agent's server-side definition instead (along with its model).

## The roles

| Role | Agent name (app-setting) | Instructions source | Format | Reasoning | Managed by |
|---|---|---|---|---|---|
| Safety | `SafetyAgentName` = `arn-safety` | neutral shim; behaviour in `prompts/safety.md` | json_object | none | `deployment/scripts/provision-foundry-agents.sh` |
| Director | `DirectorAgentName` = `arn-director` | neutral shim; behaviour in `prompts/director.md` | json_object | none | `deployment/scripts/provision-foundry-agents.sh` |
| Delivery writer | no agent setting; raw model always | `prompts/delivery-writer-system.md` + `prompts/delivery-writer.md` | json_object | raw `ReasoningEffort` | raw model; no Foundry provisioner |
| Content rewrite | no agent setting; raw model always | `prompts/rewrite-system.md` + `prompts/rewrite.md` | json_object | raw `ReasoningEffort` | raw model; no Foundry provisioner |
| Lesson | no agent setting; raw model always | `LessonSummarizer.SystemPrompt` + structured schema | structured JSON schema | raw `ReasoningEffort` | raw model; no Foundry provisioner |

Bicep emits both `AiSettings__AzureAI__SafetyAgentName` and
`AiSettings__AzureAI__DirectorAgentName`. The live dev Foundry project contains exactly these six
agents: the two role agents above plus `arn-wf-compose`, `arn-wf-edit`, `arn-wf-write-flow`, and
`arn-wf-write-incidents`. The four `arn-wf-*` workflow agents are provisioned by
`deployment/scripts/provision-workflow-agents.sh`; they are separate from the non-workflow role
agents managed by `deployment/scripts/provision-foundry-agents.sh`.

`ModelFor` has exactly three per-role arms: `Safety`, `Director`, and `Lesson`. Every other role
falls back to the shared `Model`; `Lesson` uses `LessonModel` when configured, then that same
fallback. Config keys live under `AiSettings:AzureAI:` alongside `FoundryProjectEndpoint`.

## Source-of-truth convention (READ THIS before editing a prompt)

The conversational pipeline is the only script generator in every environment.
Its on-disk workflow prompt sources are `src/ArnBulletinGenerator.Workflow/Prompts/writeIncidents.md`,
`writeFlow.md`, `compose2.md`, and `edit.md`. The station Writer prompt playbook supplies the
station-scoped `compose2` overlay over the ARN house style; the conversational writer also receives
active Station lessons. The workflow returns the conversational compose2 contract, not a legacy
matrix or editorial-block document.

Other specialised non-workflow roles use their role-specific prompt files or the inline
`LessonSummarizer.SystemPrompt` described in the table above. **Git is the source of truth; Foundry
is a deployment target.**

The writer prompt is not a pipeline switch: there is no `AiSettings:AzureAI:Pipeline` setting and
no alternate matrix writer to select.
- **There is no separate repair role.** Script-wide defects resolve deterministically
  (required-fixture splice in `ScriptContentGates`) or by a fresh draft at a corrected word target.
- **Content Rewrite is a lightweight editor role and always uses the raw model.** Its behaviour
  lives in `prompts/rewrite-system.md` and `prompts/rewrite.md`. The operator instruction is
  authoritative; the persisted job data is compact reference context, not a grounding verdict.
  The endpoint keeps only content-safety gates before returning the preview, and the operator still
  applies it through the verbatim re-voice path.
- **Editor verdict parsing fails closed.** `BulletinEditor` requires the `edit.json` response contract,
  retries one malformed response with the same prompt, and returns an `Unreviewed` verdict after a
  second malformed response or a transport failure. The workflow keeps the read intact and spends
  no revision when this happens, records `Editor unavailable - read shipped unreviewed` in the
  trace, and freezes the successful run as `succeeded-unreviewed` so the operator can distinguish
  an unavailable editor from an approval or rejection.
- **DeliveryWriter and Content Rewrite are dispatched roles but have no Foundry route.** `AgentNameFor`
  has no arm for either role, so both always use the raw-model path. This was already true in every
  environment because no agent name was ever configured in appsettings, Bicep, or App Service settings.
- **Lesson is an isolated raw-model role.** `LessonSummarizer` uses the `Lesson` model arm, which
  falls back to `Model` when `LessonModel` is empty.
- **`json_object` needs the word "json" in the input.** Every JSON role template contains
  "Return JSON…". A role whose template drops that word must not bake `json_object`.
- **Foundry-agent reasoning effort is baked per agent.** The two role agents use `none` in their
  provisioned definitions. Raw-model roles use the request-level `ReasoningEffort` setting.
- **Never edit instructions in the portal.** A portal edit is unreviewed drift and is overwritten
  by the next provision. Run `check-foundry-agent-drift.sh` after provisioning and whenever
  behaviour surprises you.
**The eval harness must match production.** `BulletinEval` reproduces the production
conversational compose2 pipeline plus the deterministic gate mutations; its configuration must use
the same `FoundryProjectEndpoint` and supported role settings, or it silently tests a different
transport.

## Safety governance

`SafetyAgentName` points the compliance-reviewed content-safety gate at a Foundry agent. Its
behavioural contract lives in `prompts/safety.md`; the agent definition carries only the
provision-managed neutral shim and response format. A portal edit is prohibited configuration
drift and `check-foundry-agent-drift.sh` must catch it. Treat changes to `prompts/safety.md` with
the same review rigour as any other change to the broadcast-standards bar.

## Provisioning

```bash
PROJECT_ENDPOINT="https://ibp-aue-dev-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator" \
  AGENT_MODEL="gpt-5.6-luna" \
  deployment/scripts/provision-foundry-agents.sh
```

`deployment/scripts/provision-foundry-agents.sh` provisions only `arn-safety` and `arn-director`.
`deployment/scripts/provision-workflow-agents.sh` provisions the four `arn-wf-*` workflow agents
from `src/ArnBulletinGenerator.Workflow/Prompts/` and their schemas.

`AGENT_MODEL` must match the deployed shared `AiSettings:AzureAI:Model` so an agent call is
behaviourally identical to the raw-model call it replaces. Bicep emits both supported role-agent
settings (`SafetyAgentName` and `DirectorAgentName`); `DeliveryWriter`, `Rewrite`, and `Lesson`
have no agent-name settings and always use the raw-model path. Run the drift check after
provisioning.
