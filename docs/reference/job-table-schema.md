---
title: "Job Table Storage Schema"
description: "The radioJobs Azure Table Storage row contract (JobEntity) for non-C# consumers: partition/row key scheme, every column's .NET type/nullability/JSON-vs-scalar shape, the six-column gzip-compression contract with its gz: sentinel and 30,000-char cap, source-fetch diagnostics, ETag/ConcurrencyFingerprint semantics, and the drop-on-incompressible-oversize caveat. Read by the Python harvest/eval tooling that parses these columns directly."
tags:
  - "persistence"
  - "table-storage"
  - "schema"
  - "jobs"
  - "python-tooling"
aliases:
  - "job entity schema"
  - "radioJobs table schema"
  - "JobEntity columns"
  - "job storage columns"
purpose: "reference"
status: "current"
last_verified: 2026-09-02
review_after: 2026-10-31
volatility: "medium"
source_of_truth: "src/ArnBulletinGenerator.Infrastructure/Persistence/JobEntity.cs, JobEntityMapper.cs, TableStorageJobRepository.cs"
verified_by:
  - "manual source review"
  - "./scripts/docs.py check"
  - "test/ArnBulletinGenerator.Infrastructure.Tests/Persistence/JobTableSchemaDocTests.cs"
common_queries:
  - "What columns does the radioJobs table have?"
  - "How is a compressed JobEntity column decoded?"
  - "What is the ConcurrencyFingerprint used for?"
---

# Job Table Storage Schema

The `radioJobs` Azure Table Storage table holds one row per bulletin generation job
(`JobEntity`, mapped by `JobEntityMapper` in
`src/ArnBulletinGenerator.Infrastructure/Persistence/`), plus a second row shape
(`JobIndexEntity`) used purely as a jobId → partition lookup index. This is the contract for
anything reading the table directly (e.g. Python harvest/eval tooling) without going through
the C# repository.

## Partition/row key scheme

- **Job rows**: `PartitionKey = "Job_{market}"` where `market` is the job's market, normalized
  (trimmed + lowercased via `MarketKey.Normalize`); an empty/null market normalizes to
  `"Job_unknown"`. `RowKey = {jobId}` (a GUID string assigned by the system, unique per job).
  Prefix range scans use `PartitionKey ge 'Job_' and PartitionKey lt 'Job\``.
- **JobIndex rows**: `PartitionKey = "JobIndex"`, `RowKey = {jobId}`. A single `Market` column
  holds the *original, unnormalized* market string, so a bare-jobId lookup can recompute the
  Job partition key without a full-table scan. Every job write upserts its index row first;
  without it the job row is unreachable by id.

## Columns (`JobEntity`)

`?` in the .NET Type column means the property is a C# nullable type (`string?`, `int?`, `double?`,
`DateTimeOffset?`). "Compressed" columns are documented in detail in
[Compression contract](#compression-contract-gzip-json-columns) below.

| Column | .NET Type | Payload | Compressed |
| --- | --- | --- | --- |
| `Station` | `string` | scalar | no |
| `Market` | `string` | scalar (unnormalized) | no |
| `TargetDurationSeconds` | `double` | scalar | no |
| `CreatedAt` | `DateTimeOffset` | scalar | no |
| `LastUpdatedAt` | `DateTimeOffset` | scalar | no |
| `Status` | `string` | scalar (`JobStatus` enum name) | no |
| `MeasuredDurationSec` | `double?` | scalar | no |
| `ScriptText` | `string?` | scalar | no |
| `TaggedScriptText` | `string?` | scalar | no |
| `DeliveryStyleId` | `string?` | scalar | no |
| `DeliveryStyleVersion` | `int?` | scalar | no |
| `VoiceProfileId` | `string?` | scalar | no |
| `VoiceProfileVersion` | `int?` | scalar | no |
| `BookendId` | `string?` | scalar | no |
| `BookendVersion` | `int?` | scalar | no |
| `PronunciationDictionaryId` | `string?` | scalar | no |
| `PronunciationVersionId` | `string?` | scalar | no |
| `GlobalPronunciationDictionaryId` | `string?` | scalar | no |
| `GlobalPronunciationVersionId` | `string?` | scalar | no |
| `AudioUrl` | `string?` | scalar | no |
| `AudioSha256` | `string?` | scalar | no |
| `ErrorMessage` | `string?` | scalar | no |
| `ScheduledForUtc` | `DateTimeOffset?` | scalar | no |
| `CurationDeadlineUtc` | `DateTimeOffset?` | scalar | no |
| `FallbackDeadlineUtc` | `DateTimeOffset?` | scalar | no |
| `MissedDeadlineUtc` | `DateTimeOffset?` | scalar | no |
| `FallbackSourceJobId` | `string?` | scalar | no |
| `FallbackAppliedAt` | `DateTimeOffset?` | scalar | no |
| `FallbackReason` | `string?` | scalar | no |
| `FallbackStreak` | `int` | scalar | no |
| `ApprovedBy` | `string?` | scalar | no |
| `ApprovedByObjectId` | `string?` | scalar | no |
| `ApprovedAt` | `DateTimeOffset?` | scalar | no |
| `PublishedBy` | `string?` | scalar | no |
| `PublishedByObjectId` | `string?` | scalar | no |
| `PublishedAt` | `DateTimeOffset?` | scalar | no |
| `DeliveryIdempotencyKey` | `string?` | scalar (delivery claim deduplication key) | no |
| `DeliveryReviewedEtag` | `string?` | scalar (job ETag reviewed when the delivery claim was made) | no |
| `DeliveryKind` | `string?` | scalar (`DeliveryKind` enum name) | no |
| `DeliveryRequestedBy` | `string?` | scalar (delivery requester display name) | no |
| `DeliveryRequestedByObjectId` | `string?` | scalar (delivery requester's immutable Entra object id) | no |
| `DeliveryFallbackSourceJobId` | `string?` | scalar (job supplying fallback audio) | no |
| `DeliveryFallbackAudioUrl` | `string?` | scalar (fallback audio asset URL) | no |
| `DeliveryFallbackReason` | `string?` | scalar (reason for the fallback delivery) | no |
| `DeliveryFallbackNotes` | `string?` | scalar (operator notes for the fallback delivery) | no |
| `DeliveryFallbackDurationSeconds` | `double?` | scalar (fallback audio duration) | no |
| `DeliveryAttemptCount` | `int` | scalar (delivery attempts made) | no |
| `DeliveryLastAttemptAt` | `DateTimeOffset?` | scalar (time of the last delivery attempt) | no |
| `DeliveryLastError` | `string?` | scalar (last delivery error) | no |
| `PreviousJobId` | `string?` | scalar | no |
| `PreviousScriptContext` | `string?` | scalar | no |
| `IncidentChangeSummaryJson` | `string?` | JSON `IncidentChangeSummary` | no |
| `NarrativeIncidentChangeSummaryJson` | `string?` | JSON `IncidentChangeSummary` | no |
| `FailedAtStatus` | `string?` | scalar (`JobStatus` enum name, optional) | no |
| `AttemptCount` | `int` | scalar | no |
| `LastEstimatedDurationSec` | `double?` | scalar | no |
| `LogsJson` | `string?` | JSON `List<JobLog>` | no |
| `IncidentsJson` | `string?` | JSON `List<JobIncident>` | **yes** |
| `AllIncidentsJson` | `string?` | JSON `List<JobIncident>` | **yes** |
| `CorridorsJson` | `string?` | JSON `List<JobCorridor>` | **yes** |
| `FlowRoadsJson` | `string?` | JSON `List<JobFlowRoad>` | **yes** |
| `ScriptSentencesJson` | `string?` | JSON `List<JobScriptSentence>` (per sentence: `CorridorKeys`, `EvidenceIds`, `RouteKeys` — the monitored drive-time runs it names; null on jobs written before 2026-08-24) | **yes** |
| `IncidentsFetchedAt` | `DateTimeOffset?` | scalar | no |
| `VoiceConfigJson` | `string?` | JSON `JobVoiceConfig` | no |
| `WordTimingsJson` | `string?` | JSON `IReadOnlyList<WordTiming>` | no |
| `PreReviewJson` | `string?` | JSON `AudioPreReviewOutcome` | no |
| `SupersededTakeJson` | `string?` | JSON `SupersededTake` — the bulletin a fallback replaced, captured immediately before `ApplyFallback`/`ApplyFillerFallback` overwrote the script (script text, tagged text, its own audio blob name, sentences, word timings, pre-review, delivery-style/bookend pins). Null when the job never generated a script or never fell back. Exactly one take is kept, never a list. | **yes** |
| `RevoiceObservationJson` | `string?` | JSON `RevoiceObservation` — the operator's pending script edit awaiting publication evidence (captured-at, editor, original script, edited script). Written by a re-voice whose words changed; cleared by a delivered publish (which enqueues it for station-lesson learning) or a full regenerate. Null otherwise. | **yes** |
| `ContentSelectionJson` | `string?` | JSON `BulletinContentSelection` (incl. `WeatherRequired` / `FuelRequired` always-include flags) | no |
| `WeatherJson` | `string?` | JSON `WeatherReport` | no |
| `FuelJson` | `string?` | JSON `FuelPriceReport` | no |
| `GoodRunRoadsJson` | `string?` | JSON `List<string>` | no |
| `MonitoredRoutesJson` | `string?` | JSON `List<MonitoredRouteStatus>` | no |
| `CoverageJson` | `string?` | JSON `CoverageArea` | no |
| `SourceDiagnosticsJson` | `string?` | JSON `List<SourceFetchDiagnostic>` (PascalCase fields; `Outcome` is the enum name) | fit via `FitTableProperty` (normally plain) |
| `AttemptHistoryJson` | `string?` | JSON `List<AttemptResult>` | no |
| `RejectionHistoryJson` | `string?` | JSON `List<RejectionEntry>` | no |
| `StatusHistoryJson` | `string?` | JSON `List<StatusHistoryEntry>` | no |
| `GeneratorInputJson` | `string?` | JSON `GeneratorInputSnapshot` (including `EditedIncidents`) | **yes** |
| `GeneratorInputRef` | `string?` | scalar (reference to the immutable external generator-input payload) | no |
| `IncidentsRef` | `string?` | scalar (reference to the immutable external incidents payload) | no |
| `AllIncidentsRef` | `string?` | scalar (reference to the immutable external all-incidents payload) | no |
| `CorridorsRef` | `string?` | scalar (reference to the immutable external corridors payload) | no |
| `FlowRoadsRef` | `string?` | scalar (reference to the immutable external flow-roads payload) | no |
| `ScriptSentencesRef` | `string?` | scalar (reference to the immutable external script-sentences payload) | no |
| `WordTimingsRef` | `string?` | scalar (reference to the immutable external word-timings payload) | no |
| `Latitude` | `double?` | scalar | no |
| `Longitude` | `double?` | scalar | no |
| `RadiusKm` | `double?` | scalar | no |
| `TimezoneId` | `string?` | scalar | no |
| `PartitionKey` | `string` | scalar (key) | no |
| `RowKey` | `string` | scalar (key) | no |
| `Timestamp` | `DateTimeOffset?` | storage-assigned | no |
| `ETag` | `ETag` | storage-assigned optimistic-concurrency token | no |

## Source fetch diagnostics

`SourceDiagnosticsJson` is nullable for rows written before source diagnostics existed. The mapper
deserialises a missing or null value as an empty list. Current rows contain one object per consulted
or jurisdiction-skipped upstream source, with PascalCase fields:
`SourceId`, `Outcome`, `Detail`, `DataAsOfUtc`, and `StaleSeconds`. `Outcome` is stored by enum name
and is one of `"Ok"`, `"ServedStale"`, `"Failed"`, or `"Skipped"` (never an integer). The payload is
normally small and stored as plain JSON, but it passes through `FitTableProperty` and therefore
follows the same `gz:` expansion and drop-on-oversize safety behavior if an unexpectedly large
value is ever written.

## Emergency-services events

`EmergencyEventsJson` is nullable for rows written before the emergency feed existed, when the feed
is disabled for the environment, and when its fetch failed — the mapper deserialises a missing or
null value as an empty list, and the feed is fail-soft by design. Current rows contain one object per
emergency incident inside the coverage area, ordered worst-tier-then-nearest, with PascalCase fields:
`Id`, `SourceFeedId`, `Agency`, `State`, `Title`, `WarningLevel`, `EventType`, `EventCategory`,
`Suburb`, `Address`, `Latitude`, `Longitude`, `DistanceKm`, `Status`, `ReportedUtc`, `UpdatedUtc`,
`SourceUrl`, `Detail`, `Licence`, and `AirEligible`.

`WarningLevel` is the Australian Warning System tier, stored by enum name and one of `"None"`,
`"Advice"`, `"WatchAndAct"`, or `"EmergencyWarning"` (never an integer). It is the only escalation
axis carried: the upstream also publishes CAP `severity`/`urgency`/`certainty`, which are deliberately
not persisted because `severity` is derived from ambulance case acuity on some feeds and therefore
carries no broadcast meaning.

`Id` is the revision-collapsed identity, not the raw upstream id: some feeds embed an update
timestamp in theirs, so one live incident is published several times and only the newest revision is
kept. `Licence` and `AirEligible` record whether the source is legally usable by a commercial
broadcaster (`CC BY-ND` forbids derivatives, `CC BY-NC-ND` forbids commercial use); records that fail
the check are still persisted so the console can show that they were seen and suppressed.

**This column is operator evidence only.** Nothing in it reaches the writer, and the upstream carries
no closure, lane or delay field, so no value here is a road-network claim.

## Source-native event identities

`IncidentsJson`, `AllIncidentsJson`, and the incident records embedded in
`GeneratorInputJson` may include `SourceRecordIdentities`. Each identity keeps the
provider scope (`SourceId`), the provider's current record identifier (`RecordId`),
and, when the provider exposes a revision chain, its stable event identifier
(`OriginalRecordId`). The source scope is part of the identity because record IDs
are not globally unique across official feeds.

Official-feed receipts in `CorridorsJson` carry the same provider record identifier
as `OfficialEvidence.SourceRecordId`. These fields are optional for legacy rows and
are populated only when the source exposes an identifier; the application must not
invent IDs from headlines or geometry.

Consumers that calculate continuity or the Event Ledger must use the stable event key resolved
from these identities (`SourceId` plus `OriginalRecordId` when present, otherwise `RecordId`),
not a provider's mutable current record ID. When multiple official records corroborate one HERE
corridor, each identity is attached to the nearest matching HERE incident; the corridor-level
source list remains an aggregate and must not be used as per-incident attribution.
The persisted corridor also carries `CorroboratingSourceCount` when fusion has separated current
condition observations from enrichment provenance. `Sources` is the operator-facing list of every
feed that contributed anything, including a planned-work permit; `CorroboratingSourceCount` counts
distinct sources observing the condition now, with a minimum of one. The nullable field is absent on
legacy rows, whose DTO mapping falls back to the historical `Sources` count.

For pre-array rows, `CorridorsJson` may contain one `OfficialEvidence` object instead of
`OfficialEvidences`. The repository reads that legacy property and promotes it to a one-item
receipt list; current writes use only the plural array shape.

## Compression contract (gzip JSON columns)

Azure Table Storage hard-caps a single string property at 64 KB (32K UTF-16 chars). Eight columns
scale with fetched traffic data (or, for the retained take and the pending re-voice observation,
with script text, sentences and word timings) and can exceed a lower, defensive cap well before
that hard limit:
`ScriptSentencesJson`, `IncidentsJson`, `AllIncidentsJson`, `CorridorsJson`, `FlowRoadsJson`,
`GeneratorInputJson`, `SupersededTakeJson`, and `RevoiceObservationJson`.

On write (`JobEntityMapper.FitTableProperty`): if the serialized JSON for one of these columns is
**≤ 30,000 characters**, it is stored as-is (plain JSON). If it exceeds 30,000 characters, it is
gzip-compressed and base64-encoded, then prefixed with the literal sentinel `"gz:"` — a value
never produced by plain JSON, so detection is unambiguous. If the compressed form is *still* over
30,000 characters, the column is dropped (stored as `null`) as a last resort, rather than
aborting the entire job write — see "Drop-on-oversize caveat" below.

To decode a stored value for one of those seven columns:

1. If the value does not start with `"gz:"`, it is plain JSON — parse it directly.
2. Otherwise, strip the 3-character `"gz:"` prefix.
3. Base64-decode the remainder to raw bytes.
4. Gunzip-decompress those bytes.
5. Decode the result as UTF-8 to recover the original JSON text.

Python consumers reading these columns MUST implement this same `gz:` check before parsing —
treating a compressed value as plain JSON will fail to parse.

## ETag semantics

`ETag` is an opaque, storage-assigned optimistic-concurrency token (not authored job data).
Table Storage returns a fresh `ETag` on every successful write; a subsequent conditional update
(`If-Match`) with a stale `ETag` is rejected with HTTP 412. The domain `Job` aggregate carries the
`ETag` it was loaded with (as a plain string) and uses it for `SaveJobAsync`'s conditional update —
on a 412 conflict, the repository re-reads the row, reconciles logs if the conflict was benign
(see `ConcurrencyFingerprint` below), and retries with the freshly-read `ETag`.

## ConcurrencyFingerprint semantics

`ConcurrencyFingerprint` is **not a persisted column** — it is a SHA-256 hex digest computed by
`JobEntityMapper.ComputeFingerprint` at load time over an entity's material (state) columns only
(`Status`, `StatusHistoryJson`, `ScriptText`, `AudioUrl`, `AudioSha256`, `ApprovedBy`,
`ApprovedByObjectId`, `ApprovedAt`, `PublishedBy`, `PublishedByObjectId`, `PublishedAt`,
`RejectionHistoryJson`, `ErrorMessage`, `AttemptCount`, `FallbackStreak`) — explicitly excluding
logs, `ETag`, and `Timestamp`. `SaveJobAsync` recomputes this fingerprint against the freshly
reloaded row after an `ETag` 412 conflict: an unchanged fingerprint means only a concurrent log
append happened underneath the save (safe to merge logs and retry), while a changed fingerprint
means a real concurrent state change occurred (e.g. an approve-vs-reject race), which is surfaced
as a conflict rather than silently overwritten.

## Drop-on-oversize caveat

For the six compressed columns, if the gzip-compressed, base64-encoded form is *still* over the
30,000-character cap, `JobEntityMapper.FitTableProperty` drops the column entirely (persists
`null` for it) rather than failing the whole job write. This is a deliberate last resort: an
incident-heavy market's traffic snapshot exceeding even the compressed cap on every job would
otherwise silently fail every write for that market. Core fields (script, audio, word timings,
status) always persist regardless. A dropped column is logged as a warning
(`[JobPersist] ... dropping it so the job still persists.`) — treat a `null` value on one of the
six compressed columns as "possibly dropped for size", not necessarily "never populated".
