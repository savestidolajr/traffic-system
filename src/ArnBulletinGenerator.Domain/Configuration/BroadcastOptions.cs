namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Configuration options for the broadcast orchestration process.
/// </summary>
public sealed class BroadcastOptions
{
    public const string SectionName = "Broadcast";
    public const int DefaultCorridorGroupingMaxGapMeters = 1000;

    public int MaxGenerationRetries { get; set; } = 3;
    public int MaxSynthesisAttempts { get; set; } = 3;

    /// <summary>
    ///     How many minutes before the scheduled time we should start generation.
    /// </summary>
    public int ScheduleLeadTimeMinutes { get; set; } = 15;
    /// <summary>Minutes the operator has to curate fetched traffic before generation auto-proceeds.</summary>
    public int CurationWindowMinutes { get; set; } = 5;

    /// <summary>
    ///     Minutes before the scheduled time when fallback should be applied if no valid audio exists.
    /// </summary>
    public int FallbackDeadlineMinutes { get; set; } = 3;

    /// <summary>
    ///     Optional URL or path to a generic pre-recorded "filler cart" audio file, played when a
    ///     bulletin fails and there is no earlier same-day bulletin to reuse (for example the first
    ///     bulletin of the day). Empty disables the filler fallback and the slot is skipped.
    /// </summary>
    public string FillerCartAudioUrl { get; set; } = "";

    /// <summary>
    ///     The filler-cart script text, synthesized per voice and cached. Empty disables
    ///     per-voice carts (evergreen FillerCartAudioUrl only). Keep in IaC — see QT-001.
    /// </summary>
    public string FillerCartScript { get; set; } = "";

    /// <summary>
    ///     Maximum gap between same-road incident observations before they become separate corridors.
    ///     This prevents a long arterial such as Sydney's roughly 20 km Parramatta Road from becoming
    ///     one centroid-less corridor when HERE reports unrelated conditions at opposite ends, which
    ///     would otherwise make the corridor's aired anchor and every downstream geometry decision
    ///     meaningless. A continuous queue remains one corridor through single-linkage clustering.
    /// </summary>
    public int CorridorGroupingMaxGapMeters { get; set; } = DefaultCorridorGroupingMaxGapMeters;

    /// <summary>
    ///     Maximum age (in minutes) of HERE traffic data before incidents are considered stale.
    ///     Incidents with DataTimestampUtc older than now minus this value are dropped.
    /// </summary>
    public int TrafficDataMaxAgeMinutes { get; set; } = 10;

    /// <summary>
    ///     Lifetime of the per-coverage HERE corridor result shared by overlapping bulletin jobs.
    ///     Kept configurable so an environment can trade duplicate upstream calls against
    ///     freshness without changing the evidence builder.
    /// </summary>
    public int TrafficCacheTtlSeconds { get; set; } = 180;

    /// <summary>
    ///     Words per minute used for target word count calculation.
    ///     Standard broadcast pace for Australian radio traffic updates.
    /// </summary>
    public int WordsPerMinute { get; set; } = 150;

    /// <summary>
    ///     Assumed spoken seconds per rundown item (corridor or good-run), used to derive how many
    ///     items a slot's duration can hold (<c>BroadcastOrchestrator.CalculateMaxCorridors</c>:
    ///     <c>clamp(slotSeconds / SlotSecondsPerItem, 6, 15)</c>). The historical 5.0 assumption is
    ///     ~2x the measured per-item speech cost (~3s at the corpus mean 185 wpm), which starved
    ///     30s slots at 6 items while the word budget demanded ~100 words — the dominant
    ///     content-limited fallback cause. Replay-panel A/B (2026-07-06, capacity 6 vs 10): 9/10
    ///     fixtures improved, deep-shortfall fixtures gained +3-4s of honest content, one near-miss
    ///     flipped to filled. Default 5.0 preserves legacy behavior; dev deploys 3.0
    ///     (30s slot -> 10 items; 15s slot unchanged at the 6-item floor).
    /// </summary>
    public double SlotSecondsPerItem { get; set; } = 5.0;

    /// <summary>
    ///     Maximum percentage adjustment (±) to word count on retry to prevent extreme scripts.
    ///     E.g., 30 means the adjusted word count cannot deviate more than 30% from the base.
    /// </summary>
    public int MaxWordCountAdjustmentPercent { get; set; } = 30;

    /// <summary>
    ///     Maximum review-refine iterations after initial script generation. 0 disables the loop.
    ///     <para>
    ///         DEFAULTS TO 0 AND SHOULD STAY THERE. This is the RETIRED outer whole-script LLM
    ///         review/refine loop from the deterministic draft/review/refine pipeline.
    ///         <c>conversational-v1</c> is now the only script pipeline
    ///         (<c>DependencyInjection</c> registers <c>ConversationalScriptGenerator</c> as the
    ///         sole <see cref="ArnBulletinGenerator.Domain.Interfaces.IScriptGenerator" />), and its
    ///         editor already owns review and refinement. Running this loop on top of it is a
    ///         duplicated second opinion that rewrites what the editor approved, at the cost of up
    ///         to two extra LLM round-trips per bulletin. The engine's DETERMINISTIC gates are
    ///         unaffected and stay on: they are grounding and safety, not a second opinion.
    ///     </para>
    ///     <para>
    ///         The former default was 3, tuned for the retired pipeline. That made ABSENCE of the
    ///         app setting silently re-enable the retired loop. Found live on 2026-09-03: test and
    ///         production both carried <c>Broadcast__MaxReviewRefineRetries=2</c> and had been
    ///         running the retired loop on real bulletins (test App Insights <c>[ReviewRefine]</c>
    ///         traces through 2026-08-27), while only dev was correctly at 0. Defaulting to 0 means
    ///         a missing or stripped setting now fails SAFE instead of resurrecting dead code.
    ///     </para>
    /// </summary>
    public int MaxReviewRefineRetries { get; set; }

    /// <summary>
    ///     Average syllables spoken per second. Radio delivery is typically faster (5.5-6.0)
    ///     than conversational speech (4.5-5.0). Default: 5.8 for Australian radio style.
    /// </summary>
    public double SyllablesPerSecond { get; set; } = 5.8;

    /// <summary>
    ///     Audition voice used where no job voice exists to speak with: delivery-style and
    ///     pronunciation previews, and the deep-pipeline guards that read a voice id for
    ///     calibration before the job's own voice is in scope. It is NOT a fallback for a job
    ///     without a voice — a broadcast must name its own voice (see
    ///     <c>OrchestrationValidation.Validate</c>) and is rejected otherwise.
    ///     <para>
    ///         There is deliberately no companion DefaultVoiceName. A hardcoded display name is a
    ///         snapshot of a provider-side label that drifts: the previous one paired this id with
    ///         "ARN Hudson" long after ElevenLabs had renamed the voice back to "Peter Michael -
    ///         Australian Male VO", and every bulletin that used it was mislabelled in the job
    ///         record, the logs and the operator console. Display names are resolved from the
    ///         provider at use time instead.
    ///     </para>
    /// </summary>
    public string DefaultVoiceId { get; set; } = "";

    /// <summary>
    ///     Default TTS provider used when a job does not specify one.
    /// </summary>
    public string DefaultVoiceProvider { get; set; } = "";

    /// <summary>
    ///     Default language code (ISO 639-1) for voice model selection.
    /// </summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    ///     Damping factor used in word target retry calculations (0.0–1.0).
    ///     Controls how aggressively the word target moves toward the observed ideal.
    /// </summary>
    public double DampingFactor { get; init; } = 0.5;

    /// <summary>
    ///     Maximum number of words the target can shift per retry step, preventing large swings.
    /// </summary>
    public int MaxPerRetryWordShift { get; init; } = 12;

    /// <summary>
    ///     Absolute maximum word deviation allowed after a refinement correction.
    ///     If the refined script still exceeds this limit, the attempt is rejected.
    /// </summary>
    public int WordTargetHardLimit { get; init; } = 12;

    /// <summary>
    ///     Maximum number of corridors stored on a job record for historical/audit purposes.
    /// </summary>
    public int MaxCorridorsForStorage { get; init; } = 20;

    /// <summary>
    ///     Default timezone ID used when a job or schedule does not specify one.
    /// </summary>
    public string DefaultTimezone { get; init; } = "Australia/Sydney";

    /// <summary>
    ///     Maximum number of due market schedules DISPATCHED concurrently by one poller tick.
    ///     Since the dispatcher model (2026-07-21) this bounds only config resolution + queue
    ///     enqueueing (~100-300 ms per market) — generation concurrency is governed by the queue
    ///     (host.json queues.batchSize per instance × autoscale instances), not by this value.
    ///     The historical 429-storm rationale is retired: Azure OpenAI TPM quota is effectively
    ///     unlimited (2026-07-21). Values below 1 are treated as 1.
    /// </summary>
    public int SchedulePollerMaxParallelMarkets { get; set; } = 8;

    /// <summary>
    ///     Upper bound (seconds) of the random start delay applied to each market inside a poller
    ///     tick. With the dispatcher model this only staggers ENQUEUE times, which queue workers
    ///     largely collapse anyway; kept as a mild smoothing lever. 0 disables the jitter.
    /// </summary>
    public int SchedulePollerStartJitterSeconds { get; set; } = 2;

    /// <summary>
    ///     When true, job DTOs expose the relative server-side audio proxy path
    ///     (<c>jobs/{jobId}/audio/{renderToken}</c>, the token being the blob filename) instead of a
    ///     time-limited SAS URL, so the operator console streams audio through the Functions backend
    ///     (Easy Auth cookie; works off-network when the storage data plane is private). The token
    ///     makes the path change per render, which the console relies on to rebind its audio element
    ///     after a re-voice. External absolute URLs (e.g. the filler cart) still pass through
    ///     unchanged. Default false keeps local dev and tests on direct SAS URLs.
    /// </summary>
    public bool UseAudioProxy { get; set; }

    /// <summary>
    ///     Minutes past the Zetta delivery deadline (<see cref="Models.Job.FallbackDeadlineUtc" />,
    ///     i.e. the scheduled playout minus <see cref="FallbackDeadlineMinutes" />) before the
    ///     poller sweep marks a still-unapproved scheduled bulletin Missed. Past that point the
    ///     file can no longer reach Zetta in time for the slot, so the status records that settled
    ///     fact instead of leaving the bulletin in the review pile. Default 10 matches the
    ///     alerting grace so "we stop asking you to act" and "we mark it missed" happen at the
    ///     same moment. &lt;= 0 disables this scheduled-job arm (ad-hoc jobs still age out via
    ///     <see cref="ExpireWaitingApprovalHours" />).
    /// </summary>
    public int MissedAfterDeadlineMinutes { get; set; } = 10;

    /// <summary>
    ///     Seconds BEFORE the Zetta delivery deadline (<see cref="Models.Job.FallbackDeadlineUtc" />)
    ///     at which the poller sweep delivers an approved-but-unpublished bulletin. Approval is the
    ///     human gate; putting the approved audio in the drop is mechanical, and the sweep is the
    ///     net for when nobody clicks Publish.
    ///     <para>
    ///         The net used to fire AT the deadline, which made it structurally unable to work:
    ///         the sweep only sees a job once its deadline is reached, and delivery refuses any
    ///         slot whose deadline has passed (<c>ZettaFileDeliveryService.DeliverAsync</c>), so
    ///         the first sighting was already ~200 ms too late and every retry later still. On
    ///         2026-08-13 a KIIS 90s bulletin approved 89 s early aired nothing while logging the
    ///         same refusal every minute for ten minutes.
    ///     </para>
    ///     <para>
    ///         MUST exceed the poller's tick interval (60 s — <c>SchedulePollerTrigger</c>'s
    ///         <c>0 * * * * *</c>), otherwise no tick is guaranteed to land inside the window: a
    ///         deadline on a minute boundary with a 45 s lead has ticks only at −60 s (too early
    ///         to be a candidate) and +0 s (too late to deliver). Default 90 guarantees at least
    ///         one tick inside the window at any alignment, and stays well under
    ///         <see cref="FallbackDeadlineMinutes" /> so the window never opens before generation.
    ///         &lt;= 0 restores the old fire-at-the-deadline behaviour and is not recommended.
    ///     </para>
    /// </summary>
    public int ApprovedDeliveryLeadSeconds { get; set; } = 90;

    /// <summary>
    ///     Hours past creation before the poller sweep marks an ad-hoc/manual WaitingApproval
    ///     bulletin Missed (QT-008). An ad-hoc trigger has no playout to be late for, so it gets
    ///     this dwell window instead of the scheduled-job clock
    ///     (<see cref="MissedAfterDeadlineMinutes" />). Stops a stale unapproved bulletin from
    ///     permanently hijacking the operator console's urgency banner days after it was
    ///     requested. &lt;= 0 disables this ad-hoc arm.
    /// </summary>
    public int ExpireWaitingApprovalHours { get; set; } = 1;

    /// <summary>Master switch for official feed corroboration and injection in the broadcast pipeline.</summary>
    public bool OfficialFeedsEnabled { get; set; } = true;

    /// <summary>
    ///     Whether to corroborate HERE incidents against official feeds. A missing authority event
    ///     is never treated as proof that a closure is absent: the fusion layer demotes only when
    ///     the feed supplies a current event naming that same road.
    /// </summary>
    public bool CorroborateIncidents { get; set; } = true;

    /// <summary>Whether to inject official emergency incidents (crashes, hazards) missing from HERE.</summary>
    public bool InjectMissingOfficialIncidents { get; set; } = true;

    /// <summary>
    ///     Maximum distance in metres between an official event and HERE incident to consider a
    ///     spatial match. Same-named roads also use this threshold whenever HERE geometry exists;
    ///     road identity remains necessary but is no longer sufficient.
    /// </summary>
    public double MaxCorroborationDistanceMeters { get; set; } = 500.0;

    /// <summary>
    ///     Boundary grace used for fresh HERE closure protection and official event validity. A
    ///     present official start more than this far in the future or end more than this far in the
    ///     past is inactive; missing times remain unknown and are allowed.
    /// </summary>
    public int CorroborationGraceMinutes { get; set; } = 30;

    /// <summary>Per-feed budget for official road-authority fetches, fetched in parallel.</summary>
    public int OfficialFeedTimeoutSeconds { get; set; } = 5;

    /// <summary>Age at which served-stale official feed data becomes a warning rather than routine.</summary>
    public int OfficialFeedFreshMinutes { get; set; } = 15;

    /// <summary>Hard ceiling for serving stale official feed data after a failed fetch.</summary>
    public int OfficialFeedMaxStaleMinutes { get; set; } = 60;
}
