# Prompt asset locations

Arn has two prompt trees with different resolvers.

## Repository-root `prompts/`

These seven raw-model role assets live here:

`system.md`, `safety.md`, `director.md`, `delivery-writer.md`,
`delivery-writer-system.md`, `rewrite.md`, and `rewrite-system.md`.

They are loaded only by
`ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI.PromptLibrary`.
That resolver honours `ARN_PROMPTS_DIR`; when it is unset, it probes the repository
`prompts/` directory through up to eight parent directories.

`prompts/agents/arn-draft.md` is separate: it is the shipped default playbook for
the `writer` role and is reached by `ShippedPlaybookProvider`.

The retained `draft.md`, `citation-fix.md`, and `style-exemplars.md` files are not
loaded by the current pipeline.

## Workflow tree (not this directory)

The workflow role prompts are
`src/ArnBulletinGenerator.Workflow/Prompts/compose2.md`,
`edit.md`, `writeFlow.md`, and `writeIncidents.md`.
Their schemas are
`src/ArnBulletinGenerator.Workflow/Schemas/writeFlow.json`,
`writeIncidents.json`, and `edit.json`.

These assets are loaded only by
`ArnBulletinGenerator.Workflow.Prompts.PromptLibrary`. It reads exactly one path
beside the assembly: it does not honour `ARN_PROMPTS_DIR` and does not walk parent
directories.
