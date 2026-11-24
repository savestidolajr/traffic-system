using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================
// Component DTOs - Shared/Common types
// =============================================================================

public sealed record TimeWindowDto(string From, string To);

public sealed record BrandingDto(string StationTag, string SignOff);

public sealed record JobInputDto(
    string BulletinType,
    TimeWindowDto TimeWindow,
    BrandingDto Branding,
    VoiceConfigDto Voice);

public sealed record ApprovalDecisionDto(
    string DecidedAt,
    string DecidedBy);

public sealed record RejectionDataDto(
    string Reason,
    string DecidedAt,
    string DecidedBy);

/// <summary>
///     Represents a single rejection entry with feedback for regeneration.
/// </summary>
public sealed record RejectionHistoryEntryDto(
    string Reason,
    string Notes,
    string? RejectedScript,
    string At,
    string By);

public sealed record PublishDataDto(
    string Destination,
    string PublishedAt,
    string PublishedBy);

public sealed record FallbackDataDto(
    string SourceJobId,
    string AppliedAt,
    string Reason,
    int Streak);

/// <summary>Durable delivery claim metadata exposed while a queued write is in flight.</summary>
public sealed record DeliveryPendingDataDto(
    string Kind,
    string IdempotencyKey,
    string ReviewedEtag,
    int AttemptCount,
    string RequestedBy);

/// <summary>
///     The bulletin the job generated before a fallback replaced it, read-only. Exists so an
///     operator (and a post-hoc review of the writer) can still read and hear what the pipeline
///     produced on a slot the standby cart or an earlier bulletin covered.
/// </summary>
public sealed record SupersededTakeDto(
    string CapturedAt,
    string Reason,
    string Text,
    string? TaggedText,
    string AudioUrl,
    double? MeasuredDurationSec,
    double? EstimatedDurationSec,
    IReadOnlyList<ScriptSentenceDto> Sentences,
    IReadOnlyList<WordTimingDto>? WordTimings,
    AudioPreReviewDto? PreReview,
    string? DeliveryStyleId,
    int? DeliveryStyleVersion);

public sealed record FailureDataDto(
    string Stage,
    string Message,
    string OccurredAt,
    int AttemptCount,
    double? LastEstimatedDurationSec,
    double TargetDurationSec,
    IReadOnlyList<AttemptResultDto> AttemptHistory);

public sealed record AttemptResultDto(
    int AttemptNumber,
    double? EstimatedDurationSec,
    double? ActualDurationSec,
    bool Succeeded,
    string? FailureReason,
    string At);

public sealed record LogEntryDto(
    string At,
    JobLogLevel Level,
    string Message);
/// <summary>Operator-facing outcome for one upstream source used by a bulletin.</summary>
public sealed record SourceFetchDiagnosticDto(
    string SourceId,
    string Outcome,
    string? Detail,
    string? DataAsOfUtc,
    int? StaleSeconds);

public sealed record StatusHistoryItemDto(
    string Status,
    string At,
    string By,
    string Note);


/// <summary>
///     Composite DTO wrapping script data for job detail views.
/// </summary>
public sealed record ScriptDto(ScriptDataDto Data);

/// <summary>
///     Composite DTO grouping audio data with measured duration metrics and optional
///     word-level timings for click-to-seek playback (approximate; see backend docs).
/// </summary>
public sealed record AudioDto(
    AudioDataDto Data,
    double MeasuredDurationSec,
    double DeltaSec,
    IReadOnlyList<WordTimingDto>? WordTimings = null);

public sealed record WordTimingDto(string Word, double StartSec, double EndSec);

// =============================================================================
// Job Summary DTO - For list views (minimal data)
// =============================================================================

/// <summary>
///     One row of a job list. <see cref="MeasuredDurationSec" /> and <see cref="DeltaSec" /> are
///     null until synthesis has produced audio for the job.
///     <para>
///         Deliberately ONE flat record, not a base/derived pair. It used to be
///         <c>JobSummaryDtoBase</c> + <c>CompletedJobSummaryDto</c>, and because
///         <see cref="JobsListResponseDto.Items" /> declares the element type, System.Text.Json
///         serialized every row as the BASE type and silently dropped both measurements from
///         every list response: the console's Duration and delta columns, their two column sorts
///         and the CSV export were all dead, with no error anywhere. (Root-level polymorphism DOES
///         use the runtime type, which is why job DETAIL responses were unaffected and the defect
///         stayed invisible.) A single record cannot regress that way. Do not reintroduce a
///         hierarchy here without also declaring <c>[JsonPolymorphic]</c>/<c>[JsonDerivedType]</c>
///         and a serialization test that reads the emitted JSON.
///     </para>
/// </summary>
public sealed record JobSummaryDto
{
    public string JobId { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string Station { get; init; } = "";
    public string Market { get; init; } = "";
    public double TargetDurationSec { get; init; }
    public string Status { get; init; } = "";
    public string LastUpdatedAt { get; init; } = "";

    /// <summary>Azure Table ETag of the row this DTO was mapped from, for the operator UI's
    ///  optimistic-concurrency precondition on approve/publish decisions. Null for a job not
    ///  yet loaded from storage.</summary>
    public string? Etag { get; init; }

    /// <summary>Measured audio duration in seconds; null until the job has audio.</summary>
    public double? MeasuredDurationSec { get; init; }

    /// <summary><see cref="MeasuredDurationSec" /> minus <see cref="TargetDurationSec" />;
    ///  null until the job has audio. Positive means the read ran over its slot.</summary>
    public double? DeltaSec { get; init; }
}

public sealed record JobsListResponseDto(
    IReadOnlyList<JobSummaryDto> Items,
    int Total,
    bool HasMore = false);

// =============================================================================
// Job Detail DTOs - One per stage, discriminated by Status
//
// Hierarchy eliminates per-stage field duplication:
//   JobDetailDtoBase → JobWithTrafficDtoBase → JobWithIncidentsDtoBase
//     → JobWithScriptDtoBase → JobWithAudioDtoBase
// =============================================================================
public sealed record BulletinEditingGuidanceDto(
    int TargetWordCount,
    int MinWordCount,
    int MaxWordCount,
    double WordsPerMinute,
    double TargetDurationSeconds);


public abstract record JobDetailDtoBase
{
    public string JobId { get; init; } = "";
    public string Station { get; init; } = "";
    /// <summary>
    ///     Per-upstream-source fetch outcomes captured for this bulletin. Empty for legacy jobs.
    /// </summary>
    public IReadOnlyList<SourceFetchDiagnosticDto> SourceDiagnostics { get; init; } = [];
    public string Market { get; init; } = "";
    public double TargetDurationSec { get; init; }
    public string CreatedAt { get; init; } = "";
    public string LastUpdatedAt { get; init; } = "";
    public JobInputDto Input { get; init; } = null!; // Always set by mapper via ApplyBase
    public IReadOnlyList<LogEntryDto> Logs { get; init; } = [];
    public IReadOnlyList<StatusHistoryItemDto> StatusHistory { get; init; } = [];
    public string Status { get; init; } = "";

    /// <summary>Azure Table ETag of the row this DTO was mapped from, for the operator UI's
    ///  optimistic-concurrency precondition on approve/publish decisions. Null for a job not
    ///  yet loaded from storage.</summary>
    public string? Etag { get; init; }
    /// <summary>Moment the curation sweep stops waiting; empty when curation is disabled or unknown.</summary>
    public string CurationDeadlineUtc { get; init; } = "";

    /// <summary>WS1 content sources used for this bulletin; null = legacy traffic-only.</summary>
    public ContentSelectionDto? Content { get; init; }

    /// <summary>Weather data the bulletin drew from, for review verification. Null when not used.</summary>
    public WeatherReportDto? Weather { get; init; }

    /// <summary>Fuel price data the bulletin drew from, for review verification. Null when not used.</summary>
    public FuelReportDto? Fuel { get; init; }

    /// <summary>Priority roads reported as flowing well in this bulletin. Null/empty = none claimed.</summary>
    public IReadOnlyList<string>? GoodRuns { get; init; }

    /// <summary>Point-in-time coverage geometry snapshot used to generate this
    ///  bulletin; null for legacy circle-only jobs (the map falls back to bbox).</summary>
    public CoverageDto? Coverage { get; init; }

    /// <summary>The STATION-scope ElevenLabs pronunciation dictionary applied to this bulletin's
    ///  synthesis (provenance); null = none attached. Lets the operator confirm which region
    ///  pronunciations aired, and is the layer their word fix corrects.</summary>
    public string? PronunciationDictionaryId { get; init; }
    /// <summary>Exact ElevenLabs pronunciation dictionary version applied to this bulletin's synthesis (provenance); null for legacy jobs without a pinned version.</summary>
    public string? PronunciationVersionId { get; init; }

    /// <summary>The platform-wide (global-scope) dictionary that also applied, underneath the
    ///  station's; null = none was active. Shown separately so provenance stays exact.</summary>
    public string? GlobalPronunciationDictionaryId { get; init; }
    /// <summary>Exact version of <see cref="GlobalPronunciationDictionaryId" /> that applied; null when none did.</summary>
    public string? GlobalPronunciationVersionId { get; init; }

    /// <summary>Word/time editing band for a verbatim re-voice edit (target + min/max the audio
    ///  pipeline can stretch/compress to the slot); null unless the job is operator-editable.</summary>
    public BulletinEditingGuidanceDto? EditingGuidance { get; init; }

    /// <summary>Pre-approval audio review (transcript of the FINAL audio vs the script):
    ///  match ratio, words with no intelligible rendering, and aired quantities that could
    ///  not be verified. Null for pre-feature jobs or when transcription was unavailable.</summary>
    public AudioPreReviewDto? PreReview { get; init; }

    /// <summary>The bulletin this job generated before a fallback replaced it, read-only. Null
    ///  when the job never generated a script or never fell back. Declared on the BASE because a
    ///  fallback job, the published job that covered its slot, and a failed job all need it.</summary>
    public SupersededTakeDto? SupersededTake { get; init; }
}

public sealed record AudioPreReviewDto(
    double MatchRatio,
    IReadOnlyList<string> SuspectWords,
    IReadOnlyList<string> NumberMismatches);

public sealed record WeatherReportDto(
    string Conditions,
    double TemperatureC,
    string? WindDescription,
    int PrecipitationProbabilityPercent,
    IReadOnlyList<string> Alerts);

public sealed record FuelSummaryDto(string FuelType, double MinPrice, double AveragePrice, int StationCount, DateTimeOffset? LatestModifiedUtc = null);

public sealed record FuelReportDto(IReadOnlyList<FuelSummaryDto> Summaries);

public abstract record JobWithTrafficDtoBase : JobDetailDtoBase
{
    public required TrafficDataDto Traffic { get; init; }
}

public abstract record JobWithIncidentsDtoBase : JobWithTrafficDtoBase
{
    public required IReadOnlyList<JobIncidentDto> SelectedIncidents { get; init; }
}

public abstract record JobWithScriptDtoBase : JobWithIncidentsDtoBase
{
    public required ScriptDto Script { get; init; }
}

public abstract record JobWithAudioDtoBase : JobWithScriptDtoBase
{
    public required AudioDto Audio { get; init; }
}

// --- Leaf stage DTOs ---

public sealed record RequestedJobDto : JobDetailDtoBase;

public sealed record TrafficFetchedJobDto : JobWithTrafficDtoBase;

public sealed record IncidentsSelectedJobDto : JobWithIncidentsDtoBase;

public sealed record ScriptReadyJobDto : JobWithScriptDtoBase;

public sealed record SynthesizingAudioJobDto : JobWithAudioDtoBase;

public sealed record AudioReadyJobDto : JobWithAudioDtoBase;

public sealed record WaitingApprovalJobDto : JobWithAudioDtoBase;

public sealed record ApprovedJobDto : JobWithAudioDtoBase
{
    public required ApprovalDecisionDto Approval { get; init; }
}

public sealed record DeliveryPendingJobDto : JobWithAudioDtoBase
{
    public required DeliveryPendingDataDto Delivery { get; init; }
}

public sealed record RejectedJobDto : JobWithAudioDtoBase
{
    public required RejectionDataDto Rejection { get; init; }

    /// <summary>
    ///     Full history of all rejection attempts with feedback.
    /// </summary>
    public required IReadOnlyList<RejectionHistoryEntryDto> RejectionHistory { get; init; }
}

public sealed record PublishedJobDto : JobWithAudioDtoBase
{
    public required ApprovalDecisionDto Approval { get; init; }

    public required PublishDataDto Publish { get; init; }

    /// <summary>
    ///     Present when the audio that aired came from the fallback chain rather than this
    ///     slot's own bulletin: a repeat of an earlier bulletin, or the standby cart.
    ///     <para>
    ///         Load-bearing since coverage started DELIVERING (2026-08-11). Before that a
    ///         covered slot stayed <c>FallbackUsed</c> forever, so provenance was implied by
    ///         the status; now the sweep publishes what it covers, and without this field a
    ///         published job looks identical whether it aired its own read or somebody else's
    ///         from three hours earlier. Null on the ordinary path.
    ///     </para>
    /// </summary>
    public FallbackDataDto? Fallback { get; init; }
}

public sealed record FallbackJobDto : JobWithAudioDtoBase
{
    public required FallbackDataDto Fallback { get; init; }
}

/// <summary>
///     A slot that aired nothing because the bulletin was never approved before the playout
///     deadline. Distinct from <see cref="RejectedJobDto" />: nobody acted - the deadline
///     passed while the bulletin still waited for approval.
/// </summary>
public sealed record MissedJobDto : JobWithAudioDtoBase
{
    public required MissedDataDto Missed { get; init; }
}

public sealed record MissedDataDto(string DeadlineAt, string At);

public sealed record FailedJobDto : JobDetailDtoBase
{
    public required FailureDataDto Failure { get; init; }

    /// <summary>
    ///     Traffic data and selected incidents available at the time of failure, enabling operator inspection
    ///     and curation before retry.
    /// </summary>
    public TrafficDataDto? Traffic { get; init; }
    public IReadOnlyList<JobIncidentDto>? SelectedIncidents { get; init; }

    /// <summary>
    ///     The strongest script/audio produced before the retry budget was exhausted. These are
    ///     inspection-only artifacts: the audio is stored unpublished and the job remains FAILED.
    /// </summary>
    public ScriptDto? Script { get; init; }
    public AudioDto? Audio { get; init; }
}

// =============================================================================
// Request/Response DTOs for Actions
// =============================================================================

/// <summary>
/// Wire DTO for taking a bulletin off the air path. Reason and notes are both optional;
/// operational notes stay on the job audit record.
/// </summary>
public sealed record RejectJobRequest(
    RejectionReasonCode? ReasonCode = null,
    string? Notes = null,
    string? ExpectedEtag = null);

/// <summary>
///     Wire DTO — mandatory revision precondition, same semantics as <see cref="ApproveJobRequest"/>:
///     the delete succeeds only while the job still carries the revision the operator reviewed, so
///     a concurrent approve/publish can never be destroyed blind (observed live 2026-08-05: a
///     DELETE 204 won a race against an in-flight approve and deleted the just-approved bulletin
///     and its audio unrecoverably). The C# default stays <c>null</c> only so the wire DTO can
///     deserialise a body that omits it — that path is a 428 rejection, not a bypass.
/// </summary>
public sealed record DeleteJobRequest(string? ExpectedEtag = null);

/// <summary>
///     Wire DTO — <b>mandatory</b> revision precondition. <see cref="ExpectedEtag"/> must carry
///     the ETag of the job revision the operator reviewed: absent (null/empty) or stale, the
///     approve endpoint answers 428 and the job is never saved, so an approval can never attach
///     to a bulletin nobody actually looked at. The C# default stays <c>null</c> only so the
///     wire DTO can deserialise a body that omits it — that path is a rejection, not a bypass.
/// </summary>
public sealed record ApproveJobRequest(string? ExpectedEtag = null);
public sealed record ProceedCurationRequest(
    string? ExpectedEtag = null,
    IReadOnlyList<EditedIncidentDto>? EditedIncidents = null);

/// <summary>
///     Wire DTO — mandatory revision precondition, same semantics as <see cref="ApproveJobRequest"/>.
/// </summary>
public sealed record PublishJobRequest(string? ExpectedEtag = null);

/// <summary>
///     Wire DTO for choosing what airs in a slot: <see cref="Kind"/> is the wire string
///     <c>"repeat"</c> (re-air <see cref="SourceJobId"/>) or <c>"cart"</c> (the filler cart).
///     <see cref="ExpectedEtag"/> carries the revision of the slot job the operator reviewed,
///     so the override can never attach to a bulletin that changed underneath them — same
///     precondition contract as <see cref="ApproveJobRequest"/>. The kind strings are parsed
///     at the trigger boundary; anything else is a 400 naming the accepted values.
/// </summary>
public sealed record SetAirSourceRequest(
    string? Kind = null,
    string? SourceJobId = null,
    string? ExpectedEtag = null);

/// <summary>
///     Wire DTO — nullable fields converted to <see cref="Option{T}" /> at the trigger boundary.
/// </summary>
public sealed record RegenerateJobRequest(
    string? Context = null,
    double? TargetDurationSec = null,
    string? RejectionReason = null,
    string? RejectionDescription = null,
    string? EditedScript = null,
    IReadOnlyList<string>? SelectedCorridors = null,
    string? DeliveryIntent = null,
    /// <summary>The approved words with the operator's v3 audio tags already placed.</summary>
    string? TonedScript = null,
    IReadOnlyList<string>? SelectedGoodRuns = null,
    bool RevoiceOnly = false,
    IReadOnlyList<EditedIncidentDto>? EditedIncidents = null,
    /// <summary>
    ///     Re-resolve the attached pronunciation dictionary to its CURRENT version instead of
    ///     replaying the version this job aired with. Set when the operator changed a rule
    ///     during review — without it, synthesis replays the pinned version and the fix is a
    ///     silent no-op. Default false keeps stale-recovery republishes byte-faithful.
    /// </summary>
    bool RefreshPronunciation = false,
    /// <summary>
    ///     Re-resolve the attached delivery style to its CURRENT version instead of replaying
    ///     the version this job aired with. Same contract as
    ///     <paramref name="RefreshPronunciation" />: set when the operator edited the style
    ///     during review, default false so a republish reproduces the read that aired.
    /// </summary>
    bool RefreshDelivery = false,
    /// <summary>
    ///     Operator change requests batched into this regenerate (pronunciation fixes, comments,
    ///     free-form instructions). Folded into the producer-feedback channel the LLM treats as
    ///     authoritative. The frontend always sent these; until the field existed
    ///     (System.Text.Json silently drops unknown members) they never reached the pipeline and
    ///     an instruction only made it to the LLM via the reject notes.
    /// </summary>
    IReadOnlyList<RegenerateFeedbackDto>? Feedback = null,
    /// <summary>
    ///     The operator chose a DIFFERENT voice during review. Resolved server-side to the
    ///     profile's CURRENT version — the same trust rule as <see cref="CreateJobCommand.VoiceProfileId" />:
    ///     never a client-supplied snapshot, so per-profile tuning (PaceBoostWpm etc.) cannot
    ///     silently drop off. Applies to BOTH the re-voice path and a full regenerate; a profile
    ///     that cannot be resolved hard-fails with a message naming it.
    /// </summary>
    string? VoiceProfileId = null);

/// <summary>
///     Operator's per-incident pick on the job-review rundown. <paramref name="Included" /> = false
///     drops that incident's HERE id from the next regeneration's fresh fetch (cleared ones ignored).
/// </summary>
public sealed record EditedIncidentDto(
    string IncidentId,
    bool Included = true,
    int Priority = 0);

/// <summary>
///     Wire DTO — one operator change request batched into a regenerate. <paramref name="Kind" /> is
///     the change family ("instruction" / "comment" / "pronunciation"), <paramref name="Category" />
///     an optional rejection-style label (e.g. "Delivery / tone"), <paramref name="Target" /> the
///     script text the change anchors to when it is selection-scoped, and <paramref name="Text" />
///     the body. Each item is rendered into the producer-feedback channel so the LLM treats it as
///     authoritative direction — the same authority a rejection note carries.
/// </summary>
public sealed record RegenerateFeedbackDto(
    string Kind,
    string? Category = null,
    string? Target = null,
    string? Text = null);

public sealed record JobActionResponseDto(
    string Status,
    string JobId,
    string Message);
