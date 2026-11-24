---
title: "Codemap — Architecture"
description: "High-level system architecture, Clean Architecture layer relationships, data flow diagrams, deployment architecture, and external system integrations for the Arn traffic bulletin platform"
purpose: "reference"
status: "current"
last_verified: 2026-08-24
review_after: 2026-11-01
volatility: "low"
verified_by:
  - "manual source tree analysis"
  - "./scripts/docs.py check"
source_of_truth: "Source tree analysis of src/, web/, deployment/, docs/CODEMAPS/FILES.md"
tags:
  - "codemap"
  - "architecture"
  - "clean architecture"
  - "data flow"
  - "deployment"
aliases:
  - "architecture codemap"
  - "system overview"
  - "architecture map"
common_queries:
  - "How does the system architecture work?"
  - "What are the Clean Architecture layers?"
  - "How do data flow through the system?"
  - "What external systems does Arn integrate with?"
related_code:
  - "src/"
  - "web/"
  - "deployment/"
  - "docs/CODEMAPS/MODULES.md"
  - "docs/CODEMAPS/FILES.md"
see_also:
  - "docs/canonical/architecture.md"
  - "docs/operations/solution-overview.md"
  - "docs/CODEMAPS/MODULES.md"
  - "docs/CODEMAPS/FILES.md"
---
# Architecture Map

**Last Updated**: 2026-08-07
**Scope**: Arn AI Traffic Bulletin Platform

## System Overview

Arn is a serverless AI-powered traffic bulletin generation system built on Azure Functions with Clean Architecture. It polls traffic data from HERE API, generates broadcast scripts via LLMs (Azure OpenAI), synthesises audio via ElevenLabs, and delivers bulletins to radio playout (Zetta) and cloud storage.

```
┌──────────────────────────────────────────────────────────────────┐
│                        Web UI (React SPA)                        │
│                  Static Web App + Vite + shadcn                  │
└──────────────────────────┬───────────────────────────────────────┘
                           │ HTTP (REST)
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                    Azure Functions (.NET 10)                     │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐  │
│  │ HTTP Triggers│  │ Queue Triggers│  │ Timer / Schedule       │  │
│  │ (CRUD ops)   │  │ (async jobs)  │  │ (polling)              │  │
│  └──────┬───────┘  └──────┬───────┘  └───────────┬────────────┘  │
│         │                 │                       │               │
│         └─────────────────┼───────────────────────┘               │
│                           ▼                                       │
│              ┌────────────────────────┐                           │
│              │   Application Layer    │                           │
│              │  (Orchestration + Use  │                           │
│              │   Cases)               │                           │
│              └───────────┬────────────┘                           │
│                          │                                        │
│              ┌───────────▼────────────┐                           │
│              │    Domain Layer        │                           │
│              │  (Models + Interfaces) │                           │
│              └───────────┬────────────┘                           │
│                          │                                        │
│              ┌───────────▼────────────┐                           │
│              │  Infrastructure Layer  │                           │
│              │ (External Adapters)    │                           │
│              └────────────────────────┘                           │
└──────────────────────────────────────────────────────────────────┘
         │              │              │              │
         ▼              ▼              ▼              ▼
   ┌──────────┐ ┌───────────┐ ┌───────────┐ ┌──────────────┐
   │ HERE API │ │ Azure     │ │ ElevenLabs│ │ Playout Drop │
   │ Traffic  │ │ OpenAI    │ │ TTS       │ │ (Azure Files)│
   └──────────┘ └───────────┘ └───────────┘ └──────────────┘
         │              │
         ▼              ▼
   ┌──────────┐ ┌──────────────┐
   │ Azure    │ │ Azure Table  │
   │ Blob     │ │ Storage      │
   └──────────┘ └──────────────┘
```

## Clean Architecture Layers

### Domain (innermost)
**Project**: `src/ArnBulletinGenerator.Domain/`

Zero external dependencies. Contains:
- **Models** — Core entities: `Job`, `TrafficIncident`, `MarketSchedule`, `Voice`, `BroadcastContext`, `DeliveryStyle`, `PronunciationDictionary`, `VoiceRotation`, `FlowRoad`
- **Value Objects** — `DurationWaterfall`, `PronunciationRule`, `PronunciationStack` (the ordered station-then-platform-wide locator set attached to one synthesis call)
- **Interfaces** — split into `Interfaces/Repositories/` (persistence contracts: `IJobRepository`, `IScheduleRepository`, `IIncidentStateRepository`, `IDeliveryStyleRepository`, `IPronunciationDictionaryRepository`, `IVoiceProfileRepository`, …) and `Interfaces/Services/` (external-adapter/cross-cutting contracts: `IScriptGenerator`, `IScriptDirector`, `IAudioSynthesizer`, `ITrafficProvider`, `IQueuePublisher`, …); a handful of root-level contracts remain ungrouped (`IFileDeliveryService`, `IVoiceRemixer`, `IContentSafetyFilter`, `IScriptSafetyReviewer`, `IAudioPostSynthesisDurationAdjustmentStrategy`)
- **Domain Services** — Pure functions: `CorridorAggregator`, `CorridorTiering`, `BulletinContentSelector`, `ContentSectionOrder`, `BlueprintSelector`, `SyllableCounter`, `IncidentDiversityFilter`, `OrchestrationValidation`, `GoodRunPolicy`, `DeliveryTags`, `IncidentCommentNormalizer`, `VoiceRotationMapping`, `FillerStatementSelector` — plus `Services/Detectors/`, the 20 deterministic Detect/Repair script gates and always-on normalisers (`RollCallDetector`, `DuplicateAnchorDetector`, `SentenceCloneDetector`, `MetaLeakDetector`, `IncidentTypeStackDetector`, `ImpactStackDetector`, `RepeatedPrepositionDetector`, `RequiredSourceDetector`, `SectionOrderDetector`, `SuburbRetentionDetector`, `ConnectorFrequencyDetector`, `RedundantTailDetector`, `VaguenessDetector`, `FalseClosureDetector`, `FragmentDetector`, `HazardBlockageDetector`, `NonVehicularIncidentFilter`, `SeverityUnderclaimDetector`, `SplitClearRunDetector`, `ClearRoadLabelNormalizer`, and the shared `ContentSourceSignals` they key off; `FakeLocationDetector`/`UnselectedSourceDetector` remain flat in `Services/`)
- **Configuration** — `IOptions` binding classes: `BroadcastOptions`, `ModelSelectionOptions`, `DurationPredictionOptions`, `DeliveryOptions`
- **Contracts** — Data transfer shape definitions (job, schedule, audio, traffic, guidance, safety, operator)
- **Errors** — Error/success message constants

### Application
**Project**: `src/ArnBulletinGenerator.Application/`

Depends on Domain only. Folder+namespace restructure 2026-07-21 (`Access/`, `Broadcasting/`, `Commands/`, `ConfigImport/`, `ConfigTransfer/`, `Jobs/`, `Libraries/`, `Mapping/`, `Metrics/`, `Scheduling/`, `Validation/`). Contains:
- **Broadcasting** — `BroadcastOrchestrator` (thin shell) composing five internal collaborators from its 2026-07-21 split: `TrafficEvidenceBuilder` (evidence fetch, itself composing `MarketContentFetcher` + `FlowPoolBuilder`), `ScriptGenerationEngine` (structured-matrix assembly, sentence-scoped review/patch, safety/director convergence, itself composing `ScriptReviewLoop` + `DeliveryDirector`), `AudioFinalizer`, `JobStatusUpdater`; plus `BulletinFallbackService`, `PerVoiceFillerCartResolver`, `ModelSelector`, `JobMessageBuilder`
- **Scheduling** — `SchedulePollerService` composing its 2026-07-21 split: `StationConfigResolver` (voice/delivery/pronunciation resolution) + `JobWatchdog` (fallback/reminder/stale-job sweeps); plus `ScheduleService`, `CoverageResolver`, `TimeZoneHelper`
- **Jobs / Libraries / Access** — `JobService`, `JobPronunciationService`, `IncidentStateService`; `VoiceProfileService`, `DeliveryStyleService`, `PronunciationDictionaryService`; `MarketAccessService`
- **Mapping** — Extension methods for domain↔DTO conversion
- **Validation** — `TrafficScriptStyleValidator`

### Infrastructure
**Project**: `src/ArnBulletinGenerator.Infrastructure/`

Implements Domain interfaces. Contains:
- **AI Providers**: Azure OpenAI Responses SDK (structured-matrix variants plus review, sentence-patch, safety, delivery/content rewrite and director roles), ElevenLabs (TTS + voice remix), HERE (traffic/flow, routing/detours, geocoding, weather, fuel)
- **Audio Pipeline**: WAV processing, duration estimation, voice calibration
- **Persistence**: Azure Table Storage repositories
- **Output**: Azure Blob Storage, playout drop share (Azure Files or Blob drop), Console debug
- **Notifications**: Webhook (Power Automate / Teams)
- **Content Safety**: Configurable filter

### Functions (Presentation)
**Project**: `src/ArnBulletinGenerator.Functions/`

Azure Functions worker host. Contains:
- **HTTP Triggers** (`Triggers/Http/`): Activity, Broadcast, FillerStatements, Editorial, VerifiedPronunciations, JobPronunciation, Schedule, Jobs (read) + JobLifecycle, Voices, VoiceProfiles, VoiceProfilePreview, VoiceCalibration, Synthesize, Operators, PriorityRoads, RoadNames, DeliveryStyles, PronunciationDictionaries, Guidance, SafetyTerms, StyleRules, RolesSource, ConfigTransfer, MapConfig, Health
- **Queue Triggers** (`Triggers/Queues/`): Broadcast + Regeneration (each with a poison handler, sharing `PoisonQueueHandler`)
- **Timer Triggers** (`Triggers/Timers/`): SchedulePoller, AuditExport, QueueDepthMetrics
- **Middleware** (`Middleware/`): Activity Logging, Authorization, Global Exception Handling; `ActivityPathResolver`
- **Auth** (`Auth/`): `ClientPrincipal` (Easy Auth/SWA header parsing), `MarketAccessGuard` (per-market operator-scoping, consolidates previously-duplicated per-trigger checks)
- **Metrics** (`Metrics/`): `ApplicationInsightsBroadcastMetrics`, `E2EFakeBroadcastMetrics`
- **Shared helpers**: `Http/HttpResponseHelper.MapFailureAsync` (centralised not-found/conflict `Result` error mapping)
- **Queue Failure Policy**: `QueueFailurePolicy` at the Functions boundary classifies orchestrator `Result` failures into acknowledged (content-limited, content-safety, terminal, permanent TTS) versus retryable (host retry → poison). Application/core errors remain `Result` values; the Functions layer owns the classify-or-throw decision. Malformed messages throw for bounded host retry then poison. Unexpected non-HTTP exceptions propagate through global middleware for host retry/poison handling; unexpected HTTP exceptions are translated to structured 500 responses.

## Data Flow

### Scheduled Bulletin Generation (primary path)

```
SchedulePollerTrigger (timer, every N min)
  └─ SchedulePollerService
       └─ Creates Job records for markets that are due
            └─ Enqueues BroadcastQueueMessage

BroadcastQueueTrigger
  └─ BroadcastOrchestrator
       ├─ ITrafficProvider (HERE) → incidents + flow congestion corridors
       ├─ IGeocodingProvider (HERE) → geolocate
       ├─ IRoutingProvider / IDetourRoutingProvider (HERE) → drive times, monitored routes, safe detours
       ├─ IWeatherProvider / IFuelPriceProvider (HERE) → weather + fuel (fail-soft, parallel)
       ├─ `IScriptGenerator` → `StructuredMatrixScriptGenerator` → evidence-cited sentence variants and selected script (Azure OpenAI Responses API)
       ├─ `IScriptReviewer` → review · `IScriptRefiner` → sentence-scoped `PatchSentenceAsync`
       ├─ `IScriptSafetyReviewer` → safety pre-review
       ├─ `IScriptDirector` → delivery-style v3 audio-tag steering (fail-safe no-op when neutral/non-v3)
       ├─ `IAudioSynthesizer` (ElevenLabs) → TTS
       ├─ IAudioPipelineService → process WAV, estimate duration
       ├─ IBroadcastOutput → save to Azure Blob
       └─ IJobRepository → persist result

Playout delivery is NOT part of this flow. IFileDeliveryService runs only from
JobService.PublishJobAsync — see "Approval and Delivery" below.
```

### Manual Broadcast (HTTP)

```
BroadcastHttpTrigger (POST /api/broadcasts)
  └─ BroadcastOrchestrator (same flow as above)
```

### Approval and Delivery

```
POST /api/jobs/{id}/publish   (Operator; job must be Approved)
  └─ JobService.PublishJobAsync
       ├─ IFileDeliveryService (ZettaFileDeliveryService)
       │    ├─ download the audio from blob, re-hash, refuse on SHA-256 mismatch
       │    ├─ resolve the market's ARN playout code (unmapped market = loud failure)
       │    └─ IDeliveryFileWriter → AzureFilesDropFileWriter (live) | BlobDropFileWriter
       │         Files REST over OAuth 443 + managed identity, ARN's exact file name,
       │         overwritten in place at the share root
       └─ IJobRepository → Published  (a delivery failure leaves the job Approved for retry)

Zetta then mounts that share itself over AD DS-authenticated SMB with its own
on-prem identity, ingests the file, stamps its own date + expiry, and DELETES it —
so a file still present in the share is an unconsumed delivery.
```

### Regeneration

```
POST /api/jobs/{id}/regenerate (job must be Rejected or Failed)
  └─ JobService.RegenerateJobAsync
       ├─ Job.RequestRegeneration → reset to Requested (clears script/audio/attempts)
       └─ regeneration-queue → RegenerationQueueTrigger
            └─ BroadcastOrchestrator (full re-orchestration; re-fetches traffic)
```

- The UI **Revise** action regenerates only after queued source changes or an explicit voice-fit escalation. Rejection records a closed reason and audit notes; only a separate, optional station lesson is replayed into later generation.
- Failed jobs regenerate directly — that is the operator retry path.
- When generation exhausts, `ScriptGenerationEngine.GenerationRunOutcome` carries the strongest attempted candidate to `AudioFinalizer.PersistFailedCandidateAsync`; the failed job retains its script and any usable audio as unpublished inspection evidence. The failed-job DTO and review pane expose those artifacts without making them approval- or playout-eligible.
- Queue dedup: a job in `Requested` is the expected pre-state (the HTTP handler
  resets it before queueing); any other status means duplicate delivery → skip.

### Web UI

```
Browser → Static Web App (React SPA)
  ├─ /onair     → OnAirPage (workstation queue + review cockpit)
  ├─ /stations  → StationsPage (station cards, schedule Studio, manual trigger)
  ├─ /voices    → VoicesPage (voice library: variants, versions)
  ├─ /operators → OperatorsPage (market/station assignments, admin)
  ├─ /history   → HistoryPage (broadcast log)
  └─ navigation → NotificationsPanel → useJobs() cache
```

### Frontend Internal Flow

```
App.tsx
  ├─ ThemeProvider / QueryClientProvider / TooltipProvider
  ├─ BrowserRouter
  ├─ NavigationBar
  │   ├─ DesktopNav / MobileNav
  │   ├─ NotificationsPanel
  │   └─ UserMenu
  └─ Routes
      ├─ pages/*
      │   └─ feature components
      │       └─ feature hooks
      │           └─ feature api hooks
      │               └─ apiClient
      └─ shared components
          ├─ components/ui/         (shadcn/Radix + aceternity motion primitives)
          ├─ components/studio/     (shared Studio shell)
          ├─ components/navigation/ (AppSidebar, UserMenu, MobileNav)
          ├─ components/map-picker/ (Leaflet coverage editor)
          └─ lib/design/            (BudgetBar, FreshnessDot, SourceGlyphs, icons)
```

### Frontend API + Cache Flow

```
apiClient
  ├─ base URL: VITE_API_BASE_URL or /api
  ├─ auth: auth.ts parses SWA /.auth/me payloads or dev auth env overrides
  ├─ mock mode: VITE_USE_MOCKS=true → lib/api/mock/createMockAdapter()
  └─ response errors → ApiError(status, originalError)

Auth
  ├─ useAuthData() caches current user with TanStack Query
  ├─ useCurrentUser()/useIsAdmin() share that cache
  └─ API requests carry no Bearer in prod — the SWA proxy injects X-MS-CLIENT-PRINCIPAL

Queries
  ├─ createQueryKeys(scope)
  ├─ apiClient GET
  ├─ Zod schema parse
  ├─ camelizeKeys() transforms backend PascalCase DTOs
  ├─ TanStack Query cache
  └─ QueryClient skips retry for ApiError 4xx statuses

Mutations
  ├─ useMutationFactory()
  ├─ optional optimistic cancel → snapshot → apply → rollback
  ├─ apiClient POST/PUT/DELETE
  ├─ query invalidation on success/settle
  └─ feature/domain handlers own toast messages; no global generic mutation toast
```

## Deployment Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                    Azure Resource Group                          │
│                                                                  │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐  │
│  │ Storage     │  │ Key Vault    │  │ AI Services             │  │
│  │ (Blob+Table │  │ (Secrets)    │  │ (Azure OpenAI + Search) │  │
│  │  +Queue)    │  │              │  │ (optional Azure AI Search)│  │
│  └─────────────┘  └──────────────┘  └────────────────────────┘  │
│                                                                    │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐  │
│  │ Function App│  │ Static Web   │  │ App Insights + Log     │  │
│  │ (Consumption│  │ App (React)  │  │ Analytics              │  │
│  │  Plan)      │  │              │  │                        │  │
│  └─────────────┘  └──────────────┘  └────────────────────────┘  │
│                                                                    │
│  ┌─────────────┐  ┌──────────────┐                               │
│  │ Action Group│  │ Workbooks    │                               │
│  │ (Alerts)    │  │ (Dashboards) │                               │
│  └─────────────┘  └──────────────┘                               │
└──────────────────────────────────────────────────────────────────┘
```

Provisioned via Bicep templates in `deployment/`. Environments: dev, test, prod.

## External Systems

| System | Purpose | Integration Point |
|---|---|---|
| HERE API | Traffic incidents + flow congestion, routing + drive times + safe detours, geocoding, weather, fuel prices | `HereTrafficProvider`, `HereRoutingProvider`, `HereGeocodingProvider`, `HereWeatherProvider`, `HereFuelPriceProvider` |
| Azure OpenAI | Structured-matrix variants plus review / sentence-patch / safety / delivery-rewrite / content-rewrite / director roles (Responses API) | `StructuredMatrixScriptGenerator`, `LlmScriptGenerator`, `ResponsesTransport` |
| Azure AI Search | Optional RAG style-example retrieval | `StyleExamplesService`, `StyleExamplesCache` |
| ElevenLabs | Text-to-speech + voice design/remix | `ElevenLabsClient`, `ElevenLabsAudioSynthesizer` |
| Azure Table Storage | Job/schedule/incident/pronunciation/delivery-style persistence | `TableStorage*Repository` classes |
| Azure Queue Storage | Broadcast and regeneration queues, poison queues | `QueuePublisher`, queue triggers |
| Azure Blob Storage | Audio file output | `AzureBlobBroadcastOutput`, `BlobAudioUrlProvider` |
| Playout drop share | Radio playout delivery | `ZettaFileDeliveryService`, `AzureFilesDropFileWriter`, `BlobDropFileWriter` |
| Power Automate | Teams notifications | `WebhookNotificationService` |
| Azure Key Vault | Secrets management | Bicep configuration |

## Architecture Decisions (Summary)

See `docs/canonical/decisions.md` for full ADR log.

- **Clean Architecture**: Domain has zero external deps; Infrastructure implements Domain interfaces; Application orchestrates use cases
- **Azure Functions Consumption Plan**: Cost-effective for intermittent traffic bulletin workload (10-min function timeout)
- **Azure Table Storage**: Schema-less, low-cost persistence for job/schedule state (not relational)
- **Azure OpenAI Responses SDK as LLM substrate**: `OpenAI.Responses.ResponsesClient` is used directly (the same client Microsoft Agent Framework's own connectors wrap) for structured-matrix variants and the non-draft review, sentence-patch, safety, delivery/content-rewrite and director roles — no `AIAgent`/session layer, since these calls carry no conversation state or tool use (2026-07-08, replaced Semantic Kernel)
- **Duration prediction**: Plugged strategy pattern — history-based or static-rate, selected at runtime via config
- **Playout delivery**: we write the approved WAV to an Azure Files share on ARN's drop account over Files REST + managed identity; Zetta mounts that share itself over AD DS SMB and deletes the file on ingest. Nothing in this codebase speaks SMB or reaches into ARN's network — the direct-SMB transport was deleted 2026-07-31 (decisions.md)
- **Generalised bulletin data model**: flow congestion corridors, monitored drive-time routes, per-station good-run roads, and gated safe detours — one station-agnostic bulletin per market (decisions.md, 2026-06-17)
- **Eight non-draft LLM roles**: review, sentence-patch, safety, director, delivery-rewrite and content-rewrite are provided by `LlmScriptGenerator`; attribute and polish are being removed, while `StructuredMatrixScriptGenerator` alone owns script generation
- **Queue failure boundary**: `QueueFailurePolicy` at the Functions layer classifies orchestrator `Result` failures into acknowledged (content-limited, content-safety, terminal, permanent TTS) versus retryable (host retry → poison); application/core errors remain `Result` values throughout. String-prefix matching is explicitly temporary pending phase-4 structured error taxonomy (decisions.md, 2026-07-10)
