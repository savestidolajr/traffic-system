---
title: "Codemap — Files"
description: "Comprehensive directory structure and file-purpose reference for the entire Arn codebase: backend C# projects, frontend React app, Bicep IaC, test projects, documentation, and scripts"
purpose: "reference"
status: "current"
last_verified: 2026-09-03
review_after: 2026-09-26
volatility: "low"
verified_by:
  - "manual filesystem scan"
  - "./scripts/docs.py check"
  - "./scripts/docs.py codemap 2026-08-12 reported the generated tables already current against git ls-files"
source_of_truth: "Filesystem scan of the Arn repository root"
tags:
  - "codemap"
  - "files"
  - "directory structure"
  - "reference"
aliases:
  - "file codemap"
  - "file map"
  - "directory listing"
  - "file purposes"
common_queries:
  - "What does this file do?"
  - "What files are in a given directory?"
  - "Where is something defined in the codebase?"
  - "What is the directory structure of the project?"
related_code:
  - "docs/CODEMAPS/ARCHITECTURE.md"
  - "docs/CODEMAPS/MODULES.md"
see_also:
  - "docs/CODEMAPS/ARCHITECTURE.md"
  - "docs/CODEMAPS/MODULES.md"
---
# File Map

**Last Updated**: 2026-09-03
**Scope**: Directory structure and file purposes for the entire Arn codebase.

---

## Repository Root

| File | Purpose |
|---|---|
| `ArnBulletinGenerator.slnx` | .NET solution file (4 source projects + 7 test projects) |
| `Directory.Build.props` | Shared MSBuild properties (nullable, implicit usings, preview features) |
| `package.json` | Root orchestrator scripts (azurite, local backend, local frontend) |
| `appsettings.json.example` | Shared app settings template |
| `AGENTS.md` | Root agent context for AI coding assistants |
| `CONTRIBUTING.md` | Contribution guidelines |
| `README.md` | Project README |
| `llms.txt` | LLM-friendly project index |
| `prompts/` | AI prompt templates (system.md, draft.md, review.md, refine.md, safety.md, attribute.md, director.md, delivery-writer.md) |
| `.gitignore` | Git ignore rules |
| `.editorconfig` | Editor formatting rules |
| `.github/copilot-instructions.md` | GitHub Copilot instructions |
| `azure-pipelines.yml` | Azure DevOps CI/CD pipeline (Dev → Prod promotion; test is manual via deploy.sh) |
| `.gitleaks.toml` | gitleaks secret-scan config + allowlist (CI SecretScan job) |

---

## `src/ArnBulletinGenerator.Domain/`

<!-- codemap:begin Domain -->
### `Configuration/`
| File | Purpose |
|---|---|
| `BroadcastOptions.cs` | Configuration options for the broadcast orchestration process. |
| `CorridorSelectionOptions.cs` | In-code defaults for corridor selection scoring, diversity, and thresholds. Values are |
| `DeliveryOptions.cs` | Global delivery-direction settings. The script director adds ElevenLabs v3 audio tags to |
| `DeliveryQueueOptions.cs` | Operational controls for the asynchronous playout delivery queue. |
| `DurationPredictionOptions.cs` | Selects which IDurationPredictor strategy is active at runtime. |
| `IncidentStateOptions.cs` | Configuration options for the incident state service. |
| `InteractiveWorkOptions.cs` | Bounds for interactive work. These defaults keep operator previews independent from the |
| `JobPersistenceOptions.cs` | Feature flags for the additive job persistence read models. Every flag defaults to OFF: a |
| `JobRetentionOptions.cs` | Safety-first retention policy. Purging is disabled unless is |
| `JobWorkIndexOptions.cs` | Controls the additive job work index read/write path. |
| `LedgerReadModelOptions.cs` | Controls the incremental ledger bucket and predecessor read models. Both default to OFF for |
| `MarketAccessOptions.cs` | Controls the short-lived market-scope cache used by operator authorization. |
| `ModelSelectionOptions.cs` | Configuration for ElevenLabs model selection per use case. |
| `MonitoredRoutesOptions.cs` | Per-market commuter routes whose current-vs-typical drive time should be available to the script. |
| `OperatorOptions.cs` | Controls the bounded operator-directory display cache and verified sign-in registration. |
| `ScheduleDueIndexOptions.cs` | Controls the additive schedule due-index read/write path. |
| `SpeechDurationOptions.cs` | Configuration for audio pipeline duration tolerances and thresholds. |
| `SpokenDurationModelOptions.cs` | Controls the calibrated spoken-duration model. The switch exists so an environment can |
### `Constants/`
| File | Purpose |
|---|---|
| `CalibrationDefaults.cs` | Fallback syllables-per-word ratio used to estimate spoken duration when a voice has no calibration data yet. |
| `ProviderNames.cs` | Well-known TTS provider names used across the application. |
| `QueueNames.cs` | The queue names, and the ONLY source of them. Consumed by the QueueTrigger |
### `Contracts/`
| File | Purpose |
|---|---|
| `ActivityContracts.cs` | Wire DTO for one recorded change/access-audit entry. |
| `AudioContracts.cs` | Synthesized audio metadata with format and duration information. |
| `EditorialSettingsContracts.cs` | Wire DTO for storing the numeric editorial settings at one scope. |
| `GuidanceContracts.cs` | Wire DTO for creating or editing a standing content-guidance directive. |
| `JobContracts.cs` | ============================================================================= |
| `JobProgressContracts.cs` | Stages exposed by the live generation progress document. |
| `LedgerContracts.cs` | ============================================================================================= |
| `LessonSummary.cs` | What the lesson summariser decided one published operator edit means for the |
| `OperatorContracts.cs` | ============================================================================= |
| `OrchestrationContracts.cs` | Request to trigger a broadcast orchestration. |
| `SafetyContracts.cs` | Wire DTO for creating/replacing a safety term (banned word or allowed exception). |
| `ScheduleContracts.cs` | Schedule summary for list and detail views. |
| `ScriptContracts.cs` | A single sentence of the bulletin script with its originating corridor keys, the monitored |
| `TrafficContracts.cs` | Traffic incident data from the HERE API or mock provider. |
### `Contracts/Mapping/`
| File | Purpose |
|---|---|
| `InputMappingExtensions.cs` | Input mapping: DTO → Domain conversions used by Domain validation. |
### `Contracts/Messages/`
| File | Purpose |
|---|---|
| `ActivityAuditMessage.cs` | Durable activity audit payload published after a successful mutating HTTP response. |
| `BroadcastQueueMessage.cs` | Processing phase for a broadcast queue message. The default combined phase preserves |
| `DeliveryQueueCommand.cs` | Durable command for the single delivery owner. The reviewed ETag and deterministic idempotency |
| `LessonLearningQueueMessage.cs` | One published operator script edit awaiting station-lesson learning. Enqueued by |
| `RegenerationQueueMessage.cs` | Message queued to trigger asynchronous regeneration orchestration. |
### `Errors/`
| File | Purpose |
|---|---|
| `ErrorMessages.cs` | Centralized error messages for the ArnBulletinGenerator domain. |
| `OperationalFailure.cs` | Classifies the operational disposition of a typed failure so queue boundaries |
| `ResultFailure.cs` | Lifts a failed into a typed , |
| `SuccessMessages.cs` | Centralized success messages for the ArnBulletinGenerator domain. |
### `Interfaces/`
| File | Purpose |
|---|---|
| `IAudioPostSynthesisDurationAdjustmentStrategy.cs` | Input to a post-synthesis duration adjustment attempt: the script, the rendered audio, and how far its actual duration is from the target. |
| `IContentSafetyFilter.cs` | Screens a generated script for content that must not air (e.g. legally sensitive or unverified allegations) before it reaches synthesis. |
| `IEmergencyIncidentFeed.cs` | An emergency-services incident feed for a market's coverage area — fires, floods, storms, |
| `IFileDeliveryService.cs` | Outcome of a playout-delivery attempt — distinguishes a real delivery from a |
| `IOfficialTrafficFeed.cs` | An authoritative road-authority feed used to corroborate HERE's picture of a coverage area. |
| `IOperatorDirectory.cs` | Reads the current Entra principals holding the application's Operator or Admin roles. |
| `IScriptSafetyReviewer.cs` | LLM-based safety review of generated script content prior to human review |
| `IShippedPlaybookProvider.cs` | Loads the shipped, block-marked default for a playbook role. |
| `IVoiceRemixer.cs` | A single remix preview: the playable audio of the remixed voice reading the sample |
### `Interfaces/Repositories/`
| File | Purpose |
|---|---|
| `IActivityLogRepository.cs` | Cross-cutting change/access audit trail — who mutated what admin/operator config, |
| `IBookendRepository.cs` | Persistence for the shared bookend library — named, versioned intro/outro pairs stations |
| `IBulletinPlaybookRepository.cs` | Persistence for the shared playbook library — named, versioned sets of station edits to a |
| `IDeliveryStyleRepository.cs` | Persistence for the shared delivery-style library — named, versioned delivery tones |
| `IEditorialSettingsRepository.cs` | Store for ARN's numeric editorial settings. One row per (Scope, ScopeId); resolution across |
| `IEventVerdictRepository.cs` | Persists operator verdicts on ledger events (the only authored state the Event Ledger |
| `IFillerAudioCacheRepository.cs` | Table-storage metadata for cached filler audio clips. Blob I/O (the actual audio bytes) |
| `IFillerStatementsRepository.cs` | Per-market filler statements: short admin-authored fill phrases used when incident |
| `IFutureLessonRepository.cs` | Explicit, station-scoped editorial lessons — taught by operators or inferred from published |
| `IGuidanceRepository.cs` | Persistence for standing-guidance directives (Layer 2 of the self-service guidance |
| `IIncidentPriorityRepository.cs` | Per-market incident priority rules and dynamic real-time incident landscape occurrences. |
| `IIncidentStateRepository.cs` | Persists per-market incident state across bulletins so a later fetch can detect new, updated, and cleared incidents instead of treating… |
| `IJobAudioArtifactRepository.cs` | Deletes only job-owned audio artifacts during a retention sweep. |
| `IJobClaimRepository.cs` | Atomically claims a queued job so duplicate deliveries cannot run concurrently. |
| `IJobPayloadRepository.cs` | Stores immutable compressed payloads that do not belong on the hot job row. |
| `IJobProgressRepository.cs` | Persists and reads the bounded structured progress document for one job. |
| `IJobReadIndexRepository.cs` | Maintains the bounded scalar index for list/history reads. |
| `IJobRepository.cs` | Persists and queries bulletin Job aggregates, including the lookups the fallback and dedupe workflows use to find a market's prior or… |
| `IJobRetentionStore.cs` | Bounded candidate/recheck/delete seam used by the retention policy service. |
| `IJobStatusRepository.cs` | Applies a lifecycle status transition with a sparse ETag-guarded table merge. |
| `IJobTimelineRepository.cs` | Appends and reads the split job lifecycle/log history rows. |
| `IJobWatchdogClaimRepository.cs` | Atomically claims a job before a watchdog sweep performs non-idempotent work. |
| `IJobWorkIndexRepository.cs` | Persists the bounded scalar work projection consumed by watchdog sweeps. |
| `ILedgerBucketRepository.cs` | Maintains and reads incremental market/day ledger buckets. |
| `IMapAttributesCacheRepository.cs` | Persistent repository for caching static HERE segment road attributes (road name, functional class, |
| `IOperatorAssignmentRepository.cs` | Operator → market assignments (Workshop 2 / SDD §4.2): a non-empty market list |
| `IPriorityRoadsRepository.cs` | Per-market priority-roads list (Workshop 2 / SDD §4.2): arterials that matter to |
| `IPronunciationDictionaryRepository.cs` | Persistence for the shared pronunciation-dictionary library — named, versioned ElevenLabs |
| `ISafetyTermIdentityRepository.cs` | Point identity index for normalized, scoped safety terms. |
| `ISafetyTermRepository.cs` | Persistence for content-safety terms (banned words + allowed exceptions) used by the |
| `IScheduleRepository.cs` | Persists per-market bulletin schedules and provides the atomic due-schedule claim (TryStartJobAsync/CompleteJobAsync) the poller uses to… |
| `ISourceParseReportRepository.cs` | Persists one source's parse coverage per fetch so deterministic parser improvements can be |
| `ISpokenDurationSampleRepository.cs` | Append-only store of measured spoken-duration samples, captured at synthesis time. |
| `ISynthesisHistoryRepository.cs` | Persistent store for historical synthesis outcomes. |
| `IVerifiedPronunciationRepository.cs` | Per-market list of script words an operator has confirmed air correctly despite the |
| `IVoiceCalibrationRepository.cs` | Persists per-voice, per-settings speech-rate calibration so duration prediction does not need to recalibrate on every synthesis. |
| `IVoiceProfileRepository.cs` | Persistence for the shared voice library — named, versioned tunings of a base voice. |
### `Interfaces/Services/`
| File | Purpose |
|---|---|
| `IAudioPipelineService.cs` | Drives script-to-audio synthesis toward a target slot duration, then fine-tunes, trims, and stitches the result into the final broadcast… |
| `IAudioProcessor.cs` | Provides low-level audio processing operations such as duration measurement, |
| `IAudioSynthesizer.cs` | Renders script text to speech via a TTS provider, honouring voice, model, and pronunciation-dictionary overrides for a given market. |
| `IAudioTranscriber.cs` | Transcribes finished bulletin audio back to text for the pre-approval audio review |
| `IAudioUrlProvider.cs` | Resolves playable URLs and byte-range streams for broadcast audio stored in our own blob container, distinguishing it from external… |
| `IAuditExporter.cs` | Snapshots the job store to a durable dated compliance export, independent of the operator-facing storage backend. |
| `IBroadcastOutput.cs` | Stores finished broadcast audio to its delivery destination and marks it published once it has aired. |
| `IConfigImportStore.cs` | Persistence for pending configuration imports awaiting a second approver (the prod |
| `ICronParser.cs` | Abstracts cron expression parsing and scheduling calculations. |
| `IDeliveryWriter.cs` | Rewrites an approved bulletin into a register an operator described in one plain sentence |
| `IDetourRoutingProvider.cs` | Finds an alternate route between two points that avoids named congested or closed road segments, for detour advice in a bulletin. |
| `IDurationEstimator.cs` | Estimates and measures audio duration. |
| `IDurationPredictor.cs` | Predicts spoken duration of text and computes syllable targets |
| `IFillerAudioCacheService.cs` | Resolves filler audio for a bulletin: cache lookup, synthesize-and-store on a miss, and |
| `IFuelPriceProvider.cs` | Fuel price summaries for a coverage area. When the supplied area carries a per-market |
| `IGenerationProgressReporter.cs` | Best-effort observer for structured, append-only generation progress. Implementations must |
| `IGeocodingProvider.cs` | Converts between a market's free-text coverage query and geographic coordinates, and resolves coordinates back to a readable place name. |
| `IInteractiveWorkGate.cs` | Boundary for synchronous LLM and TTS admission. Implementations provide independent pools, |
| `ILessonSummarizer.cs` | Isolated single-call LLM that generalises one published operator script edit into a |
| `ILlmCallCapture.cs` | Captures one physical LLM attempt. Operational metadata is always recorded; the full |
| `IMapAttributesProvider.cs` | Resolves what the map says about specific road segments — the road identity that belongs to an |
| `IModelSelector.cs` | Selects the ElevenLabs TTS model a voice should render on, honouring per-use-case (broadcast vs quality) and language overrides, and the… |
| `INotificationService.cs` | Sends operational notifications to external channels. |
| `IPreviewAudioCleanup.cs` | Protects generated preview blobs while their returned URLs may still be active and performs a |
| `IPronunciationDictionaryProvider.cs` | Manages pronunciation dictionaries in the TTS provider (ElevenLabs). Distinct from |
| `IQueuePublisher.cs` | Interface for publishing messages to Azure Storage Queues. |
| `IRoutingProvider.cs` | Fetches live-vs-typical drive time and distance between two points, backing monitored-route headlines and detour comparisons. |
| `IScheduleOccurrenceResolver.cs` | Resolves a schedule's next UTC occurrence, including optional calendar overlays. |
| `IScriptDirector.cs` | Adds ElevenLabs v3 inline audio tags to an already-approved plain script to steer HOW it |
| `IScriptGenerator.cs` | Turns fetched traffic/weather/fuel context into a spoken bulletin script, sized to the voice's speaking rate and slot duration. |
| `IScriptRefiner.cs` | The sentence-scoped refiner — the pipeline's only LLM repair (2026-08-21). Whole-script |
| `IScriptReviewer.cs` | Reviews a generated script against broadcast quality criteria. |
| `IScriptRewriter.cs` | Rewrites an approved bulletin in place to follow a free-form operator instruction ("drop the |
| `ISpeechDurationEstimator.cs` | Estimates spoken duration of text. |
| `ISpokenDurationModel.cs` | Predicts natural spoken duration from a calibrated voice and the punctuation-aware shape |
| `ITrafficProvider.cs` | Fetches live incidents, congestion corridors, and free-flowing roads for a coverage area from the traffic data source (HERE). |
| `IVoiceCalibrationService.cs` | Measures and maintains a voice's real speaking rate so duration prediction and slot fitting can be based on actual output rather than a… |
| `IVoiceCatalogCacheInvalidator.cs` | Invalidates the process-wide cached provider voice catalogue after voice mutations. |
| `IVoiceProvider.cs` | Lists the voices available from the TTS provider for assignment to a station's voice profile. |
| `IWeatherProvider.cs` | Current-conditions weather for a coverage area. When the supplied area carries a per-market |
### `Models/`
| File | Purpose |
|---|---|
| `ActivityLogEntry.cs` | One recorded change: an authenticated, successful (2xx), mutating (POST/PUT/DELETE) |
| `ActivityLogQuery.cs` | Filter/paging parameters for querying the activity log. is an |
| `AlertSeverity.cs` | Severity of an operator-facing pipeline alert notification (e.g. Teams webhook), controlling icon and log level. |
| `AttemptResult.cs` | Records the outcome of one script/synthesis attempt on a Job, including the speaking rate tried and whether it hit the target duration. |
| `AudioPreReviewOutcome.cs` | The persisted outcome of the pre-approval audio review (see |
| `AudioStreamHandle.cs` | Inclusive byte range requested for a partial audio read (HTTP Range semantics). |
| `AudioSynthesisResult.cs` | Note: byte[] Audio uses reference equality (default for arrays). |
| `Bookend.cs` | The versioned value of a bookend: the opening and closing lines ARN authored, as ONE unit. |
| `BookendRotation.cs` | How a station chooses which intro (or outro) to read for each broadcast. |
| `BroadcastContext.cs` | Base for all broadcast context types. Common properties hoisted here to eliminate duplication. |
| `BroadcastResult.cs` | Debug information from the broadcast generation process. |
| `BulletinContent.cs` | Which content sources a bulletin draws from (Workshop 1: traffic + weather + fuel in |
| `BulletinPlaybook.cs` | A named, versioned set of station edits to the instructions a bulletin role is given — |
| `CalibrationMetrics.cs` | A voice's measured or default speaking-rate metrics, used to estimate how long a given script will take to speak. |
| `DeliveryStyle.cs` | A delivery policy: the plain-language tone plus the curated ElevenLabs v3 audio-tag |
| `DetourSuggestion.cs` | An alternate route around a blocked or congested segment, with how much time it saves versus staying on the affected road. |
| `DirectoryPrincipal.cs` | A user principal that holds one or more application roles in the Entra tenant. |
| `DriveTimeResult.cs` | Live versus typical drive time and distance between two points, from which the current delay is derived. |
| `EditorialSettings.cs` | Where a settings overlay applies. Same ladder as |
| `EmergencyEvent.cs` | Australian Warning System tier, as published by the state emergency services and normalised |
| `EvidenceSources.cs` | Canonical provenance identifiers for evidence records. Every freshly-fetched record carries |
| `EvidenceUnit.cs` | One atomic, citable fact a Draft sentence can reference by . Built |
| `FallbackReasons.cs` | Stable cause tokens recorded on a job when a standby/fallback bulletin is applied, so the |
| `FillOrderJsonConverter.cs` | Persists a fill-source order as the enum NAMES rather than ordinals so a stored |
| `FillerAudioClip.cs` | Cached-clip metadata for one (filler statement × voice × voice settings × TTS model) |
| `FillerAvailability.cs` | The optional station-local eligibility window of a filler statement: a calendar-date |
| `FillerStatement.cs` | A per-market, admin-authored filler statement used to fill remaining slot time when |
| `FlowRoad.cs` | A road the live HERE flow feed reports as currently flowing freely (low jam factor) — the |
| `FutureLessonEntry.cs` | Categories of explicit, reusable editorial instruction that may be replayed later. |
| `GeneratorInputSnapshot.cs` | A complete, round-trippable snapshot of the writer-relevant input that produced a bulletin |
| `GeocodedPlace.cs` | What a reverse geocode actually resolved at a coordinate, with the ROAD and the SUBURB kept |
| `GuidanceEntry.cs` | Geographic scope of a standing-guidance directive. The numeric order encodes |
| `IncidentChangeSummary.cs` | Aggregate counts of new, updated, and cleared incidents since the last bulletin, persisted on Job for continuity narration. |
| `IncidentDeltaContext.cs` | One incident's identity and detail as carried into a new/updated/cleared/unchanged delta bucket for the script generator. |
| `IncidentExclusions.cs` | The records an operator has ruled out of a bulletin, in the one place the fetch can honour |
| `IncidentFact.cs` | What a record's condition applies to. A ramp closure is not a road closure. |
| `IncidentPriorityRule.cs` | Per-market incident class priority rule configured by operators. |
| `IncidentState.cs` | Whether a tracked incident is still active or has been marked cleared for change detection. |
| `InteractiveWork.cs` | The independent admission pools used by interactive work. |
| `Job.cs` | Lifecycle status of the bulletin Job aggregate root, which tracks script, audio, approval, and |
| `JobClaim.cs` | A lease on a queued job. The ETag is the storage concurrency token and is refreshed by |
| `JobCorridor.cs` | A ranked congestion corridor (grouped road segment) captured on a Job, recording its rank, impact score, and whether it was selected for… |
| `JobFlowRoad.cs` | A free-flowing road captured on a Job from HERE's ranked pool, recording whether it was weighted for the station and whether it aired as a… |
| `JobIncident.cs` | A traffic incident captured on a Job for review evidence, carrying the authority's own direction, lane, and closure detail alongside the… |
| `JobIndex.cs` | Canonical point-read routing information for a job. The row is maintained alongside the |
| `JobListQuery.cs` | Filter/paging parameters for browsing a bounded newest-first job window, mirroring |
| `JobLog.cs` | A single timestamped diagnostic entry appended to a Job's pipeline log for operator troubleshooting. |
| `JobLogLevel.cs` | Log severity levels for job event logging. |
| `JobPayload.cs` | Named payload families that may be moved off the hot aggregate row. |
| `JobProgressDocument.cs` | In-memory, append-only progress state for one generation job. A single reporter owns the |
| `JobReadIndex.cs` | Scalar row used for bounded newest-first job reads. |
| `JobRetention.cs` | Scalar row used by the bounded retention sweep before a full recheck. |
| `JobScriptSentence.cs` | One sentence of a bulletin script, tagged with the corridor keys and monitored drive-time |
| `JobStatusUpdate.cs` | Result of an ETag-guarded targeted status merge. |
| `JobTimeline.cs` | One append-only lifecycle or operator-log event for a job. |
| `JobVoiceConfig.cs` | Snapshot of the voice, model, and settings actually used to synthesize a Job's audio, for provenance and reproducibility. |
| `JobWorkItem.cs` | The bounded classes of due work consumed by watchdog sweeps. |
| `LedgerBucket.cs` | Scalar reference stored in an incremental market/day ledger bucket. |
| `LedgerModels.cs` | Which triage lane a ledger event currently sits in. |
| `LlmCallMetadata.cs` | Always-on facts for one physical LLM attempt. Full prompt and response payloads are optional |
| `LlmCallRecord.cs` | One captured LLM call — the exact rendered prompt sent and the exact raw response |
| `LlmTokenUsage.cs` | Token accounting for one LLM call, lifted out of the Responses API usage object. |
| `MarketSchedule.cs` | The geometry kind a coverage area uses for traffic retrieval. |
| `ModelSelectionUseCase.cs` | Distinguishes the low-latency broadcast synthesis path from the higher-fidelity quality path when IModelSelector picks a TTS model. |
| `MonitoredRouteStatus.cs` | Live-vs-typical drive time for a station's configured commuter route, backing the monitored-route headline claim in a bulletin script. |
| `NotificationKind.cs` | Operator-facing notification tiers (decided 2026-07-05 after the four-critic card |
| `OfficialTrafficEvent.cs` | One event from an authoritative road-authority feed (Live Traffic NSW, QLDTraffic, VicTraffic, etc.), |
| `OperatorAssignment.cs` | Markets an operator may act on. is the AAD object id (oid) |
| `Option.cs` | Non-generic factory methods for creating from nullable/whitespace values. |
| `PendingConfigImport.cs` | A configuration import awaiting a second approver. is the |
| `Playbook.cs` | What a station tells a role, as editable blocks rather than one file we own. |
| `PredictionContext.cs` | Contextual information used by IDurationPredictor to produce a prediction. |
| `PredictionResult.cs` | Result of a duration prediction. |
| `PreviewAudioCleanupReport.cs` | Outcome of one bounded preview artifact sweep. |
| `PriorityRoad.cs` | How strongly a flowing priority road competes for a bulletin slot. The bonus is ADDITIVE on |
| `PronunciationDictionary.cs` | Locates a specific version of an ElevenLabs pronunciation dictionary. Attached to a |
| `PronunciationRule.cs` | The kind of pronunciation rule, which decides on which TTS models it takes effect. |
| `RejectionEntry.cs` | Closed taxonomy for why an operator took a bulletin off the air path. |
| `ResolvedCartClip.cs` | The filler-cart clip resolved for one voice: the blob URL a fallback applies to the job, |
| `ResolvedFillerAudio.cs` | A resolved filler clip ready to stitch onto a bulletin's traffic audio — either freshly |
| `Result.cs` | Railway-oriented success/failure outcome used throughout the domain instead of exceptions for expected failure paths, carrying an error… |
| `ReviewIssue.cs` | Which part of the read a defect can be repaired in. |
| `RevoiceObservation.cs` | The operator's pending script edit awaiting publication evidence: the script as generated |
| `SafetyTerm.cs` | Whether a term tightens the deterministic content-safety gate () |
| `SafetyTermIdentity.cs` | Scalar pointer from a normalized safety-term identity to its canonical row. |
| `ScheduleCalendar.cs` | A reusable named cron pattern for a station's calendar. |
| `ScheduleReferenceSummary.cs` | The schedule fields needed for referential-integrity checks. Unlike |
| `ScriptReviewResult.cs` | Result of an LLM-based script review. Contains approval status, structured issues addressed to |
| `SegmentRoad.cs` | What the map says about one road segment, keyed to the segment an incident itself references. |
| `SegmentSpan.cs` | One HERE topology segment a record references, in HERE's FULL reference form (tile included) |
| `SourceFetchDiagnostic.cs` | What a bulletin actually got from one upstream data source. |
| `SourceParseContracts.cs` | Canonical field names used across every source's coverage report. |
| `SourceRecordIdentity.cs` | A source-native identifier for one observation of a traffic event. |
| `SpokenDurationCalibration.cs` | Describes how confidently a spoken-duration prediction was matched to calibration data. |
| `SpokenDurationSample.cs` | One measured (shape → natural spoken seconds) observation, captured at the moment of |
| `StatusHistoryEntry.cs` | One recorded transition in a Job's status timeline, capturing who made the change, when, and why. |
| `StorageKey.cs` | Validates user-supplied identifiers that flow into Azure Table Storage |
| `SupersededTake.cs` | The bulletin this job generated and then had replaced by a fallback. Written once, at |
| `SynthesisRecord.cs` | A single historical synthesis outcome. |
| `TimeWindowOverlap.cs` | Overlap detection for half-open station-local time windows that may wrap past midnight. |
| `TrafficCorridor.cs` | Represents a grouped collection of traffic incidents along a road corridor. |
| `TrafficIncident.cs` | A single fresh HERE traffic incident as fetched for a coverage area, before it is reduced to the leaner JobIncident review shape. |
| `VerifiedPronunciation.cs` | One script word an operator listened to and confirmed airs correctly, even though |
| `VersionedLibraryList.cs` | Query options shared by the versioned library list endpoints and bounded internal readers. |
| `Voice.cs` | Represents an available TTS voice. |
| `VoiceCalibration.cs` | Stores measured speech rate for a specific voice to enable accurate duration estimation. |
| `VoiceCalibrationLookup.cs` | Identifies one calibration read by the voice, complete settings, and TTS model that produced it. |
| `VoiceProfile.cs` | One immutable iteration of a voice profile: a settings snapshot + model, plus the |
| `VoiceRotation.cs` | How a station chooses its bulletin voice for each broadcast. |
| `VoiceSettings.cs` | Domain model for voice synthesis settings. |
| `WordTiming.cs` | Word-level timing within the final bulletin audio, used by the review UI to jump |
### `Properties/`
| File | Purpose |
|---|---|
| `AssemblyInfo.cs` | Grants the Domain test project access to internal members for direct unit testing. |
### `Services/`
| File | Purpose |
|---|---|
| `AudioPreReview.cs` | Deterministic script-vs-transcript comparison for the pre-approval audio review. |
| `AudioPreReviewReport.cs` | Builds the operator-facing pre-review message from the raw comparison. Pronunciation |
| `AuthorityPriorityNormalizer.cs` | Maps each authority's source-specific priority vocabulary to a common severity rank. |
| `BlueprintSelector.cs` | A bounded presentation blueprint for one read: it varies HOW the bulletin is organised — |
| `BookendRotationMapping.cs` | Converts between the wire-level bookend-rotation DTO (mode discriminated by a string, |
| `BookendTokenResolver.cs` | Substitutes {token} placeholders in a bookend template with per-station values, so |
| `BulletinBookendComposer.cs` | Assembles the final read from the station's bookend pair, the generated body, and any filler |
| `BulletinContentSelector.cs` | Unified rundown selection (2026-06-25): incident corridors and flowing "good-run" roads |
| `BulletinDurationFitter.cs` | One sentence of a bulletin script mapped onto the time axis of a synthesized |
| `BulletinEditingBand.cs` | The word-count band a verbatim edit must land in to fit a slot: the target the generator |
| `CandidateGrounding.cs` | The single blocking invariant for candidate grounding. This is deliberately judgement-free |
| `ClaimGate.cs` | One gate decision, with the fixed reason string a job log and the operator console read. |
| `ClosureClaimExtractor.cs` | Reads the roads a finished script claims are CLOSED, so a claim can be checked against the data |
| `ClosureVerbNormalizer.cs` | Deterministically voices a road closure as "closed", not "shut" — ARN's marked-up |
| `CompassDirection.cs` | Derives a compass direction of travel from an incident's shape geometry. HERE incident |
| `ContentFormatters.cs` | Formats non-traffic content into the same factual-bullet style as the traffic data: |
| `ContentSectionOrder.cs` | Resolves the ON-AIR read order of the trailing supplementary sections (weather and fuel) |
| `CorridorAggregator.cs` | Aggregates individual traffic incidents into corridor groups for more natural reporting. |
| `CorridorCurator.cs` | Applies an operator's corridor curation (Workshop 2 / SDD §4.2: reorder, include/exclude, |
| `CorridorEditorialRank.cs` | How much a driver needs a road, on ONE banded scale shared by every kind of rundown item. |
| `CorridorFormatter.cs` | Formats corridor data into human-readable traffic descriptions for LLM script generation. |
| `CorridorMagnitude.cs` | Classifies a corridor by its commuter-facing magnitude — the delay a listener actually |
| `CorridorTiering.cs` | The single source of truth for which corridors a bulletin actually VOICES: worst-first |
| `CoverageMapping.cs` | Converts between the wire-level coverage DTO (shape discriminated by a |
| `DeliveryRewriteValidator.cs` | The hard constraints on a register rewrite (). |
| `DeliveryTags.cs` | Helpers for ElevenLabs v3 inline audio tags (e.g. [urgent], [calm], |
| `DeliveryTimeCost.cs` | What actually makes a toned read longer than the plain one, so an overrunning read can be |
| `DurationController.cs` | What the duration controller recommends doing with a candidate read before synthesis. |
| `DynamicSlotSelector.cs` | Chooses the broadcast slot a measured honest read justifies. After the read is synthesized and |
| `EventLedgerFold.cs` | Folds a market's stored jobs into the Event Ledger: persistent identity per physical road |
| `EvidenceLabelVocabulary.cs` | The exact vocabulary the evidence builder hands the writer to DESCRIBE a condition, which |
| `ExponentialMovingAverage.cs` | Pure exponential moving average (EMA) calculator. |
| `FakeLocationDetector.cs` | Deterministic "incident type recited as a location" gate. When a corridor's condensed |
| `FetchValidityAccounting.cs` | One real fetch's validity picture, counted from the records themselves. |
| `FillerAudioCacheInvalidator.cs` | The single, shared diff-and-evict routine for filler-statement cache invalidation. Both |
| `FillerAudioCacheKey.cs` | Builds the cache key identifying one (voice × voice settings × TTS model) synthesis |
| `FillerAvailabilityMapping.cs` | Converts between the wire-level filler-availability DTO (dates as "yyyy-MM-dd", times as |
| `FillerCartAudioKey.cs` | Builds the cache key identifying one (script × voice × settings × model) filler-cart |
| `FillerPaceMatcher.cs` | Retimes a cached filler clip so it AIRS at the same speaking rate as the traffic read it is |
| `FillerStatementSelector.cs` | Selects per-market evergreen filler statements to top up a bulletin whose real traffic, |
| `FixtureLineComposer.cs` | Builds the minimal weather/fuel fixture line from the fetched data, deterministically. |
| `FixtureLines.cs` | The weather and fuel sentences of one read, as the workflow that wrote them identified them |
| `FutureLessonPolicy.cs` | Validation, identity, expiry, and capacity policy for future lessons. |
| `GoodRunPolicy.cs` | Decides which flowing roads air as "good runs" and in what role, per the confirmed editorial |
| `GuidanceComposer.cs` | The result of composing standing guidance for one bulletin: the ordered, sanitised, |
| `IncidentChangeFormatter.cs` | Renders the same-day incident delta (New / Still ongoing / Cleared / Updated) into the |
| `IncidentClassCatalog.cs` | The canonical incident classes, and the single place a provider's raw type is mapped onto one. |
| `IncidentClassCrosswalk.cs` | Canonical incident classes used by the multi-source traffic pipeline. The crosswalk is deliberately |
| `IncidentCommentNormalizer.cs` | Normalizes the operator-authored comment free-text that HERE attaches to some |
| `IncidentConditionCatalog.cs` | What a HERE incident actually IS, keyed to the AlertC cause code HERE publishes rather than to |
| `IncidentCurrencyPolicy.cs` | Decides whether a HERE incident is a current disruption or a long-term worksite, from HERE's |
| `IncidentDeltaReconciler.cs` | Suppresses a "has cleared" narrative for a road that also carries a currently active, |
| `IncidentDescriptionGrammar.cs` | The four shapes HERE's incident description takes. Measured across 793 Australian |
| `IncidentDiversityFilter.cs` | Ensures diverse functional-class representation in incident selection. |
| `IncidentEligibility.cs` | The action eligibility took on one record, kept for the job log and the operator console. |
| `IncidentFactBuilder.cs` | Reduces a fetched HERE record plus the map's verdict on its segments to the single |
| `IncidentTypeClassifier.cs` | Classifies traffic incident types by how their physical extent relates to commuter delay. |
| `IncidentWindow.cs` | How current a HERE incident is, from its own startTime/endTime window. |
| `MarketNames.cs` | The single canonical market-id → human-readable place-name mapping ("what is this market |
| `MonitoredRouteReporter.cs` | Fetches live drive times for a market's configured monitored routes and ranks them by delay, worst commute first. |
| `OfficialFeedCallBudget.cs` | The per-source budget shared by every HTTP call made while an official feed is read. NSW and |
| `OfficialIncidentNormalizer.cs` | Normalizes an official road-authority event into a FIRST-CLASS |
| `OfficialTrafficSources.cs` | The canonical registry of road-authority feed ids a station may switch off, and the only |
| `OrchestrationValidation.cs` | Validates orchestration request DTOs and converts them to domain types with fail-fast validation. |
| `PaletteSelector.cs` | The ATN road-feel vocabulary rendered into the draft prompt with each tier's phrase order |
| `PlaybookAmendmentRenderer.cs` | Renders a station's playbook edits as AMENDMENTS to instructions the model already holds. |
| `PlaybookParser.cs` | Reads a shipped default playbook from its markdown file. |
| `PolishFacts.cs` | Maps a source corridor to the role labels the naturalness-polish stage is handed in its |
| `PriorityRoads.cs` | Priority-roads logic (Workshop 2 / SDD §4.2): per-market arterials that matter to |
| `PromptSafe.cs` | Prompt-injection hardening for any text that originates outside the engineered |
| `PronunciationRuleSetEditor.cs` | What a word-or-phrase alias edit did to a rule set. |
| `RepeatedStatePhraseDetector.cs` | Deterministic anti-repetition gate: flags a bulletin that leans on the SAME content word |
| `ReplayContradictionPolicy.cs` | Decides whether a script from an earlier bulletin may be re-aired against the traffic data |
| `RoadDesignationAliases.cs` | Canonicalizes gazetted road names to the designation broadcasters and listeners actually |
| `RoadFeelTranslator.cs` | Turns an evidence severity band into the words a presenter would actually say, in code, |
| `RoadIdentityResolver.cs` | How decisively the map named the road a record sits on. |
| `RoadNameFormatter.cs` | Title-cases the SHOUTING (all-caps) road names HERE sometimes emits ("SMITH STREET MOTORWAY" |
| `RoadNamePlausibility.cs` | Performs a conservative editorial plausibility check on road names before they enter a |
| `ScriptDiff.cs` | One contiguous wording change: the original words and their replacement. Either side |
| `ScriptProvenanceMapper.cs` | Deterministic per-sentence provenance (Phase 3). Maps each script sentence to the source |
| `ScriptSentenceSplitter.cs` | Splits bulletin script text into individual sentences, excluding bracketed |
| `ScriptWordCeiling.cs` | The hard word ceiling a bulletin script may reach for a given slot, and the deterministic |
| `SentenceFragmentGuard.cs` | Detects a sentence that cannot stand alone on air because it ends mid-thought — a dangling |
| `SlotCapacity.cs` | How many rundown items (incident corridors + good-run roads) a bulletin slot can hold, |
| `SlotFitPlanner.cs` | Shared slot-fill timing logic so the production orchestrator |
| `SpeechTailTrimPolicy.cs` | Decides where a synthesised take should END — after the last spoken sound and before |
| `SpokenDurationCorrection.cs` | Corrects a duration prediction using what the SAME job already measured. |
| `SpokenDurationFeatures.cs` | Extracts the compact, deterministic shape of a spoken read used by duration models. |
| `SpokenFormNormalizer.cs` | The complete set of deterministic rewrites that make a written read speakable, applied in one |
| `SpokenFuelGrade.cs` | The fuel grade as a presenter says it. HERE labels the grade "Unleaded 91"; no Australian |
| `SpokenPriceNormalizer.cs` | Deterministically voices a fuel price in spoken words, never as a currency numeral — |
| `SpokenSyllableCounter.cs` | Counts the syllables a token is expected to take when spoken aloud, including numerals and |
| `StaleRoadworksFilter.cs` | Drops long-running roadworks that stopped being news days ago. |
| `StationLineSplitter.cs` | A bulletin script separated into the STATION-AUTHORED lines that top and tail it and the |
| `SyllableCounter.cs` | Estimates the number of syllables in text using a vowel-group heuristic. |
| `TimeOfDayNormalizer.cs` | Deterministically grounds time-of-day wording in a script. The draft model habitually opens |
| `TokenAdditiveDurationModel.cs` | Evaluates the pooled token-shape prosody formula over the shared |
| `TrafficEvidenceFusion.cs` | Configuration options governing evidence fusion across official road-authority feeds. |
| `TrafficFeedComparison.cs` | The validity picture of one market's HERE fetch against the official feeds, one probe run. |
| `TrafficTypeMapper.cs` | Single source of truth for mapping raw traffic incident type strings (e.g. "accident", |
| `TtsErrorClassifier.cs` | Classifies TTS synthesis errors into permanent (non-retryable) vs transient (retryable). |
| `UnselectedSourceDetector.cs` | Deterministic "fabricated unselected source" gate. When weather or fuel was NOT selected for |
| `VariantSelector.cs` | The result of choosing a sentence phrasing. is true only when |
| `VoiceModelResolver.cs` | Picks the TTS model a specific voice should render on, from the models the voice is |
| `VoiceRotationMapping.cs` | Converts between the wire-level voice-rotation DTO (mode discriminated by a string, window |
| `VoiceabilityClassifier.cs` | The editorial role an item can safely play in a bulletin, based only on structured evidence. |
| `WeatherPhrasingNormalizer.cs` | Fixes the temperature preposition: "temperatures ON 13 degrees" -> "temperatures AT 13 degrees". |
| `WordCounter.cs` | Counts spoken words in a script, ignoring TTS pacing tags |
| `WordTargetCalculator.cs` | Domain service for computing word targets from speech rate metrics. |
| `WordTimingBuilder.cs` | Converts character-level TTS alignment into word-level timings on the FINAL audio. |
### `Services/Detectors/`
| File | Purpose |
|---|---|
| `ClearRoadLabelNormalizer.cs` | Whole-phrase normaliser for the two clear-road data-labels the writer must never voice — |
| `ConnectorFrequencyDetector.cs` | Deterministic anti-drumbeat gate: flags a bulletin that leans on the contrast connector |
| `ContentSourceSignals.cs` | Shared sentence-level signals for weather and fuel content, used by every detector that |
| `DuplicateAnchorDetector.cs` | Deterministic "duplicate anchor" gate (BFN-006/BFN-020). ATN names a road once per item and |
| `EvidenceRestatementDetector.cs` | Deterministic restatement gate for structured drafts. A sentence commits its evidence ids |
| `FalseClosureDetector.cs` | Deterministic "false-closure" gate. A corridor whose typed data does not mark it as closed |
| `FragmentDetector.cs` | Deterministic "fragment" gate. A stray full stop before a lowercase clause-connector |
| `HazardBlockageDetector.cs` | Deterministic "hazard-blockage" gate. The prompt tells the writer never to invent a lane |
| `ImpactStackDetector.cs` | Deterministic "trailing impact-word stack" gate — the sibling of |
| `IncidentTypeStackDetector.cs` | Deterministic "trailing incident-type stack" gate (BFN-019). The data's raw incident-type |
| `MetaLeakDetector.cs` | Deterministic "meta-reference leak" gate. The prompt tells the writer never to say the |
| `NonVehicularIncidentFilter.cs` | Deterministic filter that removes non-vehicular infrastructure incidents |
| `OrphanedConnectiveDetector.cs` | Deterministic orphaned-back-reference gate: an ADDITIVE connective ("also", "as well", |
| `RedundantTailDetector.cs` | Deterministic gate for the summarising/back-referencing TAIL — a closing sentence that adds |
| `RepeatedCauseDetector.cs` | Deterministic gate for repeated explicit cause/event clauses across a read. A sentence such as |
| `RepeatedFuelSuburbDetector.cs` | Deterministic "repeated fuel suburb" gate. A shared cheapest-fuel suburb must be voiced once |
| `RepeatedPrepositionDetector.cs` | Deterministic "repeated-preposition split closure" gate. A corridor bullet carrying TWO |
| `RequiredSourceDetector.cs` | Deterministic "required source missing" gate. When a station marks weather or fuel as a |
| `RoadNameGroundingDetector.cs` | Observation-only road-grounding detector. Computes whether a draft voices a road-like name |
| `RollCallDetector.cs` | Deterministic "road-name roll-call" gate. ATN style names at most a road or two, then sweeps |
| `SecondaryItemCapDetector.cs` | Deterministic cross-sentence roll-call gate: a lead plus ONE OR TWO named secondary items, |
| `SectionOrderDetector.cs` | Deterministic "supplementary section order" gate. A station's |
| `SentenceCloneDetector.cs` | Deterministic "cloned sentence shape" gate. ATN style varies how each item is brought in; |
| `SentenceShapeDetector.cs` | Deterministic gate on the SHAPE of a read, independent of what it says. |
| `SplitClearRunDetector.cs` | Deterministic "split clear-run" gate + repair. Same-state clear roads should be voiced in ONE |
| `SpokenPunctuationNormalizer.cs` | Deterministically repairs punctuation artifacts that must never reach the voice. |
| `SuburbRetentionDetector.cs` | Deterministic suburb-retention gate. Every corridor with a resolved |
| `VaguenessDetector.cs` | Deterministic gate for vague, unsourced filler that names no specific road — the padding a |
### `ValueObjects/`
| File | Purpose |
|---|---|
| `DurationWaterfall.cs` | Tracks the estimated vs final duration across multiple estimation methods. |
| `MarketKey.cs` | The single canonical normalization for market identifiers and market-scoped storage keys: |

**Total:** 383 files
<!-- codemap:end Domain -->

---

## `src/ArnBulletinGenerator.Application/`

<!-- codemap:begin Application -->
### Root
| File | Purpose |
|---|---|
| `DependencyInjection.cs` | Registers the Application layer's DI services: orchestrator, job/schedule services, |
### `Access/`
| File | Purpose |
|---|---|
| `MarketAccessService.cs` | Provides opt-in operator → market scoping (Workshop 2 / SDD §4.2). Semantics: |
### `Broadcasting/`
| File | Purpose |
|---|---|
| `AudioByteOwner.cs` | Owns an in-memory audio byte array while a generation candidate is alive. Disposing drops the |
| `AudioFinalizer.cs` | Finalizes a validated script into an aired broadcast: saves the synthesised audio, |
| `AudioPipelineServices.cs` | The audio-side services a finalized script is rendered through: TTS synthesis, duration |
| `BookendSlotFitPlanner.cs` | Decides, BEFORE any drafting or synthesis, whether a station's bookend pair can share a |
| `BroadcastOrchestrator.cs` | Coordinates the broadcast pipeline, delegating job status, traffic evidence, script |
| `BroadcastWorkBudget.cs` | One cancellation and attempt budget for a broadcast operation. The budget is created once by |
| `BroadcastWorkBudgetOptions.cs` | Rollout settings for the per-broadcast work budget. Cancellation and byte release are always |
| `BulletinFallbackService.cs` | Applies the broadcast fallback chain to a job past deadline: reuse the last successful |
| `ContentProviders.cs` | The external evidence feeds a bulletin is generated from: traffic, weather, fuel prices, |
| `DeliveryDirector.cs` | Owns delivery direction (ElevenLabs v3 audio tags): applying the resolved delivery policy to |
| `FallbackAudioPadder.cs` | Pads a filler-cart fallback clip with per-voice filler statements when the cart alone |
| `FillerAvailabilityFilter.cs` | Filters loaded filler statements to those whose window |
| `FlowPoolBuilder.cs` | Builds the data-backed flowing-road pool used to fill good-run slots: the live free-flow |
| `IBroadcastOrchestrator.cs` | Contract for running a bulletin through the full broadcast pipeline, either from a |
| `IBulletinFallbackService.cs` | The kind of fallback applied to a job that could not produce a fresh bulletin. |
| `InteractiveWorkGate.cs` | Compatibility implementation for direct-construction tests and callers that intentionally |
| `InteractiveWorkMetrics.cs` | Default no-op sink for local runs and tests without an App Insights adapter. |
| `JobMessageBuilder.cs` | Builds rejection feedback strings and regeneration queue messages for job workflows. |
| `JobProgressReporter.cs` | Owns one job's structured progress document. Event mutation is serialized in memory while |
| `JobStatusUpdater.cs` | Owns every job-status transition, read-modify-write persistence, and log-line append used |
| `MarketContentFetcher.cs` | Fail-soft fetchers for the per-market content that depends only on the coverage area and |
| `ModelSelector.cs` | Selects the ElevenLabs model for a voice, honouring per-use-case overrides, falling back |
| `PerVoiceFillerCartResolver.cs` | Resolves the per-voice filler cart clip for a job's own voice. The cart script is |
| `ScriptContentGates.cs` | The deterministic quality boundary between the workflow drafter and synthesis, plus the |
| `ScriptGenerationEngine.cs` | Owns the generation retry loop (drafting, duration-fit re-attempts, filler top-up) and TTS |
| `ScriptPipelineRoles.cs` | The LLM-facing roles that turn traffic evidence into a broadcast script: draft, review, |
| `ScriptReviewLoop.cs` | Owns the review/refine convergence loop and the quality gates that guard it: the main |
| `TrafficEvidenceBuilder.cs` | Fetches and assembles the traffic/market evidence a bulletin is written from: cached HERE |
### `Commands/`
| File | Purpose |
|---|---|
| `JobCommands.cs` | Input DTO for creating an ad-hoc broadcast job: location, target duration, optional |
| `ScheduleCommandBuilder.cs` | Builds create/update schedule commands from the wire request plus resolved domain fields. |
| `ScheduleCommands.cs` | Input DTO for creating a market's broadcast schedule: cron cadence, coverage, voice |
### `ConfigImport/`
| File | Purpose |
|---|---|
| `BookendPlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the Bookends config section. A bookend is |
| `ConfigImportContext.cs` | Bundles every repository/service a config-import section plan builder might need, so |
| `ConfigImportHelpers.cs` | Small stateless helpers shared by every config-import section plan builder. |
| `ConfigImportPlan.cs` | The validated import plan. All-or-nothing gate: any |
| `DeliveryStylePlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the DeliveryStyles config section. |
| `FillerStatementsPlanBuilder.cs` | Plans the per-market replace-list FillerStatements config section. |
| `GuidancePlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the Guidance config section. |
| `OperatorAssignmentsPlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for global operator assignments. |
| `PlannedConfigItem.cs` | One planned entity change: the report row plus the deferred apply action. |
| `PriorityRoadsPlanBuilder.cs` | Plans the per-market replace-list PriorityRoads config section. |
| `PronunciationDictionaryPlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the PronunciationDictionaries config section. |
| `SafetyTermsPlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the SafetyTerms config section. |
| `SchedulePlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the Schedules config section (keyed |
| `VerifiedPronunciationsPlanBuilder.cs` | Plans the per-market replace-list VerifiedPronunciations config section. |
| `VoiceCalibrationsPlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for persisted voice calibrations. |
| `VoiceProfilePlanBuilder.cs` | Plans Create/Update/Unchanged/Error actions for the VoiceProfiles config section. |
### `ConfigTransfer/`
| File | Purpose |
|---|---|
| `ConfigImportService.cs` | Plans and applies config-bundle imports. Planning validates every entity through the |
| `ConfigImportSnapshotReader.cs` | Loads only the current state needed to plan one bundle. Library lists needed for schedule |
| `ConfigTransferContracts.cs` | Section names, in dependency order (libraries before the schedules that reference them). |
| `ConfigValidationLimits.cs` | Single source of truth for config-bundle field length/count limits. Shared by the |
### `Jobs/`
| File | Purpose |
|---|---|
| `IIncidentStateService.cs` | Contract for computing per-market incident deltas (new, updated, cleared) against |
| `IJobDeliveryProcessor.cs` | Owns the asynchronous delivery worker boundary without widening the legacy job-service |
| `IJobService.cs` | Contract for the job lifecycle: create, approve, reject, reject-to-fallback, publish, |
| `IncidentStateService.cs` | Tracks incident state across bulletins and produces delta context for scripting. |
| `JobProgressResponsePlanner.cs` | HTTP outcomes for a point-read progress request. |
| `JobPronunciationService.cs` | The dictionary as it now stands, and what the operator's correction did to it. |
| `JobRetentionService.cs` | Runs the disabled-by-default, checkpointed job retention policy. |
| `JobService.cs` | Application service for job lifecycle operations. |
| `MarketLocationResolver.cs` | The SINGLE market-resolution path for free-text ad-hoc broadcast locations. Every caller |
### `Learning/`
| File | Purpose |
|---|---|
| `ILessonLearningService.cs` | Turns one published operator script edit into station-lesson store changes: diff the |
| `LessonLearningService.cs` | — |
### `Ledger/`
| File | Purpose |
|---|---|
| `EventLedgerService.cs` | Serves the Event Ledger: the fold, omission verdicts, and per-job deltas. |
### `Libraries/`
| File | Purpose |
|---|---|
| `BookendService.cs` | Input for creating a new bookend pair (mints version 1). An explicit |
| `BulletinPlaybookService.cs` | Input for creating a playbook. An explicit preserves identity for config |
| `DeliveryStyleService.cs` | Input for creating a new delivery style (mints version 1). An explicit |
| `LibraryReferenceGuard.cs` | Referential-integrity guard for the shared libraries a schedule points at — voice profiles, |
| `PreviewScenarios.cs` | Two embedded, representative canned data sets ("busy" and "quiet") the Style Rules preview |
| `PronunciationDictionaryService.cs` | Input for creating a new pronunciation dictionary (uploads + mints version 1). An |
| `VoiceProfileService.cs` | Input for creating a new voice variant (mints version 1). An explicit |
### `Mapping/`
| File | Purpose |
|---|---|
| `IncidentMappingExtensions.cs` | Extension methods for mapping traffic incidents between domain representations. |
| `JobMappingExtensions.cs` | Response mapping: Domain Job → DTO conversions for HTTP responses. |
| `JobProgressMappingExtensions.cs` | Maps the domain progress document to the PascalCase HTTP contract. Keeping the projection in |
| `PriorityRoadMapping.cs` | Wire↔domain mapping for . |
| `PronunciationRuleMapping.cs` | Wire↔domain mapping for pronunciation rules. |
| `QueueMessageMappingExtensions.cs` | Maps queue messages to domain request types for orchestration. |
| `RegenerationMappingExtensions.cs` | Maps to domain types for orchestration. |
| `RequestParser.cs` | Shared JSON helpers for deserializing queue messages and HTTP request bodies. |
| `ScheduleMappingExtensions.cs` | Response mapping: MarketSchedule → ScheduleSummaryDto for API responses. |
### `Metrics/`
| File | Purpose |
|---|---|
| `IBroadcastMetrics.cs` | Custom-metric sink for the broadcast pipeline. Implementations forward to |
| `ValidityMonitorService.cs` | On-demand comparison of every enabled market against the official road-authority feeds — the |
### `Scheduling/`
| File | Purpose |
|---|---|
| `AdHocStationConfigResolver.cs` | The station configuration an ad-hoc broadcast inherits from the schedule its location |
| `CoverageResolver.cs` | Resolves coverage information for orchestration requests by geocoding |
| `ICoverageResolver.cs` | Contract for filling in an orchestration request's coverage area by geocoding its |
| `ISchedulePollerService.cs` | Contract for the scheduled broadcast sweep: finds due market schedules and drives each |
| `IScheduleService.cs` | Contract for market schedule lifecycle operations: validated creation, update, and |
| `JobWatchdog.cs` | Owns the per-tick sweeps that keep the schedule/job store healthy independently of any |
| `ScheduleOccurrenceResolver.cs` | Resolves the next schedule occurrence, applying an optional station-local calendar while |
| `SchedulePollerService.cs` | Application service for processing scheduled broadcasts. |
| `ScheduleService.cs` | Application service for schedule lifecycle operations. |
| `StationConfigResolver.cs` | Resolves a schedule's station configuration — voice, delivery style, bulletin playbook, |
| `TimeZoneHelper.cs` | Shared timezone utilities for calculating local-day boundaries in UTC. |
### `Validation/`
| File | Purpose |
|---|---|
| `OperatorTextGuard.cs` | Rejects Unicode control and format characters in operator-authored SINGLE-LINE names - |
| `TrafficScriptStyleValidator.cs` | Result of validating a generated bulletin script against ATN style rules: banned closers, |

**Total:** 96 files
<!-- codemap:end Application -->

---

## `src/ArnBulletinGenerator.Infrastructure/`

<!-- codemap:begin Infrastructure -->
### Root
| File | Purpose |
|---|---|
| `CronParser.cs` | Implementation of ICronParser using Cronos library. |
| `DependencyInjection.cs` | Registers every Infrastructure-layer implementation (HERE/ElevenLabs/Azure OpenAI |
| `QueuePublisher.cs` | Implementation of IQueuePublisher using Azure Storage Queues. |
| `StartupSafety.cs` | Startup safety validations that must fail fast rather than silently deliver. |
### `AiProviders/`
| File | Purpose |
|---|---|
| `CachedVoiceProvider.cs` | Process-wide voice catalogue cache. Concurrent misses share one provider call, callers may |
### `AiProviders/AzureOpenAI/`
| File | Purpose |
|---|---|
| `BulletinDataComposer.cs` | Composes EVERY factual data section a bulletin may draw from — traffic, weather, |
| `DraftSentence.cs` | One sentence of a structured Draft response. are committed BEFORE |
| `DraftWordBudget.cs` | Computes the draft word-count floor and the "low corridor" flag for a bulletin. |
| `HandlebarsTemplateRenderer.cs` | Compiles and renders the prompts/*.md Handlebars templates directly against |
| `LessonSummarizer.cs` | Isolated single-call LLM behind : given one published |
| `LlmScriptGenerator.cs` | Provider for the non-drafting LLM roles — Review, sentence-scoped Patch, Safety, Direction, |
| `ModelResponseParser.cs` | Parses the MODEL'S OWN output content — the review/safety/attribution/director JSON the |
| `PromptLibrary.cs` | Owns the surviving on-disk templates plus pure render helpers that project runtime data |
| `RateLimitRetryHandler.cs` | HTTP delegating handler that retries requests on 429 (Too Many Requests) and 503 (Service Unavailable), |
| `ResponsesTransport.cs` | The Azure OpenAI Responses API transport shared by every role |
| `TokenUsageParser.cs` | Lifts the usage block out of a raw Responses API body. Both transport paths — the |
| `TrafficNarrativeSummarizer.EvidenceUnits.cs` | Evidence-unit projection of the traffic narrative. Every here |
| `TrafficNarrativeSummarizer.cs` | Formats structured traffic corridor data into concise factual bullets |
### `AiProviders/AzureSpeech/`
| File | Purpose |
|---|---|
| `AzureSpeechTranscriber.cs` | Azure AI Speech fast transcription (api-version 2024-11-15) against the existing |
### `AiProviders/Conversational/`
| File | Purpose |
|---|---|
| `CalibratedDurationOracle.cs` | Answers the workflow's duration questions with the engine's voice-calibrated predictor |
| `ConversationalScriptGenerator.cs` | Runs the conversational bulletin workflow behind the deployed generator seam. |
| `PlaybookPreviewService.cs` | Request for a non-persistent preview of a station playbook overlay. |
| `ShippedPlaybookProvider.cs` | Resolves the same on-disk defaults exposed by the playbook HTTP surface. |
### `AiProviders/ElevenLabs/`
| File | Purpose |
|---|---|
| `ElevenLabsAudioSynthesizer.cs` | Audio synthesizer using ElevenLabs API. |
| `ElevenLabsClient.cs` | HTTP client for ElevenLabs Text-to-Speech API. |
| `ElevenLabsContracts.cs` | Represents a voice available in ElevenLabs. |
| `ElevenLabsTtsBudget.cs` | Process-wide bulkhead and absolute operation deadline for ElevenLabs generation calls. |
### `AiProviders/Location/`
| File | Purpose |
|---|---|
| `DataQuollEmergencyFeed.cs` | DataQuoll's GeoJSON emergency-services feed. This is a separate evidence rail from official |
| `HereApiHelper.cs` | Consolidated HERE API HTTP helpers with unified error handling. |
| `HereFlowCandidateSelector.cs` | Selects the small set of raw HERE flow rows whose road identity can affect a bulletin. |
| `HereFuelPriceProvider.cs` | Fetches nearby fuel station prices from the HERE Fuel Prices v3 API for a market's coverage |
| `HereGeoFilter.cs` | Builds the HERE Traffic API v7 in= geospatial filter for a |
| `HereGeocodingProvider.cs` | Resolves place names to coordinates via HERE Geocoding, and batch reverse-geocodes many |
| `HereMapAttributesProvider.cs` | — |
| `HereRequestBudget.cs` | Worker-wide concurrency gates for HERE enrichment. A worker can process more than one bulletin |
| `HereRoutingProvider.cs` | Provides routing and drive time information via HERE Routing API v8. |
| `HereTrafficProvider.cs` | Live HERE Traffic v7 adapter that fetches incident and flow-tile data, classifies roads |
| `HereWeatherProvider.cs` | Fetches current conditions, forecast, and alerts from the HERE Destination Weather v3 |
| `LiveTrafficNswFeed.cs` | Live Traffic NSW open-data hazards (Transport for NSW, CC-BY, keyless). Fetches the statewide |
| `MainRoadsWaFeed.cs` | Main Roads Western Australia ArcGIS TravelInformation open data feed. The incident and |
| `OfficialFeedGeoJson.cs` | Shared GeoJSON / JSON reduction for state official feeds: flatten geometry to points, keep only |
| `OfficialFeedRetryHandler.cs` | Retries short-lived official-feed throttles and server failures without allowing retry |
| `QldTrafficFeed.cs` | QLDTraffic GeoJSON v2 (TMR, CC-BY, public API key). One statewide events call; the class split |
| `TasAlertFeed.cs` | Tasmania Police TasALERT road alerts feed (RSS / XML). |
| `TrafficSaFeed.cs` | South Australia TrafficSATime layer-1 incidents open feed. The ArcGIS FeatureSet is filtered |
| `VicTrafficFeed.cs` | Victoria DataVic unplanned disruptions feed (Department of Transport and Planning, CC-BY, keyless). |
### `Audio/`
| File | Purpose |
|---|---|
| `AudioDurationFineTuner.cs` | Duration fine-tuning core for : trims trailing silence, |
| `AudioMathHelper.cs` | Shared audio alignment math used by WAV processing components. |
| `AudioPipelineService.cs` | Aggregates audio synthesis, duration estimation, and post-processing into a cohesive pipeline. |
| `EmbeddedSpokenDurationCalibration.cs` | Loads the immutable fitted spoken-duration calibration from the Infrastructure assembly. |
| `FillerAudioCacheService.cs` | Resolves filler audio: a cache hit downloads the stored blob and its measured |
| `PcmAudioStitcher.cs` | Concatenates whole WAV clips (traffic synthesis + resolved filler audio) into one file, |
| `SpeechDurationEstimator.cs` | Estimates the spoken duration of a script from its word count using a configurable |
| `StaticRateDurationPredictor.cs` | Predicts spoken duration using a static syllable rate derived from |
| `VoiceCalibrationService.cs` | Calibrates voice-specific speech rates by synthesizing a reference phrase |
| `WavDurationProcessor.cs` | Post-processes synthesized WAV audio to hit a target duration exactly, via |
| `WavHeaderBuilder.cs` | Wraps raw 16-bit PCM samples in a standard RIFF/WAVE header to produce a playable WAV file. |
| `WavInfoParser.cs` | Parsed WAV header metadata (sample rate, channels, bit depth, data chunk location) that |
| `WavLoudnessNormalizer.cs` | Normalizes 16-bit PCM WAV loudness to a consistent target so every bulletin goes to air |
| `WavValidator.cs` | Confirms a byte array is a well-formed WAV file by checking the RIFF/WAVE signature bytes. |
### `Audio/DurationStrategies/`
| File | Purpose |
|---|---|
| `SentencePauseInsertionDurationAdjustmentStrategy.cs` | Inserts tiny silence segments inside already-synthesized WAV audio to close small underruns |
### `Calibration/`
| File | Purpose |
|---|---|
| `HistoryBasedDurationPredictor.cs` | Predicts spoken duration by interpolating historical synthesis outcomes. |
| `InMemorySynthesisHistoryRepository.cs` | In-memory synthesis history store with optional JSON file persistence. |
### `Configuration/`
| File | Purpose |
|---|---|
| `AudioLoudnessOptions.cs` | Loudness normalization applied to the final bulletin audio so every broadcast goes to |
| `AuditExportOptions.cs` | Settings for the daily audit-evidence export. The platform is the system of |
| `AuditQueueOptions.cs` | Controls durable activity-audit queue publishing and consumption. Disabled by default so |
| `AzureAIOptions.cs` | Azure OpenAI/Foundry connection settings for script generation: endpoint, model, auth |
| `BlobAudioCacheOptions.cs` | Process-local bounds for the audio provider's blob metadata and user-delegation-key caches. |
| `ConfigImportOptions.cs` | Governance posture for configuration bundle imports (the admin export/import surface). |
| `ContentSafetyOptions.cs` | Configurable content-safety policy for generated scripts: banned words, place-name |
| `CoverageOptions.cs` | Controls the bounded read path for the parser coverage ledger. Explicit market scopes are |
| `DebugOptions.cs` | Dev-only debugging switches. Never enable in a shared or |
| `ElevenLabsOptions.cs` | Configuration options for ElevenLabs Text-to-Speech integration. |
| `EmergencyFeedOptions.cs` | Configuration for the optional DataQuoll emergency-services evidence feed. The master gate |
| `FuelOptions.cs` | Configuration for the HERE Fuel Prices integration: enable flag, base URL, tracked fuel |
| `GuidanceOptions.cs` | Feature switches and bounds for guidance management reads. |
| `HealthOptions.cs` | Controls the liveness and dependency-health probe cadence. Liveness uses the cached |
| `HereOptions.cs` | Configuration options for HERE Maps API integration. |
| `HttpOptions.cs` | HTTP transport behaviour switches. |
| `IncidentLandscapeOptions.cs` | Controls the additive incident-landscape top-N read model and bounded inventory fan-out. |
| `LlmCaptureOptions.cs` | Controls optional full-payload LLM diagnostics. Metadata is always emitted; full prompt and |
| `MapAttributesCacheOptions.cs` | Bounds and expiry settings for the two-level HERE map attributes cache. |
| `OfficialFeedsOptions.cs` | The authoritative road-authority feeds used to corroborate HERE. Endpoint defaults are |
| `PreviewAudioCleanupOptions.cs` | Bounds the prospective cleanup sweep for synchronous preview audio. The minimum retention is |
| `ProviderOptions.cs` | Provider configuration for output services. |
| `ProviderTypes.cs` | Available output providers for broadcast audio. |
| `QueueClaimOptions.cs` | Feature flag for atomic queue-job claims and lease renewal. |
| `SafetyTermIdentityOptions.cs` | Controls the additive SafetyTerm identity index rollout. |
| `StorageOptions.cs` | Azure Storage connection settings (account, container, retry policy) shared by every blob |
| `SynthesisHistoryOptions.cs` | Configuration for synthesis history storage. Production uses the table-backed |
| `TelemetryOptions.cs` | Controls the temporary custom-event compatibility stream for the operations workbook. |
| `VersionedLibraryOptions.cs` | Independent rollout switches for normalized versioned-library storage and bounded list |
| `WeatherOptions.cs` | Configuration for the HERE Destination Weather integration: enable flag, base URL, |
| `WebhookOptions.cs` | Configuration options for webhook notifications (Power Automate Workflows). |
| `ZettaOptions.cs` | Transport used to deliver approved audio to the playout ingest. |
### `ContentSafety/`
| File | Purpose |
|---|---|
| `ConfigurableContentSafetyFilter.cs` | Deterministic content-safety filter. Enforces the UNION of the engineered config |
### `Diagnostics/`
| File | Purpose |
|---|---|
| `BlobLlmCallCapture.cs` | Captures LLM attempt metadata immediately and queues optional full prompt/request/response |
### `Fakes/`
| File | Purpose |
|---|---|
| `FakeAudioSynthesizer.cs` | Deterministic ARN_E2E stand-in for text-to-speech: returns a 220Hz sine-tone WAV whose |
| `FakeDeliveryFileWriter.cs` | Deterministic ARN_E2E stand-in for playout delivery: writes the audio to a local temp |
| `FakeDetourRoutingProvider.cs` | Deterministic ARN_E2E stand-in for detour routing: returns a fixed named detour with a |
| `FakeExternalCallLog.cs` | Thread-safe, timestamped record of every fake external-provider call made during an ARN_E2E run, for test assertions. |
| `FakeFuelPriceProvider.cs` | Deterministic ARN_E2E stand-in for fuel prices: returns fixed Unleaded/Diesel prices with |
| `FakeGeocodingProvider.cs` | Deterministic ARN_E2E stand-in for geocoding: resolves a small hardcoded set of Gold |
| `FakeNotificationService.cs` | Deterministic ARN_E2E stand-in for Teams notifications: logs each job/alert notification |
| `FakePronunciationDictionaryProvider.cs` | Deterministic ARN_E2E stand-in for the ElevenLabs pronunciation dictionary API: derives |
| `FakeRoutingProvider.cs` | Deterministic ARN_E2E stand-in for drive-time routing: returns a fixed duration, delay, |
| `FakeScriptAiProvider.cs` | Deterministic ARN_E2E stand-in for the entire script AI pipeline (generation, review, |
| `FakeTrafficProvider.cs` | Deterministic ARN_E2E stand-in for HERE Traffic: returns a fixed set of incidents, |
| `FakeVoiceProvider.cs` | Deterministic ARN_E2E stand-in for the ElevenLabs voice catalog: returns two fixed voices, |
| `FakeVoiceRemixer.cs` | Deterministic ARN_E2E stand-in for ElevenLabs voice remixing: derives stable fake |
| `FakeWeatherProvider.cs` | Deterministic ARN_E2E stand-in for HERE Weather: always returns a fixed fine, 23 degree, light-wind report instead of calling the live API. |
### `Identity/`
| File | Purpose |
|---|---|
| `EntraOperatorDirectory.cs` | Reads application-role holders from Microsoft Graph for the operator display surface. |
### `Notifications/`
| File | Purpose |
|---|---|
| `TeamsCardBuilder.cs` | Builds the Adaptive Card JSON (as serializable dictionaries) for each |
| `WebhookNotificationService.cs` | Sends structured notifications to a webhook endpoint (Power Automate Workflows) via HTTP POST. |
| `WebhookRetryHandler.cs` | Retries a webhook POST on a transient failure, honouring Retry-After when the |
### `Output/`
| File | Purpose |
|---|---|
| `AzureBlobBroadcastOutput.cs` | Uploads synthesized bulletin audio to Azure Blob Storage and tags it published once the |
| `AzureBlobJobAudioArtifactRepository.cs` | Deletes only safe, job-owned WAV names from the broadcast container. |
| `AzureFilesDropFileWriter.cs` | Writes delivery files to an AD DS-authenticated Azure Files SMB share on the |
| `BlobAudioUrlProvider.cs` | Provides audio URLs with User Delegation SAS tokens for secure blob access. |
| `BlobDropFileWriter.cs` | Writes delivery files to an SFTP-enabled Azure Blob storage drop account. |
| `ConsoleBroadcastOutput.cs` | Local-development broadcast output sink that logs save/publish calls instead of writing |
| `IDeliveryFileWriter.cs` | Writes a delivery file to the configured playout drop transport — an |
| `LocalAudioUrlProvider.cs` | Simple URL provider for local development that returns a dummy URL. |
| `MockDropFileWriter.cs` | In-memory playout drop. Exercises the FULL delivery path — integrity seal, market-code |
| `PreviewAudioCleanupService.cs` | Cleans up only the short-lived audio artifacts created by synchronous preview endpoints. |
| `ZettaFileDeliveryService.cs` | Delivers broadcast audio to the playout ingest. Downloads audio from blob |
### `Persistence/`
| File | Purpose |
|---|---|
| `BlobConfigImportStore.cs` | Blob-backed store for pending configuration imports. One JSON envelope per import under |
| `JobClaimEntity.cs` | Row shape for radioJobClaims: the single Claim partition and job id row key |
| `JobEntity.cs` | The radioJobs Table Storage row shape for a bulletin generation job. |
| `JobEntityMapper.cs` | Maps between the domain aggregate and its Table |
| `JobIndexMaintainer.cs` | Maintains the denormalized watchdog work rows after a canonical job write. |
| `JobWorkIndexEntity.cs` | Scalar Azure Table row used by the watchdog job work index. |
| `JobWorkIndexProjection.cs` | Projects a job into exactly the watchdog eligibility sets. |
| `LastSuccessfulJobEntity.cs` | Scalar CAS pointer to the newest qualifying continuity/fallback job. |
| `OptionMappingExtensions.cs` | Extension methods that reduce boilerplate when converting between |
| `SafeJson.cs` | Safe JSON deserialization helpers that eliminate repeated |
| `TableClientFactory.cs` | Centralizes TableClient creation from , |
| `TableRepositoryBase.cs` | Base class for Azure Table Storage repositories, providing shared |
| `TableStorageActivityLogRepository.cs` | PartitionKey = UTC date bucket, "yyyy-MM-dd" (same format AuditExportTimerTrigger.cs uses). |
| `TableStorageAuditExporter.cs` | Exports the raw radioJobs table — scripts, approval/rejection history, |
| `TableStorageBookendRepository.cs` | Azure Table persistence for bookend pairs. New writes use a compact current row and |
| `TableStorageBulletinPlaybookRepository.cs` | Azure Table persistence for bulletin playbooks. New writes use a compact current row and |
| `TableStorageDeliveryStyleRepository.cs` | Partition key: PK = "DeliveryStyle" (all styles share one partition — admin-managed, low volume). |
| `TableStorageEditorialSettingsRepository.cs` | Partition key: "EditorialSettings". Row key: the scope ("Global", "Market:sydney", |
| `TableStorageEventVerdictRepository.cs` | PartitionKey = "v:{market}" for event verdicts (RowKey = sanitized event key), and |
| `TableStorageFillerAudioCacheRepository.cs` | PartitionKey = FillerStatement.Id, RowKey = cache key (voice/settings/model configuration). |
| `TableStorageFillerStatementsRepository.cs` | PartitionKey = "FillerStatements" (single partition for all markets). |
| `TableStorageFutureLessonRepository.cs` | PartitionKey = normalized station id. RowKey = stable SHA-256 identity for |
| `TableStorageGuidanceRepository.cs` | Standing-guidance directives (Layer 2 of the self-service guidance model). |
| `TableStorageIncidentPriorityRepository.cs` | Azure Table Storage implementation for per-market incident priorities and accumulated landscape occurrences. |
| `TableStorageIncidentStateRepository.cs` | Partition key format: {marketId} (e.g., "sydney", "melbourne") — enables fast lookup by market. |
| `TableStorageJobClaimRepository.cs` | Stores queue-processing leases in radioJobClaims. Claims use an insert-or-ETag-conditional |
| `TableStorageJobPayloadRepository.cs` | Stores large immutable job payloads as compressed, content-addressed entities. The aggregate |
| `TableStorageJobProgressRepository.cs` | Stores one replacement progress document per job and one exponential moving average row per |
| `TableStorageJobReadIndexRepository.cs` | Scalar market/month and global/month job rows for bounded newest-first list reads. The |
| `TableStorageJobRepository.cs` | Partition key format: "Job_{market}" where market = job.Market trimmed+lowercased; null/empty -> "Job_unknown" |
| `TableStorageJobRetentionStore.cs` | Bounded, ETag-guarded retention and orphan-reconciliation storage operations. |
| `TableStorageJobTimelineRepository.cs` | Append-only storage for job status transitions and diagnostic log entries. Rows use a |
| `TableStorageJobWorkIndexRepository.cs` | Azure Table implementation of the bounded scalar job work index. |
| `TableStorageLedgerBucketRepository.cs` | Incremental scalar ledger buckets. One row per settled job, partitioned by normalized |
| `TableStorageMapAttributesCacheRepository.cs` | Azure Table Storage implementation of the persistent HERE map-attributes cache. |
| `TableStorageOperatorAssignmentRepository.cs` | PartitionKey = "Operator" (single partition for all operator assignments). |
| `TableStoragePriorityRoadsRepository.cs` | PartitionKey = "PriorityRoads" (single partition for all markets). |
| `TableStoragePronunciationDictionaryRepository.cs` | Partition key: PK = "PronunciationDictionary" (all dictionaries share one partition — |
| `TableStorageSafetyTermIdentityRepository.cs` | Persists the normalized safety-term identity index. The index is deliberately separate from |
| `TableStorageSafetyTermRepository.cs` | Content-safety terms (banned words + allowed exceptions) for the deterministic filter. |
| `TableStorageScheduleRepository.cs` | Partition key format: PK = "Schedule" (all schedules share one partition — low volume is acceptable). |
| `TableStorageSourceParseReportRepository.cs` | Azure Table Storage implementation of the per-fetch parse coverage ledger. Rows are retained for |
| `TableStorageSpokenDurationSampleRepository.cs` | Table Storage store for measured spoken-duration samples. |
| `TableStorageSynthesisHistoryRepository.cs` | Azure Table Storage-backed synthesis history repository with in-memory partition caching. |
| `TableStorageVerifiedPronunciationRepository.cs` | Operator "sounded fine" verdicts that mute a recurring audio-pre-review suspect word. |
| `TableStorageVoiceCalibrationRepository.cs` | Partition key format: "Calibration" prefix — all calibrations share one partition (low volume, acceptable). |
| `TableStorageVoiceProfileRepository.cs` | Partition key: PK = "VoiceProfile" (all profiles share one partition — admin-managed, low volume). |
| `VersionHistoryTrimmer.cs` | Keeps an append-only version history inside Azure Table's single-property ceiling. |
| `VersionedLibraryStorage.cs` | Shared key, cursor and bounded bulk-read mechanics for versioned libraries. |
### `Serialization/`
| File | Purpose |
|---|---|
| `JsonDefaults.cs` | Shared instances to avoid redundant allocations. |
| `OptionJsonConverterFactory.cs` | System.Text.Json converter factory that (de)serializes the domain's Option&lt;T&gt; |

**Total:** 177 files
<!-- codemap:end Infrastructure -->

---

## `src/ArnBulletinGenerator.Functions/`

<!-- codemap:begin Functions -->
### Root
| File | Purpose |
|---|---|
| `Program.cs` | Azure Functions Worker host entry point: wires config including Key Vault, registers DI services and middleware, then starts the worker… |
### `Auth/`
| File | Purpose |
|---|---|
| `ClientPrincipal.cs` | Parsed caller identity from the X-MS-CLIENT-PRINCIPAL blob: AAD object id, |
| `MarketAccessGuard.cs` | Shared market-scoping guard for HTTP triggers keyed by a market id. Returns a 403 |
| `OperatorRegistrar.cs` | Identity of a person the platform has just seen, as presented by their own token. |
| `SwaRolesSourceTokenValidator.cs` | A sign-in whose access token passed full cryptographic validation. |
### `ConfigTransfer/`
| File | Purpose |
|---|---|
| `ConfigExportBuilder.cs` | Builds import-shaped configuration bundles by delegating all reads to the bounded, |
| `ConfigSnapshotReader.cs` | Reads the current configuration needed by one export. Section reads are independent unless |
| `ConfigTransferContracts.cs` | 202 response when the apply parked as a pending import for a second approver. |
### `Contracts/`
| File | Purpose |
|---|---|
| `ErrorResponse.cs` | Wire format for HTTP error responses: message, optional detail, and the numeric status |
| `VoiceListResponse.cs` | Wire format for the ListVoices response: the ElevenLabs voice catalog mapped to |
### `Http/`
| File | Purpose |
|---|---|
| `DependencyHealthSnapshot.cs` | Holds the last dependency-health result and refreshes it at most once at a time. Provider |
| `HttpRequestParser.cs` | Deserializes a JSON request body into a typed DTO, turning empty bodies, malformed |
| `HttpResponseHelper.cs` | Builds consistent JSON HTTP responses for the Functions triggers: standard |
| `JsonSerializationDefaults.cs` | Default JSON serialization options for HTTP requests and responses. |
| `MarketScopeRequest.cs` | Shared request helpers for admin/operator HTTP triggers: market-scope filtering |
| `StorageKey.cs` | HTTP-boundary wrapper around : |
| `VersionedLibraryListQueryParser.cs` | Parses optional list summary/paging query parameters without changing legacy defaults. |
### `Metrics/`
| File | Purpose |
|---|---|
| `ApplicationInsightsBroadcastMetrics.cs` | Forwards broadcast-pipeline custom metrics to Application Insights. |
| `ApplicationInsightsInteractiveWorkMetrics.cs` | Publishes bounded-cardinality cache, coalescing, wait, and execution telemetry for |
| `ApplicationInsightsLlmCallTelemetry.cs` | Emits one classic Application Insights dependency for each Azure OpenAI HTTP attempt. |
| `E2EFakeBroadcastMetrics.cs` | Fake-mode broadcast metrics sink for deterministic E2E testing. |
### `Middleware/`
| File | Purpose |
|---|---|
| `ActivityLoggingMiddleware.cs` | Cross-cutting change/access audit: generically logs every authenticated, successful |
| `ActivityPathResolver.cs` | Maps an absolute request path (HttpRequestData.Url.AbsolutePath) to an audit |
| `AuthorizationMiddleware.cs` | Defense-in-depth auth + role-based access control. The Function App's Easy Auth |
| `GlobalExceptionHandlingMiddleware.cs` | Catches any exception an HTTP function leaks, logging it and returning a generic 500 |
### `Triggers/Http/`
| File | Purpose |
|---|---|
| `ActivityHttpTrigger.cs` | Compliance-sensitive read surface over the cross-cutting activity/change audit trail |
| `BookendsHttpTrigger.cs` | The shared bookend library: named, versioned intro/outro pairs stations read around every |
| `BroadcastHttpTrigger.cs` | HTTP trigger for ad-hoc broadcasts: takes a free-text location, resolves a matching |
| `ConfigTransferHttpTrigger.cs` | The admin config-transfer surface: export the whole editable configuration as one JSON |
| `DeliveryStylesHttpTrigger.cs` | The shared delivery-style library: named, versioned ElevenLabs v3 audio-tag policies that |
| `EditorialHttpTrigger.cs` | Numeric editorial settings endpoints. Stored values layer Global → Market → Station and |
| `FillerStatementsHttpTrigger.cs` | Per-market filler-statement management (BFN-002). Short statements used to fill remaining |
| `GuidanceHttpTrigger.cs` | Standing-guidance management (self-service guidance, Layer 2) plus the candidate |
| `HealthHttpTrigger.cs` | Health-check endpoint pinging blob storage, Key Vault, HERE, ElevenLabs, and Azure |
| `IncidentPrioritiesHttpTrigger.cs` | Per-market incident priorities and dynamic landscape API triggers. |
| `JobLifecycleHttpTrigger.cs` | HTTP trigger surface for job actions: approve, reject, reject-to-fallback, publish, |
| `JobProgressHttpTrigger.cs` | Read-side endpoint for the live generation progress document. It authorizes from the cheap |
| `JobPronunciationHttpTrigger.cs` | The review cockpit's pronunciation fix: one word → respelling correction against the |
| `JobStationLineResolver.cs` | Collects the STATION-AUTHORED lines a job's aired script may contain — the market's filler |
| `JobsHttpTrigger.cs` | Read-side HTTP trigger for jobs: paginated list/detail scoped to the caller's markets, |
| `LedgerHttpTrigger.cs` | The Event Ledger API: the Desk's triage feed (fold + verdicts + trust + feed health), |
| `MapConfigHttpTrigger.cs` | Serves the console's runtime client configuration: the browser-safe HERE Maps JS key, |
| `OperatorsHttpTrigger.cs` | Operator → market assignment management (Workshop 2 / SDD §4.2). Admin manages |
| `PlaybookPreviewHttpTrigger.cs` | Auditions a draft compose2 playbook overlay against the latest replayable bulletin. |
| `PlaybooksHttpTrigger.cs` | The shared bulletin-playbook library: named, versioned overlays on the shipped role |
| `PlayoutTrayHttpTrigger.cs` | Read surface for the playout drop tray: whether a market's delivery file is still |
| `PriorityRoadsHttpTrigger.cs` | Per-market priority-roads management (Workshop 2 / SDD §4.2). Operators maintain the |
| `PronunciationDictionariesHttpTrigger.cs` | The shared pronunciation-dictionary library: named, versioned ElevenLabs dictionaries |
| `RolesSourceHttpTrigger.cs` | Static Web Apps custom-auth roles bridge. SWA receives the AAD app-role claim |
| `SafetyTermsHttpTrigger.cs` | Content-safety term management (Admin-only): the banned-word list and the place-name |
| `ScheduleHttpTrigger.cs` | HTTP trigger surface for market schedules: list, get, create-or-update, and delete, |
| `ScriptAuditionHttpTrigger.cs` | Auditions a draft read WITHOUT touching the job. |
| `ScriptDirectionHttpTrigger.cs` | Returns the DIRECTED TEXT for a draft so an operator can iterate on delivery before |
| `SourceParseCoverageHttpTrigger.cs` | Read-only operator surface for the deterministic parser coverage ledger. The persisted contract |
| `SynthesizeHttpTrigger.cs` | Audio-only synthesis endpoint for local testing. |
| `ValidityProbeHttpTrigger.cs` | On-demand run of the data-validity probe, returning the per-market comparison so an engineer |
| `VerifiedPronunciationsHttpTrigger.cs` | Per-market verified-pronunciation management: the words an operator has listened to and |
| `VoiceCalibrationHttpTrigger.cs` | Triggers voice calibration — synthesizes a reference phrase and measures |
| `VoiceProfilePreviewHttpTrigger.cs` | Audition endpoints for the shared voice library: preview synthesizes a standard sample |
| `VoiceProfilesHttpTrigger.cs` | The shared voice library: named, versioned tunings ("variants") of a base ElevenLabs voice |
| `VoiceoverHttpTrigger.cs` | Renders ARBITRARY copy — an advertisement, a promo, a station ident — on a library voice, |
| `VoicesHttpTrigger.cs` | HTTP trigger that lists the ElevenLabs voice catalog with each voice's resolved TTS |
### `Triggers/Queues/`
| File | Purpose |
|---|---|
| `ActivityAuditPoisonQueueTrigger.cs` | Observes activity audit messages that exhausted queue retries. Audit payloads are retained |
| `ActivityAuditQueueTrigger.cs` | Consumes durable activity audit messages and records them at least once. The repository |
| `BroadcastPoisonQueueTrigger.cs` | Consumes the broadcast queue's poison sub-queue, handing the failed message to |
| `BroadcastQueueTrigger.cs` | Consumes the broadcast queue, deduplicating redelivered or stale-worker messages before |
| `DeliveryPoisonQueueTrigger.cs` | Consumes delivery commands after the queue host exhausts retries, marks the pending job failed, |
| `DeliveryQueueTrigger.cs` | Consumes durable approved/covered delivery commands. The application processor owns |
| `LessonLearningPoisonQueueTrigger.cs` | Acknowledges lesson-learning messages after the queue host has exhausted retries. |
| `LessonLearningQueueTrigger.cs` | Consumes published operator edits and turns them into station-lesson store changes. |
| `PoisonQueueHandler.cs` | Shared poison-queue handler that marks a job Failed with an audit entry and sends a |
| `QueueFailurePolicy.cs` | Pure Functions-boundary policy for classifying queue orchestration failures. |
| `RegenerationPoisonQueueTrigger.cs` | Consumes the regeneration queue's poison sub-queue, handing the failed message to |
| `RegenerationQueueTrigger.cs` | Consumes the regeneration queue, skipping messages whose job left Requested status |
### `Triggers/Timers/`
| File | Purpose |
|---|---|
| `AuditExportTimerTrigger.cs` | Daily snapshot of the job table (scripts + approval evidence) into the |
| `JobRetentionTimerTrigger.cs` | Daily bounded retention sweep; disabled by default for migration safety. |
| `PreviewAudioCleanupTimerTrigger.cs` | Hourly bounded sweep for expired synchronous preview audio. |
| `QueueDepthMetricsTrigger.cs` | Samples Azure Storage queue depth and oldest-ready-message age every five minutes. |
| `SchedulePollerTrigger.cs` | Timer-triggered function that polls for due schedules and invokes the broadcast orchestrator. |

**Total:** 79 files
<!-- codemap:end Functions -->

---

## `src/ArnBulletinGenerator.Workflow/`

<!-- codemap:begin Workflow -->
### Root
| File | Purpose |
|---|---|
| `ConversationalBulletinWorkflow.cs` | The conversational shape: a writer offers a menu, a composer assembles it, and an editor |
| `StagedBulletinWorkflow.cs` | The staged workflow: EVIDENCE -> MEANING -> SELECTION -> DISCOURSE -> WORDS -> VERIFICATION. |
| `WordRateDurationOracle.cs` | A duration oracle backed by a single words-per-minute rate. |
### `Contracts/`
| File | Purpose |
|---|---|
| `IBulletinWorkflow.cs` | The substitutable seam. One implementation is one whole approach to turning fetched bulletin |
| `Ports.cs` | The only way a workflow reaches a language model. One port serves every role: the role name |
| `StationPolicy.cs` | What ARN may configure, split by the stage it governs. |
### `Model/`
| File | Purpose |
|---|---|
| `BulletinBriefs.cs` | What the writer hands the composer: a partition of the source events into distinct stories, |
| `BulletinEvent.cs` | One real-world thing the bulletin can talk about — a corridor, the weather block, the fuel |
| `BulletinPlan.cs` | What the read will be ABOUT, in what order, with what grouped — and not one word of prose. |
| `CheckResult.cs` | One deterministic check's verdict over a composed read. |
| `ComposedRead.cs` | A whole bulletin, written in one pass, with the evidence each sentence drew on. |
| `GroundingReport.cs` | Atomic entailment over a finished read. Three states, not two: a claim nothing contradicts is |
### `Prompts/`
| File | Purpose |
|---|---|
| `ExemplarSelector.cs` | Selects a small, deterministic ATN ground-truth bank for the composer. The bank is a |
| `PolicyRenderer.cs` | Renders ARN's configuration into prompt text, one method per stage that is allowed to see it. |
| `PromptLibrary.cs` | Loads role prompts and response schemas from the application output directory, cached per |
| `StationPlaybook.cs` | The instructions a station actually gets: our shipped default with their edits applied. |
### `Stages/`
| File | Purpose |
|---|---|
| `Composer.cs` | Writes the complete read after code has fixed both its semantic content and its order. Keeping |
| `EventRank.cs` | How much a driver needs an event, on one scale shared by every kind of event. |
| `Fitter.cs` | Buys semantic content against a word budget. Whole stories are fitted or dropped whole, |
| `Grounder.cs` | Checks atomic entailment after composition. Keeping this as a separate, narrow pass prevents a |
| `HardChecks.cs` | Cheap, deterministic gates over the assembled read. These checks deliberately run after |
| `Normalizer.cs` | Turns citation-level evidence into event-level meaning. Keeping the source key as the event |
| `Planner.cs` | Asks the planning role to decide the semantic content and order of a read before any prose |
| `WordEstimator.cs` | Estimates the words consumed by semantic content before a composer has written prose. |
### `Stages/Conversational/`
| File | Purpose |
|---|---|
| `BriefDivision.cs` | Splits the normalized events between the writers, and puts their briefs back together. |
| `BulletinComposer.cs` | Writes the read from the writer's story briefs. This role sees no source data: the |
| `BulletinEditor.cs` | Reviews the conversational read as a senior radio editor. Unlike the composer, this role sees |
| `BulletinWriter.cs` | Decides what the bulletin is about and briefs the composer, without writing a word of it. |
| `ConversationalChecks.cs` | The deterministic floor under the conversational workflow. |
| `PhraseOverlap.cs` | Measures repeated phrasing between two reads without treating the names that make a traffic |
### `Trace/`
| File | Purpose |
|---|---|
| `GenerationProgressTraceRelay.cs` | Adapts the workflow's diagnostic trace to the domain progress reporter. The trace remains the |
| `TraceHtmlRenderer.cs` | Renders the trace as a deliberately boring, self-contained forensic record. The prompts and |
| `TraceRecorder.cs` | Collects trace events during a run and freezes them into a . |
| `WorkflowTrace.cs` | Everything that happened in one workflow run, in order. The trace is the product, not a |

**Total:** 34 files
<!-- codemap:end Workflow -->

Clean-room, substitutable bulletin-generation workflow (`EVIDENCE -> MEANING -> SELECTION -> DISCOURSE -> WORDS -> VERIFICATION`). Pure core: references Domain only. Selected by id behind `IBulletinWorkflow`, so a new approach is trialled by registering another implementation rather than editing the pipeline that is airing.

---

## `src/ArnBulletinGenerator.Workflow.Hosting/`

<!-- codemap:begin Workflow.Hosting -->
### Root
| File | Purpose |
|---|---|
| `AzureOpenAiCallBudget.cs` | Immutable limits for one Azure OpenAI logical operation. |
| `AzureOpenAiWorkflowLlm.cs` | over the Azure OpenAI Responses API, sending the role's |
| `WorkflowServiceCollectionExtensions.cs` | Registers the workflow seam. Nothing here edits or replaces the deployed pipeline: a workflow |

**Total:** 3 files
<!-- codemap:end Workflow.Hosting -->

Adapters that bind the workflow's ports to real infrastructure: Azure OpenAI Responses transport (sending role prompts explicitly, never server-side agent instructions) and DI registration.

---

## `web/`

### Root Config
| File | Purpose |
|---|---|
| `package.json` | Web app dependencies and scripts (`dev`, `dev:mock`, `build`, `lint`, `test`) |
| `bun.lock` / `package-lock.json` | Bun and npm lockfiles retained for reproducible local installs |
| `vite.config.ts` | Vite config: React plugin, `@` alias, `/api` dev proxy with simulated Easy Auth principal, vendor chunk splitting |
| `vite.config.js` / `vite.config.d.ts` | Generated JS/type output for Vite config consumers |
| `tsconfig.json` | TypeScript project references and compiler settings |
| `tsconfig.node.json` | TypeScript config for Vite/tooling files |
| `tailwind.config.js` | Tailwind/shadcn design token and content scan config |
| `postcss.config.js` | PostCSS config (Tailwind, autoprefixer) |
| `eslint.config.js` | ESLint + TypeScript/React/prettier configuration |
| `components.json` | shadcn/ui component registry config |
| `index.html` | Vite HTML entry point |
| `.prettierrc` | Prettier formatting config |
| `.npmrc` | npm configuration |
| `.env.example` | Documented frontend environment variables |
| `README.md` | Frontend-specific development notes |
| `.oxlintrc.json` | **Tailwind design-system conformance gate** (oxlint + oxlint-tailwindcss). Compiles `src/index.css` to build the real design system, so verdicts are machine-independent. Two tiers: errors (deprecated/duplicate/unnecessary-arbitrary/unnecessary-whitespace classes, hardcoded colours, non-token values, `dark:` without a light counterpart, unknown classes) wired into `npm run lint` so CI gates them; warnings that need adjudication against the compiled stylesheet (`no-conflicting-classes` with a documented allow-list, `prefer-scale-token` at step 1). `no-contradicting-variants` is off — it cannot see `cn()`'s twMerge composition and reported 11/12 false positives. `npm run lint:tw` |
| `scripts/ui-inventory.mjs` | Generates **`docs/UI-INVENTORY.md`** + `docs/ui-inventory.json` from the TypeScript checker: every shared component in `src/components/` and `src/components/ui/`, the props it REQUIRES (props unions rendered as "satisfy one branch" — that is how `SelectTrigger`'s mandatory accessible name surfaces), its cva variants, its usage count, and its one-line JSDoc summary. `npm run ui:inventory`; `--check` fails when the committed doc is stale and `scripts/verify.sh` runs it |
| `scripts/ux-audit.mjs` | Deterministic UX audit harness — crawls every route×state in mock mode (puppeteer) across **8 variants: 4 viewports × light/dark** (`VARIANTS` = `VIEWPORTS` × `THEMES`; the variant id IS the `vp` key downstream, e.g. `desktop` / `desktop-dark`). Theme is driven through next-themes' own storage channel, and axe runs **once per theme** after a warm-up cycle that puts both passes on an equally settled page — without it, lazily-mounted subtrees read as dark-only violations. Captures accessibility tree + text + axe-core WCAG violations per theme (`axe` light, `axeDark`; selector/HTML/summary + box) + console + overflow + raw HTML + screenshots into `audit/`, plus **Set-of-Mark** marked screenshots (`<id>.<vp>.marked.png` — every element badged with a number) + mark legends (`data/<id>.<vp>.marks.json`; each mark carries `fixed`/`clipped` artifact flags — element/ancestor is `position:fixed`\|`sticky`, or sits past a scrollable ancestor — so reviewers discount full-page-stitch overlap/clip false positives), plus **computed-style fingerprints** (`styleFingerprint` over should-be-uniform roles — `h1/h2/h3`, shadcn card family, `[role=dialog]`, `label`/`input`/`textarea`, tabs) diffed cross-page into **`style-lexicon.json`** (exact-px spacing-symmetry + consistency candidates), with pixelmatch visual diff; renders the machine pre-scan `audit/axe-findings.html`. `npm run audit` (start the mock server on `--host 127.0.0.1 --port 5173` first) |
| `scripts/ux-review.mjs` | LLM-authored **manual** UX review → `audit/review.html`. Findings reference **Set-of-Mark numbers** (`mark`/`marks`) — the box is resolved exactly from the element's DOM rect (VLMs are unreliable at freehand coords) — or a `box` % fallback; each may carry a `confidence` (0–1 self-rated certainty it is a genuine, objective defect). Commands: `pages\|add\|merge [--replace] [--min-confidence X]\|marks --page id [--vp]\|list\|rm\|clear\|build` (or `npm run review`). **Fan-out**: spawn N `designer` (vision) subagents over disjoint page sets, each viewing `*.marked.png` and writing `audit/incoming/<id>.json` with mark refs + confidence, then `merge --min-confidence X` (single writer; drops low-confidence noise) |
| `audit/` | Generated UX corpus + reports (`data/*.json`, `data/*.html`, `data/*.marks.json` mark legends, `lexicon.json` label-drift, **`style-lexicon.json`** computed-style consistency diff, `consistency-digest.md` readable digest, `report.md`, `index.html`, `axe-findings.html` machine pre-scan, **`review.html`** reviewer findings + `review-notes.json` + `incoming/`, `after/*.marked.png` Set-of-Mark shots, before/after/diff shots) — gitignored |

### `public/`
| File | Purpose |
|---|---|
| `favicon.svg` | SVG favicon |
| `favicon.ico` | ICO favicon |
| `staticwebapp.config.json` | Azure Static Web Apps routing/navigation fallback config + immutable `Cache-Control` for `/assets/*` (content-hashed bundles) |
| `mock/audio-bulletin.wav` | Local mock audio fixture for the frontend mock adapter |

### `src/` — per-file web map lives in `web/docs/CODEMAPS/` (single source of truth)

Entry: `main.tsx` → `App.tsx` (providers, query defaults, sidebar layout, route table), `index.css`.

#### Pages (`src/pages/`)
| Page | Route | Purpose |
|---|---|---|
| `HomePage.tsx` | `/` | Dashboard / landing |
| `OnAirPage.tsx` | `/onair` | Live operator workstation (queue + review) |
| `StationsPage.tsx` | `/stations` | Station + schedule management (cards + Studio) |
| `VoicesPage.tsx` | `/voices` | Voice library (variants / versions) |
| `OperatorsPage.tsx` | `/operators` | Operator market/station assignments (admin) |
| `HistoryPage.tsx` | `/history` | Broadcast log (settled outcomes) |
| `ActivityPage.tsx` | `/activity` | Activity/change audit log (admin) |
| `NotFoundPage.tsx` | `*` | 404 fallback |

#### Features (`src/features/`)
| Area | Purpose |
|---|---|
| `activity/` | Activity/change audit log: global admin feed + per-station history tab (reuses entity-diff) |
| `jobs/` | Bulletin lifecycle: review cockpit (`components/review/`), Adjust board with Incidents + Flow tabs (`review/adjust/`, incl. `FlowBoard` good-run curation), broadcast log (`review/history/`), deep-dive tabs (`review/canvas/`), provenance/claims/word-timings (`lib/`, `hooks/`) |
| `schedules/` | Schedule Studio (`ScheduleStudio` + `sections/`), voice-profile + LLM-model selectors, cron utilities |
| `stations/` | Station profile cards, trigger Studio, coverage preview, voice picker |
| `voices/` | Voice workshop/studio, version history, calibration display, model capabilities |
| `operators/` | Operator assignment cards + dialog + hooks |
| `onair/` | Workstation (`workstation/`: queue beam, pipeline stepper, review pane), coverage board (`board/`), slot logic |
| `notifications/` | Waiting/failed-job detection, panel, audible alarm |

#### Shared (`src/lib/`, `src/components/`, `src/hooks/`)
| Path | Purpose |
|---|---|
| `lib/api/` | Axios client, query factory, route paths, mock adapter (`mock/`), shared Zod schemas |
| `lib/design/` | Micro-viz primitives (`BudgetBar`, `FreshnessDot`, `SourceGlyphs`) + icon registry |
| `lib/{routes,formatting,auth,layout,toast-action,dom}.ts` | Routing, formatting, auth, layout, toast helpers |
| `components/studio/` | Shared Studio shell (dialog / body / rail / canvas / footer / submit) |
| `components/navigation/` | App sidebar, user menu, mobile nav |
| `components/map-picker/` | Leaflet coverage editor (circle/bbox/corridor/polygon) + geocoding |
| `components/ui/aceternity/` | Motion primitives (aurora, spotlight, moving-border) |
| `components/` | `CommandPalette`, `MetricCard`, `DataBoundary`, `Placeholder`, `FormInputField`, `layout/PageContainer` |
| `hooks/` | `use-current-user`, `use-global-keydown`, `use-debounce`, `use-second-tick` |
| `test/` | Vitest factories + helpers |

---

## `test/` — Backend Test Projects

### `test/ArnBulletinGenerator.Domain.Tests/`
| File | Purpose |
|---|---|
| `Models/*.cs` | Unit tests for domain models |
| `Services/*.cs` | Unit tests for domain services (10 test files) |
| `ValueObjects/*.cs` | Unit tests for value objects |
| `Contracts/Mapping/*.cs` | Contract mapping tests |

### `test/ArnBulletinGenerator.Application.Tests/`
| File | Purpose |
|---|---|
| `BroadcastOrchestratorTests.cs` | Orchestrator workflow tests |
| `CoverageResolverTests.cs` | Coverage resolution tests |
| `IncidentStateServiceTests.cs` | State management tests |
| `JobServiceTests.cs` | Job service tests |
| `JobServiceAdditionalTests.cs` | Additional job service tests |
| `JobMessageBuilderTests.cs` | Queue message building tests |
| `ModelSelectorTests.cs` | Model selection tests |
| `SchedulePollerServiceTests.cs` | Schedule polling tests |
| `ScheduleServiceTests.cs` | Schedule CRUD tests |
| `TimeZoneHelperTests.cs` | Time zone tests |
| `Mapping/*.cs` | 6 mapping extension test files |
| `Validation/*.cs` | Script validation tests |

### `test/ArnBulletinGenerator.Functions.Tests/`
| File | Purpose |
|---|---|
| `ActivityHttpTriggerTests.cs` | Activity HTTP trigger tests |
| `ActivityLoggingMiddlewareTests.cs` | Activity middleware tests (incl. stream-replay proof) |
| `ActivityPathResolverTests.cs` | Path resolver theory tests |
| `BroadcastHttpTriggerTests.cs` | HTTP trigger tests |
| `BroadcastQueueTriggerTests.cs` | Queue trigger tests |
| `JobsHttpTriggerTests.cs` | Jobs HTTP trigger tests |
| `ScheduleHttpTriggerTests.cs` | Schedule HTTP trigger tests |
| `SchedulePollerTriggerTests.cs` | Poller trigger tests |
| `RegenerationQueueTriggerTests.cs` | Regeneration trigger tests |
| `VoicesHttpTriggerTests.cs` | Voices trigger tests |
| `AuthorizationMiddlewareTests.cs` | Auth middleware tests |
| `GlobalExceptionHandlingMiddlewareTests.cs` | Exception middleware tests |
| `HttpRequestParserTests.cs` | Request parsing tests |
| `HttpResponseHelperTests.cs` | Response helper tests |
| `MockHttpRequestData.cs` | Test helper mock |

### `test/ArnBulletinGenerator.Infrastructure.Tests/`
| File | Purpose |
|---|---|
| `AiProviders/*.cs` | LLM, ElevenLabs, location provider tests |
| `Audio/*.cs` | Audio processing tests (10 test files) |
| `Calibration/*.cs` | Duration prediction tests |
| `Configuration/*.cs` | Options validation tests |
| `ContentSafety/*.cs` | Content filter tests |
| `Notifications/*.cs` | Webhook notification tests |
| `Output/*.cs` | Output delivery tests |
| `Persistence/*.cs` | Repository implementation tests |
| `Serialization/*.cs` | JSON serialization tests |
| `Mocks/*.cs` | In-memory test doubles |
| `WireMock/*.cs` | HTTP mocking for external API tests |
| `CronParserTests.cs` | Cron parsing tests |
| `QueuePublisherTests.cs` | Queue publishing tests |
| `DependencyInjectionTests.cs` | DI registration validation |
| `BulletinCreationIntegrationTests.cs` | End-to-end bulletin test |
| `CorridorRankingIntegrationTests.cs` | Corridor ranking integration |
| `DurationCalibrationE2ETests.cs` | Duration calibration E2E |
| `EndToEndTests.cs` | General end-to-end tests |
| `PerformanceProfilingTests.cs` | Performance benchmarks |
| `RealServiceProfilingTests.cs` | Real service profiling |
| `TtsIntegrationTests.cs` | TTS integration tests |
| `TestConfigurationHelper.cs` | Test config loading helper |

### `test/ArnBulletinGenerator.E2E.Tests/`
| File | Purpose |
|---|---|
| `ActivityAuditE2ETests.cs` | Real-host + Azurite E2E: schedule PUT appears in `/api/activity` |

---

## `docs/` — Project Documentation

### `canonical/` — Stable reference docs
| File | Purpose |
|---|---|
| `architecture.md` | Current PoC and production target architecture |
| `decisions.md` | Architecture Decision Record log |

### `reference/` — Supporting reference
| File | Purpose |
|---|---|
| `per-station-configuration.md` | Every setting an operator or administrator can change per station |
| `prompt-iteration-guide.md` | How ARN changes bulletin wording without a developer |
| `job-table-schema.md` | The `radioJobs` Table Storage row contract for non-C# consumers |
| `activity-audit-log.md` | The config-change audit trail and how to read it |
| `azure-naming-convention.md` | ARN's confirmed Azure naming convention |
| `arn-brand-guidelines.md` | ARN branding the operator UI follows |
| `architecture-diagrams.md` | The published architecture figures and when to read each |
| `bulletin-requirements-corpus.md` | Accumulated definition of a good bulletin |
| `atn-bulletin-style-samples.md` | Transcripts of real ATN bulletins — the style ground truth |
| `atn-bulletin-samples-sanitised.md` | The ARN-supplied ATN corpora, sponsor/station wrappers removed |
| `atn-severity-phrasing-palette.md` | HERE data categories mapped to on-air phrasing |
| `atn-audio-corpus/` | Machine transcripts + manifest for the ARN-supplied airchecks |
| `pronunciation/` | Per-market place-name pronunciation guides seeding each dictionary |
| `delivery-device-map.md` | Measured effect of each text-level delivery device on the voice |
| `foundry-role-agents.md` | How pipeline roles route through Foundry prompt agents |
| `here-staleness-landscape-quantified-across-7-markets-vs-official-sources-2026-08-17.md` | Measurement study behind the corroboration decision |
| `playout-delivery-defect-classes.md` | Playout/delivery defect classes observed |
| `arn-voice-candidate-demos.md` | The four ARN-produced voice demo renders |
| `assets/` | ARN brand guidelines PDF, voice-candidate demo audio |

### `operations/` — Operations reference
| File | Purpose |
|---|---|
| `local-development.md` | Local dev environment setup guide |
| `slo.md` | Service level objectives |
| `disaster-recovery.md` | Disaster recovery plan |

### `CODEMAPS/` — Structural maps (this file set)
| File | Purpose |
|---|---|
| `ARCHITECTURE.md` | High-level system overview and data flows |
| `MODULES.md` | Module descriptions, APIs, dependencies |
| `FILES.md` | Full file listing with purposes |

### Infrastructure
| File | Purpose |
|---|---|
| `_manifest.yaml` | Auto-generated doc manifest (~1200 lines) |
| `_sections.yaml` | Doc section definitions |
| `_search.jsonl` | Search index for AI agent retrieval |
| `_data.js` | Doc data bundle |
| `index.html` | Doc portal HTML |
| `infrastructure-visualisation.html` | Architecture Mermaid diagram |

---

## `deployment/` — Infrastructure as Code

| File | Purpose |
|---|---|
| `main.bicep` | Root Bicep orchestrator (~350 lines) |
| `main.bicepparam` | Default parameters |
| `main.dev.bicepparam` | Dev environment overrides |
| `main.test.bicepparam` | Test environment overrides |
| `main.prod.bicepparam` | Production environment overrides |
| `README.md` | Deployment documentation |
| `modules/storage.bicep` | Storage account |
| `modules/function-app.bicep` | Function app + App Service plan |
| `modules/key-vault.bicep` | Key Vault + access policies |
| `modules/ai-services.bicep` | Azure OpenAI + AI Search |
| `modules/monitoring.bicep` | Log Analytics + App Insights |
| `modules/static-web-app.bicep` | Static Web App |
| `modules/action-group.bicep` | Alert action group |
| `modules/alerts.bicep` | Metric/log alert rules |
| `modules/workbooks.bicep` | Workbooks/dashboards |
| `modules/private-endpoints.bicep` | Private endpoint configuration |
| `modules/roles-ai-services.bicep` | AI Services RBAC |
| `modules/roles-delivery-drop.bicep` | Zetta delivery-drop RBAC |
| `modules/roles-key-vault.bicep` | Key Vault RBAC |
| `modules/roles-storage.bicep` | Storage RBAC |
| `modules/linked-backend.bicep` | SWA ↔ Functions proxy |
| `scripts/deploy.sh` | Deployment script |
| `scripts/smoke-test.sh` | Smoke test script |
| `scripts/setup-autoscale.sh` | Autoscale setup |
| `scripts/provision-foundry-agents.sh` | Foundry agent provisioning |

---

## `architecture/` — LikeC4 Architecture-as-Code

| File | Purpose |
|---|---|
| `spec.c4` | LikeC4 specification (element kinds, relationship kinds, tags) |
| `model.c4` | System model (elements, relationships, deployment) |
| `views.c4` | Diagram view definitions (4 named views) |
| `likec4.config.json` | LikeC4 project configuration |
| `images/` | Exported PNG diagram images |

---

## `scripts/`

| File | Purpose |
|---|---|
| `docs.py` | Doc manifest generation, link checking, search index |
| `export-diagrams.sh` | Export LikeC4 diagrams to PNG |
| `local-start.sh` | Start local dev environment (Functions + frontend + Azurite) |
| `local-stop.sh` | Stop local dev environment |
| `test-bulletin.sh` | Local bulletin pipeline test (HTTP or schedule poller). For triggering, `arn trigger --env local` wraps this; still the underlying local engine. |
| `tools/arn/arn.py` | **`arn` — the master testing + ops CLI**: one entrypoint for env / fixtures / run / trigger (`--wait`) / job (incl. approve/reject/publish/regenerate/audio/watch) / perf / safety / report / coverage **+ ops: deploy / settings / logs / pim / ci / foundry / health / config**. |
| `foundry-traces.py` | Foundry agent LLM-call traces via App Insights REST — `list` recent calls, `flow <operation_id>`, `call --agent`. For a KNOWN job, `arn job traces <id>` is the per-job path; this is the cross-call / recent-activity view. |
