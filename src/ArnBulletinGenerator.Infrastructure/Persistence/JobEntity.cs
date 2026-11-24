using Azure;
using Azure.Data.Tables;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     The <c>radioJobs</c> Table Storage row shape for a bulletin generation job.
///     PartitionKey="Job_{market}" (see <see cref="JobEntityMapper.JobPartition"/>), RowKey=jobId.
///     Most nullable string columns are raw JSON snapshots of a <see cref="Domain.Models.Job"/>
///     collection/value-object field; nine of them (<c>ScriptSentencesJson</c>,
///     <c>IncidentsJson</c>, <c>AllIncidentsJson</c>, <c>CorridorsJson</c>, <c>FlowRoadsJson</c>,
///     <c>GeneratorInputJson</c>, <c>SupersededTakeJson</c>, <c>LogsJson</c>, and
///     <c>RejectionHistoryJson</c>) are additionally subject to the gzip-compression contract
///     documented on <see cref="JobEntityMapper"/>.
///     <c>SourceDiagnosticsJson</c> is also passed through the same size-fitting helper, although
///     its normal payload is small and remains plain JSON.
/// </summary>
/// <remarks>
///     The <c>SourceDiagnosticsJson</c> collection is nullable for compatibility with rows written
///     before source diagnostics were introduced; the mapper exposes a missing value as an empty
///     collection on the domain job.
/// </remarks>
internal sealed class JobEntity : ITableEntity
{
    public string Station { get; set; } = default!;
    public string Market { get; set; } = default!;
    public double TargetDurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public string Status { get; set; } = default!;

    public double? MeasuredDurationSec { get; set; }
    public string? ScriptText { get; set; }
    public string? TaggedScriptText { get; set; }
    public string? DeliveryStyleId { get; set; }
    public int? DeliveryStyleVersion { get; set; }
    // Voice profile provenance: which profile's CURRENT tuning the VoiceConfigJson snapshot came
    // from (set at creation and on a regenerate voice override). Plain scalars — same as the
    // delivery-style pins, deliberately outside the gzip-compressed set.
    public string? VoiceProfileId { get; set; }
    public int? VoiceProfileVersion { get; set; }
    public string? BookendId { get; set; }
    public int? BookendVersion { get; set; }
    // Station layer (what an operator's word fix corrects) and the platform-wide layer that
    // applied underneath it. Plain scalars — deliberately outside the gzip-compressed set.
    public string? PronunciationDictionaryId { get; set; }
    public string? PronunciationVersionId { get; set; }
    public string? GlobalPronunciationDictionaryId { get; set; }
    public string? GlobalPronunciationVersionId { get; set; }
    public string? AudioUrl { get; set; }
    public string? AudioSha256 { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public DateTimeOffset? CurationDeadlineUtc { get; set; }
    public DateTimeOffset? FallbackDeadlineUtc { get; set; }
    public DateTimeOffset? MissedDeadlineUtc { get; set; }
    public string? FallbackSourceJobId { get; set; }
    public DateTimeOffset? FallbackAppliedAt { get; set; }
    public string? FallbackReason { get; set; }
    public int FallbackStreak { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedByObjectId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? PublishedByObjectId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    // Durable asynchronous playout-delivery claim and attempt state. Kept as plain scalar columns
    // so the pending transition can be a sparse conditional merge without reading large snapshots.
    public string? DeliveryIdempotencyKey { get; set; }
    public string? DeliveryReviewedEtag { get; set; }
    public string? DeliveryKind { get; set; }
    public string? DeliveryRequestedBy { get; set; }
    public string? DeliveryRequestedByObjectId { get; set; }
    public string? DeliveryFallbackSourceJobId { get; set; }
    public string? DeliveryFallbackAudioUrl { get; set; }
    public string? DeliveryFallbackReason { get; set; }
    public string? DeliveryFallbackNotes { get; set; }
    public double? DeliveryFallbackDurationSeconds { get; set; }
    public int DeliveryAttemptCount { get; set; }
    public DateTimeOffset? DeliveryLastAttemptAt { get; set; }
    public string? DeliveryLastError { get; set; }
    // Continuity provenance: which prior same-day bulletin this run chained from,
    // the prior script it avoided repeating, and the incident delta vs that bulletin.
    public string? PreviousJobId { get; set; }
    public string? PreviousScriptContext { get; set; }
    public string? IncidentChangeSummaryJson { get; set; }
    public string? NarrativeIncidentChangeSummaryJson { get; set; }

    public string? FailedAtStatus { get; set; }
    public int AttemptCount { get; set; }
    public double? LastEstimatedDurationSec { get; set; }

    public string? LogsJson { get; set; }
    public string? IncidentsJson { get; set; }
    public string? AllIncidentsJson { get; set; }
    public string? CorridorsJson { get; set; }
    public string? FlowRoadsJson { get; set; }
    public string? ScriptSentencesJson { get; set; }
    public DateTimeOffset? IncidentsFetchedAt { get; set; }
    public string? VoiceConfigJson { get; set; }
    public string? WordTimingsJson { get; set; }
    public string? PreReviewJson { get; set; }
    // The bulletin a fallback replaced, written once at fallback time. Carries sentences and
    // word timings, so it rides the gzip size-fitting contract like the traffic columns.
    public string? SupersededTakeJson { get; set; }
    // The operator's pending re-voice script edit awaiting publication evidence. Two full
    // scripts, so it rides the same gzip size-fitting contract.
    public string? RevoiceObservationJson { get; set; }
    public string? ContentSelectionJson { get; set; }
    public string? WeatherJson { get; set; }
    public string? FuelJson { get; set; }
    public string? GoodRunRoadsJson { get; set; }
    public string? MonitoredRoutesJson { get; set; }
    public string? CoverageJson { get; set; }
    public string? SourceDiagnosticsJson { get; set; }
    public string? AttemptHistoryJson { get; set; }
    public string? RejectionHistoryJson { get; set; }
    public string? StatusHistoryJson { get; set; }
    public string? GeneratorInputJson { get; set; }
    // Stable references for payloads moved to the immutable payload read model.
    public string? GeneratorInputRef { get; set; }
    public string? IncidentsRef { get; set; }
    public string? AllIncidentsRef { get; set; }
    public string? CorridorsRef { get; set; }
    public string? FlowRoadsRef { get; set; }
    public string? ScriptSentencesRef { get; set; }
    public string? WordTimingsRef { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public double? RadiusKm { get; set; }
    public string? TimezoneId { get; set; }
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

/// <summary>
///     Table Storage row shape for the JobIndex lookup entity, resolving a bare jobId to its
///     per-market partition. PartitionKey="JobIndex", RowKey=jobId, Market holds the original
///     (unnormalized) market value so <see cref="JobEntityMapper.JobPartition"/> can compute the
///     target partition key.
/// </summary>
internal sealed class JobIndexEntity : ITableEntity
{
    public string Market { get; set; } = default!;
    public DateTimeOffset? ReadIndexLastUpdatedAt { get; set; }
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
