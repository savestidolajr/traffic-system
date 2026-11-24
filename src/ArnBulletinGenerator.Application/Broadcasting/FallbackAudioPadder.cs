using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Pads a filler-cart fallback clip with per-voice filler statements when the cart alone
///     leaves the slot materially under-covered (bug-bash 2026-08-05 finding 32: a 15.98s cart
///     aired in a 30s slot — ~14s of dead air in a paid traffic slot). The cart's own text is
///     never duplicated: statements whose normalised text already appears in the cart script are
///     excluded via <see cref="FillerStatementSelector" />'s <c>previousSameDayScript</c>
///     anti-repeat, and within-call distinctness is the selector's own normalised-text rule —
///     no second normaliser lives here.
///     <para>
///         RELIABILITY CONTRACT: the fallback is the slot's last line of defence, so it must
///         NEVER fail closed. Every failure path — statement fetch, selection, synthesis,
///         stitching, fine-tune, upload — returns None and the caller applies the plain cart
///         exactly as before. Padding can only improve the fallback; it can never replace it
///         with a failure.
///     </para>
/// </summary>
public sealed class FallbackAudioPadder(
    IFillerStatementsRepository fillerStatementsRepository,
    IFillerAudioCacheService fillerAudioCache,
    IAudioPipelineService audioPipeline,
    IDurationEstimator durationEstimator,
    IBroadcastOutput broadcastOutput,
    IOptions<SpeechDurationOptions> speechDurationOptions,
    ILogger<FallbackAudioPadder> logger,
    TimeProvider timeProvider)
{
    /// <summary>Inter-clip pause when stitching filler audio onto the cart — the same audio-engineering
    /// constant the generation engine uses between its stitched clips.</summary>
    private const double CartToFillerPauseSeconds = 0.35;

    /// <summary>
    ///     A fallback clip is "materially short" when its underrun exceeds this. One second is a
    ///     hole the slot's own silence tolerance (MaxSilencePadSeconds, 0.3s) cannot absorb and a
    ///     listener will notice; anything smaller is not worth the padding machinery. Shared with
    ///     the fallback service so the visibility warning and the padding trigger use one rule.
    /// </summary>
    public const double MateriallyShortUnderrunSeconds = 1.0;

    /// <summary>Hard ceiling on padded statements — the same top-up ceiling the generation path applies.</summary>
    private const int MaxPadStatements = 3;

    private readonly SpeechDurationOptions _durationOptions = speechDurationOptions.Value;

    /// <summary>
    ///     Attempts to extend <paramref name="cartAudio" /> toward the job's slot with the market's
    ///     filler statements, uploads the result, and returns the padded clip's URL plus its
    ///     natural (pre-fine-tune) and file durations. Returns None when the cart is not materially
    ///     short, no statement fits, the voice cannot be resolved, anything fails — or the padded
    ///     result is not strictly longer than the plain cart. Never throws.
    /// </summary>
    public async Task<Option<PaddedFallback>> TryPadAsync(
        Job job,
        string cartScript,
        byte[] cartAudio,
        double cartDuration,
        CancellationToken cancellationToken,
        double? maxSpeedUpFactorOverride = null,
        double? maxSlowDownFactorOverride = null)
    {
        try
        {
            double resolvedMaxSlowDownFactor = Math.Max(
                1.0, maxSlowDownFactorOverride ?? _durationOptions.MaxSlowDownFactor);
            double slot = job.TargetDurationSeconds;
            double gap = slot - cartDuration;
            if (gap <= MateriallyShortUnderrunSeconds || job.VoiceConfig.IsNone)
                return Option<PaddedFallback>.None;

            var vc = job.VoiceConfig.Value;
            if (string.IsNullOrWhiteSpace(vc.VoiceId) || string.IsNullOrWhiteSpace(vc.Model) || vc.Settings.IsNone)
                return Option<PaddedFallback>.None;

            var statementsResult = await fillerStatementsRepository.GetStatementsAsync(job.Market, cancellationToken);
            if (statementsResult.IsFailure)
            {
                logger.LogWarning(
                    "[FallbackPad] Job {JobId} could not load filler statements ({Error}); keeping the plain cart.",
                    job.JobId, statementsResult.Error);
                return Option<PaddedFallback>.None;
            }

            // Strict no-overshoot selection against the remaining gap: the padded file must never
            // exceed the slot, so a statement is only eligible while it fits the gap exactly.
            // Availability windows are applied HERE at the load boundary, in station-local time —
            // a statement outside its window must not pad the fallback. Degrades to UTC when the
            // job's timezone is absent or unresolvable (never throws, honouring the class's
            // fail-open contract), and an all-ineligible library flows into the existing
            // selected.Count == 0 None path below — the plain cart airs exactly as before.
            var (localDate, localTime) = TimeZoneHelper.GetLocalDateAndTime(
                timeProvider.GetUtcNow().UtcDateTime, job.TimezoneId);
            var (eligibleStatements, excludedByWindow) =
                FillerAvailabilityFilter.Apply(statementsResult.Value, localDate, localTime);
            if (excludedByWindow > 0)
                logger.LogInformation(
                    "[FallbackPad] Job {JobId} excluded {Excluded} filler statement(s) by their "
                    + "availability window; {Remaining} remain eligible",
                    job.JobId, excludedByWindow, eligibleStatements.Count);

            var selected = FillerStatementSelector.Select(
                eligibleStatements, gap, previousSameDayScript: cartScript, maxStatements: MaxPadStatements);
            if (selected.Count == 0)
                return Option<PaddedFallback>.None;

            var resolved = new List<ResolvedFillerAudio>(selected.Count);
            foreach (var statement in selected)
            {
                var result = await fillerAudioCache.ResolveAsync(
                    statement, vc.VoiceId, vc.Settings.Value, vc.Model, cancellationToken);
                if (result.IsSuccess)
                    resolved.Add(result.Value);
                else
                    logger.LogWarning(
                        "[FallbackPad] Job {JobId} could not resolve audio for filler statement {StatementId}: {Error}",
                        job.JobId, statement.Id, result.Error);
            }
            if (resolved.Count == 0)
                return Option<PaddedFallback>.None;

            var clips = new List<byte[]> { cartAudio };
            clips.AddRange(resolved.Select(r => r.Audio));
            byte[] stitched = audioPipeline.StitchAudioClips(clips, CartToFillerPauseSeconds);
            double naturalDuration = durationEstimator.GetDuration(stitched);
            if (naturalDuration <= 0)
                return Option<PaddedFallback>.None;

            byte[] finalAudio = stitched;
            double fileDuration = naturalDuration;
            if (naturalDuration > slot)
            {
                // The inter-clip pauses can push a full-gap selection marginally over the slot;
                // compress within the resolved speed-up budget to land exactly on it. Beyond the
                // budget the fine-tuner returns the raw clip, and the strictly-longer check below
                // rejects it.
                byte[] compressed = audioPipeline.FineTuneToTargetDuration(
                    stitched, slot, maxSpeedUpFactorOverride, maxSlowDownFactorOverride);
                double compressedDuration = durationEstimator.GetDuration(compressed);
                if (compressedDuration > 0 && compressedDuration <= slot)
                {
                    finalAudio = compressed;
                    fileDuration = compressedDuration;
                }
            }
            else if (naturalDuration >= slot / resolvedMaxSlowDownFactor)
            {
                // Natural read is within the resolved slow-down cap of the slot — close the residual
                // hole with a pitch-preserving stretch instead of airing it. Farther under than the
                // cap the fine-tuner would pad with raw silence (dead air), so the stitched file airs
                // as-is: it "approaches" the slot, which is strictly better than the plain cart.
                byte[] stretched = audioPipeline.FineTuneToTargetDuration(
                    stitched, slot, maxSpeedUpFactorOverride, maxSlowDownFactorOverride);
                double stretchedDuration = durationEstimator.GetDuration(stretched);
                if (stretchedDuration > 0 && stretchedDuration >= naturalDuration)
                {
                    finalAudio = stretched;
                    fileDuration = stretchedDuration;
                }
            }

            // The padded result must be strictly better than the plain cart, or it is not worth
            // replacing the fallback URL with.
            if (fileDuration <= cartDuration + 0.05)
                return Option<PaddedFallback>.None;

            string identifier = $"filler_pad_{job.JobId}";
            var saveResult = await broadcastOutput.SaveAsync(finalAudio, identifier, cancellationToken);
            if (saveResult.IsFailure)
            {
                logger.LogWarning(
                    "[FallbackPad] Job {JobId} could not upload padded fallback ({Error}); keeping the plain cart.",
                    job.JobId, saveResult.Error);
                return Option<PaddedFallback>.None;
            }

            logger.LogInformation(
                "[FallbackPad] Job {JobId} padded the {Cart:F1}s cart to {File:F1}s ({Natural:F1}s natural, {Filler:F1}s of filler) toward the {Slot:F0}s slot.",
                job.JobId, cartDuration, fileDuration, naturalDuration, naturalDuration - cartDuration, slot);
            return Option<PaddedFallback>.Some(new PaddedFallback($"{identifier}.wav", naturalDuration, fileDuration));
        }
        catch (Exception ex)
        {
            // Never fail closed: the fallback is the slot's last line of defence.
            logger.LogError(ex, "[FallbackPad] Job {JobId} padding failed unexpectedly; keeping the plain cart.", job.JobId);
            return Option<PaddedFallback>.None;
        }
    }
}

/// <summary>A successfully padded fallback clip: the uploaded blob URL plus the natural (pre-fine-tune)
/// spoken duration and the final file duration that will air.</summary>
public sealed record PaddedFallback(string Url, double NaturalDurationSeconds, double FileDurationSeconds);
