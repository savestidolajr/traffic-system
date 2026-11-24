using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Application service for processing scheduled broadcasts.
///     Extracts orchestration logic from the timer trigger.
/// </summary>
public sealed class SchedulePollerService(
    IScheduleRepository scheduleRepository,
    IJobRepository jobRepository,
    IJobService jobService,
    INotificationService notificationService,
    IBulletinFallbackService bulletinFallbackService,
    ICronParser cronParser,
    IModelSelector modelSelector,
    IVoiceProfileService voiceProfileService,
    IVoiceProvider voiceProvider,
    IDeliveryStyleService deliveryStyleService,
    IBulletinPlaybookService playbookService,
    IBookendService bookendService,
    IPronunciationDictionaryService pronunciationDictionaryService,
    IQueuePublisher queuePublisher,
    IOptions<BroadcastOptions> broadcastOptions,
    TimeProvider timeProvider,
    IBroadcastMetrics metrics,
    ILogger<SchedulePollerService> logger,
    IJobWorkIndexRepository? workIndexRepository = null,
    IOptions<JobWorkIndexOptions>? workIndexOptions = null) : ISchedulePollerService
{
    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;
    private readonly ScheduleOccurrenceResolver _occurrenceResolver = new(cronParser, logger);

    // Collaborators extracted from this class: each owns one slice and gets exactly the
    // dependencies its methods use. Built here (plain `new`, no DI changes) so the public
    // constructor signature — which the test suite constructs directly — stays unchanged.
    // Built via one static factory (not two separate field initializers, which cannot
    // reference each other) so JobWatchdog's stale-job re-resolution path shares the SAME
    // StationConfigResolver instance as the poller's own dispatch path — recovery must keep
    // calling the identical resolver logic, never a duplicate.
    private readonly (StationConfigResolver Config, JobWatchdog Watchdog) _collaborators = BuildCollaborators(
        modelSelector, voiceProfileService, voiceProvider, deliveryStyleService, playbookService, bookendService,
        pronunciationDictionaryService,
        broadcastOptions, timeProvider, logger, scheduleRepository, jobRepository, jobService,
        notificationService, bulletinFallbackService, cronParser, queuePublisher, metrics,
        workIndexRepository, workIndexOptions);

    private StationConfigResolver _stationConfigResolver => _collaborators.Config;
    private JobWatchdog _jobWatchdog => _collaborators.Watchdog;

    private static (StationConfigResolver Config, JobWatchdog Watchdog) BuildCollaborators(
        IModelSelector modelSelector,
        IVoiceProfileService voiceProfileService,
        IVoiceProvider voiceProvider,
        IDeliveryStyleService deliveryStyleService,
        IBulletinPlaybookService playbookService,
        IBookendService bookendService,
        IPronunciationDictionaryService pronunciationDictionaryService,
        IOptions<BroadcastOptions> broadcastOptions,
        TimeProvider timeProvider,
        ILogger<SchedulePollerService> logger,
        IScheduleRepository scheduleRepository,
        IJobRepository jobRepository,
        IJobService jobService,
        INotificationService notificationService,
        IBulletinFallbackService bulletinFallbackService,
        ICronParser cronParser,
        IQueuePublisher queuePublisher,
        IBroadcastMetrics metrics,
        IJobWorkIndexRepository? workIndexRepository,
        IOptions<JobWorkIndexOptions>? workIndexOptions)
    {
        var config = new StationConfigResolver(modelSelector, voiceProfileService, voiceProvider,
            deliveryStyleService, playbookService, bookendService, pronunciationDictionaryService, broadcastOptions,
            timeProvider, logger);
        var watchdog = new JobWatchdog(scheduleRepository, jobRepository, jobService, notificationService,
            bulletinFallbackService, cronParser, queuePublisher, broadcastOptions, timeProvider, metrics,
            logger, config, workIndexRepository, workIndexOptions,
            watchdogClaimRepository: jobRepository as IJobWatchdogClaimRepository);
        return (config, watchdog);
    }

    /// <summary>
    ///     Processes all schedules that are due for broadcast.
    /// </summary>
    public async Task ProcessDueSchedulesAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Recover any jobs stuck in Requested state
        await _jobWatchdog.RecoverStaleJobsAsync(nowUtc, cancellationToken);

        // Recover schedule locks where the referenced job is gone or terminal
        await _jobWatchdog.RecoverStaleScheduleLocksAsync(nowUtc, cancellationToken);

        // Settle slots that are already beyond the delivery grace BEFORE fallback processing.
        // Otherwise an app restart can publish an hours-old backlog to the live drop share.
        await _jobWatchdog.MarkMissedExpiredSlotsAsync(nowUtc, cancellationToken);

        // Cover jobs that have crossed the delivery deadline but remain inside the grace window.
        await _jobWatchdog.ProcessFallbacksAsync(nowUtc, cancellationToken);

        // Rescue coverage that was attached but never delivered. Runs AFTER the fallback sweep so
        // a cover applied on this very tick is already published and never seen here; anything this
        // finds is a genuine stranding (delivery failed, or the process died mid-sequence).
        await _jobWatchdog.RecoverUndeliveredCoveredSlotsAsync(nowUtc, cancellationToken);
        await _jobWatchdog.ProceedPastCurationDeadlineAsync(nowUtc, cancellationToken);

        // Approval reminders / escalations for bulletins still actionable near playout.
        await _jobWatchdog.EscalateOverdueApprovalsAsync(nowUtc, cancellationToken);

        int leadTimeMinutes = Math.Max(_broadcastOptions.ScheduleLeadTimeMinutes, 0);
        var thresholdUtc = nowUtc.AddMinutes(leadTimeMinutes);

        var dueSchedulesResult = await scheduleRepository.GetDueSchedulesAsync(thresholdUtc, cancellationToken);
        if (dueSchedulesResult.IsFailure)
        {
            logger.LogError("Failed to get due schedules: {Error}", dueSchedulesResult.Error);
            return;
        }

        var dueSchedules = dueSchedulesResult.Value.ToList();
        if (dueSchedules.Count == 0) return;

        logger.LogInformation("Found {Count} due schedule(s): {Markets}",
            dueSchedules.Count,
            string.Join(", ", dueSchedules.Select(s => s.MarketId)));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _broadcastOptions.SchedulePollerMaxParallelMarkets),
            CancellationToken = cancellationToken
        };
        try
        {
            await Parallel.ForEachAsync(dueSchedules, parallelOptions, async (schedule, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * _broadcastOptions.SchedulePollerStartJitterSeconds), ct);
                await ProcessScheduleAsync(schedule, ct);
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Parallel schedule processing was cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception during parallel schedule processing");
        }
    }

    /// <summary>
    ///     Processes a single schedule - creates job, fetches context, runs orchestration.
    /// </summary>
    public async Task ProcessScheduleAsync(MarketSchedule schedule, CancellationToken cancellationToken)
    {
        string jobId = Guid.NewGuid().ToString("N");
        // Missed-window detection: if NextRunUtc is more than 2 minutes in the past
        // we treat this as a missed window (a deploy or downtime crossed the slot).
        // The catch-up still fires the bulletin, but we emit a metric so ops can see it.
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (schedule.NextRunUtc.HasValue)
        {
            var lateBy = nowUtc - schedule.NextRunUtc.Value;
            if (lateBy > TimeSpan.FromMinutes(2))
            {
                logger.LogWarning(
                    "[CatchUp] Missed window for {MarketId} — scheduled {Scheduled:o}, processing now ({LateBySec:F0}s late)",
                    schedule.MarketId, schedule.NextRunUtc.Value, lateBy.TotalSeconds);
                metrics.TrackFallbackActivated(schedule.MarketId, "missed_window_catchup");
                metrics.TrackPipelineStage("missed_window_lateness", lateBy.TotalSeconds,
                    schedule.MarketId, success: false);
            }
        }
        logger.LogInformation("Processing schedule {MarketId} with job {JobId}", schedule.MarketId, jobId);

        // Try to acquire the schedule lock (concurrent execution guard)
        var lockResult = await scheduleRepository.TryStartJobAsync(schedule.MarketId, jobId, cancellationToken);
        if (lockResult.IsFailure)
        {
            logger.LogWarning("Could not start job for {MarketId}: {Error}", schedule.MarketId, lockResult.Error);
            return;
        }

        try
        {
            var scheduledForUtc = schedule.NextRunUtc.GetValueOr(timeProvider.GetUtcNow().UtcDateTime);
            var curationDeadlineUtc = CalculateCurationDeadlineUtc(scheduledForUtc, _broadcastOptions);
            var fallbackDeadlineUtc = scheduledForUtc.AddMinutes(-_broadcastOptions.FallbackDeadlineMinutes);
            // Check for previous broadcast on the same calendar day (UTC) for context
            var previousContext = await GetPreviousBroadcastContextAsync(schedule, cancellationToken);
            var voice = await _stationConfigResolver.ResolveVoiceAsync(schedule, cancellationToken);
            var delivery = await _stationConfigResolver.ResolveDeliveryStyleAsync(schedule, cancellationToken);
            var playbook = await _stationConfigResolver.ResolvePlaybookAsync(schedule, cancellationToken);
            var pronunciation = await _stationConfigResolver.ResolvePronunciationStackAsync(schedule, voice.Model, cancellationToken);
            var bookends = await _stationConfigResolver.ResolveBookendsAsync(schedule, cancellationToken);

            var jobResult = CreateJob(jobId, schedule, voice, previousContext, scheduledForUtc,
                curationDeadlineUtc, fallbackDeadlineUtc);
            if (jobResult.IsFailure)
            {
                logger.LogError("Could not create job for {MarketId}: {Error}", schedule.MarketId, jobResult.Error);
                return;
            }

            // A slot whose air time has already passed cannot reach playout, so generating for it
            // buys nothing and costs a full draft/review/safety pass and a TTS render.
            // ZettaFileDeliveryService refuses to write to the share once ScheduledForUtc has gone
            // by — deliberately, since the 2026-08-11 incident where a restart sweep wrote 15
            // expired slots' stale audio to production — so every one of those jobs was destined
            // for MISSED before its first token. Test 2026-08-24: 21 missed slots in one morning,
            // 20 of which generated a real script first; job d06dfc18 was created at 07:45:02 for
            // a 07:45:00 slot and was refused delivery 61 seconds later.
            //
            // The slot is lost either way — this does not save it, and the upstream question of
            // why the poller reached it late is separate and still open. What it removes is the
            // spend and the misleading record of a bulletin that was never airable. The job is
            // still written, as Missed, so the gap stays visible to an operator rather than
            // silently disappearing; the finally block below releases the lock and advances the
            // schedule exactly as a dispatched slot would.
            var slotNowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (slotNowUtc >= scheduledForUtc)
            {
                var expiredJob = jobResult.Value;
                expiredJob.MarkMissed(scheduledForUtc, slotNowUtc);
                await jobRepository.SaveJobAsync(expiredJob, cancellationToken);
                logger.LogWarning(
                    "Schedule {MarketId} reached at {Now:O}, after its {ScheduledFor:O} slot had already "
                    + "aired — recorded as missed without generating, since delivery would refuse it.",
                    schedule.MarketId, slotNowUtc, scheduledForUtc);
                return;
            }

            await jobRepository.SaveJobAsync(jobResult.Value, cancellationToken);

            // Dispatch to the broadcast queue (dispatcher model, 2026-07-21): generation runs on
            // queue workers, so the poller tick stays O(seconds) regardless of how many markets
            // share a clock slot and the per-minute watchdog sweeps (fallback deadlines,
            // escalations, stale recovery) can never be starved by long generations. The message
            // snapshots the FULL resolved station config — voice/model, delivery style, playbook,
            // pronunciation — so queue redelivery and recovery replay exactly what was resolved.
            var message = BuildScheduledQueueMessage(
                jobId, schedule, voice, delivery, playbook, pronunciation, bookends);
            var publishResult = await queuePublisher.PublishBroadcastMessageAsync(message, cancellationToken);

            if (publishResult.IsFailure)
            {
                logger.LogError("Failed to dispatch broadcast for {MarketId}: {Error}",
                    schedule.MarketId, publishResult.Error);
                var job = jobResult.Value;
                var markFailedResult = job.MarkFailed($"Dispatch failed: {publishResult.Error}",
                    timeProvider.GetUtcNow().UtcDateTime);
                await jobRepository.SaveJobAsync(job, cancellationToken);
                if (markFailedResult.IsSuccess)
                    await _jobWatchdog.NotifySafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);
            }
            else
            {
                logger.LogInformation("Broadcast dispatched for {MarketId}, JobId: {JobId}",
                    schedule.MarketId, jobId);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Operation cancelled for schedule {MarketId}", schedule.MarketId);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error processing schedule {MarketId}", schedule.MarketId);
            try
            {
                var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
                if (jobResult.IsSuccess)
                {
                    var markFailedResult = jobResult.Value.MarkFailed(ex.Message, timeProvider.GetUtcNow().UtcDateTime);
                    await jobRepository.SaveJobAsync(jobResult.Value, cancellationToken);
                    if (markFailedResult.IsSuccess)
                        await _jobWatchdog.NotifySafeAsync(jobResult.Value, NotificationKind.FailedNotCovered, cancellationToken);
                }
            }
            catch (Exception markEx)
            {
                logger.LogError(markEx, "Failed to mark job {JobId} as failed", jobId);
            }
        }
        finally
        {
            // Release the lock and calculate next run time. When ScheduleLeadTimeMinutes exceeds
            // the cron's own inter-occurrence gap (e.g. a 15-min lead on a */10-min test cron),
            // "the occurrence right after the one that just fired" can already sit inside the
            // very same lead window — the next poller tick (seconds later) would see it as due
            // and fire again for what looks like the same occurrence (observed 2026-07-19/20:
            // two jobs ~61s apart from one schedule enable). CalculateNextRun's strictly-after
            // contract guarantees each step moves to a genuinely later occurrence, so skipping
            // forward past every occurrence that would already be due by completion time can
            // never revisit the one that just fired — it only throttles a schedule whose period
            // is shorter than its own lead time down to roughly one fire per lead-time span,
            // instead of firing every occurrence back-to-back.
            var baseTime = schedule.NextRunUtc.GetValueOr(timeProvider.GetUtcNow().UtcDateTime);
            var nextRunUtc = _occurrenceResolver.NextRunUtc(schedule, baseTime);
            var notDueBeforeUtc =
                timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_broadcastOptions.ScheduleLeadTimeMinutes);
            for (int guard = 0;
                 guard < MaxNextRunSkipIterations && nextRunUtc.HasValue && nextRunUtc.Value <= notDueBeforeUtc;
                 guard++)
            {
                nextRunUtc = _occurrenceResolver.NextRunUtc(schedule, nextRunUtc.Value);
            }
            // Use CancellationToken.None so this always completes — a cancellation in finally
            // would fault the enclosing Parallel.ForEachAsync and abort all markets.
            await scheduleRepository.CompleteJobAsync(schedule.MarketId, nextRunUtc, CancellationToken.None);
        }
    }

    // Defensive bound on the "skip past occurrences already inside the lead window" loop in
    // ProcessScheduleAsync's finally block — never a realistic ceiling for any sane cron/lead
    // combination, just a guarantee against a pathological config ever looping unbounded.
    private const int MaxNextRunSkipIterations = 1000;
    /// <summary>
    ///     Computes the fixed curation cutoff for a scheduled slot. This is shared with the
    ///     watchdog's legacy-row recovery path so a missing persisted deadline cannot drift from
    ///     the poller's slot calculation.
    /// </summary>
    internal static DateTime CalculateCurationDeadlineUtc(DateTime scheduledForUtc, BroadcastOptions options) =>
        scheduledForUtc.AddMinutes(-Math.Max(options.ScheduleLeadTimeMinutes - options.CurationWindowMinutes, 0));

    private sealed record PreviousBroadcastContext(Option<string> Script, Option<string> JobId);

    #region Private Helpers

    private async Task<Option<PreviousBroadcastContext>> GetPreviousBroadcastContextAsync(MarketSchedule schedule,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var (dayStartUtc, dayEndUtc) = TimeZoneHelper.GetLocalDayWindowUtc(nowUtc, schedule.Timezone);
        var previousJobResult = await jobRepository.GetLatestBulletinForMarketTodayAsync(
            schedule.MarketId, schedule.Station, dayStartUtc, dayEndUtc, cancellationToken);

        return previousJobResult.IsSuccess
            ? previousJobResult.Value.Map(prevJob =>
                new PreviousBroadcastContext(prevJob.ScriptText, Option<string>.Some(prevJob.JobId)))
            : Option<PreviousBroadcastContext>.None;
    }

    private Result<Job> CreateJob(
        string jobId,
        MarketSchedule schedule,
        StationConfigResolver.ResolvedVoice voice,
        Option<PreviousBroadcastContext> previousContext,
        DateTime scheduledForUtc,
        DateTime curationDeadlineUtc,
        DateTime fallbackDeadlineUtc)
    {
        return Job.Create(jobId, schedule.Station, schedule.MarketId, schedule.TargetDurationSeconds,
                timeProvider.GetUtcNow().UtcDateTime,
                Option<double>.Some(schedule.Coverage.Latitude),
                Option<double>.Some(schedule.Coverage.Longitude),
                Option<double>.Some(schedule.Coverage.RadiusKm),
                Option<string>.Some(schedule.Timezone),
                Option<CoverageArea>.Some(schedule.Coverage))
            .Map(job =>
            {
                job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
                    _broadcastOptions.DefaultVoiceProvider,
                    voice.VoiceId,
                    voice.VoiceName,
                    voice.Model,
                    voice.Settings,
                    voice.PaceBoostWpm));
                // Voice profile provenance pins (parity with CreateJobAsync): which profile's
                // CURRENT tuning this VoiceConfig came from. None when the default voice fallback
                // resolved — the resolver only sets these when a real profile was applied.
                job.VoiceProfileId = Option.OfNullable(voice.VoiceProfileId);
                job.VoiceProfileVersion = Option.OfNullable(voice.VoiceProfileVersion);
                job.PreviousScriptContext = previousContext.Bind(ctx => ctx.Script);
                job.PreviousJobId = previousContext.Bind(ctx => ctx.JobId);
                job.ScheduledForUtc = Option<DateTime>.Some(scheduledForUtc);
                job.CurationDeadlineUtc = Option<DateTime>.Some(curationDeadlineUtc);
                job.FallbackDeadlineUtc = Option<DateTime>.Some(fallbackDeadlineUtc);
                job.ContentSelection = schedule.ContentSelection;
                return job;
            });
    }

    /// <summary>
    ///     Builds the full-fidelity broadcast queue message for a scheduled run. Snapshots
    ///     everything the poller resolved (voice/model/settings, delivery style, playbook, both
    ///     pronunciation layers, content selection, fill order) so the queue worker replays exactly
    ///     this resolution regardless of redelivery timing or later schedule edits. Continuity
    ///     (previous script/job) is intentionally NOT carried: it is persisted on the job record
    ///     at creation and restored by the orchestrator's job-context enrichment.
    /// </summary>
    private static BroadcastQueueMessage BuildScheduledQueueMessage(string jobId, MarketSchedule schedule,
        StationConfigResolver.ResolvedVoice voice, StationConfigResolver.ResolvedDelivery delivery,
        PlaybookOverlay? playbook, PronunciationStack pronunciation, Option<ResolvedBookends> bookends)
    {
        return new BroadcastQueueMessage(
            jobId,
            schedule.MarketId,
            schedule.MarketId,
            (int)schedule.TargetDurationSeconds,
            schedule.Coverage.Latitude,
            schedule.Coverage.Longitude,
            schedule.Coverage.RadiusKm,
            voice.VoiceId,
            Model: voice.Model,
            VoiceSettings: voice.Settings.Match(s => (VoiceSettings?)s, () => null),
            TimezoneId: schedule.Timezone,
            Coverage: schedule.Coverage,
            Content: schedule.ContentSelection.Match<BulletinContentSelection?>(c => c, () => null),
            PaceBoostWpm: voice.PaceBoostWpm,
            FillOrder: schedule.FillOrder,
            StationId: schedule.Station,
            DeliveryStyle: delivery.Policy.Match(p => (DeliveryStylePolicy?)p, () => null),
            DeliveryStyleId: delivery.Id.Match(i => (string?)i, () => null),
            DeliveryStyleVersion: delivery.Version.Match(v => (int?)v, () => null),
            Playbook: playbook,
            PronunciationLocator: pronunciation.Station.Match(l => (PronunciationLocator?)l, () => null),
            GlobalPronunciationLocator: pronunciation.Global.Match(l => (PronunciationLocator?)l, () => null),
            Bookends: bookends.Match(b => (ResolvedBookends?)b, () => null),
            Phase: schedule.CurationEnabled ? Phase.Fetch : Phase.FetchAndGenerate);
    }

    #endregion
}
