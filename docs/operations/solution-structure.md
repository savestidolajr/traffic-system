---
title: "How the Code Is Organised"
description: "The six backend projects, the dependency rules between them, where the console code lives, and which tests enforce the boundaries"
tags:
  - "developer"
  - "architecture"
  - "codebase"
  - "clean-architecture"
  - "tests"
aliases:
  - "solution structure"
  - "code layout"
  - "project structure"
  - "layers"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The source tree and the architecture tests in test/ArnBulletinGenerator.ArchitectureTests"
verified_by:
  - "Source tree read against docs/CODEMAPS/ARCHITECTURE.md and MODULES.md, 2026-09-01"
  - "Layer rules read from test/ArnBulletinGenerator.ArchitectureTests"
related_code:
  - "src/"
  - "web/src/"
  - "test/"
common_queries:
  - "How is the code organised?"
  - "Which project should I change?"
  - "What are the layer rules?"
  - "Where do the tests live?"
see_also:
  - "docs/operations/local-development.md"
  - "docs/operations/delivery-pipeline.md"
  - "docs/canonical/architecture.md"
---

# How the Code Is Organised

The backend is a .NET Azure Functions application in six projects, and the
console is a React single-page application. This page is about the code. For
what the platform does and how the parts behave at runtime, read the
[platform architecture](/how-it-works/architecture/) first; the two are worth
reading in that order.

## The six backend projects

```
src/
  ArnBulletinGenerator.Domain/            models, contracts, pure decisions
  ArnBulletinGenerator.Application/       orchestration and use cases
  ArnBulletinGenerator.Infrastructure/    adapters for everything external
  ArnBulletinGenerator.Functions/          the host: triggers, auth, middleware
  ArnBulletinGenerator.Workflow/           conversational workflow, stages, contracts
  ArnBulletinGenerator.Workflow.Hosting/  Azure OpenAI workflow transport and DI
```

Dependencies point inward only:

| Project | May depend on | Holds |
|---|---|---|
| `Domain` | nothing | Entities, value objects, interfaces, pure domain services |
| `Application` | `Domain` | Orchestration, scheduling, job and library services |
| `Workflow` | `Domain` | Conversational workflow contracts, stages, prompt and trace models |
| `Workflow.Hosting` | `Workflow` | Azure OpenAI workflow transport and service registration |
| `Infrastructure` | `Domain`, `Workflow`, `Workflow.Hosting` | Implementations of the Domain interfaces and external adapters |
| `Functions` | `Domain`, `Application`, `Infrastructure` | Trigger definitions, authorisation, middleware, composition root |

`Domain` referencing an Azure package, or `Application` reaching into
`Infrastructure`, is the mistake this layout exists to prevent. The
consequence is that the interesting decisions are testable without a network,
a storage emulator, or a Functions host.

### These rules are tests, not conventions

`test/ArnBulletinGenerator.ArchitectureTests` fails the build when a boundary is
crossed. It is the cheapest test in the solution and the one most worth
understanding before your first change:

| Test | What it prevents |
|---|---|
| `Domain_ShouldNotDependOn_Application` / `_Infrastructure` / `_Functions` | Inward dependencies turning into a cycle |
| `Application_ShouldNotDependOn_Infrastructure` / `_Functions` | Use cases binding to a vendor adapter |
| `Infrastructure_ShouldNotDependOn_Application` / `_Functions` | Adapters reaching back up the stack |
| `Domain_ShouldNotReference_AzurePackages` | An SDK type leaking into the model |
| `Domain_ShouldNotReference_SystemNetHttp` | The domain making a network call |
| `Application_Namespaces_MatchFolderPath`, `Functions_Namespaces_MatchFolderPath` | Namespaces drifting from the tree |
| `Application_RootLevel_Files_AreAllowlisted`, `Functions_RootLevel_Files_AreAllowlisted` | New files accumulating at a project root |
| `FunctionAttributes_OnlyAppear_UnderTriggersFolder` | Entry points appearing outside `Triggers/` |

If one of these fails, the fix is nearly always to move a type rather than to
relax the test.

## What lives in each project

### Domain

No external dependencies at all.

- **Models** — `Job`, `TrafficIncident`, `MarketSchedule`, `Voice`,
  `BroadcastContext`, `DeliveryStyle`, `PronunciationDictionary`,
  `VoiceRotation`, `FlowRoad`.
- **Value objects** — `DurationWaterfall`, `PronunciationRule`,
  `PronunciationStack`.
- **Interfaces** — `Interfaces/Repositories/` for persistence contracts and
  `Interfaces/Services/` for external adapters and cross-cutting concerns.
- **Domain services** — pure functions: `CorridorAggregator`,
  `CorridorTiering`, `BulletinContentSelector`, `BlueprintSelector`,
  `SyllableCounter`, `GoodRunPolicy`, and others.
- **`Services/Detectors/`** — the deterministic script gates and normalisers
  (`RollCallDetector`, `MetaLeakDetector`, `FalseClosureDetector`,
  `VaguenessDetector` and the rest). These are the checks that run on every
  script without asking a model anything, and they are pure functions with
  unit tests. Adding a new script rule almost always means adding one here.
- **Configuration, contracts, errors** — options classes bound with
  `IOptions`, data-transfer shapes, and message constants.

### Application

Depends on `Domain` only. Organised by capability: `Access/`, `Broadcasting/`,
`Commands/`, `ConfigImport/`, `ConfigTransfer/`, `Jobs/`, `Learning/`,
`Libraries/`, `Ledger/`, `Mapping/`, `Metrics/`, `Scheduling/`, `Validation/`.

The two entry points worth knowing:

- **`Broadcasting/BroadcastOrchestrator`** is a thin shell. The work is in the
  collaborators it composes: `TrafficEvidenceBuilder` (which itself composes
  `MarketContentFetcher` and `FlowPoolBuilder`), `ScriptGenerationEngine`
  (composing `ScriptContentGates` and `DeliveryDirector`), `AudioFinalizer`
  and `JobStatusUpdater`. Follow the orchestrator to find the stage you care
  about, then work inside that collaborator.
- **`Scheduling/SchedulePollerService`** starts due bulletins, composing
  `StationConfigResolver` (which resolves voice, delivery style and
  pronunciation for a slot) and `JobWatchdog` (fallback, reminder and
  stale-job sweeps).

### Infrastructure

Implements the `Domain` interfaces, one adapter per external system: Azure
OpenAI, ElevenLabs, HERE traffic, routing, geocoding, weather and fuel; the
audio pipeline; Azure Table Storage repositories; blob and playout-share
output; webhook notifications; content-safety filtering.

An external service changing its API should touch this project and nothing
else. If a change here forces a change in `Application`, the interface in
`Domain` was probably shaped around the vendor rather than around the need.

### Functions

The host and the composition root.

- **`Triggers/Http/`** — one file per resource: jobs, schedules, voices,
  operators, guidance, pronunciation dictionaries, delivery styles, config
  transfer, health, and the rest.
- **`Triggers/Queues/`** — broadcast and regeneration, each with a poison
  handler sharing `PoisonQueueHandler`.
- **`Triggers/Timers/`** — the scheduler, audit export, and queue-depth
  metrics.
- **`Middleware/`** — activity logging, authorisation, global exception
  handling.
- **`Auth/`** — `ClientPrincipal` parses the platform's authentication header;
  `MarketAccessGuard` applies per-station operator scoping in one place rather
  than per trigger.
- **`Metrics/`** — `ApplicationInsightsBroadcastMetrics` is where every custom
  metric and event is emitted. The names it produces are catalogued on the
  [monitoring page](/operate/dashboard-guide/); change one here and that
  catalogue, the workbooks and the alert rules all need the same change.

## The convention that will surprise you

**The core does not throw.** `Application` and `Domain` return `Result` and
`Option` values, and failure is data rather than control flow. Exceptions are
an edge concern.

The decision about what an error *means* therefore sits at the boundary:
`QueueFailurePolicy` in `Functions` classifies an orchestrator failure as
either acknowledged (content-limited, blocked by the content-safety check,
terminal, or a permanent synthesis failure) or retryable, and only the
retryable ones are allowed to reach the host's retry and poison-queue path. A
malformed message throws deliberately, so the host retries it a bounded number
of times and then poisons it.

Two consequences when you add a failure case:

1. Returning a new failure from the orchestrator without classifying it in
   `QueueFailurePolicy` means it takes the default path, which may retry
   something that can never succeed.
2. Throwing from inside `Application` to signal an expected condition
   bypasses the classification entirely.

## The console

```
web/src/
  pages/          one per route: onair, stations, voices, operators, history
  components/     ui/ (primitives), studio/, navigation/, map-picker/
  lib/            api client, auth, design tokens and shared visuals
```

Data access is uniform and worth following rather than reinventing: a feature
page calls a feature hook, which calls an API hook, which calls the shared
client. Server data is cached by TanStack Query with generated query keys,
responses are validated against a schema, and mutations use a shared factory
that handles optimistic updates, rollback and invalidation. In production the
browser sends no bearer token: the hosting platform injects the caller's
identity header, which the backend middleware reads.

## Where the tests are

| Project | Covers |
|---|---|
| `Domain.Tests` | Pure decisions: content selection, detectors, duration maths |
| `Application.Tests` | Orchestration and use cases with adapters substituted |
| `Infrastructure.Tests` | Adapter behaviour, serialisation, persistence mapping |
| `Functions.Tests` | Triggers, authorisation, failure classification |
| `Workflow.Tests` | Conversational workflow stages, schemas, and policy rendering |
| `Workflow.LiveTests` | Opt-in real Azure OpenAI workflow runs and trace capture |
| `ArchitectureTests` | The layer boundaries above |
| `E2E.Tests` | The whole pipeline against a Functions host with fake providers |
| `ExternalSmoke.Tests` | Real vendor calls, excluded from the normal run |

`E2E.Tests` needs a running host. `Workflow.LiveTests` and
`ExternalSmoke.Tests` spend real vendor-call budget and are opt-in or gated;
the workflow live tests remain skipped unless `ARN_LIVE_WORKFLOW=1`. Both
workflow test projects are covered on the [end-to-end tests page](/develop/e2e-testing/).

## Which project should I change?

| Change | Where to start |
|---|---|
| A new script quality rule | A detector in `Domain/Services/Detectors/` |
| Wording, style or prompt behaviour | The prompt templates, then `ScriptGenerationEngine` |
| A new per-station setting | `Domain` model and repository interface, then `Application/Libraries/`, then a trigger, then the console |
| An external service's API changed | That adapter in `Infrastructure` only |
| A new API endpoint | A trigger in `Functions/Triggers/Http/`, delegating to `Application` |
| New telemetry | `Functions/Metrics/`, then the monitoring page and workbooks |
| Scheduling or fallback behaviour | `Application/Scheduling/` |
| Console layout or a new screen | `web/src/pages/` and the feature's hooks |

Build and test commands are on the [building and running
page](/develop/local-development/). What happens to a change after you push it
is on the [delivery pipeline page](/develop/delivery-pipeline/).
