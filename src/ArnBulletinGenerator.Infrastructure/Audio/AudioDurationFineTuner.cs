using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Duration fine-tuning core for <see cref="AudioPipelineService" />: trims trailing silence,
///     slows reads to honestly fill underruns within the configured cap, closes any residual gap
///     with a trimmed non-silent bed, and exposes the spoken-duration/exact-duration helpers the
///     pipeline shares across its synthesis attempts.
/// </summary>
internal sealed class AudioDurationFineTuner(
    IAudioProcessor audioProcessor,
    SpeechDurationOptions durationOptions,
    ILogger<AudioPipelineService> logger)
{
    /// <summary>
    ///     Core of <see cref="AudioPipelineService.FineTuneToTargetDuration" />, additionally reporting whether the
    ///     residual was closed by appending a non-silent bed (<see cref="CloseUnderrunToBed" />).
    ///     Callers need this: the bed leaves the delivered speech byte-for-byte untouched, so
    ///     the waterfall logging must not rescale word timings using the bed's
    ///     length or the karaoke highlight stretches into the bed's dead time.
    /// </summary>
    internal byte[] FineTuneToTargetDurationCore(byte[] audio, double targetDuration,
        double? maxSpeedUpFactorOverride, out bool bedClosed, double? maxSlowDownFactorOverride = null)
    {
        bedClosed = false;
        audio = TrimTrailingSilenceIfPossible(audio);
        double currentDuration = audioProcessor.GetDuration(audio);
        double delta = currentDuration - targetDuration;
        double absDelta = Math.Abs(delta);

        logger.LogInformation(
            "[AudioPipeline:FineTune] Raw duration: {Current:F3}s | Target: {Target:F3}s | Delta: {Delta:F3}s",
            currentDuration, targetDuration, delta);

        // Already at target - no adjustment needed
        double fineTuningThreshold = durationOptions.FineTuningThresholdSeconds;
        double maxSpeedUpFactor = maxSpeedUpFactorOverride ?? durationOptions.MaxSpeedUpFactor;
        double maxSlowDownFactor = maxSlowDownFactorOverride ?? durationOptions.MaxSlowDownFactor;
        // The seconds guard must track the factor: a pace-boosted request (override > configured)
        // legitimately manufactures overruns past the default window, so widen the window to the
        // seconds the effective factor can actually absorb. Unboosted requests (null override)
        // keep the configured default — extra compression stays strictly opt-in per voice profile.
        double maxSafeOverrunTrim = maxSpeedUpFactorOverride is > 0
            ? Math.Max(durationOptions.MaxSafeOverrunTrimSeconds, targetDuration * (maxSpeedUpFactorOverride.Value - 1.0))
            : durationOptions.MaxSafeOverrunTrimSeconds;

        if (absDelta <= fineTuningThreshold)
        {
            logger.LogInformation(
                "[AudioPipeline:FineTune] Within threshold ({Threshold}s) — skipping adjustment (exact delta: {Delta:F6}s)",
                fineTuningThreshold, delta);
            return EnsureExactDurationIfPossible(audio, targetDuration);
        }

        // Underrun: fill toward the slot by slowing the read within the configured cap
        // (pitch-preserving), then close any residual gap with trailing silence. Content fill is
        // still preferred upstream; this only closes what remains after the read is as long as it
        // can honestly be. A gap too large to close within the cap is left for the caller to reject
        // (→ fallback) rather than padded with long dead air.
        if (delta < 0)
        {
            if (durationOptions.UseTrimmedBedCloser)
            {
                bedClosed = true;
                return CloseUnderrunToBed(audio, targetDuration);
            }
            byte[] slowed = SlowFillToTarget(audio, currentDuration, targetDuration, maxSlowDownFactorOverride);
            double slowedDuration = audioProcessor.GetDuration(slowed);
            byte[] paddedToTarget = audioProcessor.EnsureDuration(slowed, targetDuration);
            if (slowedDuration > currentDuration + durationOptions.FineTuningThresholdSeconds)
                logger.LogInformation(
                    "[AudioPipeline:FineTune] Underrun {Delta:F3}s — slow-filled {Before:F3}s -> {Slowed:F3}s, padding residual to {Target:F3}s.",
                    -delta, currentDuration, slowedDuration, targetDuration);
            else
                logger.LogInformation(
                    "[AudioPipeline:FineTune] Underrun {Delta:F3}s — slow-fill disabled (MaxSlowDownFactor={Factor:F2}), read left at {Before:F3}s, padding residual to {Target:F3}s.",
                    -delta, maxSlowDownFactor, currentDuration, targetDuration);
            return EnsureExactDurationIfPossible(paddedToTarget, targetDuration);
        }

        // For small/moderate overruns, use waveform trim-to-target path.
        if (absDelta <= maxSafeOverrunTrim && currentDuration / targetDuration <= maxSpeedUpFactor)
        {
            byte[] adjusted = audioProcessor.AdjustSpeed(audio, targetDuration);
            double adjustedDuration = audioProcessor.GetDuration(adjusted);
            logger.LogInformation(
                "[AudioPipeline:FineTune] Overrun adjusted: {Before:F3}s → {After:F3}s (target {Target:F3}s, residual: {Residual:F6}s)",
                currentDuration, adjustedDuration, targetDuration, adjustedDuration - targetDuration);
            return EnsureExactDurationIfPossible(adjusted, targetDuration);
        }

        logger.LogWarning(
            "[AudioPipeline:FineTune] Overrun {Delta:F3}s exceeds the {Factor:F2}x speed-up cap — leaving raw for the orchestrator to regenerate a shorter script.",
            delta, maxSpeedUpFactor);
        return audio;
    }

    internal byte[] TrimTrailingSilenceIfPossible(byte[] audio)
    {
        if (audioProcessor is not WavDurationProcessor wavProcessor)
            return audio;

        return wavProcessor.TrimTrailingSilence(audio);
    }

    internal double GetSpokenDurationIfPossible(byte[] audio)
    {
        if (audioProcessor is not WavDurationProcessor wavProcessor)
            return audioProcessor.GetDuration(audio);

        return wavProcessor.GetSpokenDuration(audio);
    }

    internal byte[] TrimToSpokenDuration(byte[] audio)
    {
        double spokenSeconds = GetSpokenDurationIfPossible(audio);
        if (spokenSeconds <= 0 || audioProcessor is not WavDurationProcessor wavProcessor)
            return audio;

        return wavProcessor.TrimToDuration(audio, spokenSeconds);
    }

    /// <summary>
    ///     Slow the read (pitch-preserving) toward <paramref name="targetDuration" /> to fill an
    ///     underrun, but never beyond <see cref="SpeechDurationOptions.MaxSlowDownFactor" /> — or
    ///     <paramref name="maxSlowDownFactorOverride" /> when the voice's pace boost has earned extra
    ///     slow-down headroom (see
    ///     <see cref="ArnBulletinGenerator.Domain.Services.WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor" />).
    ///     Returns the (possibly) stretched audio; any residual gap to the slot is closed with silence
    ///     by the caller. A residual larger than the silence budget signals a slot that content +
    ///     slow-fill could not honestly fill (surfaced as SlotFilled=false downstream).
    /// </summary>
    internal byte[] SlowFillToTarget(byte[] audio, double naturalDuration, double targetDuration,
        double? maxSlowDownFactorOverride = null)
    {
        if (naturalDuration <= 0 || naturalDuration >= targetDuration)
            return audio;

        double maxSlowDownFactor = maxSlowDownFactorOverride ?? durationOptions.MaxSlowDownFactor;
        double fillTarget = Math.Min(targetDuration, naturalDuration * maxSlowDownFactor);
        if (fillTarget <= naturalDuration + durationOptions.FineTuningThresholdSeconds)
            return audio;

        return audioProcessor.AdjustSpeed(audio, fillTarget);
    }

    internal byte[] EnsureExactDurationIfPossible(byte[] audio, double targetSeconds)
    {
        if (audioProcessor is not WavDurationProcessor wavProcessor)
            return audio;

        return wavProcessor.EnsureExactDuration(audio, targetSeconds);
    }

    /// <summary>
    ///     Closes an underrun to the EXACT slot by appending a bed/outro trimmed to the residual,
    ///     leaving the spoken content (read + any stitched filler) byte-for-byte untouched — no
    ///     time-stretch of already-correct speech, and no dead-zone (any residual, however small,
    ///     is closed exactly by the bed rather than left short when it exceeds the silence pad but
    ///     cannot be reached within the slow-down cap). The bed is non-silent, so the composed file
    ///     is voiced to the slot end and passes <see cref="AudioPipelineService.IsSlotFilled" /> (no trailing dead air).
    ///     Used only when <see cref="SpeechDurationOptions.UseTrimmedBedCloser" /> is set.
    /// </summary>
    internal byte[] CloseUnderrunToBed(byte[] audio, double targetDuration)
    {
        byte[] spoken = TrimTrailingSilenceIfPossible(audio);
        double spokenDuration = audioProcessor.GetDuration(spoken);
        double residual = targetDuration - spokenDuration;

        // At/over the slot, or no WAV processor to trim a bed with → fall back to a plain pad.
        if (residual <= durationOptions.FineTuningThresholdSeconds || audioProcessor is not WavDurationProcessor wav)
            return EnsureExactDurationIfPossible(audioProcessor.EnsureDuration(spoken, targetDuration), targetDuration);

        // Generate a bed a little longer than needed, then trim to the exact residual.
        byte[] bed = wav.TrimToDuration(GenerateNeutralBed(spoken, residual + 0.25), residual);
        byte[] composed = PcmAudioStitcher.Concatenate(new List<byte[]> { spoken, bed }, 0.0);
        double composedDuration = audioProcessor.GetDuration(composed);
        logger.LogInformation(
            "[AudioPipeline:BedClose] Underrun {Residual:F3}s closed with a trimmed bed — spoken {Spoken:F3}s left untouched, composed {Composed:F3}s (target {Target:F3}s).",
            residual, spokenDuration, composedDuration, targetDuration);
        return EnsureExactDurationIfPossible(composed, targetDuration);
    }

    /// <summary>
    ///     Neutral low-level bed matching <paramref name="formatSource" />'s WAV format, of at least
    ///     <paramref name="seconds" />. Non-silent (a quiet 110 Hz tone) so it counts as filled slot
    ///     time rather than trailing dead air. This is a PLACEHOLDER for ARN's real per-station music
    ///     bed / sign-off outro; swap the source once that asset is configured.
    /// </summary>
    private static byte[] GenerateNeutralBed(byte[] formatSource, double seconds)
    {
        var info = WavInfoParser.GetWavInfo(formatSource);
        int sampleRate = info is { SampleRate: > 0 } ? info.SampleRate : 16000;
        short channels = info is { Channels: > 0 } ? info.Channels : (short)1;
        short bits = info?.BitsPerSample == 8 ? (short)8 : (short)16;
        int bytesPerSample = bits / 8;
        int frames = Math.Max(1, (int)(seconds * sampleRate));
        byte[] pcm = new byte[frames * channels * bytesPerSample];
        for (int i = 0; i < frames; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * 110.0 * i / sampleRate) * 1200);
            for (int c = 0; c < channels; c++)
            {
                int idx = (i * channels + c) * bytesPerSample;
                if (bits == 16)
                {
                    pcm[idx] = (byte)(v & 0xFF);
                    pcm[idx + 1] = (byte)((v >> 8) & 0xFF);
                }
                else
                {
                    pcm[idx] = (byte)(128 + (v >> 8));
                }
            }
        }
        return WavHeaderBuilder.WrapInWavHeader(pcm, sampleRate, channels, bits);
    }
}
