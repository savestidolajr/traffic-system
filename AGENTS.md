# Arn Agent Instructions

This repository uses a lean, markdown-based memory system for AI-assisted development. Keep project knowledge in version-controlled files, but do not create documentation before there are facts worth preserving.

`AGENTS.md` is the canonical always-on instruction file. Tool-specific files such as `.github/copilot-instructions.md` should be thin adapters that point back here instead of duplicating rules.

## Start Here

Before substantial work:

1. Run `./scripts/docs.py route "<task summary>"` — ranked docs and sections for the task.
2. Open the routed docs or sections that match the work.
3. For any task that **tests, triggers, evaluates, diagnoses, or deploys** bulletins in any environment, reach for the **`arn` CLI** first: run `python3 tools/arn/arn.py` for the full command tree. It is the single front door.

Do not scan every document by default. Use `route` first, then read only what matches the task.

## Live Documentation

| Path | Purpose |
|------|---------|
| `docs/_manifest.yaml` | Generated human-readable doc routing index. |
| `docs/_search.jsonl` | Generated line-oriented search index for agents and tools. |
| `docs/_sections.yaml` | Generated section-level routing index. |
| `llms.txt` | Generated compact LLM entrypoint. |
| `docs/canonical/decisions.md` | Durable decisions that future work must understand. |
| `docs/CODEMAPS/` | Architecture, module, and file maps. Consult when reviewing code, navigating unfamiliar areas, or understanding module boundaries. |
| `tools/arn/arn.py` | The **`arn` CLI**: one entrypoint to test, trigger, evaluate, diagnose, and deploy bulletins across any env. Run `python3 tools/arn/arn.py` for the tree. |
| `scripts/docs.py` | Documentation harness for creating docs and decision entries and regenerating indexes. |
| `scripts/verify.sh` | Canonical local gate, and what the pre-push hook runs. Default = every gate **restricted to the areas your changes touch**, run in parallel lanes (unrecognised paths escalate to every area); `--quick` drops the slow tail (E2E, web perf, Slow-trait tests); `--full` = every gate, every area, no scoping — the exact CI mirror, for large or cross-cutting pushes. `--explain` prints the selection without running. Run before yielding any code change. |

Create new docs only when needed:

- `docs/canonical/architecture.md` after a real architecture exists.
- `docs/reference/<topic>.md` for detailed topic knowledge that should not live only in code or chat.
- `docs/operations/<topic>.md` for repeatable commands, runbooks, deployment, or maintenance procedures.
- only when preserving superseded material.

## Documentation Pact

When you change code, configuration, architecture, behavior, or workflow, update the matching documentation in the same task.
You are not the only agent working on this project. Assume the next session will be a different agent with zero context of your conversation. Every change you make creates implicit knowledge that dies when your session ends. If you don't write it down in the right place, the next agent will repeat your work, contradict your decisions, or break what you built. Documentation is not a record of what you did — it is the handoff to someone who doesn't know you existed.


Required rules:

- Update `docs/canonical/decisions.md` when a decision affects future implementation choices.
- Add a focused reference or operations doc only when durable details would otherwise be lost.
- When changing `deployment/*.bicep`, cloud infrastructure, security posture, or integration architecture: update `docs/operations/as-built.md` (deployed reality) and `docs/operations/known-limitations.md` (accepted constraints), plus `architecture/*.c4` (LikeC4 model/views). Run `./scripts/export-diagrams.sh` after `.c4` changes to regenerate PNGs.
- Use `./scripts/docs.py new-doc`, `./scripts/docs.py add-decision`, or `./scripts/docs.py new-skill` when creating new documentation or skills.
- Regenerate indexes after adding, removing, renaming, or materially changing docs/skills: `./scripts/docs.py generate`.
- Run `./scripts/docs.py check` before completing any task that changed docs, skills, prompts, or the harness.
- **Any claim about live deployed state must carry how and when it was checked**, inline, e.g.
  "verified live 2026-08-12". A claim without provenance is a guess with good formatting: on
  2026-08-12 eight canonical-doc claims measured FALSE, and one `verified_by` entry asserted a
  check it had never done. Never restate an unsourced claim as if you had verified it.

If facts are unknown, record a clear open question in `docs/operations/open-items.md` instead of guessing.

## Frontmatter Schema

Every live markdown doc under `docs/` must include:

```yaml
---
title: "Short Human Title"
description: "One sentence explaining when to read this doc"
tags: [topic]
aliases: [common search phrase]
purpose: "overview"
status: "current"
last_verified: 2026-05-19
review_after: 2026-06-19
volatility: "medium"
source_of_truth: "Short note naming the canonical source of truth"
verified_by:
  - "./scripts/docs.py check"
related_code: [optional/path]
common_queries:
  - "Question this doc answers"
see_also:
  - docs/related-file.md
---
```

Use `status: current`, `draft`, `needs-review`, `stale`, or `archived`.
Use `volatility: low`, `medium`, or `high` to show how often facts are expected to change.

## Context Architecture

- Keep always-on instructions short and behavioral.
- Use `./scripts/docs.py route "<task>"` for discovery before opening detailed docs.
- Use `docs/_search.jsonl` and `docs/_sections.yaml` for search and section-level routing.
- Use `llms.txt` as the compact external-facing LLM index.
- Use focused docs for durable facts, decisions, and runbooks.
- Treat `./scripts/docs.py` as the only documentation harness interface.

## Quality Bar

- Keep changes minimal and direct.
- Prefer explicit commands and file paths.
- Do not add speculative architecture.
- Prefer small, durable docs over long chronological logs.

## Measuring Live Environments

This project's recurring defect is a confident claim nobody checked — see `docs/canonical/decisions.md`
and for the measured cases. Treat every number about a
live environment as unverified until you have run the query yourself.

- **State the clock.** Timestamp measurements in UTC. This host runs UTC+10, so the local date rolls
  over ten hours early; comparing local `date` to UTC telemetry once manufactured a fake outage.
- **`az monitor activity-log list` silently truncates at 50 events** — always pass `--max-events`.
- **Use workspace tables, not classic Application Insights ones:** `AppRequests`, `AppTraces`,
  `AppExceptions`, `AppDependencies`, `AzureDiagnostics` via `az monitor log-analytics query -w <guid>`.
  Classic `requests` returned 167 rows where `AppRequests` held 10,033.
- **Host-emitted log rows use `Properties.Category`;** `Properties.CategoryName` is worker-side and
  blank on them. Parse `-o json` — Log Analytics `-o tsv` column order is unreliable.
- **Absence inside a retention window is not "never".** Workspace 30d, App Insights component 90d, ADO
  build history ~28d. Older than that is UNKNOWN.
- **Distinguish "unreachable" from "absent".** Test and prod deny public network access, so a failed
  secret or blob read from outside the VNet means invisible, not missing.
- Label conclusions MEASURED, `[INFERENCE]` or UNKNOWN, and never promote one to another.
