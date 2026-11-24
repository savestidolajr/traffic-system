---
title: "Codemap — Modules"
description: "Module descriptions, public APIs, dependencies, exports, and usage examples for every project in the Arn codebase: backend layers, frontend, deployment IaC, and documentation"
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
  - "modules"
  - "API"
  - "dependencies"
  - "exports"
aliases:
  - "module codemap"
  - "module map"
  - "public APIs"
  - "project dependencies"
common_queries:
  - "What are the main modules and their dependencies?"
  - "What public APIs does each project expose?"
  - "How do I register DI for a module?"
  - "Which module implements a given domain interface?"
related_code:
  - "docs/CODEMAPS/ARCHITECTURE.md"
  - "docs/CODEMAPS/FILES.md"
  - "src/"
  - "web/"
  - "deployment/"
see_also:
  - "docs/CODEMAPS/ARCHITECTURE.md"
  - "docs/CODEMAPS/FILES.md"
  - "docs/canonical/architecture.md"
---
# Module Map

**Last Updated**: 2026-08-07
**Scope**: Public APIs and module dependencies for Arn backend, frontend, infrastructure, and deployment.

---

## Backend Modules

### ArnBulletinGenerator.Domain

**Purpose**: Core domain models, interfaces, and pure business logic with zero external dependencies.

**Location**: `src/ArnBulletinGenerator.Domain/`

- `Interfaces/Repositories/*.cs` — 21 persistence-contract interfaces
- `Interfaces/Services/*.cs` — 33 external-adapter/cross-cutting service-contract interfaces
- `Interfaces/*.cs` (root) — 6 ungrouped interfaces
- `Models/*.cs` — 71 domain models and enums
- `Services/*.cs` — 57 pure domain services
- `Services/Detectors/*.cs` — 18 deterministic Detect/Repair script gates + always-on normalisers
- `Configuration/*.cs` — 8 IOptions configuration classes
- `Contracts/*.cs` — 14 root data contract files + 3 nested contract files
- `Constants/*.cs` — 3 constant files (QueueNames, ProviderNames, CalibrationDefaults)
- `Errors/*.cs` — 3 error/message constants
- `ValueObjects/*.cs` — 2 value objects

**Dependencies**: None external

**Exports (Interfaces)**:
- `IJobRepository` — Job CRUD + query operations
- `IScheduleRepository` — Schedule CRUD + lookup
- `IIncidentStateRepository` — Incident state snapshot management
- `IVoiceCalibrationRepository` — Voice calibration data access
- `ISynthesisHistoryRepository` — Synthesis history persistence
- `IScriptGenerator` — Generate broadcast script from traffic data
- `IScriptReviewer` — Review generated script quality
- `IScriptRefiner` — Sentence-scoped `PatchSentenceAsync` repair only
- `IAudioSynthesizer` — Convert script text to audio
- `IActivityLogRepository` — Activity/change audit log persistence
- `IAudioPipelineService` — Full audio processing pipeline
- `IFillerAudioCacheRepository` — Cached filler-audio-clip metadata persistence (table only)
- `IFillerAudioCacheService` — Resolve filler audio (cache lookup, synthesize-and-store on a miss) + cache invalidation
- `IDurationEstimator` — Estimate broadcast audio duration
- `IDurationPredictor` — Predict duration using configurable strategy
- `ITrafficProvider` — Fetch traffic incident data
- `IRoutingProvider` — Calculate route drive times
- `IDetourRoutingProvider` — Safe avoided-segment detour routing
- `IGeocodingProvider` — Geocode location names to coordinates
- `IMapAttributesCacheRepository` — persistent batch cache for static HERE segment road attributes
- `IVoiceProvider` — List available voices
- `IVoiceCalibrationService` — Manage voice calibration parameters
- `IQueuePublisher` — Publish messages to Azure Queue
- `IBroadcastOutput` — Write broadcast output (blob or console)
- `IAudioUrlProvider` — Provide public URLs for generated audio
- `IFileDeliveryService` — Deliver files to external systems (Zetta)
- `INotificationService` — Send notifications (webhook)
- `ICronParser` — Parse cron expressions
- `IModelSelector` — Select AI model by use case
- `IContentSafetyFilter` — Filter unsafe content
- `ICoverageResolver` — Determine traffic coverage area
- `IAudioProcessor` — Process raw audio bytes
- `IWeatherProvider` — Fetch weather data (HERE Destination Weather)
- `IFuelPriceProvider` — Fetch fuel prices (HERE Fuel Prices)
- `IScriptSafetyReviewer` — LLM safety pre-review of the script
- `IScriptAttributor` — Attribute script sentences to source corridors
- `IVoiceProfileRepository` — Voice profile/variant persistence
- `IPronunciationDictionaryProvider` — ElevenLabs pronunciation dictionary management (create/add/remove rules)
- `IPronunciationDictionaryRepository` — Pronunciation dictionary library persistence
- `IVoiceRemixer` — Text-driven voice remix (ElevenLabs)
- `IAudioPostSynthesisDurationAdjustmentStrategy` — Post-TTS duration tweaks
- `IDeliveryStyleRepository` — Delivery-style library persistence (named, versioned audio-tag policies)
- `IScriptDirector` — ElevenLabs v3 audio-tag steering (fail-safe no-op when neutral)

**Exports (Domain Services)**:
- `BulletinDurationFitter` — Fit script length to target duration
- `BulletinEditingBand` — Word/time band a verbatim re-voice edit must hit to fit the slot (target + slow-fill/speed-up min-max)
- `CompassDirection` — Shape bearing → citybound/outbound/compass quadrant
- `ContentFormatters` — Format weather/fuel/good-run content for the prompt
- `CorridorAggregator` — Aggregate incidents + flow into traffic corridors
- `CorridorCurator` — Apply operator corridor curation (include/exclude/reorder)
- `CorridorFormatter` — Format corridor names for display
- `CorridorMagnitude` — Classify a corridor's commuter-facing magnitude tier (delay/queue, not flat severity)
- `GoodRunPolicy` — Deterministic good-run gating: fill when no incidents, market-weighted-only when incidents lead
- `GuidanceComposer` — Compose standing guidance by precedence (Station>Market>Global), cap, sanitise
- `PromptSafe` — Prompt-injection hardening (strip reserved delimiter tokens, wrap data envelopes)
- `CoverageMapping` — Coverage shape/geometry mapping helpers
- `ExponentialMovingAverage` — EMA computation for duration prediction
- `IncidentChangeFormatter` — Format incident change summaries
- `IncidentDiversityFilter` — Filter similar incidents
- `IncidentTypeClassifier` — Classify incident type (standing footprint vs queue)
- `MonitoredRouteReporter` — Build monitored drive-time reports from routing
- `OrchestrationValidation` — Validate broadcast orchestration preconditions
- `PriorityRoads` — Priority-road boost + good-run detection
- `ScriptSentenceSplitter` — Split script into sentences (mirrors frontend)
- `SyllableCounter` — Count syllables in text
- `TrafficTypeMapper` — Map traffic incident types
- `TtsErrorClassifier` — Classify TTS synthesis errors
- `WordCounter` — Count words in script text
- `WordTargetCalculator` — Calculate target word counts for time slots
- `WordTimingBuilder` — Build per-word timings from synthesis marks
- `VoiceRotationMapping` — Voice-rotation DTO ↔ policy mapping (single/round-robin/time-of-day)
- `DeliveryTags` — ElevenLabs v3 inline audio-tag helpers (strip, detect, word-immutability guard)
- `IncidentCommentNormalizer` — Decode + advisory-strip HERE operator `comment` free-text
- `BulletinContentSelector` — Unified rundown selection: corridors + good-runs ranked together; suppressed minors free airtime
- `CorridorTiering` — Single source of truth for voiceable corridors (tier caps, minor suppression under a major headline)
- `FillerStatementSelector` — Greedy gap-covering filler selection (anti-repeat, ≤3 cap)
- `FillerAudioCacheKey` — Cache key for one (voiceId × VoiceSettings × TTS model) filler-synthesis configuration
- `FillerCartAudioKey` — Cache key for one (script × voiceId × VoiceSettings × TTS model) filler-cart clip (script-hash suffix self-invalidates on wording change)
- `FillerAudioCacheInvalidator` — Shared diff-and-evict routine invalidating cached filler audio on statement text change/removal
- `SlotFitPlanner` — Shared slot-fill word-target logic (production + eval mirror)
- `RoadNameFormatter` — Title-case shouting road names (Mc-aware) + abbreviation-aware `IsSameRoad`
- `VoiceModelResolver` — Pick a voice's TTS model from its verified base models
- Deterministic Detect/Repair script gates (static classes, `readonly record struct` results, in `Services/Detectors/` since 2026-07-21): `RollCallDetector`, `DuplicateAnchorDetector`, `SentenceCloneDetector`, `MetaLeakDetector`, `IncidentTypeStackDetector`, `ImpactStackDetector`, `RepeatedPrepositionDetector`, `FakeLocationDetector` (remains flat in `Services/`), `UnselectedSourceDetector` (remains flat in `Services/`), `RequiredSourceDetector`, `SectionOrderDetector`, `SuburbRetentionDetector`, `ConnectorFrequencyDetector`, `RedundantTailDetector`, `VaguenessDetector`, `FalseClosureDetector`, `FragmentDetector`, `HazardBlockageDetector`, `NonVehicularIncidentFilter`, `SeverityUnderclaimDetector`, `SplitClearRunDetector` — run in the review-refine loop, the final post-loop check, and `FinalizeExpandedScript`
- `ContentSourceSignals` — Shared weather/fuel sentence signals (`Services/Detectors/`) backing `UnselectedSourceDetector` (fabricated unselected source), `RequiredSourceDetector` (always-include source omitted) and `SectionOrderDetector`
- `ContentSectionOrder` — Resolves the on-air read order of the trailing supplementary sections from a station's `FillOrder` (whichever of weather/fuel sits earlier reads earlier), and describes it for the draft prompt
- `PronunciationRuleSetEditor` — Pure rule-set alias editing with collision and validation checks
- `BlueprintSelector` — bounded, seed-stable per-read presentation blueprint (opener/flow/clear-road placement/close), rendered into `draft.md`; `ClearRoadLabelNormalizer` (also `Services/Detectors/`) — always-on "flowing well"/"moving well" rewrite, applied with `ClosureVerbNormalizer`/`TimeOfDayNormalizer`

---

### ArnBulletinGenerator.Application

**Purpose**: Use case orchestration, scheduling, and business workflow logic.

**Location**: `src/ArnBulletinGenerator.Application/`

**Key Files** (folder+namespace restructure 2026-07-21: `Access/`, `Broadcasting/`, `Commands/`, `ConfigImport/`, `ConfigTransfer/`, `Jobs/`, `Libraries/`, `Mapping/`, `Metrics/`, `Scheduling/`, `Validation/`):
- `Broadcasting/BroadcastOrchestrator.cs` — Thin broadcast-workflow shell; composes `TrafficEvidenceBuilder`, `ScriptGenerationEngine`, `AudioFinalizer`, `JobStatusUpdater`
- `Broadcasting/TrafficEvidenceBuilder.cs` — HERE corridor fetch with configurable three-minute caching, keyed single-flight coalescing, official-feed reuse, incident-delta computation and job audit trail, composing `MarketContentFetcher` (weather/fuel/routes/guidance/filler/style-rules/future-lessons fan-out) and `FlowPoolBuilder` (flowing-road pool) — both extracted collaborators (internal)
- `Broadcasting/MarketContentFetcher.cs` — Fail-soft per-market content fetchers, extracted from `TrafficEvidenceBuilder` (internal collaborator)
- `Broadcasting/FlowPoolBuilder.cs` — Data-backed flowing-road pool for good-run slots, extracted from `TrafficEvidenceBuilder` (internal collaborator)
- `Broadcasting/ScriptGenerationEngine.cs` — Structured-matrix assembly, sentence-scoped review/patch, safety/director convergence loop + synthesis for one attempt, composing `ScriptReviewLoop`, `DeliveryDirector` (internal collaborator)
- `Broadcasting/ScriptReviewLoop.cs` — Sentence-scoped review/patch loop, final grounding gate, style + content-safety gates, extracted from `ScriptGenerationEngine` (internal collaborator)
- `Broadcasting/DeliveryDirector.cs` — Delivery direction (v3 audio tags), extracted from `ScriptGenerationEngine` (internal collaborator)
- `Broadcasting/AudioFinalizer.cs` — Persist audio, re-voice, resolve to `WaitingApproval` (internal collaborator)
- `Broadcasting/JobStatusUpdater.cs` — Every job-status transition + log-line append (internal collaborator)
- `Broadcasting/BulletinFallbackService.cs` — Shared fallback chain (last good same-day → filler cart); used by the poller and the orchestrator's content-limited path
- `Broadcasting/PerVoiceFillerCartResolver.cs` — Resolves the filler-cart clip for a job's OWN voice config via `IFillerAudioCacheService.ResolveCartUrlAsync` (per-voice-first, evergreen WAV second)
- `Broadcasting/ModelSelector.cs` — AI model selection by use case
- `Broadcasting/JobMessageBuilder.cs` — Build queue messages for broadcast jobs
- `Access/MarketAccessService.cs` — Per-market/station access scoping for operators
- `Jobs/JobService.cs` — Job management use cases
- `Jobs/JobPronunciationService.cs` — Applies job pronunciation rules through the dictionary service
- `Jobs/IncidentStateService.cs` — Incident state diff management
- `Scheduling/SchedulePollerService.cs` — Polling logic for time-based schedule triggers; composes `StationConfigResolver` + `JobWatchdog` via one shared factory
- `Scheduling/StationConfigResolver.cs` — Resolves a schedule's voice/delivery-style/pronunciation-dictionary config, extracted from `SchedulePollerService`; also reused by `JobWatchdog`'s stale-job recovery
- `Scheduling/JobWatchdog.cs` — Per-tick fallback-coverage/reminder/stale-job/stale-lock sweeps, extracted from `SchedulePollerService`
- `Scheduling/ScheduleService.cs` — Schedule management use cases
- `Scheduling/CoverageResolver.cs` — Resolve traffic coverage configuration
- `Scheduling/TimeZoneHelper.cs` — Time zone conversion utilities
- `ConfigTransfer/ConfigImportService.cs` — Coordinates per-section `ConfigImport/*PlanBuilder` classes in dependency order
- `ConfigImport/*PlanBuilder.cs` — 16 config-import files, including 9 per-section plan builders
- `ConfigTransfer/ConfigTransferContracts.cs` / `ConfigValidationLimits.cs` — Config bundle/report DTOs + shared field-length/count limits
- `Libraries/VoiceProfileService.cs` — Voice profile (variant/version) lifecycle
- `Libraries/PronunciationDictionaryService.cs` — Pronunciation dictionary lifecycle (uploads rule diff, mints versions)
- `Libraries/DeliveryStyleService.cs` — Delivery-style library lifecycle (CRUD, version restore, archive/unarchive)
- `Libraries/PreviewScenarios.cs` — Canned scenarios for the style-rules preview
- `Mapping/*.cs` — 8 mapping extension files
- `Commands/*.cs` — 3 command/query record files
- `Validation/TrafficScriptStyleValidator.cs` — Script style validation
- `Metrics/IBroadcastMetrics.cs` — Metrics abstraction
- `DependencyInjection.cs` — DI registration for Application layer

**Dependencies**:
- `ArnBulletinGenerator.Domain`

**Exports**:
- `IBroadcastOrchestrator` — `ExecuteAsync(Job, CancellationToken)`: Orchestrate bulletin generation
- `IJobService` — `GetJobAsync, CreateJobAsync, UpdateJobStatusAsync, GetJobsAsync`
- `IScheduleService` — `GetSchedulesAsync, UpsertScheduleAsync, DeleteScheduleAsync`
- `ISchedulePollerService` — `CheckAndTriggerDueSchedulesAsync`
- `IBulletinFallbackService` — `TryApplyFallbackAsync(Job, CancellationToken)`: apply same-day/filler fallback, set `FallbackUsed`
- `IIncidentStateService` — `GetOrCreateStateAsync, UpdateStateAsync, GetDeltasAsync`
- `IModelSelector` — `GetModelUseCase(string modelName)`
- `ICoverageResolver` — `GetCoverageRegion(CoverageRequest)`
- `IBroadcastMetrics` — `RecordBroadcastResult, RecordDurationPrediction, RecordSynthesis`
- `IDeliveryStyleService` — `ListAsync, GetAsync, CreateAsync, UpdateAsync, RestoreVersionAsync, ArchiveAsync, UnarchiveAsync`

**Usage Example**:
```csharp
// DI registration
services.AddApplication();

// Orchestrate a broadcast
var orchestrator = serviceProvider.GetRequiredService<IBroadcastOrchestrator>();
var result = await orchestrator.ExecuteAsync(job, cancellationToken);
```

---

### ArnBulletinGenerator.Infrastructure

**Purpose**: Implementations of Domain interfaces, external service adapters, and persistence.

**Location**: `src/ArnBulletinGenerator.Infrastructure/`

**Key Sub-Modules**:

#### AI Providers

| Sub-module | Purpose | Key Files |
|---|---|---|
| `AiProviders/AzureOpenAI/` | Azure OpenAI role providers for review, sentence-patch, safety, director, delivery-rewrite and content-rewrite, plus data composition and narrative summarisation; `LlmScriptGenerator` is not the production draft generator and its legacy draft/attribute/polish paths are being removed | `LlmScriptGenerator.cs`, `ResponsesTransport.cs`, `PromptLibrary.cs`, `StyleExamplesCache.cs`, `DraftCandidateSelector.cs`, `RateLimitRetryHandler.cs`, `BulletinDataComposer.cs`, `TrafficNarrativeSummarizer.cs`, `TrafficNarrativeSummarizer.EvidenceUnits.cs`, `CitationVerifier.cs`, `DraftSentence.cs`, `DraftWordBudget.cs`, `ModelResponseParser.cs`, `HandlebarsTemplateRenderer.cs` |
| `AiProviders/StructuredMatrix/` | Production `IScriptGenerator`: creates evidence-cited sentence variants, selects the optimal combination, and assembles the broadcast script | `StructuredMatrixScriptGenerator.cs` |
| `AiProviders/ElevenLabs/` | TTS voice synthesis | `ElevenLabsClient.cs`, `ElevenLabsAudioSynthesizer.cs` |
| `AiProviders/Location/` | HERE traffic+flow, routing+detours, geocoding, weather, fuel | `HereTrafficProvider.cs`, `HereRoutingProvider.cs`, `HereGeocodingProvider.cs`, `HereWeatherProvider.cs`, `HereFuelPriceProvider.cs` |
| `AiProviders/Agent/` | Style examples for consistent voice | `StyleExamplesService.cs` |

#### Audio Pipeline

| Purpose | Key Files |
|---|---|
| WAV processing | `Audio/WavDurationProcessor.cs`, `WavHeaderBuilder.cs`, `WavInfoParser.cs`, `WavValidator.cs`, `PcmAudioStitcher.cs` |
| Duration strategies | `Audio/DurationStrategies/SentencePauseInsertionDurationAdjustmentStrategy.cs` |
| Prediction | `Audio/StaticRateDurationPredictor.cs`, `Calibration/HistoryBasedDurationPredictor.cs` |
| Calibration | `Audio/VoiceCalibrationService.cs`, `Calibration/InMemorySynthesisHistoryRepository.cs` |
| Filler audio cache | `Audio/FillerAudioCacheService.cs` (resolve: cache-hit blob download or cache-miss synthesize+trim+normalize+upload+persist) |

#### Persistence

| Purpose | Key Files |
|---|---|
| Job repository | `Persistence/TableStorageJobRepository.cs` |
| Schedule repository | `Persistence/TableStorageScheduleRepository.cs` |
| Incident state | `Persistence/TableStorageIncidentStateRepository.cs` |
| Voice calibration | `Persistence/TableStorageVoiceCalibrationRepository.cs` |
| Synthesis history | `Persistence/TableStorageSynthesisHistoryRepository.cs` |
| Voice profile | `Persistence/TableStorageVoiceProfileRepository.cs` |
| Pronunciation dictionary | `Persistence/TableStoragePronunciationDictionaryRepository.cs` |
| Delivery style | `Persistence/TableStorageDeliveryStyleRepository.cs` |
| Priority roads | `Persistence/TableStoragePriorityRoadsRepository.cs` |
| Filler statements | `Persistence/TableStorageFillerStatementsRepository.cs` |
| Filler audio cache | `Persistence/TableStorageFillerAudioCacheRepository.cs` |
| Map attributes cache | `Persistence/TableStorageMapAttributesCacheRepository.cs` (persistent static HERE segment attributes) |
| Operator assignment | `Persistence/TableStorageOperatorAssignmentRepository.cs` |
| Station future lessons | `Persistence/TableStorageFutureLessonRepository.cs` |
| Audit export | `Persistence/TableStorageAuditExporter.cs` |
| Base infrastructure | `Persistence/TableRepositoryBase.cs`, `TableClientFactory.cs`, `SafeJson.cs` |

#### Output

| Purpose | Key Files |
|---|---|
| Azure Blob output | `Output/AzureBlobBroadcastOutput.cs`, `Output/BlobAudioUrlProvider.cs` |
| Console debug | `Output/ConsoleBroadcastOutput.cs`, `Output/LocalAudioUrlProvider.cs` |
| Playout delivery | `Output/ZettaFileDeliveryService.cs`, `Output/AzureFilesDropFileWriter.cs`, `Output/BlobDropFileWriter.cs` |

#### Other

| Purpose | Key Files |
|---|---|
| Notifications | `Notifications/WebhookNotificationService.cs`, `Notifications/WebhookRetryHandler.cs` |
| Content safety | `ContentSafety/ConfigurableContentSafetyFilter.cs`, `Persistence/TableStorageSafetyTermRepository.cs` |
| Serialization | `Serialization/JsonDefaults.cs`, `OptionJsonConverterFactory.cs` |
| Queue publishing | `QueuePublisher.cs` |
| Cron parsing | `CronParser.cs` |

**Dependencies**:
- `ArnBulletinGenerator.Domain`
- NuGet: `Azure.Storage.Blobs`, `Azure.Storage.Queues`, `Azure.Data.Tables`, `OpenAI`, `Handlebars.Net`, `Polly`, `Polly.Extensions.Http`

**Exports**:
- `DependencyInjection.AddInfrastructure(IServiceCollection, IConfiguration)` — registers all infrastructure services
- All domain interfaces implemented by concrete classes above

**Usage Example**:
```csharp
services.AddInfrastructure(configuration);
```

---

### ArnBulletinGenerator.Functions

**Purpose**: Azure Functions host — HTTP, queue, and timer triggers that wire the application to the cloud.

**Location**: `src/ArnBulletinGenerator.Functions/`

**Key Files**:

| File | Type | Route/Trigger | Purpose |
| `Triggers/Http/ActivityHttpTrigger.cs` | HTTP GET | `/api/activity` | Activity/change audit log query (admin) |
| `Triggers/Http/BroadcastHttpTrigger.cs` | HTTP POST | `/api/broadcasts` | Manual broadcast trigger |
| `Triggers/Http/FillerStatementsHttpTrigger.cs` | HTTP GET/PUT | `/api/markets/{market}/filler-statements`, `/api/filler-statements/inventory` | Market filler-statement management and inventory |
| `Triggers/Http/EditorialHttpTrigger.cs` | HTTP CRUD | `/api/editorial/blocks`, `/api/editorial/blocks/{key}`, `/api/editorial/preview`, `/api/editorial/settings` | Editorial blocks and settings management |
| `Triggers/Http/VerifiedPronunciationsHttpTrigger.cs` | HTTP GET/PUT | `/api/markets/{market}/verified-pronunciations`, `/api/verified-pronunciations/inventory` | Market verified-pronunciation management and inventory |
| `Triggers/Http/JobPronunciationHttpTrigger.cs` | HTTP POST | `/api/jobs/{jobId}/pronunciation-rules` | Apply a pronunciation rule to a job dictionary |
| `Triggers/Http/JobsHttpTrigger.cs` | HTTP GET | `/api/jobs{/jobId}`, `/api/jobs/{jobId}/audio{/renderToken}` | Job listing and read access |
| `Triggers/Http/JobLifecycleHttpTrigger.cs` | HTTP POST/DELETE | `/api/jobs/{jobId}/{approve,reject,reject-to-fallback,publish,regenerate,air-source}`, `/api/jobs/{jobId}` | Job lifecycle mutations |
| `Triggers/Http/ScheduleHttpTrigger.cs` | HTTP CRUD | `/api/schedules{/id}` | Schedule management |
| `Triggers/Http/VoicesHttpTrigger.cs` | HTTP GET | `/api/voices` | Available voice listing |
| `Triggers/Http/VoiceProfilesHttpTrigger.cs` | HTTP CRUD | `/api/voices/profiles` | Voice profile CRUD and version restore |
| `Triggers/Http/VoiceProfilePreviewHttpTrigger.cs` | HTTP POST | `/api/voices/profiles/{preview,remix}` | Voice preview and remix |
| `Triggers/Http/VoiceoverHttpTrigger.cs` | HTTP POST/GET | `/api/voices/profiles/{id}/voiceover`, `/api/voices/voiceovers/{fileName}` | Renders arbitrary copy on a profile's current configuration (Operator-reachable) and streams it back as a downloadable attachment |
| `Triggers/Http/PronunciationDictionariesHttpTrigger.cs` | HTTP CRUD | `/api/pronunciation-dictionaries` | Pronunciation dictionary management and preview |
| `Triggers/Http/DeliveryStylesHttpTrigger.cs` | HTTP CRUD | `/api/delivery-styles` | Delivery-style library management and preview |
| `Triggers/Http/VoiceCalibrationHttpTrigger.cs` | HTTP POST | `/api/voices/calibrate` | Voice calibration |
| `Triggers/Http/SynthesizeHttpTrigger.cs` | HTTP POST | `/api/synthesize` | Ad-hoc TTS |
| `Triggers/Http/OperatorsHttpTrigger.cs` | HTTP GET/PUT | `/api/operators` | Operator assignments |
| `Triggers/Http/PriorityRoadsHttpTrigger.cs` | HTTP GET/PUT | `/api/markets/{market}/priority-roads` | Per-market priority roads |
| `Triggers/Http/RoadNamesHttpTrigger.cs` | HTTP GET | `/api/markets/{market}/road-names` | The traffic feed's own road names for a market's coverage — autofill source for priority roads (12h `IMemoryCache`) |
| `Triggers/Http/HealthHttpTrigger.cs` | HTTP GET | `/api/health` | Health probe endpoint |
| `Triggers/Http/GuidanceHttpTrigger.cs` | HTTP CRUD | `/api/markets/{market}/guidance`, `/api/guidance` | Standing-guidance management |
| `Triggers/Http/SafetyTermsHttpTrigger.cs` | HTTP PUT/DELETE | `/api/safety/terms` | Safety-term management |
| `Triggers/Http/StyleRulesHttpTrigger.cs` | HTTP CRUD | `/api/style-rules{/versions/{v}/restore,/preview}` | Editorial style-rules management |
| `Triggers/Http/RolesSourceHttpTrigger.cs` | HTTP POST | `/api/GetRoles` | SWA rolesSource bridge |
| `Triggers/Http/ConfigTransferHttpTrigger.cs` | HTTP CRUD | `/api/config/{export,schema,import,imports/pending}` | Config export/import and approval |
| `Triggers/Http/MapConfigHttpTrigger.cs` | HTTP GET | `/api/map-config` | Serves browser-safe HERE Maps key |
| `Triggers/Queues/BroadcastQueueTrigger.cs` | Queue | `broadcast` | Asynchronous broadcast execution |
| `Triggers/Queues/RegenerationQueueTrigger.cs` | Queue | `regeneration` | Regeneration execution |
| `Triggers/Queues/BroadcastPoisonQueueTrigger.cs` | Queue | `broadcast-poison` | Broadcast poison handling |
| `Triggers/Queues/RegenerationPoisonQueueTrigger.cs` | Queue | `regeneration-poison` | Regeneration poison handling |
| `Triggers/Queues/PoisonQueueHandler.cs` | Static | — | Shared poison-message handling |
| `Triggers/Queues/QueueFailurePolicy.cs` | Static | — | Queue failure classification |
| `Triggers/Timers/SchedulePollerTrigger.cs` | Timer | periodic | Poll schedules and enqueue broadcasts |
| `Triggers/Timers/AuditExportTimerTrigger.cs` | Timer | daily | Export audit logs |
| `Triggers/Timers/QueueDepthMetricsTrigger.cs` | Timer | every 5 min | Sample queue depths |
| `Middleware/ActivityLoggingMiddleware.cs` | Middleware | all | Logs every authenticated, successful, mutating admin/operator request |
| `Middleware/AuthorizationMiddleware.cs` | Middleware | all | AAD auth (Easy Auth + SWA principal) + Operator/Admin RBAC |
| `Middleware/GlobalExceptionHandlingMiddleware.cs` | Middleware | all | Structured error responses (HTTP context); re-throws non-HTTP exceptions for host retry/poison handling |
| `Middleware/ActivityPathResolver.cs` | Static | — | Maps a request path to an audit (resource, entityId) pair |
| `Metrics/ApplicationInsightsBroadcastMetrics.cs` | Metrics | — | AppInsights-backed `IBroadcastMetrics` implementation |
| `Metrics/E2EFakeBroadcastMetrics.cs` | Metrics | — | Fake `[E2E-Metric]`-logging `IBroadcastMetrics` sink for deterministic E2E testing |
| `Program.cs` | Entry | N/A | Host builder, DI configuration |
| `Triggers/Queues/QueueFailurePolicy.cs` | Static | Functions-boundary classify-or-throw for queue orchestration failures — content-limited/content-safety/terminal/TTS → acknowledge; unknown → throw for host retry → poison |
| `Auth/MarketAccessGuard.cs` | Static | Extension `EnsureMarketAccessAsync` — 403s an operator not assigned to the given marketId; consolidates previously-duplicated per-trigger checks |
| `Auth/ClientPrincipal.cs` | Static | — | Parses the `X-MS-CLIENT-PRINCIPAL` Easy Auth/SWA header |
| `Http/HttpResponseHelper.cs` | Static | — | Builds structured HTTP responses; `MapFailureAsync` centralises not-found/conflict `Result` error mapping |

**Dependencies**:
- `ArnBulletinGenerator.Application`
- `ArnBulletinGenerator.Infrastructure`
- `ArnBulletinGenerator.Domain`
- NuGet: `Microsoft.Azure.Functions.Worker`, `Microsoft.Azure.Functions.Worker.Extensions.*`

**Usage Example**:
```csharp
// Functions host entry point (Program.cs)
host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(workerApp => { ... })
    .ConfigureServices((context, services) =>
    {
        services.AddApplication();
        services.AddInfrastructure(context.Configuration);
    })
    .Build();
await host.RunAsync();
```

---

### ArnBulletinGenerator.Workflow

**Purpose**: Clean-room, substitutable bulletin-generation workflow implementing EVIDENCE → MEANING → SELECTION → DISCOURSE → WORDS → VERIFICATION. Its governing rule is that the fitter fits facts rather than finished prose. Pure core with a Domain-only dependency.

**Location**: `src/ArnBulletinGenerator.Workflow/`

- `Contracts/*.cs` — 3 files: the `IBulletinWorkflow` seam, the `IWorkflowLlm` / `IDurationOracle` ports, and the station policy carried into the workflow
- `Model/*.cs` — 5 files: event-shaped evidence, prose-free plan, story briefs, composed read, and the deterministic check result
- `Stages/*.cs` — 4 pure-code files (Normalizer / WordEstimator / Fitter / EventRank) plus `Stages/Conversational/*.cs` — 5 files: the writer, composer and editor desks, the brief division, and the deterministic `ConversationalChecks` floor
- `Trace/*.cs` — 3 files: trace model, recorder, and the self-contained HTML renderer
- `Prompts/*.md` and `Schemas/*.json` — 4 prompt files and 3 schema files: one prompt per LLM role, and a strict response schema for each role that returns JSON (the composer returns plain prose, so it has no schema by design)
- The two root files `ConversationalBulletinWorkflow.cs` and `WordRateDurationOracle.cs`

There is ONE workflow, `conversational-v1`. The `staged-v1` alternative and the id-based `WorkflowSelector` were removed on 2026-08-30: the id was hardcoded, so nothing could select the other pipeline and its thirteen blocking `HardChecks` read as live guards while never running. Four LLM roles — `writeFlow`, `writeIncidents`, `compose2`, `edit` — replace the original six agents. Every run emits `workflow-traces/<RunId>.html` plus an `index.html`.

---

### ArnBulletinGenerator.Workflow.Hosting

**Purpose**: Binds the workflow's ports to real infrastructure.

**Location**: `src/ArnBulletinGenerator.Workflow.Hosting/`

- `AzureOpenAiWorkflowLlm.cs` — Sends each role's instructions explicitly on every call rather than relying on server-side Foundry agent instructions, so a captured trace holds the complete model input
- `WorkflowServiceCollectionExtensions.cs` — Registers the workflow hosting services

`AzureOpenAiWorkflowLlm` sends each role's instructions explicitly on every call rather than relying on server-side Foundry agent instructions, so a captured trace holds the complete model input. Under the agent route the exporter could only store a placeholder saying the instructions lived server-side, which is why a bad read could not be explained after the fact — and worse, the agent route discards the rendered `systemPrompt` entirely, so per-station playbook overlays were silently inert while the trace claimed they had applied (measured on dev 2026-08-29). Dev and prod therefore use the raw model route; the four `arn-wf-*` agents survive only because the test environment still names them.

---

## Frontend Module

### ArnBulletinGenerator Web

**Purpose**: Operator-facing SPA for monitoring bulletin jobs, reviewing generated traffic content, managing schedules, and triggering manual bulletin generation.

**Location**: `web/`

**Routes** (`src/lib/routes.ts`):

| Route | Page | Primary modules |
|---|---|---|
| `/` | `HomePage` | dashboard / landing |
| `/onair` | `OnAirPage` | `features/onair` (workstation queue + review), `features/jobs` review cockpit |
| `/stations` | `StationsPage` | `features/stations` (cards, trigger Studio), `features/schedules` (Studio) |
| `/voices` | `VoicesPage` | `features/voices` (workshop, versions) |
| `/operators` | `OperatorsPage` | `features/operators` (assignments, admin) |
| `/history` | `HistoryPage` | `features/jobs` broadcast log |
| `/activity` | `ActivityPage` | `features/activity` activity/change audit log (admin) |
| `*` | `NotFoundPage` | 404 fallback |

#### Feature Modules

| Module | Purpose |
|---|---|
| `features/activity/` | Activity/change audit log: global admin feed + per-station history tab (reuses entity-diff) |
| `features/jobs/` | Bulletin lifecycle: review cockpit (`components/review/`), Adjust board (`review/adjust/`), broadcast log (`review/history/`), deep-dive tabs (`review/canvas/`), provenance/claims/word-timings |
| `features/schedules/` | Schedule Studio + `sections/`, voice-profile + LLM-model selectors, cron utilities |
| `features/stations/` | Station profile cards, trigger Studio, coverage preview, voice picker |
| `features/voices/` | Voice workshop/studio, version history, calibration display, model capabilities |
| `features/operators/` | Operator market/station assignment cards + dialog + hooks |
| `features/onair/` | Live workstation (`workstation/`: queue beam, pipeline stepper, review pane), coverage board (`board/`), slot logic |
| `features/notifications/` | Waiting/failed-job detection, panel, audible alarm |

#### Shared

| Module | Key exports |
|---|---|
| `src/components/studio/` | Shared Studio shell (`Studio`, `StudioSubmitButton`) |
| `src/components/navigation/` | `AppSidebar`, `UserMenu`, `MobileNav` |
| `src/components/map-picker/` | Leaflet coverage editor (`MapPicker`) + shape editors + geocoding |
| `src/components/ui/aceternity/` | Motion primitives (aurora, spotlight, moving-border) |
| `src/lib/design/` | `BudgetBar`, `FreshnessDot`, `SourceGlyphs`, icon registry |
| `src/lib/api/` | `apiClient`, query factory, `paths`, mock adapter, shared schemas |
| `src/hooks/` | `useCurrentUser`, `useGlobalKeydown`, `useDebounce`, `useSecondTick` |

> Per-file frontend detail lives in `web/docs/CODEMAPS/` (single source of truth for the web app).

**Frontend Dependency Flow**:
```text
App.tsx → pages/* → feature components/hooks → feature api hooks → apiClient
useCurrentUser/useIsAdmin → auth.ts/useAuthData → shared TanStack Query auth cache
apiClient → auth.ts token parsing → backend API or VITE_USE_MOCKS mock adapter
API responses → Zod parse + camelizeKeys() → TanStack Query cache → UI
API errors → ApiError(status) → QueryClient skips retry for 4xx
Mutations → useMutationFactory() → optional optimistic update → apiClient → invalidation/rollback
Mutation failures → feature-level toastAction/domain handlers, not global generic mutation toasts
```

**Dependencies**:
- Runtime: `react`, `react-router-dom`, `@tanstack/react-query`, `@tanstack/react-table`, `axios`, `lucide-react`, `sonner`, `date-fns`, `cronstrue`, `leaflet`, `react-leaflet`, `react-hook-form`, `zod`, `cmdk`, `next-themes`, `@dnd-kit/*`
- UI: `@radix-ui/*`, `tailwindcss`, `class-variance-authority`, `tailwind-merge`
- Dev/test: `vite`, `vitest`, `typescript`, `eslint`, `prettier`, `puppeteer`, `pixelmatch`

**Exports**: No package-level library export; modules are consumed internally through the `@/*` alias and feature/component barrels.

**Usage Example**:
```bash
cd web && bun run dev:mock  # Frontend only with in-memory API
cd web && bun run build     # Typecheck + production build
```

---

## Deployment / IaC Module

### Azure Bicep Templates

**Purpose**: Infrastructure-as-Code for Azure resource provisioning across dev/test/prod environments.

**Location**: `deployment/`

**Key Files**:

| File | Purpose |
|---|---|
| `main.bicep` | Root orchestrator — wires all modules together |
| `main.dev.bicepparam` | Dev environment parameter values |
| `main.test.bicepparam` | Test environment parameter values |
| `main.prod.bicepparam` | Production environment parameter values |

#### Modules

| Module | File | Purpose |
|---|---|---|
| Storage | `modules/storage.bicep` | Blob/Table/Queue storage account |
| Monitoring | `modules/monitoring.bicep` | Log Analytics + App Insights |
| Key Vault | `modules/key-vault.bicep` | Secrets management |
| AI Services | `modules/ai-services.bicep` | Azure OpenAI + AI Search |
| Function App | `modules/function-app.bicep` | Azure Functions (consumption plan) |
| Static Web App | `modules/static-web-app.bicep` | React SPA hosting |
| Action Group | `modules/action-group.bicep` | Alert notification routing |
| Alerts | `modules/alerts.bicep` | Metric/log alert rules |
| Workbooks | `modules/workbooks.bicep` | Ops dashboards |
| Private Endpoints | `modules/private-endpoints.bicep` | VNet integration |
| Role Assignments | `modules/role-assignments.bicep` | RBAC |
| Linked Backend | `modules/linked-backend.bicep` | SWA ↔ Functions proxy binding |

#### Scripts

| File | Purpose |
|---|---|
| `scripts/deploy.sh` | Full deployment orchestration |
| `scripts/smoke-test.sh` | Post-deployment health checks |
| `scripts/setup-autoscale.sh` | Autoscale configuration |
| `scripts/provision-foundry-agents.sh` | Foundry agent provisioning |

**Exports**: Azure resource group and resource outputs (endpoints, connection strings, IDs)

**Usage Example**:
```bash
./deployment/scripts/deploy.sh -e dev
```

---

## Documentation Module

**Location**: `docs/`

**Structure**: See `docs/_manifest.yaml` for full index.

| Subdirectory | Purpose |
|---|---|
| `canonical/` | Stable reference: architecture, decisions, risks, brief |
| `reference/` | Source context, inventory, backlog, stakeholders |
| `memory/` | Weekly project memory entries |
| `working/` | Active working documents, sprint prompts |
| `generated/` | Auto-generated views (actions, decisions, risks, events) |
| `operations/` | SLOs, DR plan, local dev guide |
| `CODEMAPS/` | This file set — structural maps |

**Usage Example**:
```bash
./scripts/docs.py generate   # Regenerate manifest, search index, generated views
./scripts/docs.py check      # Validate all doc links and frontmatter
```
