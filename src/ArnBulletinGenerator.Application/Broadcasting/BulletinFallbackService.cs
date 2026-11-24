using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Applies the broadcast fallback chain to a job past deadline: reuse the last successful
///     same-day bulletin, or the configured filler cart if none exists.
/// </summary>
/// <inheritdoc cref="IBulletinFallbackService" />
public sealed class BulletinFallbackService(
    IScheduleRepository scheduleRepository,
    IJobRepository jobRepository,
    IOptions<BroadcastOptions> broadcastOptions,
    PerVoiceFillerCartResolver perVoiceFillerCart,
    FallbackAudioPadder fallbackAudioPadder,
    TimeProvider timeProvider,
    ILogger<BulletinFallbackService> logger) : IBulletinFallbackService
{
    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;

    public async Task<FallbackOutcome> TryApplyFallbackAsync(
        Job job, string reason, CancellationToken cancellationToken = default)
    {
        if (job.Status is JobStatus.Published or JobStatus.Approved or JobStatus.FallbackUsed)
            return new FallbackOutcome(FallbackKind.None);
        var scheduleResult = await scheduleRepository.GetScheduleAsync(job.Market, cancellationToken);
        string scheduleTimezone = scheduleResult.IsSuccess ? scheduleResult.Value.Timezone : "UTC";

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var referenceUtc = job.ScheduledForUtc.GetValueOr(nowUtc);
        var (dayStartUtc, dayEndUtc) = GetLocalDayWindow(referenceUtc, scheduleTimezone);

        var fallbackResult = await jobRepository.GetLastSuccessfulJobForMarketAsync(
            job.Market,
            job.Station,
            dayStartUtc,
            dayEndUtc,
            job.JobId,
            cancellationToken);

        if (fallbackResult.IsFailure || fallbackResult.Value.IsNone)
            return await TryFillerCartAsync(job, reason, cancellationToken);

        var fallbackJob = fallbackResult.Value.Value;
        // The "last successful" lookup can return the job itself or an audioless row; neither is a
        // usable source. Skip without falling through to the filler cart, matching prior behaviour.
        if (fallbackJob.JobId == job.JobId || fallbackJob.AudioUrl.IsNone)
            return new FallbackOutcome(FallbackKind.None);

        // The streak counts along the SLOT chain: the neighbour we found is the previous slot's
        // outcome, covered or not. Job.ApplyFallback's own status check can never see a chain
        // because a covered slot publishes seconds after its cover (observed live 2026-08-17).
        int chainStreak = fallbackJob.FallbackSourceJobId.HasValue || fallbackJob.Status == JobStatus.FallbackUsed
            ? fallbackJob.FallbackStreak + 1
            : 1;

        // COPY from the chain's root original, never from a covered neighbour: a cover's fields are
        // second-hand (observed live 2026-08-17: one degenerate ancestor — empty script, 17.0s
        // audio, no measured duration — replicated through every later cover of the evening, so
        // operators saw published bulletins with no script and false dead-air warnings). The root
        // is the bulletin a human (or the sweep) actually approved, with its full record intact.
        // Legacy chains (before this fix) point one link back per cover, so a day of covers is a
        // long walk; post-fix covers point straight at their root, so the walk is one hop. The cap
        // bounds a corrupt cycle, and a walk that ENDS on a still-covered link (broken fetch or cap)
        // must not be copied from — that is exactly the second-hand-fields defect this exists to fix.
        var source = fallbackJob;
        for (int hops = 0; source.FallbackSourceJobId.HasValue && hops < 24; hops++)
        {
            var rootResult = await jobRepository.GetJobAsync(source.FallbackSourceJobId.Value, cancellationToken);
            if (rootResult.IsFailure)
            {
                logger.LogWarning(
                    "[Fallback] Job {JobId}: cover chain broken at {SourceJobId} ({Error})",
                    job.JobId, source.FallbackSourceJobId.Value, rootResult.Error);
                break;
            }

            source = rootResult.Value;
        }

        // The resolved source must be a true ORIGINAL with audio: still-a-cover (broken/deep chain),
        // audioless, or a filler-cart cover (FallbackAppliedAt with no source id — its audio is the
        // cart, not a bulletin) all fall through to the filler cart rather than replicate
        // second-hand fields. A legitimate original may lack a script (ScriptOnly-era rows).
        bool stillACover = source.FallbackSourceJobId.HasValue || source.FallbackAppliedAt.HasValue;
        if (source.JobId == job.JobId || source.AudioUrl.IsNone || stillACover)
            return await TryFillerCartAsync(job, reason, cancellationToken);

        // A replay may not claim a closure this job's own fresh fetch contradicts. Measured before
        // this guard: 53 of 54 dev replays voiced a road absent from their own evidence — the
        // Greensborough Bypass closure re-aired 15 times, all Published, ~3 hours after it cleared
        // (RISK-37b). Adjudicated only when THIS job actually fetched: an absent fetch can neither
        // corroborate nor contradict, and refusing on it would kill every legitimate outage replay.
        // The filler cart makes no traffic claims, so it is the safe cover for a contradicted slot.
        if (job.IncidentsFetchedAt.HasValue && source.ScriptText.HasValue)
        {
            var contradictions = ReplayContradictionPolicy.Contradictions(
                source.ScriptText.Value, job.Corridors, job.Incidents);
            if (contradictions.Count > 0)
            {
                await jobRepository.AddLogAsync(job.JobId,
                    $"Same-day replay refused: {string.Join("; ", contradictions)}. " +
                    "Covering with the filler cart instead.",
                    JobLogLevel.Warn, cancellationToken);
                logger.LogWarning(
                    "[Replay] Job {JobId} refused replay of {FallbackJobId}: {Contradictions}",
                    job.JobId, source.JobId, string.Join("; ", contradictions));
                return await TryFillerCartAsync(job, reason, cancellationToken);
            }
        }

        var applyResult = job.ApplyFallback(source, timeProvider.GetUtcNow().UtcDateTime, reason: reason,
            fallbackStreak: chainStreak);
        if (applyResult.IsSuccess)
        {
            await jobRepository.SaveJobAsync(job, cancellationToken);
            await LogCoverageGapAsync(job, job.MeasuredDurationSec.Match(v => (double?)v, () => null), "same-day bulletin", cancellationToken);
            logger.LogInformation(
                "Fallback applied for job {JobId} from root {SourceJobId} (chain neighbour {NeighbourJobId}, streak {Streak})",
                job.JobId, source.JobId, fallbackJob.JobId, chainStreak);
            return new FallbackOutcome(FallbackKind.SameDayBulletin);
        }

        logger.LogWarning(
            "Fallback could not be applied to job {JobId}: {Error}",
            job.JobId, applyResult.Error);
        return new FallbackOutcome(FallbackKind.None);
    }

    private async Task<FallbackOutcome> TryFillerCartAsync(Job job, string reason, CancellationToken cancellationToken)
    {
        var result = await ApplyFillerCartAsync(job, reason, cancellationToken: cancellationToken);
        return result.IsSuccess ? new FallbackOutcome(FallbackKind.FillerCart) : new FallbackOutcome(FallbackKind.None);
    }

    /// <inheritdoc cref="IBulletinFallbackService.ApplyFillerCartAsync" />
    public async Task<Result> ApplyFillerCartAsync(
        Job job, string reason, string by = "system", CancellationToken cancellationToken = default)
    {
        // Per-voice cart first: the cart script synthesized with the JOB's own voice and cached
        // per (voice × settings × model × script). Falls back to the evergreen WAV.
        string? cartUrl = null;
        double? measuredDurationSec = null;
        bool padded = false;
        var perVoiceClip = await perVoiceFillerCart.ResolveAsync(job, cancellationToken);
        if (perVoiceClip.HasValue)
        {
            logger.LogInformation("Applying per-voice filler cart to job {JobId}", job.JobId);
            cartUrl = perVoiceClip.Value.Url;
            measuredDurationSec = perVoiceClip.Value.DurationSeconds;

            // Finding 32: the fallback's purpose is to COVER the slot, so when the cart alone is
            // materially short of it (measured 15.98s of a 30s slot on dev), pad it toward the
            // slot with the market's own filler statements. Strictly best-effort: any failure
            // keeps the plain cart below.
            var paddedClip = await fallbackAudioPadder.TryPadAsync(
                job, _broadcastOptions.FillerCartScript, perVoiceClip.Value.Audio,
                perVoiceClip.Value.DurationSeconds, cancellationToken,
                maxSpeedUpFactorOverride: job.GeneratorInput?.EditorialSettings.MaxSpeedUpFactor,
                maxSlowDownFactorOverride: job.GeneratorInput?.EditorialSettings.MaxSlowDownFactor);
            if (paddedClip.HasValue)
            {
                cartUrl = paddedClip.Value.Url;
                measuredDurationSec = paddedClip.Value.FileDurationSeconds;
                padded = true;
                await jobRepository.AddLogAsync(job.JobId,
                    $"Filler cart padded to {paddedClip.Value.FileDurationSeconds:F1}s " +
                    $"({paddedClip.Value.NaturalDurationSeconds:F1}s natural) toward the {job.TargetDurationSeconds:F0}s slot " +
                    $"— the plain cart covered only {perVoiceClip.Value.DurationSeconds:F1}s.",
                    JobLogLevel.Warn, cancellationToken);
            }
        }
        else if (!string.IsNullOrWhiteSpace(_broadcastOptions.FillerCartAudioUrl))
        {
            cartUrl = _broadcastOptions.FillerCartAudioUrl;
            logger.LogInformation("Applying evergreen filler cart to job {JobId}", job.JobId);
        }

        if (cartUrl is null)
        {
            logger.LogWarning(
                "No fallback job found for {Market}/{Station}, and no filler cart configured",
                job.Market, job.Station);
            return Result.Failure("No filler cart is configured for this market, and no per-voice cart could be produced.");
        }

        var fillerResult = job.ApplyFillerFallback(
            cartUrl,
            timeProvider.GetUtcNow().UtcDateTime,
            by,
            reason: reason,
            measuredDurationSec: measuredDurationSec);

        if (fillerResult.IsFailure)
        {
            logger.LogWarning(
                "Filler cart configured but could not be applied to job {JobId}: {Error}",
                job.JobId, fillerResult.Error);
            return fillerResult;
        }

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
            return saveResult;

        if (measuredDurationSec.HasValue)
            await LogCoverageGapAsync(job, measuredDurationSec, padded ? "padded filler cart" : "filler cart", cancellationToken);
        else
            await LogUnknownCoverageAsync(job, cancellationToken);
        logger.LogWarning(
            "Filler cart applied for {Market}/{Station} (no same-day bulletin available)",
            job.Market, job.Station);
        return Result.Success();
    }

    /// <summary>
    ///     The visibility half of finding 32: a fallback that under-covers its slot must never be
    ///     silent. Writes the measured-vs-target gap to the operator-visible job log so an
    ///     operator sees the hole before approving, and logs the same to telemetry.
    /// </summary>
    private async Task LogCoverageGapAsync(Job job, double? measured, string kind, CancellationToken cancellationToken)
    {
        if (!measured.HasValue)
            return;
        double gap = job.TargetDurationSeconds - measured.Value;
        if (gap <= FallbackAudioPadder.MateriallyShortUnderrunSeconds)
            return;
        await jobRepository.AddLogAsync(job.JobId,
            $"{kind} covers {measured.Value:F1}s of the {job.TargetDurationSeconds:F0}s slot — " +
            $"{gap:F1}s of dead air if approved as-is. Regenerate or approve knowing the slot is under-covered.",
            JobLogLevel.Warn, cancellationToken);
        logger.LogWarning(
            "[{Kind}] Job {JobId} fallback covers {Measured:F1}s of the {Target:F0}s slot — {Gap:F1}s under-filled.",
            kind, job.JobId, measured.Value, job.TargetDurationSeconds, gap);
    }

    /// <summary>The evergreen cart's duration cannot be measured here (no bytes in the Application
    /// layer) — say so in the operator-visible job log rather than pretend the slot is covered.</summary>
    private async Task LogUnknownCoverageAsync(Job job, CancellationToken cancellationToken)
    {
        await jobRepository.AddLogAsync(job.JobId,
            $"Evergreen filler cart applied — its duration could not be verified, so the {job.TargetDurationSeconds:F0}s slot " +
            "may be under-covered. Listen before approving.",
            JobLogLevel.Warn, cancellationToken);
        logger.LogWarning(
            "[FillerCart] Job {JobId} evergreen filler cart applied — its duration could not be verified; " +
            "the {Target:F0}s slot may be under-covered.",
            job.JobId, job.TargetDurationSeconds);
    }

    private static (DateTime dayStartUtc, DateTime dayEndUtc) GetLocalDayWindow(DateTime nowUtc, string timezoneId)
    {
        return TimeZoneHelper.GetLocalDayWindowUtc(nowUtc, timezoneId);
    }
}
