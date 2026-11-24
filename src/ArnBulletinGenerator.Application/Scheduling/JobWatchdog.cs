using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Owns the per-tick sweeps that keep the schedule/job store healthy independently of any
///     single schedule's own dispatch: fallback coverage for jobs past their deadline, approval
///     reminders/escalations, stale-job and stale-lock recovery. Extracted from
///     <see cref="SchedulePollerService" />, whose <see cref="SchedulePollerService.ProcessDueSchedulesAsync" />
///     runs these sweeps before scanning for newly-due schedules.
/// </summary>
internal sealed class JobWatchdog(
    IScheduleRepository scheduleRepository,
    IJobRepository jobRepository,
    IJobService jobService,
    INotificationService notificationService,
    IBulletinFallbackService bulletinFallbackService,
    ICronParser cronParser,
    IQueuePublisher queuePublisher,
    IOptions<BroadcastOptions> broadcastOptions,
    TimeProvider timeProvider,
    IBroadcastMetrics metrics,
    ILogger<SchedulePollerService> logger,
    StationConfigResolver stationConfigResolver,
    IJobWorkIndexRepository? workIndexRepository = null,
    IOptions<JobWorkIndexOptions>? workIndexOptions = null,
    IJobWatchdogClaimRepository? watchdogClaimRepository = null)
{
    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;
    private readonly JobWorkIndexOptions _workIndexOptions = workIndexOptions?.Value ?? new();
    private readonly IJobWorkIndexRepository? _workIndexRepository = workIndexRepository;
    private readonly IJobWatchdogClaimRepository? _watchdogClaimRepository = watchdogClaimRepository;
    private readonly ScheduleOccurrenceResolver _occurrenceResolver = new(cronParser, logger);
    private static readonly JobStatus[] MissableStatuses =
    [
        JobStatus.Requested,
        JobStatus.HereFetched,
        JobStatus.IncidentsSelected,
        JobStatus.AwaitingCuration,
        JobStatus.ScriptReady,
        JobStatus.SynthesizingAudio,
        JobStatus.AudioReady,
        JobStatus.WaitingApproval,
        JobStatus.Approved,
        JobStatus.Failed
    ];


    private bool IndexedPopulationEnabled =>
        _workIndexRepository is not null && _workIndexOptions.ReadEnabled && !_workIndexOptions.KillSwitch;

    private async Task<Result<IReadOnlyList<Job>>> ReadPopulationAsync(
        JobWorkKind kind,
        DateTime nowUtc,
        Func<CancellationToken, Task<Result<IEnumerable<Job>>>> legacyReader,
        CancellationToken cancellationToken)
    {
        if (!IndexedPopulationEnabled)
        {
            var legacy = await legacyReader(cancellationToken);
            return legacy.IsSuccess
                ? Result<IReadOnlyList<Job>>.Success(legacy.Value.ToList())
                : Result<IReadOnlyList<Job>>.Failure(legacy.Error);
        }

        var indexed = await ReadIndexedPopulationAsync(kind, nowUtc, cancellationToken);
        if (indexed.IsSuccess)
        {
            if (_workIndexOptions.CompareEnabled)
            {
                var legacy = await legacyReader(cancellationToken);
                if (legacy.IsSuccess)
                {
                    var indexedIds = indexed.Value.Select(job => job.JobId)
                        .ToHashSet(StringComparer.Ordinal);
                    var legacyIds = legacy.Value.Select(job => job.JobId)
                        .ToHashSet(StringComparer.Ordinal);
                    if (!indexedIds.SetEquals(legacyIds))
                    {
                        logger.LogWarning(
                            "[WorkIndexCompare] {Kind} candidate sets differ: indexed={IndexedCount}, legacy={LegacyCount}",
                            kind, indexedIds.Count, legacyIds.Count);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "[WorkIndex] {Kind} legacy candidate read failed; comparison skipped: {Error}",
                        kind, legacy.Error);
                }
            }

            return indexed;
        }

        logger.LogWarning(
            "[WorkIndex] {Kind} read failed; falling back to the legacy candidate query: {Error}",
            kind, indexed.Error);
        var fallback = await legacyReader(cancellationToken);
        return fallback.IsSuccess
            ? Result<IReadOnlyList<Job>>.Success(fallback.Value.ToList())
            : Result<IReadOnlyList<Job>>.Failure(fallback.Error);
    }

    private async Task<Result<IReadOnlyList<Job>>> ReadIndexedPopulationAsync(
        JobWorkKind kind,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (_workIndexRepository is null)
            return Result<IReadOnlyList<Job>>.Failure("Work index repository is unavailable.");

        var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var lowerBound = now.AddMinutes(-Math.Max(0, _workIndexOptions.LookbackMinutes));
        var upperBound = now.AddMinutes(Math.Max(0, _workIndexOptions.HorizonMinutes));
        var firstBucket = FloorToMinute(lowerBound);
        var lastBucket = FloorToMinute(upperBound);
        var pageSize = Math.Clamp(_workIndexOptions.PageSize, 1, 500);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _workIndexOptions.TickBudgetSeconds)));

        var itemsByJob = new Dictionary<string, JobWorkItem>(StringComparer.Ordinal);
        try
        {
            for (var bucket = firstBucket; bucket <= lastBucket; bucket = bucket.AddMinutes(1))
            {
                budget.Token.ThrowIfCancellationRequested();
                string? continuation = null;
                do
                {
                    budget.Token.ThrowIfCancellationRequested();
                    var page = await _workIndexRepository.ReadPageAsync(
                        kind, bucket, continuation, pageSize, budget.Token);
                    if (page.IsFailure)
                        return Result<IReadOnlyList<Job>>.Failure(page.Error);

                    foreach (var item in page.Value.Items)
                    {
                        if (item.Kind != kind || string.IsNullOrWhiteSpace(item.JobId)
                            || item.DueUtc < lowerBound || item.DueUtc > upperBound)
                            continue;
                        if (!itemsByJob.ContainsKey(item.JobId))
                            itemsByJob[item.JobId] = item;
                    }

                    var next = page.Value.ContinuationToken;
                    if (string.Equals(next, continuation, StringComparison.Ordinal)
                        && next is not null)
                    {
                        return Result<IReadOnlyList<Job>>.Failure(
                            "Work index returned an unchanged continuation token.");
                    }

                    continuation = next;
                } while (continuation is not null);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<IReadOnlyList<Job>>.Failure("Work index read exceeded the watchdog tick budget.");
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<Job>>.Failure(ex.Message);
        }

        var jobs = new List<Job>(itemsByJob.Count);
        foreach (var item in itemsByJob.Values
                     .OrderBy(item => item.DueUtc)
                     .ThenBy(item => item.JobId, StringComparer.Ordinal))
        {
            var job = await jobRepository.GetJobAsync(item.JobId, cancellationToken);
            if (job.IsFailure)
                return Result<IReadOnlyList<Job>>.Failure(
                    $"Work index candidate {item.JobId} could not be read: {job.Error}");
            // The work index is an ADVISORY projection: a sweep that already settled this job
            // updates the row after the aggregate, so a concurrent sweep can still read the old
            // row and load a job the settlement has moved on. The live status is authoritative,
            // so a candidate whose current status no longer belongs to this work kind is dropped
            // rather than acted on twice.
            if (!IsEligibleForKind(kind, job.Value.Status))
                continue;
            // A stale-recovery claim advances the aggregate touch timestamp before the work-index
            // projection is refreshed. Re-check the live row so that old index rows cannot make a
            // later concurrent sweep republish the same Requested job.
            if (kind == JobWorkKind.StaleRequested
                && now - job.Value.LastUpdatedAt < TimeSpan.FromMinutes(3))
                continue;
            jobs.Add(job.Value);
        }

        return Result<IReadOnlyList<Job>>.Success(jobs);
    }

    /// <summary>
    ///     The live statuses a work kind may still act on. Mirrors the legacy candidate queries,
    ///     which are the definition of each sweep's population.
    /// </summary>
    private static bool IsEligibleForKind(JobWorkKind kind, JobStatus status) => kind switch
    {
        JobWorkKind.CurationDue => status == JobStatus.AwaitingCuration,
        JobWorkKind.UndeliveredCovered => status == JobStatus.FallbackUsed,
        JobWorkKind.Escalation => status == JobStatus.WaitingApproval,
        JobWorkKind.StaleRequested => status == JobStatus.Requested,
        JobWorkKind.Missable => MissableStatuses.Contains(status),
        JobWorkKind.FallbackDue => status is not (JobStatus.Published or JobStatus.FallbackUsed
            or JobStatus.Missed),
        _ => true
    };

    private static DateTime FloorToMinute(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    private async Task<bool> TryClaimForSweepAsync(
        Job job,
        string operation,
        CancellationToken cancellationToken,
        TimeSpan? minimumAge = null)
    {
        if (_watchdogClaimRepository is null)
            return true;

        var claim = await _watchdogClaimRepository.TryClaimWatchdogJobAsync(
            job, minimumAge, cancellationToken);
        if (claim.IsFailure)
        {
            logger.LogWarning(
                "Failed to claim job {JobId} for {Operation}: {Error}",
                job.JobId, operation, claim.Error);
            return false;
        }

        return claim.Value;
    }

    /// <summary>
    ///     Resumes parked curation jobs once their five-minute window expires. The state transition
    ///     is saved before queue publication, so a second poller tick cannot enqueue the same job.
    /// </summary>
    internal async Task ProceedPastCurationDeadlineAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await ReadPopulationAsync(
            JobWorkKind.CurationDue, nowUtc,
            ct => jobRepository.ListJobsAsync([JobStatus.AwaitingCuration], ct),
            cancellationToken);
        if (candidates.IsFailure)
        {
            logger.LogWarning("Failed to list curation candidates: {Error}", candidates.Error);
            return;
        }

        foreach (var job in candidates.Value)
        {
            // The persisted slot deadline is authoritative. For legacy rows that predate the
            // column, derive the same cutoff the poller used from the scheduled slot. If neither
            // value exists, there is no safe way to prove the slot is still inside its window:
            // proceed immediately. No data defect may cost a bulletin.
            var deadlineUtc = job.CurationDeadlineUtc;
            if (deadlineUtc.IsNone)
            {
                logger.LogWarning(
                    "Job {JobId} reached curation without a persisted deadline; deriving it from ScheduledForUtc.",
                    job.JobId);
                deadlineUtc = job.ScheduledForUtc.Map(scheduledForUtc =>
                    SchedulePollerService.CalculateCurationDeadlineUtc(scheduledForUtc, _broadcastOptions));
                if (deadlineUtc.IsNone)
                {
                    logger.LogWarning(
                        "Job {JobId} reached curation without a persisted deadline or ScheduledForUtc; proceeding immediately so the slot is not stalled.",
                        job.JobId);
                }
            }

            if (deadlineUtc.HasValue && nowUtc < deadlineUtc.Value)
                continue;
            if (!job.VoiceConfig.HasValue)
            {
                logger.LogWarning("Cannot proceed curation job {JobId}: no resolved voice configuration", job.JobId);
                continue;
            }

            if (!await TryClaimForSweepAsync(
                    job, "curation deadline", cancellationToken))
                continue;

            var voice = job.VoiceConfig.Value;

            job.TransitionTo(JobStatus.IncidentsSelected, "system (curation deadline)",
                Option<string>.Some("Proceeded past curation deadline"), nowUtc);
            var save = await jobRepository.SaveJobAsync(job, cancellationToken);
            if (save.IsFailure)
                continue;

            var message = new BroadcastQueueMessage(
                job.JobId, job.Market, job.Market, (int)job.TargetDurationSeconds,
                job.Latitude.GetValueOr(0), job.Longitude.GetValueOr(0), job.RadiusKm.GetValueOr(1),
                voice.VoiceId, Model: voice.Model,
                VoiceSettings: voice.Settings.Match(s => (VoiceSettings?)s, () => null),
                TimezoneId: job.TimezoneId.Match(v => (string?)v, () => null),
                Coverage: job.Coverage.Match(c => (CoverageArea?)c, () => null),
                Content: job.ContentSelection.Match<BulletinContentSelection?>(c => c, () => null),
                PaceBoostWpm: voice.PaceBoostWpm,
                StationId: job.Station,
                Phase: Phase.Generate);

            var publish = await queuePublisher.PublishBroadcastMessageAsync(message, cancellationToken);
            if (publish.IsFailure)
            {
                job.TransitionTo(JobStatus.AwaitingCuration, "system (curation deadline)",
                    Option<string>.Some($"Queue publish failed: {publish.Error}"), nowUtc);
                await jobRepository.SaveJobAsync(job, cancellationToken);
                logger.LogError("Failed to proceed curation job {JobId}: {Error}", job.JobId, publish.Error);
                continue;
            }

            await jobRepository.AddLogAsync(job.JobId,
                "Proceeded past curation deadline", JobLogLevel.Info, cancellationToken);
            logger.LogInformation("Proceeded past curation deadline for job {JobId}", job.JobId);
        }
    }
    internal async Task ProcessFallbacksAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        // The horizon deliberately reaches PAST now, by the approved-delivery lead: an approved
        // bulletin has to be delivered while playout still accepts it, and a sweep that first sees
        // the job at its deadline can only ever arrive late. Everything else in the loop therefore
        // re-checks the REAL clock (HasReachedDeadline) before settling a slot.
        var horizonUtc = nowUtc.AddSeconds(Math.Max(_broadcastOptions.ApprovedDeliveryLeadSeconds, 0));
        var candidatesResult = await ReadPopulationAsync(
            JobWorkKind.FallbackDue, nowUtc,
            ct => jobRepository.ListJobsNeedingFallbackAsync(horizonUtc, ct),
            cancellationToken);
        if (candidatesResult.IsFailure)
        {
            logger.LogWarning("Failed to list fallback candidates: {Error}", candidatesResult.Error);
            return;
        }

        foreach (var job in candidatesResult.Value)
        {
            if (job.Status is JobStatus.FallbackUsed or JobStatus.Rejected or JobStatus.Missed
                or JobStatus.Published) continue;

            // A bulletin having AUDIO is not the same as the slot being covered: audio lives in
            // blob storage, and the only thing that puts it in the playout drop is a publish.
            // Approved is the one status the sweep DELIVERS rather than supersedes: a human has
            // signed off, so the slot is covered by airing that bulletin, not a standby.
            bool approvedAwaitingDelivery = job.Status is JobStatus.Approved && job.AudioUrl.HasValue;

            // Every other arm below settles a slot that is already lost — covering it with a
            // repeat/cart, or aging it out. None of that may touch a slot whose deadline is still
            // ahead of us, and inside the delivery lead it is: operators routinely approve in the
            // last two minutes, and superseding a live slot would throw away a valid bulletin.
            if (!approvedAwaitingDelivery && !IsDeadlineDue(job, nowUtc)) continue;

            // A restart can surface a backlog long after its slots have passed. Never let the
            // fallback sweep turn that backlog into fresh writes to the playout share. The
            // missed-slot sweep runs first and normally settles these jobs; this guard remains
            // here so a failed save or future sweep reordering still cannot deliver stale audio.
            if (IsPastMissedWindow(job, nowUtc))
            {
                logger.LogWarning(
                    "[Fallback] Job {JobId} ({Market}) is past the missed-slot window; refusing stale delivery.",
                    job.JobId, job.Market);
                continue;
            }


            // Skipping any job whose audio existed (the old IsValidAudio test) meant the one case
            // the fallback chain exists for — "no operator approves before the deadline"
            // (design intent: a dedicated fallback-fill cart guarantees no dead air) — got
            // neither the same-day repeat nor the cart, and nothing reached the station.
            if (approvedAwaitingDelivery)
            {
                if (!await TryClaimForSweepAsync(
                        job, "approved delivery", cancellationToken))
                    continue;
                await PublishApprovedBeforeDeadlineAsync(job, nowUtc, cancellationToken);
                continue;
            }

            // Dispatcher-model guard (2026-07-21): a deadline-passed job whose worker is
            // demonstrably ALIVE gets a short grace before the sweep covers it. Under the old
            // in-process execution the singleton tick physically could not sweep mid-generation,
            // so a near-deadline dispatch always got to finish; with queue execution the sweep
            // would steal the job seconds before its fresh audio lands and the worker's result
            // would be discarded by the finalized guard. Alive = an active pipeline status
            // touched within the grace window (workers advance status every few seconds). A
            // crashed/stalled worker goes silent, the grace lapses, and the next tick covers it —
            // the dead-air guarantee survives with a bounded, observable delay.
            if (IsActivelyGenerating(job, nowUtc))
            {
                logger.LogInformation(
                    "[Fallback] Job {JobId} ({Market}) is past its fallback deadline but its worker is active " +
                    "(status {Status}, touched {AgeSec:F0}s ago) — deferring the sweep one tick.",
                    job.JobId, job.Market, job.Status, (nowUtc - job.LastUpdatedAt).TotalSeconds);
                continue;
            }

            if (!await TryClaimForSweepAsync(
                    job, "fallback coverage", cancellationToken))
                continue;

            var outcome = await bulletinFallbackService.TryApplyFallbackAsync(
                job, FallbackReasons.MissedWindow, cancellationToken);
            switch (outcome.Kind)
            {
                case FallbackKind.SameDayBulletin:
                    metrics.TrackFallbackActivated(job.Market, "missed_window_or_invalid_audio");
                    await DeliverFallbackAsync(job, "the last approved bulletin from today",
                        cancellationToken);
                    // Closure card when an operator was actually asked to act (urgent tier
                    // fired); quiet covered card otherwise. Routine content-limited fallbacks
                    // never reach this path and stay silent (card design 2026-07-05).
                    await NotifySafeAsync(job,
                        job.Logs.Any(l => l.Message.StartsWith($"Job escalated: {UrgentReason}", StringComparison.Ordinal))
                            ? NotificationKind.MissedDeadlineClosure
                            : NotificationKind.FailedCovered,
                        cancellationToken);
                    break;
                case FallbackKind.FillerCart:
                    metrics.TrackFallbackActivated(job.Market, "filler_cart");
                    await DeliverFallbackAsync(job, "the standby cart", cancellationToken);
                    await NotifySafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);
                    break;
                case FallbackKind.None:
                    // Nothing available to air. The job stays in its current state and this
                    // path re-runs every poll tick, so dedupe the alert via a job log marker.
                    if (!job.Logs.Any(l => l.Message.StartsWith(NoFallbackAlertMarker, StringComparison.Ordinal)))
                    {
                        await NotifySafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);
                        await jobRepository.AddLogAsync(job.JobId,
                            $"{NoFallbackAlertMarker}: no same-day bulletin or filler available",
                            JobLogLevel.Warn, cancellationToken);
                    }
                    metrics.TrackPipelineStage("fallback_none", 0, job.Market, false);

                    break;
            }
        }
    }

    /// <summary>
    ///     Retries coverage that was attached to a slot but never reached playout, and says so out
    ///     loud when it can no longer be rescued.
    ///     <para>
    ///         <see cref="ProcessFallbacksAsync" /> attaches the cover and delivers it in the same
    ///         iteration, so a healthy covered slot holds <see cref="JobStatus.FallbackUsed" /> for
    ///         about two tenths of a second before <c>system (deadline)</c> approves and publishes
    ///         it (measured on deployed dev and test 2026-08-24: 0.104s to 0.223s across six
    ///         samples). A job that is still FallbackUsed on a later tick therefore did not merely
    ///         arrive early — its delivery FAILED, or the process died between the two saves.
    ///     </para>
    ///     <para>
    ///         Nothing used to look at it again.
    ///         <c>TableStorageJobRepository.ListJobsNeedingFallbackAsync</c> filters
    ///         <c>Status ne 'FallbackUsed'</c>, and the loop above skips the status too, so a
    ///         stranded cover sat silently until someone read the row by hand — 39 of them across
    ///         dev and test, aged 12.7 to 28.7 days when this sweep was written.
    ///     </para>
    ///     <para>
    ///         Three arms, because a stranded cover is in one of three genuinely different
    ///         situations. Its slot is still ahead: retry, exactly as
    ///         <see cref="PublishApprovedBeforeDeadlineAsync" /> retries an undelivered approved
    ///         bulletin. Its slot has just passed: it aired nothing, which is an operator-visible
    ///         event, so alert once. Its slot passed long ago: leave it entirely — those are the
    ///         pre-2026-08-11 residue and re-alerting on historical rows would page someone about
    ///         weeks-old slots on the first tick after deploy.
    ///     </para>
    /// </summary>
    internal async Task RecoverUndeliveredCoveredSlotsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await ReadPopulationAsync(
            JobWorkKind.UndeliveredCovered, nowUtc,
            ct => jobRepository.ListJobsAsync([JobStatus.FallbackUsed], ct),
            cancellationToken);
        if (candidates.IsFailure)
        {
            logger.LogWarning("Failed to list undelivered covered slots: {Error}", candidates.Error);
            return;
        }

        foreach (var job in candidates.Value)
        {
            // FallbackUsed without an applied-at stamp is not a covered slot this sweep owns.
            if (!job.FallbackAppliedAt.HasValue)
                continue;

            // Beyond the missed-slot window there is nothing to rescue and nothing new to say.
            if (IsPastCoveredSlotAlertWindow(job, nowUtc))
                continue;

            // The cover is still deliverable until its air time — the same cutoff
            // PublishApprovedBeforeDeadlineAsync and JobService.PublishJobAsync use for a covered
            // slot, deliberately NOT FallbackDeadlineUtc (which is already behind us by
            // construction: a cover only exists past that deadline).
            bool slotStillReachable = !job.ScheduledForUtc.HasValue || nowUtc <= job.ScheduledForUtc.Value;
            if (slotStillReachable)
            {
                if (!await TryClaimForSweepAsync(
                        job, "covered-slot delivery", cancellationToken))
                    continue;
                logger.LogWarning(
                    "[Fallback] Job {JobId} ({Market}) is covered but undelivered ({AgeSec:F0}s after the cover was "
                    + "applied) — retrying delivery while the slot is still reachable.",
                    job.JobId, job.Market, (nowUtc - job.FallbackAppliedAt.Value).TotalSeconds);
                await DeliverFallbackAsync(job, "the cover already chosen for this slot", cancellationToken);
                continue;
            }

            if (job.Logs.Any(l => l.Message.StartsWith(CoverUndeliveredMarker, StringComparison.Ordinal)))
                continue;

            if (!await TryClaimForSweepAsync(
                    job, "covered-slot alert", cancellationToken))
                continue;

            logger.LogError(
                "[Fallback] Job {JobId} ({Market}) was covered at {AppliedAt:O} but the audio never reached playout "
                + "and its slot has passed — the slot aired nothing.",
                job.JobId, job.Market, job.FallbackAppliedAt.Value);
            metrics.TrackZettaDeliveryFailure(job.Market, "covered_slot_undelivered");
            await jobRepository.AddLogAsync(job.JobId,
                $"{CoverUndeliveredMarker}: this slot was covered but the audio never reached playout, "
                + "and the slot has now passed. Nothing aired.",
                JobLogLevel.Error, cancellationToken);
            await NotifySafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);
        }
    }

    /// <summary>
    ///     True once a covered-but-undelivered slot is old enough that re-reporting it is noise.
    ///     Keyed on the air slot (a cover's real cutoff) plus the same
    ///     <see cref="BroadcastOptions.MissedAfterDeadlineMinutes" /> grace
    ///     <see cref="IsPastMissedWindow" /> uses, so "too old to alert" means the same span
    ///     everywhere. A job with no slot can never be judged stale and stays eligible.
    /// </summary>
    private bool IsPastCoveredSlotAlertWindow(Job job, DateTime nowUtc)
    {
        if (!job.ScheduledForUtc.HasValue || _broadcastOptions.MissedAfterDeadlineMinutes <= 0)
            return false;

        return nowUtc - job.ScheduledForUtc.Value
            >= TimeSpan.FromMinutes(_broadcastOptions.MissedAfterDeadlineMinutes);
    }

    /// <summary>
    ///     True when the slot's delivery deadline is at or behind the real clock. The sweep's query
    ///     horizon runs ahead of now by <see cref="BroadcastOptions.ApprovedDeliveryLeadSeconds" />,
    ///     so "the query returned it" no longer implies "its deadline passed" — every arm that
    ///     SETTLES a slot has to ask this rather than trust the candidate list.
    ///     <para>
    ///         A candidate with NO deadline counts as due. The real query cannot produce one (its
    ///         filter is <c>FallbackDeadlineUtc le horizon</c>, which a null never matches), so this
    ///         only decides the behaviour of a hand-built candidate — and there the pre-existing
    ///         answer was "sweep it", which the no-dead-air guarantee depends on.
    ///     </para>
    /// </summary>
    private static bool IsDeadlineDue(Job job, DateTime nowUtc)
    {
        return !job.FallbackDeadlineUtc.HasValue || nowUtc >= job.FallbackDeadlineUtc.Value;
    }

    private bool IsPastMissedWindow(Job job, DateTime nowUtc)
    {
        if (!job.FallbackDeadlineUtc.HasValue || _broadcastOptions.MissedAfterDeadlineMinutes <= 0)
            return false;

        return nowUtc - job.FallbackDeadlineUtc.Value
            >= TimeSpan.FromMinutes(_broadcastOptions.MissedAfterDeadlineMinutes);
    }


    /// <summary>
    ///     Gets a slot the sweep just covered into the playout drop.
    ///     <para>
    ///         Why this call exists at all:
    ///         <see cref="IBulletinFallbackService.TryApplyFallbackAsync" /> only attaches the
    ///         fallback audio to the job and marks it <c>FallbackUsed</c> — it does NOT deliver,
    ///         because <c>JobService.PublishJobAsync</c> is the only caller of the file-delivery
    ///         service. Until 2026-08-11 nothing here called through, so the standby cart — which
    ///         exists precisely for when nobody is watching — depended on somebody watching, and
    ///         the slot aired silence against the no-dead-air guarantee. Deleting
    ///         this call restores that bug exactly.
    ///     </para>
    ///     <para>
    ///         The sequence itself lives in <see cref="IJobService.DeliverCoveredSlotAsync" />
    ///         because three paths cover slots (this sweep, the orchestrator's unairable route,
    ///         and an operator's manual override) and each one that grew its own copy shipped a
    ///         variant of the same silent bug. A cover whose delivery fails HERE is picked back up
    ///         by <see cref="RecoverUndeliveredCoveredSlotsAsync" />, which is the only thing that
    ///         ever looks at <c>FallbackUsed</c> again.
    ///     </para>
    /// </summary>
    private async Task DeliverFallbackAsync(Job job, string what, CancellationToken cancellationToken)
    {
        var result = await jobService.DeliverCoveredSlotAsync(
            job.JobId, what, SystemDeadlineActor, cancellationToken);
        if (result.IsSuccess)
        {
            // Same rule as the job-log line: success here means the slot was COVERED and the
            // publish went through, not that audio reached playout — delivery is skipped when
            // playout is off for the environment or for this station.
            logger.LogInformation(
                "[Fallback] Job {JobId} ({Market}) covered by {What}.",
                job.JobId, job.Market, what);
            return;
        }

        // DeliverCoveredSlotAsync has already written the operator-facing job log; this is the
        // market-tagged telemetry line the alert rules read.
        logger.LogWarning(
            "[Fallback] Job {JobId} ({Market}) was covered by {What} but delivery failed: {Error}",
            job.JobId, job.Market, what, result.Error);
    }

    /// <summary>
    ///     Who the audit trail credits when the deadline, not a person, signs a fallback off.
    ///     Named rather than inlined so every entry in a job's history reads identically and
    ///     an operator can tell a system sign-off from their own at a glance.
    /// </summary>
    private const string SystemDeadlineActor = "system (deadline)";

    /// <summary>
    ///     Delivers an already-APPROVED bulletin that nobody published, in the window BEFORE its
    ///     delivery deadline. Approval is the human gate; getting the approved audio into the
    ///     playout drop is mechanical, and leaving it undelivered was dead air on a bulletin an
    ///     operator had already signed off.
    ///     <para>
    ///         The window matters: <c>ZettaFileDeliveryService.DeliverAsync</c> refuses any slot
    ///         whose deadline has passed, so this must run before it, never at it. See
    ///         <see cref="BroadcastOptions.ApprovedDeliveryLeadSeconds" /> for why firing at the
    ///         deadline could never work.
    ///     </para>
    ///     <para>
    ///         Failing here is not fatal to the slot: the next tick sees the job still Approved
    ///         and retries while the window is open, and the operator can still publish by hand. A
    ///         delivery failure leaves the job Approved by design (see
    ///         <c>JobService.PublishJobAsync</c>).
    ///     </para>
    /// </summary>
    private async Task PublishApprovedBeforeDeadlineAsync(Job job, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Past the delivery cutoff the refusal is certain, so an attempt is not a retry — it is
        // a guaranteed failure repeated every tick until the missed-slot window closes. That is
        // exactly what buried the 2026-08-13 KIIS 90s slot under twenty identical warnings and
        // sent ten duplicate failure metrics for one event. Say it once, count it once, and leave
        // the slot for MarkMissedExpiredSlotsAsync to settle.
        // Strictly AFTER, mirroring ZettaFileDeliveryService.DeliverAsync's own test: at exactly
        // the cutoff the share still accepts the file, so that tick is a real chance, not spam.
        // The cutoff mirrors JobService.PublishJobAsync exactly: a normal bulletin dies at
        // FallbackDeadlineUtc, but a COVERED slot (FallbackAppliedAt set) is deliverable until
        // its air time — it only ever exists past the fallback deadline, so holding its retries
        // to that clock would give a transiently-failed cover zero retry ticks.
        var deliveryCutoff = job.FallbackAppliedAt.HasValue ? job.ScheduledForUtc : job.FallbackDeadlineUtc;
        if (deliveryCutoff.HasValue && nowUtc > deliveryCutoff.Value)
        {
            if (job.Logs.Any(l => l.Message.StartsWith(DeliveryWindowClosedMarker, StringComparison.Ordinal)))
                return;

            var lateBy = nowUtc - deliveryCutoff.Value;
            logger.LogError(
                "[Fallback] Job {JobId} ({Market}) was approved but its delivery window closed "
                + "{LateBy} ago — playout will not accept it and the slot will air nothing.",
                job.JobId, job.Market, lateBy);
            metrics.TrackZettaDeliveryFailure(job.Market, "approved_delivery_window_closed");
            await jobRepository.AddLogAsync(job.JobId,
                $"{DeliveryWindowClosedMarker}: approved, but the playout delivery deadline "
                + $"({deliveryCutoff.Value:HH:mm:ss} UTC) passed {lateBy:hh\\:mm\\:ss} before this "
                + "bulletin was published, so it cannot be sent. Publish earlier or contact playout.",
                JobLogLevel.Error, cancellationToken);
            return;
        }

        var publishResult = await jobService.PublishJobAsync(
            job.JobId, ApprovalDeadlineActor, job.ETag, null, cancellationToken);

        if (publishResult.IsSuccess)
        {
            logger.LogInformation(
                "[Fallback] Job {JobId} ({Market}) was approved but unpublished — delivered automatically "
                + "ahead of its deadline.",
                job.JobId, job.Market);
            await jobRepository.AddLogAsync(job.JobId,
                "Approved but not published — delivered to playout automatically ahead of the deadline.",
                JobLogLevel.Warn, cancellationToken);
            return;
        }

        logger.LogWarning(
            "[Fallback] Job {JobId} ({Market}) is approved but automatic delivery failed: {Error}",
            job.JobId, job.Market, publishResult.Error);
        await jobRepository.AddLogAsync(job.JobId,
            $"Automatic delivery ahead of the approval deadline failed: {publishResult.Error}",
            JobLogLevel.Warn, cancellationToken);
    }

    /// <summary>Who the audit trail credits when the sweep, not a person, publishes.</summary>
    private const string ApprovalDeadlineActor = "system (approval deadline)";

    /// <summary>
    ///     Stable prefix so the once-per-slot "cannot be delivered" entry is recognisable in the
    ///     job's own log across ticks — the sweep re-reads that log to avoid repeating itself,
    ///     the same dedupe mechanism <see cref="NoFallbackAlertMarker" /> uses.
    /// </summary>
    private const string DeliveryWindowClosedMarker = "Playout delivery window closed";

    private const string NoFallbackAlertMarker = "Fallback alert sent";

    /// <summary>
    ///     Stable prefix for the once-per-slot "covered but never delivered" entry, deduped through
    ///     the job's own log exactly like the two markers above.
    /// </summary>
    private const string CoverUndeliveredMarker = "Covered slot never delivered";

    // Grace window for the fallback sweep's in-flight guard: a worker actively generating
    // advances job status every few seconds, so a job in an active pipeline status untouched
    // for longer than this is treated as crashed/stalled and gets covered. Kept well under
    // FallbackDeadlineMinutes so a deferred sweep still lands before playout.
    private const int InFlightGraceMinutes = 2;

    /// <summary>
    ///     True when the job is in an active generation status and was touched recently enough
    ///     that a live queue worker is plausibly still producing its audio.
    /// </summary>
    private static bool IsActivelyGenerating(Job job, DateTime nowUtc)
    {
        return job.Status is JobStatus.Requested or JobStatus.HereFetched or JobStatus.IncidentsSelected
                   or JobStatus.ScriptReady or JobStatus.SynthesizingAudio or JobStatus.AudioReady
               && nowUtc - job.LastUpdatedAt <= TimeSpan.FromMinutes(InFlightGraceMinutes);
    }

    internal async Task NotifySafeAsync(Job job, NotificationKind kind, CancellationToken ct)
    {
        var result = await notificationService.SendJobNotificationAsync(job, kind, ct);
        if (result.IsFailure)
            logger.LogWarning("Fallback notification failed for job {JobId}: {Error}", job.JobId, result.Error);
    }

    // Stable dedupe tokens — EscalateJobAsync fires once per (job, reason).
    private const string ReminderReason = "approval reminder — inside the notify window";
    private const string UrgentReason = "playout imminent — inside the escalation window";

    // A job whose playout passed longer ago than this never alerts: a days-stale
    // waiting job must not suddenly page anyone (see QT-004/QT-008, 2026-07-05).
    private const int PostPlayoutGraceMinutes = 10;

    // Suppress the reminder tier for very fresh jobs: at generation time the
    // WaitingApproval card has just been sent, and with NotifyBeforePlayoutMinutes
    // >= ScheduleLeadTimeMinutes the reminder window is already open at creation.
    private const int MinJobAgeForReminderMinutes = 2;

    /// <summary>
    ///     Two-tier approval alerts (SoW "approval notifications and reminders"; wired 2026-07-05,
    ///     QT-004 — previously dead code). A WaitingApproval job whose playout time is inside its
    ///     schedule's NotifyBeforePlayoutMinutes window gets a reminder card; inside
    ///     EscalationBeforePlayoutMinutes (or past playout, within grace) an urgent card.
    ///     Each tier fires exactly once per job via EscalateJobAsync's per-reason dedupe.
    /// </summary>
    internal async Task EscalateOverdueApprovalsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var waitingResult = await ReadPopulationAsync(
            JobWorkKind.Escalation, nowUtc,
            ct => jobRepository.ListJobsAsync([JobStatus.WaitingApproval], ct),
            cancellationToken);
        if (waitingResult.IsFailure)
        {
            logger.LogWarning("Failed to list waiting jobs for escalation: {Error}", waitingResult.Error);
            return;
        }

        var waiting = waitingResult.Value.Where(j => j.ScheduledForUtc.HasValue).ToList();
        if (waiting.Count == 0) return;

        var schedulesResult = await scheduleRepository.ListSchedulesAsync(cancellationToken);
        if (schedulesResult.IsFailure)
        {
            logger.LogWarning("Failed to list schedules for escalation: {Error}", schedulesResult.Error);
            return;
        }

        var schedules = schedulesResult.Value
            .GroupBy(s => s.MarketId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var job in waiting)
        {
            if (!schedules.TryGetValue(job.Market, out var schedule)) continue;

            var playoutUtc = job.ScheduledForUtc.Value;
            if (nowUtc > playoutUtc.AddMinutes(PostPlayoutGraceMinutes)) continue;

            if (nowUtc >= playoutUtc.AddMinutes(-schedule.EscalationBeforePlayoutMinutes))
            {
                await jobService.EscalateJobAsync(job.JobId, UrgentReason, cancellationToken);
            }
            else if (nowUtc >= playoutUtc.AddMinutes(-schedule.NotifyBeforePlayoutMinutes)
                     && nowUtc - job.CreatedAt >= TimeSpan.FromMinutes(MinJobAgeForReminderMinutes))
            {
                await jobService.EscalateJobAsync(job.JobId, ReminderReason, cancellationToken);
            }
        }
    }

    /// <summary>
    ///     QT-008 hygiene sweep: any unpublished scheduled job that can no longer make its slot
    ///     is marked Missed before fallback delivery is considered. This includes an approved
    ///     bulletin that was never published: approval is not permission to air stale traffic
    ///     after the slot has passed. The transition is silent because the point is to settle a
    ///     past slot, not to manufacture an operator decision or notification.
    ///
    ///     Scheduled jobs use <see cref="Job.FallbackDeadlineUtc" /> plus
    ///     <see cref="BroadcastOptions.MissedAfterDeadlineMinutes" />. Ad-hoc jobs have no slot,
    ///     so only WaitingApproval jobs age out against <see cref="Job.CreatedAt" /> plus
    ///     <see cref="BroadcastOptions.ExpireWaitingApprovalHours" />. Each arm is disabled by
    ///     its option being &lt;= 0.
    internal async Task MarkMissedExpiredSlotsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidatesResult = await ReadPopulationAsync(
            JobWorkKind.Missable, nowUtc,
            ct => jobRepository.ListJobsAsync(MissableStatuses, ct),
            cancellationToken);
        if (candidatesResult.IsFailure)
        {
            logger.LogWarning("Failed to list jobs for missed-slot sweep: {Error}", candidatesResult.Error);
            return;
        }

        var adHocDwell = TimeSpan.FromHours(_broadcastOptions.ExpireWaitingApprovalHours);
        var scheduledGrace = TimeSpan.FromMinutes(_broadcastOptions.MissedAfterDeadlineMinutes);
        foreach (var job in candidatesResult.Value)
        {
            Option<DateTime> deadlineUtc;
            TimeSpan window;
            string deadlineKind;
            if (job.FallbackDeadlineUtc.HasValue)
            {
                if (scheduledGrace <= TimeSpan.Zero) continue;
                deadlineUtc = job.FallbackDeadlineUtc;
                window = scheduledGrace;
                deadlineKind = "Zetta delivery";
            }
            else
            {
                // Ad-hoc bulletins have no promised playout time. Preserve approved/in-flight
                // ones for manual handling; only an untouched review item ages out.
                if (job.Status != JobStatus.WaitingApproval || adHocDwell <= TimeSpan.Zero) continue;
                deadlineUtc = Option<DateTime>.Some(job.CreatedAt);
                window = adHocDwell;
                deadlineKind = "ad-hoc dwell";
            }

            if (nowUtc - deadlineUtc.Value < window) continue;

            if (!await TryClaimForSweepAsync(
                    job, "missed-slot settlement", cancellationToken))
                continue;

            var missedResult = job.MarkMissed(deadlineUtc.Value, nowUtc);
            if (missedResult.IsFailure)
            {
                logger.LogWarning("Failed to mark stale job {JobId} missed: {Error}",
                    job.JobId, missedResult.Error);
                continue;
            }

            var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
            if (saveResult.IsFailure)
            {
                logger.LogWarning("Failed to save missed job {JobId}: {Error}", job.JobId, saveResult.Error);
                continue;
            }

            logger.LogInformation(
                "Marked stale job {JobId} missed - {DeadlineKind} deadline {Deadline:O} UTC was {MinutesPast:F1} minutes past",
                job.JobId, deadlineKind, deadlineUtc.Value, (nowUtc - deadlineUtc.Value).TotalMinutes);
        }
    }

    private static bool IsTerminalJobStatus(JobStatus status)
    {
        return status is JobStatus.WaitingApproval or JobStatus.Approved or JobStatus.Rejected
            or JobStatus.Published or JobStatus.FallbackUsed or JobStatus.Failed or JobStatus.Missed;
    }

    internal async Task RecoverStaleScheduleLocksAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(cancellationToken);
        if (schedulesResult.IsFailure)
        {
            logger.LogWarning("Failed to list schedules for lock recovery: {Error}", schedulesResult.Error);
            return;
        }

        var lockedSchedules = schedulesResult.Value
            .Where(s => s.Enabled && s.CurrentJobId.HasValue)
            .ToList();

        if (lockedSchedules.Count == 0) return;

        foreach (var schedule in lockedSchedules)
        {
            string jobId = schedule.CurrentJobId.Value;
            var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);

            bool shouldRelease;
            string reason;

            if (jobResult.IsFailure)
            {
                // Job doesn't exist — orphaned lock
                shouldRelease = true;
                reason = $"referenced job {jobId} no longer exists";
            }
            else
            {
                var job = jobResult.Value;
                if (IsTerminalJobStatus(job.Status))
                {
                    // Job completed/failed but lock wasn't released
                    shouldRelease = true;
                    reason = $"referenced job {jobId} is in terminal state {job.Status}";
                }
                else
                {
                    // Job is still in-progress — check if it's been too long (10 minutes)
                    var jobAge = nowUtc - job.CreatedAt;
                    shouldRelease = jobAge > TimeSpan.FromMinutes(10);
                    reason = shouldRelease
                        ? $"referenced job {jobId} has been running for {jobAge.TotalMinutes:F0} minutes (status: {job.Status})"
                        : string.Empty;
                }
            }

            if (!shouldRelease) continue;

            logger.LogWarning(
                "Releasing stale schedule lock on {MarketId}: {Reason}",
                schedule.MarketId, reason);

            var nextRunUtc = _occurrenceResolver.NextRunUtc(schedule,
                timeProvider.GetUtcNow().UtcDateTime);
            await scheduleRepository.CompleteJobAsync(schedule.MarketId, nextRunUtc, cancellationToken);
        }
    }

    internal async Task RecoverStaleJobsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var staleResult = await ReadPopulationAsync(
            JobWorkKind.StaleRequested, nowUtc,
            ct => jobRepository.ListStaleRequestedJobsAsync(nowUtc.AddMinutes(-3), ct),
            cancellationToken);
        if (staleResult.IsFailure)
        {
            logger.LogWarning("Failed to query stale jobs: {Error}", staleResult.Error);
            return;
        }

        var staleJobs = staleResult.Value.ToList();
        if (staleJobs.Count == 0) return;

        logger.LogWarning("Found {Count} stale Requested job(s), attempting recovery", staleJobs.Count);

        foreach (var job in staleJobs)
        {
            // Republishing is not idempotent at the queue boundary. Claim the stale row with its
            // loaded ETag before resolving configuration or publishing; a concurrent sweep that
            // loses the conditional update must abandon this candidate rather than retry it.
            if (!await TryClaimForSweepAsync(
                    job, "stale recovery", cancellationToken, TimeSpan.FromMinutes(3)))
                continue;

            // Try job fields first, fall back to schedule
            double latitude, longitude, radiusKm;
            string? timezoneId;
            string? scheduleVoiceId = null;

            if (job.Latitude.HasValue && job.Longitude.HasValue && job.RadiusKm.HasValue)
            {
                latitude = job.Latitude.Value;
                longitude = job.Longitude.Value;
                radiusKm = job.RadiusKm.Value;
                timezoneId = job.TimezoneId.Match(tz => tz, () => null!);
            }
            else
            {
                // Backward compat: old jobs without geo fields need schedule lookup
                var scheduleResult = await scheduleRepository.GetScheduleAsync(job.Market, cancellationToken);
                if (!scheduleResult.IsSuccess || scheduleResult.Value is null)
                {
                    logger.LogWarning(
                        "Stale recovery failed for job {JobId}: no geo data on job and no schedule found for market {Market}",
                        job.JobId, job.Market);
                    var markFailedResult = job.MarkFailed("Stale recovery failed: no geo data on job and no schedule found for market", timeProvider.GetUtcNow().UtcDateTime);
                    await jobRepository.SaveJobAsync(job, cancellationToken);
                    if (markFailedResult.IsSuccess)
                        await NotifySafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);
                    continue;
                }

                var schedule = scheduleResult.Value;
                latitude = schedule.Coverage.Latitude;
                longitude = schedule.Coverage.Longitude;
                radiusKm = schedule.Coverage.RadiusKm;
                timezoneId = schedule.Timezone;
                scheduleVoiceId = _broadcastOptions.DefaultVoiceId;
            }

            string? voiceId = job.VoiceConfig.Match(vc => vc.VoiceId, () => scheduleVoiceId);
            string? model = job.VoiceConfig.Match(vc => vc.Model, () => null!);
            var voiceSettings =
                job.VoiceConfig.Match(vc => vc.Settings.Match(s => (VoiceSettings?)s, () => null), () => null);
            double paceBoostWpm = job.VoiceConfig.Match(vc => vc.PaceBoostWpm, () => 0);

            // Re-resolve delivery style, playbook + pronunciation from the schedule (fail-soft):
            // they are not persisted on the job record, and a recovered scheduled job must not
            // silently regenerate without its station's speech configuration.
            var recoveredDelivery = default(StationConfigResolver.ResolvedDelivery);
            PlaybookOverlay? recoveredPlaybook = null;
            var recoveredPronunciation = PronunciationStack.Empty;
            var recoverySchedule = await scheduleRepository.GetScheduleAsync(job.Market, cancellationToken);
            if (recoverySchedule is { IsSuccess: true, Value: not null })
            {
                recoveredDelivery = await stationConfigResolver.ResolveDeliveryStyleAsync(recoverySchedule.Value, cancellationToken);
                recoveredPlaybook = await stationConfigResolver.ResolvePlaybookAsync(recoverySchedule.Value, cancellationToken);
                recoveredPronunciation = await stationConfigResolver.ResolvePronunciationStackAsync(
                    recoverySchedule.Value, model ?? string.Empty, cancellationToken);
            }

            var message = new BroadcastQueueMessage(
                job.JobId,
                job.Market,
                job.Market,
                (int)job.TargetDurationSeconds,
                latitude,
                longitude,
                radiusKm,
                voiceId,
                model,
                VoiceSettings: voiceSettings,
                TimezoneId: timezoneId,
                Coverage: job.Coverage.Match(c => (CoverageArea?)c, () => null),
                Content: job.ContentSelection.Match<BulletinContentSelection?>(c => c, () => null),
                PaceBoostWpm: paceBoostWpm,
                StationId: job.Station,
                DeliveryStyle: recoveredDelivery.Policy.Match(p => (DeliveryStylePolicy?)p, () => null),
                DeliveryStyleId: recoveredDelivery.Id.Match(i => (string?)i, () => null),
                DeliveryStyleVersion: recoveredDelivery.Version.Match(v => (int?)v, () => null),
                Playbook: recoveredPlaybook,
                PronunciationLocator: recoveredPronunciation.Station.Match(l => (PronunciationLocator?)l, () => null),
                GlobalPronunciationLocator: recoveredPronunciation.Global.Match(l => (PronunciationLocator?)l, () => null));

            var publishResult = await queuePublisher.PublishBroadcastMessageAsync(message, cancellationToken);
            if (publishResult.IsFailure)
            {
                logger.LogError("Failed to re-publish stale job {JobId}: {Error}", job.JobId, publishResult.Error);
                var markFailedResult = job.MarkFailed($"Stale recovery failed: {publishResult.Error}", timeProvider.GetUtcNow().UtcDateTime);
                await jobRepository.SaveJobAsync(job, cancellationToken);
                if (markFailedResult.IsSuccess)
                    await NotifySafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);
                continue;
            }

            await jobRepository.AddLogAsync(job.JobId,
                "Recovered stale Requested job — re-published to broadcast queue", JobLogLevel.Warn, cancellationToken);
            logger.LogInformation("Recovered stale job {JobId} for market {Market}", job.JobId, job.Market);
        }
    }
}
