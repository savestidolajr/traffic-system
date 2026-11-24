using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Lifecycle status of the bulletin Job aggregate root, which tracks script, audio, approval, and
///     audit history through this state machine from request to delivery.
///     <para>
///         <b>APPEND ONLY — never insert or reorder.</b> <c>StatusHistory</c> is persisted with the
///         enum's INTEGER ordinal (<c>JobEntityMapper</c> serialises it with no string-enum
///         converter), so inserting a member renumbers every later one and silently rewrites the
///         meaning of every history row already in Table Storage. That happened on 2026-08-19 when
///         <see cref="AwaitingCuration" /> was added after <see cref="IncidentsSelected" />: a
///         stored 3 (ScriptReady) began rendering as AwaitingCuration and a stored 9 (Published) as
///         Rejected, so healthy historical jobs read as failures in the console. Hence the ordering
///         below is chronologically odd on purpose — AwaitingCuration occurs between
///         IncidentsSelected and ScriptReady in the LIFECYCLE, but sits last in the ENUM so every
///         pre-existing ordinal keeps its original meaning. Nothing compares these values
///         relationally, so declaration order carries no semantics beyond the wire format.
///     </para>
/// </summary>
public enum JobStatus
{
    Requested,
    HereFetched,
    IncidentsSelected,
    ScriptReady,
    SynthesizingAudio,
    AudioReady,
    WaitingApproval,
    Approved,
    Rejected,
    Published,
    FallbackUsed,
    Failed,
    Missed,
    /// <summary>
    ///     Parked for operator curation between <see cref="IncidentsSelected" /> and
    ///     <see cref="ScriptReady" />. Declared after the historical members deliberately — see the
    ///     type's remarks.
    AwaitingCuration,
    /// <summary>
    ///     The operator decision has been durably claimed for asynchronous playout delivery.
    ///     Declared after all historical members so persisted integer status ordinals remain stable.
    /// </summary>
    DeliveryPending,

    /// <summary>Delivery was explicitly cancelled before external playout completed.</summary>
    Cancelled
}

public sealed class Job
{
    private readonly List<AttemptResult> _attemptHistory = [];
    private readonly List<JobCorridor> _corridors = [];
    private readonly List<JobScriptSentence> _scriptSentences = [];
    private readonly List<JobIncident> _incidents = [];
    private readonly List<JobIncident> _allIncidents = [];
    private readonly List<JobFlowRoad> _flowRoads = [];
    private readonly List<JobLog> _logs = [];
    private readonly List<RejectionEntry> _rejectionHistory = [];
    private readonly List<StatusHistoryEntry> _statusHistory = [];

    /// <summary>
    ///     Creates a new <see cref="Job" /> with validation, returning a <see cref="Result{T}" />.
    ///     Use this instead of the direct constructor.
    /// </summary>
    public static Result<Job> Create(
        string jobId,
        string station,
        string market,
        double targetDurationSeconds,
        DateTime utcNow,
        Option<double> latitude = default,
        Option<double> longitude = default,
        Option<double> radiusKm = default,
        Option<string> timezoneId = default,
        Option<CoverageArea> coverage = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Result<Job>.Failure("Job ID is required.");
        if (string.IsNullOrWhiteSpace(station))
            return Result<Job>.Failure("Station is required.");
        if (string.IsNullOrWhiteSpace(market))
            return Result<Job>.Failure("Market is required.");

        var job = new Job
        {
            JobId = jobId,
            Station = station,
            Market = market,
            TargetDurationSeconds = targetDurationSeconds,
            Latitude = latitude,
            Longitude = longitude,
            RadiusKm = radiusKm,
            TimezoneId = timezoneId,
            Coverage = coverage
        };
        job.CreatedAt = utcNow;
        job.LastUpdatedAt = utcNow;
        job.Status = JobStatus.Requested;
        job._statusHistory.Add(new StatusHistoryEntry(JobStatus.Requested, job.CreatedAt, "system"));
        return Result<Job>.Success(job);
    }

    /// <summary>
    ///     Private constructor for reconstitution — skips the automatic Requested history entry.
    /// </summary>
    private Job()
    {
    }

    public string JobId { get; init; } = null!;
    public string Station { get; init; } = null!;
    public string Market { get; init; } = null!;
    /// <summary>
    ///     Left publicly settable: the application layer's job-regeneration workflow legitimately
    ///     applies an operator-supplied override on regeneration (RegenerateJobCommand.TargetDurationSec).
    /// </summary>
    public double TargetDurationSeconds { get; set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     Left publicly settable: the multi-step generation pipeline (BroadcastOrchestrator) "touches"
    ///     this directly between phase saves without a full status transition, independent of
    ///     <see cref="TransitionTo" />.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }
    public JobStatus Status { get; private set; }

    // Optional / Computed
    public Option<double> MeasuredDurationSec { get; set; }
    public Option<string> ScriptText { get; set; }

    /// <summary>Complete snapshot of the writer input that produced this script, for deterministic replay.</summary>
    public GeneratorInputSnapshot? GeneratorInput { get; set; }

    /// <summary>
    ///     The synthesised script — the plain <see cref="ScriptText"/> with ElevenLabs v3
    ///     inline audio tags applied by the director. None when no tagging occurred (neutral
    ///     style, non-v3 model, or director fallback), in which case the plain script was spoken.
    /// </summary>
    public Option<string> TaggedScriptText { get; set; }

    /// <summary>The delivery style applied to this bulletin (audio-tag policy). None = neutral/today.</summary>
    public Option<string> DeliveryStyleId { get; set; }

    /// <summary>The version of <see cref="DeliveryStyleId"/> applied, for provenance.</summary>
    public Option<int> DeliveryStyleVersion { get; set; }

    /// <summary>
    ///     The bookend pair read around this bulletin, and its library version, for provenance.
    ///     None = the station had no bookend (or it could not be resolved and was dropped). The
    ///     resolved TEXT is already inside <see cref="ScriptText" /> — these record WHICH library
    ///     entry produced it, so a wording change can be traced to the reads it affected.
    /// </summary>
    public Option<string> BookendId { get; set; }

    /// <inheritdoc cref="BookendId" />
    public Option<int> BookendVersion { get; set; }

    /// <summary>The STATION-scope ElevenLabs pronunciation dictionary applied to this bulletin's
    ///     synthesis, for provenance. None = the station had none attached (the operator can't
    ///     otherwise tell which region pronunciations aired). This is the layer an operator's
    ///     one-word fix targets — see JobPronunciationService.</summary>
    public Option<string> PronunciationDictionaryId { get; set; }

    /// <summary>The pronunciation dictionary version id applied, paired with
    ///     <see cref="PronunciationDictionaryId"/>.</summary>
    public Option<string> PronunciationVersionId { get; set; }

    /// <summary>The platform-wide (global-scope) dictionary that also applied to this bulletin,
    ///     underneath the station's. None = no global dictionary was active. Recorded separately
    ///     because the two layers are edited by different people under different rules.</summary>
    public Option<string> GlobalPronunciationDictionaryId { get; set; }

    /// <summary>The version id applied, paired with
    ///     <see cref="GlobalPronunciationDictionaryId"/>.</summary>
    public Option<string> GlobalPronunciationVersionId { get; set; }
    public Option<string> AudioUrl { get; set; }
    /// <summary>
    ///     Uppercase-hex SHA-256 of the exact audio bytes; the content-identity used to verify
    ///     delivery matches the approved audio. Null when no audio is sealed (filler fallback).
    /// </summary>
    public string? AudioSha256 { get; set; }
    public Option<string> ErrorMessage { get; set; }
    public Option<DateTime> ScheduledForUtc { get; set; }
    /// <summary>Fixed slot-owned curation deadline; unlike LastUpdatedAt, edits cannot move it.</summary>
    public Option<DateTime> CurationDeadlineUtc { get; set; }
    public Option<DateTime> FallbackDeadlineUtc { get; set; }
    public Option<string> FallbackSourceJobId { get; set; }
    public Option<DateTime> MissedDeadlineUtc { get; set; }
    public Option<DateTime> FallbackAppliedAt { get; set; }
    public Option<string> FallbackReason { get; set; }

    /// <summary>
    ///     The bulletin a fallback replaced on this job. None when the job never produced a
    ///     script, or never fell back. Written by <see cref="ApplyFallback" /> /
    ///     <see cref="ApplyFillerFallback" /> immediately before they overwrite the script, and
    ///     deliberately NOT cleared by <see cref="RequestRegeneration" /> — it is a historical
    ///     fact about a fallback that did happen, like <see cref="FallbackDeadlineUtc" />.
    /// </summary>
    public Option<SupersededTake> SupersededTake { get; private set; }

    /// <summary>
    ///     The operator's script edit awaiting publication evidence: set by a re-voice with
    ///     changed words, cleared by a successful publish (which enqueues it for station-lesson
    ///     learning) or by a full regenerate (the words are being rewritten, so the edit no
    ///     longer describes what airs).
    /// </summary>
    public Option<RevoiceObservation> PendingRevoiceObservation { get; set; }

    /// <summary>
    ///     Consecutive fallback count ending at this job (1 = the first fallback since the last
    ///     genuine success or a filler-cart restart; N = the Nth fallback in an unbroken chain).
    ///     0 for a job that has never used fallback. Lets an operator or diagnostic read a single
    ///     job row and immediately know whether a market is chronically stuck in fallback, instead
    ///     of walking <see cref="FallbackSourceJobId"/> backwards one hop at a time.
    /// </summary>
    public int FallbackStreak { get; private set; }
    public Option<JobStatus> FailedAtStatus { get; set; }
    public int AttemptCount { get; private set; }
    public Option<double> LastEstimatedDurationSec { get; set; }
    public IReadOnlyList<AttemptResult> AttemptHistory => _attemptHistory;
    public Option<JobVoiceConfig> VoiceConfig { get; set; }

    /// <summary>
    ///     The voice profile whose CURRENT tuning produced <see cref="VoiceConfig" />, for
    ///     provenance. None = the voice was resolved another way (bare VoiceId, provider default).
    ///     Pinned at creation (CreateJobAsync / schedule poller) and on a regenerate voice
    ///     override; survives the regeneration reset exactly like <see cref="VoiceConfig" />
    ///     itself — a regenerate without an override re-airs the same voice, so the pins still
    ///     describe the config on the row.
    /// </summary>
    public Option<string> VoiceProfileId { get; set; }

    /// <summary>The profile version of <see cref="VoiceProfileId" /> applied, for provenance.</summary>
    public Option<int> VoiceProfileVersion { get; set; }

    /// <summary>
    ///     Approximate word-level audio timings for click-to-seek in the review UI.
    ///     None for legacy jobs, script-only jobs, or when the TTS provider gave no alignment.
    /// </summary>
    public Option<IReadOnlyList<WordTiming>> WordTimings { get; set; }

    /// <summary>
    ///     Pre-approval audio review outcome (transcript-vs-script). None for jobs generated
    ///     before the feature or when transcription was unavailable at synthesis time.
    /// </summary>
    public Option<AudioPreReviewOutcome> PreReview { get; set; }

    /// <summary>
    ///     Content sources this bulletin was requested with (WS1). Persisted so
    ///     regeneration reproduces the same selection. None = legacy/traffic-only.
    /// </summary>
    public Option<BulletinContentSelection> ContentSelection { get; set; }

    /// <summary>Weather data used by this bulletin, for operator verification in review.</summary>
    public Option<WeatherReport> Weather { get; set; }

    /// <summary>Fuel price data used by this bulletin, for operator verification in review.</summary>
    public Option<FuelPriceReport> Fuel { get; set; }

    /// <summary>Priority roads with no incidents in the fresh fetch — reported on air as good runs (WS2-012).</summary>
    public IReadOnlyList<string> GoodRunRoads { get; set; } = [];
    public IReadOnlyList<JobCorridor> Corridors => _corridors;

    /// <summary>
    ///     The full ranked pool of roads HERE flow reported as flowing freely when this bulletin
    ///     was generated — the data-backed candidates surfaced in the review Flow tab. Each carries
    ///     whether it was station-weighted and whether it aired (<see cref="JobFlowRoad.IsSelected"/>).
    ///     Empty for jobs persisted before this field existed.
    /// </summary>
    public IReadOnlyList<JobFlowRoad> FlowRoads => _flowRoads;
    public IReadOnlyList<JobScriptSentence> ScriptSentences => _scriptSentences;
    public IReadOnlyList<JobIncident> Incidents => _incidents;

    /// <summary>
    ///     Every incident fetched for this job (all corridors), not just the selected-corridor
    ///     subset in <see cref="Incidents"/>. Surfaced as the review evidence pool so the UI can
    ///     show non-selected incidents under "Not in this bulletin". Empty for jobs persisted
    ///     before this field existed — callers fall back to <see cref="Incidents"/>.
    /// </summary>
    public IReadOnlyList<JobIncident> AllIncidents => _allIncidents;
    public Option<DateTime> IncidentsFetchedAt { get; set; }
    /// <summary>
    ///     Per-source fetch outcomes captured for this bulletin. Empty for jobs persisted before
    ///     source diagnostics were introduced; never null.
    /// </summary>
    public IReadOnlyList<SourceFetchDiagnostic> SourceDiagnostics { get; set; } = [];
    public IReadOnlyList<JobLog> Logs => _logs;
    public IReadOnlyList<StatusHistoryEntry> StatusHistory => _statusHistory;
    public IReadOnlyList<RejectionEntry> RejectionHistory => _rejectionHistory;
    public Option<double> Latitude { get; init; }
    public Option<double> Longitude { get; init; }
    public Option<double> RadiusKm { get; init; }

    /// <summary>
    ///     Point-in-time snapshot of the full coverage geometry (shape +
    ///     bbox/corridor/polygon) used to generate this bulletin, preserved even
    ///     if the station's coverage later changes. None for legacy circle-only jobs.
    /// </summary>
    public Option<CoverageArea> Coverage { get; init; }
    public Option<string> TimezoneId { get; init; }
    public Option<string> PreviousScriptContext { get; set; }
    public Option<string> PreviousJobId { get; set; }
    public Option<IncidentChangeSummary> IncidentChangeSummary { get; set; }

    /// <summary>
    ///     Same shape as <see cref="IncidentChangeSummary"/> but computed from the RECONCILED delta
    ///     — i.e. after <c>IncidentDeltaReconciler.SuppressClearedOnActiveRoads</c> drops any
    ///     cleared-item whose road still has an active corridor elsewhere in this bulletin. This is
    ///     the change-summary the SCRIPT GENERATOR actually saw; <see cref="IncidentChangeSummary"/>
    ///     is deliberately the pre-reconciliation raw delta (audit trail keeps ground truth) and can
    ///     legitimately list a road the script never mentions clearing, or omit one the script
    ///     correctly does — auditing a script against <see cref="IncidentChangeSummary"/> alone can
    ///     make a correct "has cleared" claim look fabricated (2026-07-07 audit false-positive on
    ///     job 4417add2: "Ipswich Motorway congestion has cleared" was reconciled-correct but absent
    ///     from the raw summary's capped display). Empty for jobs persisted before this field existed.
    /// </summary>
    public Option<IncidentChangeSummary> NarrativeIncidentChangeSummary { get; set; }

    /// <summary>
    ///     Configured commuter routes with live-vs-typical drive times, as seen by the script
    ///     generator for this bulletin (WS2 monitored-route headline feature). Persisted so a
    ///     drive-time claim in the script (e.g. "the Pacific Motorway into the city is a 30-minute
    ///     run") can be verified against real data without digging into <c>GeneratorInputJson</c> —
    ///     per-corridor <c>DriveTimeMinutes</c> is a DIFFERENT, narrower field and is usually null;
    ///     monitored-route drive times are the actual source for this class of claim. Empty for jobs
    ///     persisted before this field existed, or where no route is configured for the market.
    /// </summary>
    public IReadOnlyList<MonitoredRouteStatus> MonitoredRoutes { get; set; } = [];

    // ── Audit Trail ──
    /// <summary>Identity of the user who approved this job (if applicable).</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    ///     Immutable Entra object id of the user who approved this job (if applicable). Display
    ///     names (<see cref="ApprovedBy"/>) are mutable (rename, re-issued account) and therefore
    ///     insufficient for audit/compliance evidence of who authorised air; the object id never
    ///     changes for the lifetime of the identity.
    /// </summary>
    public string? ApprovedByObjectId { get; set; }

    /// <summary>When the job was approved.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Identity of the user who published this job (if applicable).</summary>
    public string? PublishedBy { get; set; }

    /// <summary>
    ///     Immutable Entra object id of the user who published this job (if applicable). See
    ///     <see cref="ApprovedByObjectId"/> for why the display name alone is insufficient.
    /// </summary>

    public string? PublishedByObjectId { get; set; }

    /// <summary>When the job was published.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Opaque Azure Table ETag for optimistic concurrency. Null means new entity (creation path).</summary>
    public string? ETag { get; set; }
    /// <summary>
    ///     Durable delivery claim metadata. These fields are populated atomically with
    ///     <see cref="JobStatus.DeliveryPending" /> and remain until the worker reaches a terminal
    ///     delivery outcome.
    /// </summary>
    public string? DeliveryIdempotencyKey { get; set; }
    public string? DeliveryReviewedEtag { get; set; }
    public Option<DeliveryKind> DeliveryKind { get; set; }
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

    /// <summary>
    ///     Opaque snapshot of the row's material (non-log) columns taken at load: status,
    ///     script, audio, approval/publish audit fields, rejections, error, attempt count,
    ///     fallback streak. Used by the storage layer to tell a benign concurrent log append
    ///     apart from a real concurrent state change when reconciling an ETag conflict. Null
    ///     for a job not yet loaded from storage (creation path).
    /// </summary>
    public string? ConcurrencyFingerprint { get; set; }

    /// <summary>
    ///     Reconstitutes a Job from persisted data, loading StatusHistory directly
    ///     instead of generating a synthetic Requested entry.
    /// </summary>
    /// <param name="createdAt">
    ///     The persisted canonical CreatedAt, when it may diverge from the first status-history
    ///     entry (legacy rows). Null derives CreatedAt from <paramref name="statusHistory"/> as before.
    /// </param>
    /// <param name="status">
    ///     The persisted canonical Status. Null derives Status from the last status-history entry
    ///     as before.
    /// </param>
    public static Job Reconstitute(
        string jobId,
        string station,
        string market,
        double targetDurationSeconds,
        IEnumerable<StatusHistoryEntry> statusHistory,
        Option<double> latitude = default,
        Option<double> longitude = default,
        Option<double> radiusKm = default,
        Option<string> timezoneId = default,
        IEnumerable<JobCorridor>? corridors = null,
        Option<CoverageArea> coverage = default,
        DateTime? createdAt = null,
        JobStatus? status = null,
        int attemptCount = 0,
        int fallbackStreak = 0)
    {
        var job = new Job
        {
            JobId = jobId,
            Station = station,
            Market = market,
            TargetDurationSeconds = targetDurationSeconds,
            Latitude = latitude,
            Longitude = longitude,
            RadiusKm = radiusKm,
            TimezoneId = timezoneId,
            Coverage = coverage
        };
        job._statusHistory.AddRange(statusHistory);
        if (corridors is not null) job._corridors.AddRange(corridors);

        if (job._statusHistory.Count > 0)
        {
            job.CreatedAt = job._statusHistory[0].At;
            job.LastUpdatedAt = job._statusHistory[^1].At;
            job.Status = job._statusHistory[^1].Status;
        }

        if (createdAt.HasValue) job.CreatedAt = createdAt.Value;
        if (status.HasValue) job.Status = status.Value;
        job.AttemptCount = attemptCount;
        job.FallbackStreak = fallbackStreak;

        return job;
    }

    /// <summary>
    ///     Transitions the job to the given status, recording the change in StatusHistory.
    /// </summary>
    public void TransitionTo(JobStatus newStatus, string by = "system", Option<string> note = default, DateTime utcNow = default)
    {
        Status = newStatus;
        LastUpdatedAt = utcNow;
        _statusHistory.Add(new StatusHistoryEntry(newStatus, utcNow, by, note));
    }

    // No Delivered: a job ends at Published. There WAS a Delivered member, never assigned by
    // any code path, while the SDD and the state-machine diagram both described it as shipped.
    // Deleted 2026-07-31 — the playout system returns no receipt, and because the drop file is
    // overwritten per station its disappearance identifies the station's latest publish, not a
    // specific bulletin.
    private static bool IsTerminal(JobStatus status)
    {
        return status is JobStatus.Published
            or JobStatus.Failed or JobStatus.FallbackUsed or JobStatus.Missed or JobStatus.Cancelled;
    }

    /// <summary>
    ///     Approves the job. Valid from WaitingApproval, an in-flight pipeline state (the
    ///     ScriptOnly auto-approve path), or FallbackUsed. Re-approving an already-Approved
    ///     job is rejected: a second approval would overwrite the recorded approver identity
    ///     and timestamp, which are the audit/compliance evidence for who authorised air.
    /// </summary>
    public Result Approve(DateTime utcNow, string by = "operator", Option<string> note = default,
        string? byObjectId = null)
    {
        // The real rule, previously enforced ONLY by JobService: a bulletin must have reached
        // review before anyone can authorise it for air. The domain guard used to permit every
        // in-flight state (Requested, HereFetched, IncidentsSelected, ScriptReady,
        // SynthesizingAudio, AudioReady), so a job with no script and no audio could be approved
        // by any caller that reached the model directly. FallbackUsed is included because an
        // aired-fallback bulletin is a legitimate thing to approve. Missed is deliberately NOT
        // included: the slot has passed, so there is nothing left to authorise for air.
        if (Status != JobStatus.WaitingApproval && Status != JobStatus.FallbackUsed)
            return Result.Failure(string.Format(ErrorMessages.Job.CannotApproveFromStatus, Status));

        ApprovedBy = by;
        ApprovedByObjectId = byObjectId;
        ApprovedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
        TransitionTo(JobStatus.Approved, by, note, utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Rejects the job with a rejection entry. Valid only from WaitingApproval.
    ///     <paramref name="by" /> is the authenticated actor who rejected — it must be passed
    ///     explicitly (no default) so the StatusHistory audit stamp can never silently fall back
    ///     to the placeholder "operator" while the real identity lives only in
    ///     <see cref="RejectionHistory" /> (observed live 2026-08-05).
    /// </summary>
    public Result Reject(RejectionEntry entry, DateTime utcNow, string by)
    {
        if (Status != JobStatus.WaitingApproval)
            return Result.Failure(ErrorMessages.Job.CannotRejectFromStatus);

        AddRejection(entry);
        ErrorMessage = Option<string>.Some(entry.Reason);
        TransitionTo(JobStatus.Rejected, by, Option<string>.Some($"Rejected: {entry.Reason}"), utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Marks an unpublished bulletin as Missed once its playout slot is no longer reachable.
    ///     This includes a bulletin approved but not published in time: approval does not make
    ///     stale traffic safe to air after the slot. Deliberately NOT a rejection: no
    ///     <see cref="RejectionEntry" /> and no <see cref="ErrorMessage" />, because an expired
    ///     slot must not masquerade as an operator taking it off air or feed expiry text into
    ///     the producer-feedback prompt.
    /// </summary>
    public Result MarkMissed(DateTime deadlineUtc, DateTime utcNow, string by = "system")
    {
        if (Status is JobStatus.Rejected or JobStatus.Published or JobStatus.FallbackUsed or JobStatus.Missed)
            return Result.Failure(ErrorMessages.Job.CannotMarkMissedFromStatus);

        MissedDeadlineUtc = Option<DateTime>.Some(deadlineUtc);
        AddLog(new JobLog(utcNow, JobLogLevel.Warn,
            $"Missed the playout deadline ({deadlineUtc:O} UTC) - the slot passed before this bulletin was published."));
        TransitionTo(JobStatus.Missed, by, Option<string>.Some("Missed the playout deadline"), utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Completes a previously claimed delivery. Only the delivery worker may call this
    ///     transition, so an external write is always observed before <see cref="Published" />.
    /// </summary>
    public Result CompleteDelivery(DateTime utcNow, string by = "system", string? byObjectId = null)
    {
        if (Status != JobStatus.DeliveryPending)
            return Result.Failure($"Delivery is not pending: {Status}");

        PublishedBy = by;
        PublishedByObjectId = byObjectId;
        PublishedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
        TransitionTo(JobStatus.Published, by, Option<string>.Some("External delivery succeeded"), utcNow);
        return Result.Success();
    }

    /// <summary>Ends a pending delivery without claiming that any bytes reached playout.</summary>
    public Result CancelDelivery(DateTime utcNow, string by = "system", string? reason = null)
    {
        if (Status != JobStatus.DeliveryPending)
            return Result.Failure($"Delivery is not pending: {Status}");

        ErrorMessage = Option.OfNullable(reason);
        TransitionTo(JobStatus.Cancelled, by, Option.OfNullable(reason), utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Publishes the job for broadcast. Valid only from Approved.
    /// </summary>
    public Result Publish(DateTime utcNow, string by = "operator", string? byObjectId = null)
    {
        if (Status != JobStatus.Approved)
            return Result.Failure(ErrorMessages.Job.MustBeApprovedToPublish);

        PublishedBy = by;
        PublishedByObjectId = byObjectId;
        PublishedAt = new DateTimeOffset(utcNow, TimeSpan.Zero);
        TransitionTo(JobStatus.Published, by, default, utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Marks the job as failed, recording the status it failed at. Valid from any non-terminal state.
    /// </summary>
    public Result MarkFailed(string error, DateTime utcNow, string by = "system")
    {
        if (IsTerminal(Status))
            return Result.Failure(string.Format(ErrorMessages.Job.CannotFailTerminalJob, Status));

        FailedAtStatus = Option<JobStatus>.Some(Status);
        ErrorMessage = Option<string>.Some(error);
        TransitionTo(JobStatus.Failed, by, Option<string>.Some(error), utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Resets the job for regeneration. Valid from Rejected (operator requested
    ///     changes), Failed (operator retries a dead pipeline), and FallbackUsed (an
    ///     operator retries a slot the standby covered) — all three re-enter
    ///     orchestration from scratch.
    /// </summary>
    /// <param name="preserveEvidence">
    ///     Set for a RE-VOICE, which re-renders words that are already fixed rather than
    ///     re-writing them from fresh traffic. The corridor/incident snapshot still describes
    ///     the script about to be synthesised, so clearing it strands the operator with a
    ///     bulletin making road claims and "0 incidents cited" to check them against. The
    ///     script itself is likewise kept — the re-voice needs those exact words as its input.
    /// </param>
    public Result RequestRegeneration(DateTime utcNow, string by = "operator", Option<string> note = default,
        bool preserveEvidence = false)
    {
        // Missed is deliberately NOT regenerable: regeneration resets the job in place (clears
        // audio, script, corridors, incidents, attempt history), which would erase the evidence
        // that the slot aired nothing and silently decrement the missed-slot count.
        if (Status is not (JobStatus.Rejected or JobStatus.Failed or JobStatus.FallbackUsed))
            return Result.Failure(ErrorMessages.Job.CannotRegenerateFromStatus);

        ErrorMessage = Option<string>.None;
        AudioUrl = Option<string>.None;
        MeasuredDurationSec = Option<double>.None;
        // FallbackDeadlineUtc is deliberately KEPT. It belongs to the SLOT, not to the
        // attempt: the bulletin still has to reach playout by the same moment, and the
        // sweep that guarantees the slot is covered only sees jobs whose deadline is set
        // (TableStorageJobRepository.ListJobsNeedingFallbackAsync filters on it, and a null
        // never matches). Clearing it took a regenerated scheduled bulletin out of that
        // sweep entirely — no auto-delivery once approved, no repeat or cart if the new
        // attempt failed, and no alert: silent dead air on the most common operator action.
        // A fresh attempt is not swept out from under itself because IsActivelyGenerating
        // grants an in-flight worker its grace window.
        AttemptCount = 0;
        _attemptHistory.Clear();

        if (!preserveEvidence)
        {
            ScriptText = Option<string>.None;
            TaggedScriptText = Option<string>.None;
            DeliveryStyleId = Option<string>.None;
            DeliveryStyleVersion = Option<int>.None;
            BookendId = Option<string>.None;
            BookendVersion = Option<int>.None;
            // Delivery styles, bookends and script sentences are reset so the new
            // attempt captures the latest upstream state and generated script cleanly.
            _scriptSentences.Clear();
            SourceDiagnostics = [];
            // A full regenerate rewrites the words, so a pending operator edit no longer
            // describes anything that can air; discard it rather than learn from stale text.
            PendingRevoiceObservation = Option<RevoiceObservation>.None;
        }

        TransitionTo(JobStatus.Requested, by, note.HasValue ? note : Option<string>.Some("Regeneration requested"), utcNow);
        return Result.Success();
    }

    /// <summary>
    ///     Snapshots the bulletin currently on this job into <see cref="SupersededTake" />, so the
    ///     fallback about to overwrite the script does not destroy it. No-op when there is no
    ///     script to keep. Last write wins: the retained take must always be whatever existed
    ///     immediately before the fallback that replaced it.
    /// </summary>
    private void CaptureSupersededTake(DateTime utcNow, string? reason)
    {
        if (!ScriptText.HasValue)
            return;

        SupersededTake = Option<SupersededTake>.Some(new SupersededTake(
            utcNow,
            reason ?? "Replaced by fallback",
            ScriptText.GetValueOr(string.Empty),
            TaggedScriptText,
            AudioUrl,
            AudioSha256,
            MeasuredDurationSec,
            LastEstimatedDurationSec,
            _scriptSentences.ToList(),
            WordTimings,
            PreReview,
            DeliveryStyleId,
            DeliveryStyleVersion,
            BookendId,
            BookendVersion));
    }

    /// <summary>
    ///     Applies fallback data from another job. Valid from any non-terminal state.
    /// </summary>
    public Result ApplyFallback(Job source, DateTime utcNow, string by = "system", string? reason = null,
        int? fallbackStreak = null)
    {
        if (Status is JobStatus.Published or JobStatus.FallbackUsed)
            return Result.Failure(string.Format(ErrorMessages.Job.CannotApplyFallbackToTerminalJob, Status));

        CaptureSupersededTake(utcNow, reason);
        AudioSha256 = source.AudioSha256;
        AudioUrl = source.AudioUrl;
        ScriptText = source.ScriptText;
        TaggedScriptText = source.TaggedScriptText;
        DeliveryStyleId = source.DeliveryStyleId;
        DeliveryStyleVersion = source.DeliveryStyleVersion;
        BookendId = source.BookendId;
        BookendVersion = source.BookendVersion;
        MeasuredDurationSec = source.MeasuredDurationSec;
        WordTimings = source.WordTimings;
        PreReview = source.PreReview;
        SetIncidents(source.Incidents);
        SetAllIncidents(source.AllIncidents);
        SourceDiagnostics = source.SourceDiagnostics.ToList();
        IncidentsFetchedAt = source.IncidentsFetchedAt;
        SetScriptSentences(source._scriptSentences);
        IncidentChangeSummary = source.IncidentChangeSummary;
        FallbackSourceJobId = Option<string>.Some(source.JobId);
        FallbackAppliedAt = Option<DateTime>.Some(utcNow);
        FallbackReason = Option.OfNullable(reason);
        // The caller passes the streak counted along the SLOT chain (the previously covered slot's
        // streak + 1) because a covered slot is Published seconds after its cover, so the legacy
        // status check below can never see a chain: every cover-of-a-cover reported streak 1
        // (observed live 2026-08-17, dev). The legacy calculation remains for callers with no chain.
        FallbackStreak = fallbackStreak ?? (source.Status == JobStatus.FallbackUsed ? source.FallbackStreak + 1 : 1);
        TransitionTo(JobStatus.FallbackUsed, by, Option<string>.Some(source.JobId), utcNow);
        AddLog(new JobLog(utcNow, JobLogLevel.Warn,
            $"Fallback applied from job {source.JobId} (fallback streak: {FallbackStreak})."));
        return Result.Success();
    }

    /// <summary>
    ///     Applies a generic pre-recorded filler cart when a bulletin failed and there is no
    ///     earlier same-day bulletin to reuse. Valid from any non-terminal state.
    ///     <paramref name="measuredDurationSec" /> surfaces the clip's measured duration on the
    ///     job (bug-bash 2026-08-05 finding 32) so an operator sees exactly how much of the slot
    ///     the fallback covers before approving — a silent 14s hole in a 30s slot was the defect.
    /// </summary>
    public Result ApplyFillerFallback(string fillerAudioUrl, DateTime utcNow, string by = "system", string? reason = null,
        double? measuredDurationSec = null)
    {
        if (string.IsNullOrWhiteSpace(fillerAudioUrl))
            return Result.Failure("Filler cart audio URL is required.");

        if (Status is JobStatus.Published or JobStatus.FallbackUsed)
            return Result.Failure(string.Format(ErrorMessages.Job.CannotApplyFallbackToTerminalJob, Status));

        CaptureSupersededTake(utcNow, reason);
        AudioUrl = Option<string>.Some(fillerAudioUrl);
        ScriptText = Option<string>.None;
        TaggedScriptText = Option<string>.None;
        MeasuredDurationSec = measuredDurationSec.HasValue
            ? Option<double>.Some(measuredDurationSec.Value)
            : Option<double>.None;
        WordTimings = Option<IReadOnlyList<WordTiming>>.None;
        PreReview = Option<AudioPreReviewOutcome>.None;
        AudioSha256 = null;
        _scriptSentences.Clear();
        FallbackSourceJobId = Option<string>.None;
        FallbackAppliedAt = Option<DateTime>.Some(utcNow);
        FallbackReason = Option.OfNullable(reason);
        FallbackStreak = 1;
        TransitionTo(JobStatus.FallbackUsed, by, Option<string>.Some("filler-cart"), utcNow);
        AddLog(new JobLog(utcNow, JobLogLevel.Warn,
            "Generic filler cart applied (no same-day bulletin available) (fallback streak: 1)."));
        return Result.Success();
    }

    public void AddLog(JobLog log)
    {
        _logs.Add(log);
    }

    public void AddIncident(JobIncident incident)
    {
        _incidents.Add(incident);
    }

    public void AddAttempt(AttemptResult attempt)
    {
        _attemptHistory.Add(attempt);
    }

    public void AddStatusEntry(StatusHistoryEntry entry)
    {
        _statusHistory.Add(entry);
    }

    public void AddRejection(RejectionEntry entry)
    {
        _rejectionHistory.Add(entry);
    }

    public void SetLogs(IEnumerable<JobLog> logs)
    {
        _logs.Clear();
        _logs.AddRange(logs);
    }

    public void SetCorridors(IEnumerable<JobCorridor> corridors)
    {
        _corridors.Clear();
        _corridors.AddRange(corridors);
    }

    public void SetFlowRoads(IEnumerable<JobFlowRoad> flowRoads)
    {
        _flowRoads.Clear();
        _flowRoads.AddRange(flowRoads);
    }

    public void SetScriptSentences(IEnumerable<JobScriptSentence> sentences)
    {
        _scriptSentences.Clear();
        _scriptSentences.AddRange(sentences);
    }

    public void SetIncidents(IEnumerable<JobIncident> incidents)
    {
        _incidents.Clear();
        _incidents.AddRange(incidents);
    }

    public void SetAllIncidents(IEnumerable<JobIncident> incidents)
    {
        _allIncidents.Clear();
        _allIncidents.AddRange(incidents);
    }

    /// <summary>
    ///     Test/fixture-only override for <see cref="Status" /> — bypasses transition guards
    ///     and does not append a <see cref="StatusHistoryEntry" />. Production status changes
    ///     MUST go through <see cref="TransitionTo" /> (or a workflow method built on it, e.g.
    ///     <see cref="Approve" />/<see cref="Publish" />); persisted rehydration goes through
    ///     <see cref="Reconstitute" />.
    /// </summary>
    public void SetStatus(JobStatus status)
    {
        Status = status;
    }

    /// <summary>
    ///     Test/fixture-only override for <see cref="CreatedAt" />. Production code MUST NOT call
    ///     this — CreatedAt is fixed at <see cref="Create" />/<see cref="Reconstitute" /> time.
    /// </summary>
    public void SetCreatedAt(DateTime createdAt)
    {
        CreatedAt = createdAt;
    }

    /// <summary>
    ///     Test/fixture-only override for <see cref="AttemptCount" />. Production code resets this
    ///     via <see cref="RequestRegeneration" />; persisted rehydration goes through
    ///     <see cref="Reconstitute" />.
    /// </summary>
    public void SetAttemptCount(int attemptCount)
    {
        AttemptCount = attemptCount;
    }

    /// <summary>
    ///     Test/fixture-only override for <see cref="FallbackStreak" />. Production code sets this
    ///     via <see cref="ApplyFallback" />/<see cref="ApplyFillerFallback" />; persisted
    ///     rehydration goes through <see cref="Reconstitute" />.
    /// </summary>
    public void SetFallbackStreak(int fallbackStreak)
    {
        FallbackStreak = fallbackStreak;
    }

    /// <summary>
    ///     Persistence rehydration (and test fixtures) only. Production writes go through
    ///     <see cref="ApplyFallback" />/<see cref="ApplyFillerFallback" />, which capture the take
    ///     before they overwrite the script.
    /// </summary>
    public void SetSupersededTake(Option<SupersededTake> take)
    {
        SupersededTake = take;
    }

    public void SetAttemptHistory(IEnumerable<AttemptResult> attempts)
    {
        _attemptHistory.Clear();
        _attemptHistory.AddRange(attempts);
    }

    public void SetStatusHistory(IEnumerable<StatusHistoryEntry> entries)
    {
        _statusHistory.Clear();
        _statusHistory.AddRange(entries);
    }

    public void SetRejectionHistory(IEnumerable<RejectionEntry> entries)
    {
        _rejectionHistory.Clear();
        _rejectionHistory.AddRange(entries);
    }
}
