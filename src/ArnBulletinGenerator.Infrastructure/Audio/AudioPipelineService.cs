using System.Diagnostics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.ValueObjects;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static ArnBulletinGenerator.Domain.Services.SyllableCounter;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Aggregates audio synthesis, duration estimation, and post-processing into a cohesive pipeline.
///     Encapsulates retry logic and speed adjustments for TTS synthesis.
/// </summary>
public sealed class AudioPipelineService(
    IAudioSynthesizer audioSynthesizer,
    IAudioProcessor audioProcessor,
    ISpeechDurationEstimator speechDurationEstimator,
    ISynthesisHistoryRepository synthesisHistoryRepository,
    IEnumerable<IAudioPostSynthesisDurationAdjustmentStrategy> postSynthesisStrategies,
    IOptions<BroadcastOptions> broadcastOptions,
    IOptions<SpeechDurationOptions> durationOptions,
    WavLoudnessNormalizer loudnessNormalizer,
    IOptions<AudioLoudnessOptions> loudnessOptions,
    ILogger<AudioPipelineService> logger) : IAudioPipelineService, IDurationEstimator
{
    /// <summary>Default words-per-minute assumption aligned with LlmScriptGenerator (150 WPM).</summary>
    private const double DefaultWpm = 150.0;

    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;
    private readonly SpeechDurationOptions _durationOptions = durationOptions.Value;
    private readonly AudioLoudnessOptions _loudnessOptions = loudnessOptions.Value;

    private readonly IReadOnlyList<IAudioPostSynthesisDurationAdjustmentStrategy> _postSynthesisStrategies =
        postSynthesisStrategies.ToList();

    private readonly AudioDurationFineTuner _fineTuner = new(audioProcessor, durationOptions.Value, logger);

    public byte[] FineTuneToTargetDuration(byte[] audio, double targetDuration,
        double? maxSpeedUpFactorOverride = null, double? maxSlowDownFactorOverride = null)
    {
        return _fineTuner.FineTuneToTargetDurationCore(
            audio, targetDuration, maxSpeedUpFactorOverride, out _, maxSlowDownFactorOverride);
    }

    public async Task<Result<AudioSynthesisResult>> SynthesizeWithDurationTargetAsync(
        string script,
        double targetDuration,
        Option<string> voiceIdOverride = default,
        Option<VoiceSettings> settings = default,
        Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        double initialSpeed = 0,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null,
        double? maxSpeedUpFactorOverride = null,
        bool allowDynamicSlot = false,
        double? maxSlowDownFactorOverride = null,
        string? lockedSuffix = null,
        FixtureLines? fixtures = null,
        string? lockedPrefix = null,
        int? maxSynthesisAttemptsOverride = null)
    {
        int maxSynthesisAttempts = Math.Max(1, maxSynthesisAttemptsOverride ?? _broadcastOptions.MaxSynthesisAttempts);
        double effectiveTolerance = _durationOptions.EffectiveToleranceSeconds;
        double candidateTolerance = _durationOptions.CandidateToleranceSeconds;
        double exactHitThreshold = _durationOptions.ExactHitThresholdSeconds;
        double effectiveMaxSpeedUpFactor = maxSpeedUpFactorOverride ?? _durationOptions.MaxSpeedUpFactor;
        double effectiveMaxSlowDownFactor = maxSlowDownFactorOverride ?? _durationOptions.MaxSlowDownFactor;

        var pipelineSw = Stopwatch.StartNew();
        string currentScript = script;
        int initialWordCount = WordCounter.Count(DeliveryTags.Strip(script));

        logger.LogInformation(
            "[AudioPipeline] Starting synthesis — Target: {Target}s | Script: {Chars} chars, {Words} words | MaxAttempts: {Max}",
            targetDuration, script.Length, initialWordCount, maxSynthesisAttempts);

        string? lastError = null;
        Option<OperationalFailure> lastFailureMetadata = default;
        double? lastActualDuration = null;
        int lastWordCount = initialWordCount;
        double lastEstimatedDuration = speechDurationEstimator.EstimateDuration(DeliveryTags.Strip(script));
        double lastWordCountEstimate = initialWordCount / DefaultWpm * 60.0;

        logger.LogInformation(
            "[AudioPipeline] Duration controls — FinalTolerance={FinalTolerance:F2}s, CandidateTolerance={CandidateTolerance:F2}s",
            effectiveTolerance, candidateTolerance);

        double userSpeed = settings.HasValue ? settings.Value.Speed : 1.0;
        double currentSpeed = userSpeed;

        for (int attempt = 1; attempt <= maxSynthesisAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastWordCount = WordCounter.Count(DeliveryTags.Strip(currentScript));
            lastEstimatedDuration = speechDurationEstimator.EstimateDuration(DeliveryTags.Strip(currentScript));
            lastWordCountEstimate = lastWordCount / DefaultWpm * 60.0;

            logger.LogInformation(
                "[AudioPipeline] Attempt {Attempt}/{Max} script stats — {Chars} chars, {Words} words, Estimated={Estimated:F2}s",
                attempt, maxSynthesisAttempts, currentScript.Length, lastWordCount,
                lastEstimatedDuration);

            // Build per-attempt voice settings with current (possibly adjusted) speed
            var attemptSettings = settings.HasValue
                ? Option<VoiceSettings>.Some(settings.Value with { Speed = currentSpeed })
                : Option<VoiceSettings>.Some(VoiceSettings.Default with { Speed = currentSpeed });

            logger.LogInformation("[AudioPipeline] Attempt {Attempt}/{Max}: Synthesizing (speed: {Speed:F3})...",
                attempt, maxSynthesisAttempts, currentSpeed);

            var synthSw = Stopwatch.StartNew();
            var audioResult = await audioSynthesizer.SynthesizeAsync(currentScript, attemptSettings, voiceIdOverride,
                modelOverride, cancellationToken, market, pronunciationStack);
            synthSw.Stop();

            logger.LogInformation(
                "[AudioPipeline] TTS provider responded — Attempt: {Attempt} | Latency: {LatencyMs}ms | Success: {Success}",
                attempt, synthSw.ElapsedMilliseconds, audioResult.IsSuccess);

            if (audioResult.IsFailure)
            {
                logger.LogError(
                    "[AudioPipeline] Synthesis FAILED on attempt {Attempt}/{Max} — Error: {Error} | Elapsed: {LatencyMs}ms",
                    attempt, maxSynthesisAttempts, audioResult.Error, synthSw.ElapsedMilliseconds);
                lastError = audioResult.Error;
                lastFailureMetadata = Option<OperationalFailure>.Some(
                    TtsErrorClassifier.IsPermanentTtsError(lastError)
                        ? OperationalFailure.Permanent("tts.permanent", lastError)
                        : OperationalFailure.Retryable("tts.retryable", lastError));

                if (lastFailureMetadata.Value.Disposition is FailureDisposition.Permanent)
                {
                    logger.LogWarning("[AudioPipeline] Permanent TTS error, not retrying: {Error}", lastError);
                    break;
                }

                if (attempt < maxSynthesisAttempts)
                    logger.LogWarning("[AudioPipeline] Retrying synthesis (attempt {Next}/{Max})...", attempt + 1,
                        maxSynthesisAttempts);

                continue;
            }

            byte[] rawAudio = audioResult.Value!.Audio;
            var attemptAlignment = audioResult.Value.Alignment;
            var effectiveModel = Option.OfWhitespace(audioResult.Value.ModelId).Or(modelOverride);
            double rawDuration = audioProcessor.GetDuration(rawAudio);
            byte[] audioData = _fineTuner.TrimTrailingSilenceIfPossible(rawAudio);
            int audioSizeBytes = audioData.Length;
            double actualDuration = audioProcessor.GetDuration(audioData);
            double durationDiff = actualDuration - targetDuration;
            double instantWpm = actualDuration > 0 ? lastWordCount / (actualDuration / 60.0) : 0;
            lastActualDuration = actualDuration;

            logger.LogInformation(
                "[AudioPipeline] Raw synthesis complete — Attempt: {Attempt} | Size: {SizeKB:F1} KB ({SizeBytes} bytes) | Duration: {Actual:F3}s (raw {Raw:F3}s) | Target: {Target:F3}s | Diff: {Diff:+0.000;-0.000}s | WPM: {Wpm:F0}",
                attempt, audioSizeBytes / 1024.0, audioSizeBytes, actualDuration, rawDuration, targetDuration,
                durationDiff, instantWpm);

            // CASE 1: Already at exact target
            if (Math.Abs(durationDiff) < exactHitThreshold)
            {
                logger.LogInformation(
                    "[AudioPipeline] EXACT HIT — Actual: {Actual:F3}s | Target: {Target:F3}s | Diff: {Diff:F6}s | Attempt: {Attempt}",
                    actualDuration, targetDuration, durationDiff, attempt);

                byte[] exactAudio = _fineTuner.FineTuneToTargetDurationCore(
                    audioData, targetDuration, maxSpeedUpFactorOverride, out bool exactBedClosed, maxSlowDownFactorOverride);
                double exactDuration = audioProcessor.GetDuration(exactAudio);
                return LogWaterfallAndSucceed(
                    exactAudio,
                    currentScript,
                    targetDuration,
                    lastEstimatedDuration,
                    lastWordCountEstimate,
                    lastWordCount,
                    exactDuration,
                    currentSpeed,
                    pipelineSw,
                    voiceIdOverride,
                    effectiveModel,
                    attemptSettings,
                    effectiveTolerance,
                    attemptAlignment,
                    actualDuration,
                    exactBedClosed);
            }

            // CASE 2: near target, OR an overrun small enough to compress transparently with the
            // pitch-preserving stretch. Both finalize by fine-tuning to the exact target.
            bool overrunWithinStretch =
                durationDiff > 0 && actualDuration <= targetDuration * effectiveMaxSpeedUpFactor;
            if (Math.Abs(durationDiff) <= candidateTolerance || overrunWithinStretch)
            {
                logger.LogInformation(
                    "[AudioPipeline] Within candidate tolerance ({Tolerance:F2}s). Attempting strict finalization — Raw: {Raw:F3}s -> Target: {Target:F3}s (delta: {Delta:F3}s)",
                    candidateTolerance, actualDuration, targetDuration, durationDiff);

                byte[] bestTrimmed = _fineTuner.TrimTrailingSilenceIfPossible(audioData);
                double bestDuration = audioProcessor.GetDuration(bestTrimmed);
                byte[] fineTunedAudio = _fineTuner.FineTuneToTargetDurationCore(
                    bestTrimmed, targetDuration, maxSpeedUpFactorOverride, out bool candidateBedClosed, maxSlowDownFactorOverride);
                double fineTunedDuration = audioProcessor.GetDuration(fineTunedAudio);
                double finalDelta = fineTunedDuration - targetDuration;

                logger.LogInformation(
                    "[AudioPipeline] Duration fine-tune complete — Before: {Before:F3}s → After: {After:F3}s | Target: {Target:F3}s | Residual: {Residual:F6}s",
                    bestDuration, fineTunedDuration, targetDuration, finalDelta);

                if (Math.Abs(finalDelta) > effectiveTolerance)
                {
                    var audioProposal = await TryAdjustAudioWithStrategiesAsync(
                        currentScript,
                        fineTunedAudio,
                        targetDuration,
                        fineTunedDuration,
                        attempt,
                        maxSynthesisAttempts,
                        cancellationToken);

                    if (audioProposal is not null)
                    {
                        fineTunedAudio = _fineTuner.TrimTrailingSilenceIfPossible(audioProposal.Audio);
                        fineTunedDuration = audioProcessor.GetDuration(fineTunedAudio);
                        finalDelta = fineTunedDuration - targetDuration;
                        lastError = audioProposal.Reason;
                        candidateBedClosed = false;

                        logger.LogInformation(
                            "[AudioPipeline] Post-synthesis strategy '{Strategy}' applied — NewDuration={Duration:F3}s (residual {Residual:F6}s).",
                            audioProposal.StrategyName,
                            fineTunedDuration,
                            finalDelta);
                    }
                }

                if (Math.Abs(finalDelta) <= effectiveTolerance)
                    return LogWaterfallAndSucceed(
                        fineTunedAudio,
                        currentScript,
                        targetDuration,
                        lastEstimatedDuration,
                        lastWordCountEstimate,
                        lastWordCount,
                        fineTunedDuration,
                        currentSpeed,
                        pipelineSw,
                        voiceIdOverride,
                        effectiveModel,
                        attemptSettings,
                        effectiveTolerance,
                        attemptAlignment,
                    actualDuration,
                    candidateBedClosed);

                if (attempt < maxSynthesisAttempts && fineTunedDuration > 0)
                {
                    lastError = string.Format(
                        ErrorMessages.Validation.DurationActualTooFar,
                        fineTunedDuration,
                        effectiveTolerance);

                    double oldSpeed = currentSpeed;
                    currentSpeed = Math.Clamp(fineTunedDuration / targetDuration * currentSpeed, userSpeed, 1.2);
                    logger.LogInformation(
                        "[AudioPipeline] Adjusting speed from {OldSpeed:F3} to {NewSpeed:F3} for attempt {Attempt}",
                        oldSpeed, currentSpeed, attempt + 1);
                    continue;
                }

                return Result<AudioSynthesisResult>.Failure(OperationalFailure.InvalidInput(
                    "duration.mismatch",
                    string.Format(ErrorMessages.Validation.DurationActualTooFar,
                        fineTunedDuration, effectiveTolerance)));
            }

            // CASE 3: Outside candidate tolerance — best-effort speed adjustment.
            logger.LogWarning(
                "[AudioPipeline] CASE 3 — Delta ({Delta:F3}s) exceeds candidate tolerance ({Tolerance:F2}s). Attempting best-effort speed adjustment.",
                durationDiff, candidateTolerance);

            // Overrun beyond the transparent stretch range: rather than fight the (bucketed,
            // model-dependent) speed parameter, MEASURE where each sentence lands from the TTS
            // alignment and remove the minimum traffic content needed to fit. The shared fitter
            // protects locked bookends and required closes; the sub-second residual is closed by
            // the pitch-preserving fine-tune before re-synthesis.
            if (durationDiff > 0 && attempt < maxSynthesisAttempts && attemptAlignment.HasValue)
            {
                double overrunAllowance = Math.Max(
                    0.0, (targetDuration * effectiveMaxSpeedUpFactor) - targetDuration - 0.75);
                // Keep enough natural speech for slow-fill to reach the slot without excess silence.
                string? trimmedScript = BulletinDurationFitter.FitByTrimming(
                    currentScript, attemptAlignment.Value, targetDuration, overrunAllowance,
                    lockedSuffix: lockedSuffix, fixtures: fixtures, lockedPrefix: lockedPrefix,
                    minSeconds: (targetDuration - _durationOptions.MaxSilencePadSeconds)
                        / Math.Max(1.0, effectiveMaxSlowDownFactor));
                if (!string.IsNullOrWhiteSpace(trimmedScript) && trimmedScript != currentScript)
                {
                    logger.LogInformation(
                        "[AudioPipeline] Measure-then-fit: trimming script {OldWords}->{NewWords} words to fit {Target:F1}s slot (overrun {Overrun:F2}s), re-synthesizing.",
                        WordCounter.Count(DeliveryTags.Strip(currentScript)), WordCounter.Count(DeliveryTags.Strip(trimmedScript)), targetDuration,
                        durationDiff);
                    currentScript = trimmedScript;
                    currentSpeed = userSpeed;
                    continue;
                }
            }

            // Underrun beyond the candidate band: fill toward the slot by slowing the read within
            // the configured cap (pitch-preserving), then pad any residual. If content + slow-fill
            // still cannot reach the slot within the silence budget, LogWaterfallAndSucceed reports
            // SlotFilled=false and the orchestrator falls back rather than ship a short bulletin.
            if (durationDiff < 0)
            {
                double effectiveTarget = targetDuration;
                bool honestFill = true;
                if (allowDynamicSlot && _durationOptions.UseDynamicSlot)
                {
                    double spoken = _fineTuner.GetSpokenDurationIfPossible(audioData);
                    if (spoken <= 0) spoken = actualDuration;
                    double chosen = DynamicSlotSelector.Select(
                        spoken, targetDuration, _durationOptions.StandardSlotSeconds, effectiveMaxSpeedUpFactor);
                    double bedFraction = chosen > 0 ? (chosen - spoken) / chosen : 1.0;
                    if (bedFraction <= _durationOptions.MaxBedFractionForSlot + 1e-9)
                    {
                        if (chosen < targetDuration)
                            logger.LogInformation(
                                "[AudioPipeline:DynamicSlot] Read {Spoken:F1}s justifies a {Slot:F0}s slot (scheduled {Orig:F0}s) — bed {Bed:P0}.",
                                spoken, chosen, targetDuration, bedFraction);
                        effectiveTarget = chosen;
                    }
                    else
                    {
                        // Too thin for any honest slot: don't air mostly-bed — leave it short so the
                        // orchestrator falls back to the cart (content-limited).
                        honestFill = false;
                        logger.LogInformation(
                            "[AudioPipeline:DynamicSlot] Read {Spoken:F1}s too thin for any honest slot (smallest {Slot:F0}s needs {Bed:P0} bed) — content-limited.",
                            spoken, chosen, bedFraction);
                    }
                }

                bool bedClosed = _durationOptions.UseTrimmedBedCloser && honestFill;
                byte[] padded;
                if (_durationOptions.UseTrimmedBedCloser && honestFill)
                {
                    padded = _fineTuner.CloseUnderrunToBed(audioData, effectiveTarget);
                    logger.LogInformation(
                        "[AudioPipeline] Underrun {Underrun:F2}s — bed-closed to {Target:F1}s (spoken read untouched).",
                        effectiveTarget - actualDuration, effectiveTarget);
                }
                else
                {
                    byte[] slowed = _fineTuner.SlowFillToTarget(
                        audioData, actualDuration, effectiveTarget, maxSlowDownFactorOverride);
                    padded = _fineTuner.EnsureExactDurationIfPossible(
                        audioProcessor.EnsureDuration(slowed, effectiveTarget), effectiveTarget);
                    double slowedDuration = audioProcessor.GetDuration(slowed);
                    if (slowedDuration > actualDuration + _durationOptions.FineTuningThresholdSeconds)
                        logger.LogInformation(
                            "[AudioPipeline] Underrun {Underrun:F2}s — slow-filled {Before:F3}s -> {Slowed:F3}s, padding residual to {Target:F1}s.",
                            effectiveTarget - actualDuration, actualDuration, slowedDuration, effectiveTarget);
                    else
                        logger.LogInformation(
                            "[AudioPipeline] Underrun {Underrun:F2}s — slow-fill disabled (MaxSlowDownFactor={Factor:F2}), read left at {Before:F3}s, padding residual to {Target:F1}s.",
                            effectiveTarget - actualDuration, effectiveMaxSlowDownFactor, actualDuration, effectiveTarget);
                }
                return LogWaterfallAndSucceed(
                    padded, currentScript, effectiveTarget, lastEstimatedDuration, lastWordCountEstimate,
                    lastWordCount, audioProcessor.GetDuration(padded), currentSpeed, pipelineSw,
                    voiceIdOverride, effectiveModel, attemptSettings, effectiveTolerance,
                    attemptAlignment, actualDuration, bedClosed);
            }

            // Overrun without usable alignment to trim: speed UP within the cap, never below user speed.
            if (attempt < maxSynthesisAttempts && actualDuration > 0)
            {
                lastError = string.Format(ErrorMessages.Validation.DurationActualTooFar, actualDuration,
                    effectiveTolerance);

                double oldSpeed = currentSpeed;
                currentSpeed = Math.Clamp(actualDuration / targetDuration * currentSpeed, userSpeed, 1.2);
                logger.LogInformation(
                    "[AudioPipeline] Adjusting speed from {OldSpeed:F3} to {NewSpeed:F3} for attempt {Attempt}",
                    oldSpeed, currentSpeed, attempt + 1);
                continue;
            }

            // Last attempt: best-effort fine-tuning instead of giving up
            byte[] bestEffortAudio = _fineTuner.FineTuneToTargetDurationCore(
                audioData, targetDuration, maxSpeedUpFactorOverride, out bool bestEffortBedClosed, maxSlowDownFactorOverride);
            double bestEffortDuration = audioProcessor.GetDuration(bestEffortAudio);
            double bestEffortDelta = Math.Abs(bestEffortDuration - targetDuration);

            logger.LogWarning(
                "[AudioPipeline] Best-effort adjustment on final attempt — Before: {Before:F3}s → After: {After:F3}s | Target: {Target:F3}s | Residual: {Residual:F3}s",
                actualDuration, bestEffortDuration, targetDuration, bestEffortDuration - targetDuration);

            if (bestEffortDelta <= effectiveTolerance)
                return LogWaterfallAndSucceed(
                    bestEffortAudio,
                    currentScript,
                    targetDuration,
                    lastEstimatedDuration,
                    lastWordCountEstimate,
                    lastWordCount,
                    bestEffortDuration,
                    currentSpeed,
                    pipelineSw,
                    voiceIdOverride,
                    effectiveModel,
                    attemptSettings,
                    effectiveTolerance,
                    attemptAlignment,
                    actualDuration,
                    bestEffortBedClosed);

            logger.LogError(
                "[AudioPipeline] Best-effort fine-tune INSUFFICIENT on final attempt — Delta: {Delta:F3}s exceeds tolerance {Tolerance:F2}s",
                bestEffortDelta, effectiveTolerance);

            return Result<AudioSynthesisResult>.Failure(OperationalFailure.InvalidInput(
                "duration.mismatch",
                string.Format(ErrorMessages.Validation.DurationActualTooFar,
                    bestEffortDuration, effectiveTolerance)));
        }

        pipelineSw.Stop();
        logger.LogError(
            "[AudioPipeline] ALL {Max} synthesis attempts EXHAUSTED — Last error: {Error} | Total elapsed: {ElapsedMs}ms | Words: {Words} | Target: {Target}s",
            maxSynthesisAttempts, lastError, pipelineSw.ElapsedMilliseconds, lastWordCount,
            targetDuration);
        if (lastActualDuration.HasValue)
            return Result<AudioSynthesisResult>.Failure(OperationalFailure.InvalidInput(
                "duration.mismatch",
                string.Format(ErrorMessages.Validation.DurationActualTooFar,
                    lastActualDuration.Value, effectiveTolerance)));

        return Result<AudioSynthesisResult>.Failure(
            lastFailureMetadata.GetValueOr(OperationalFailure.Retryable(
                "tts.exhausted", lastError ?? "Synthesis failed after all attempts")));
    }


    public double EstimateDuration(string text, double secondsPerWord = 0.4)
    {
        return speechDurationEstimator.EstimateDuration(text, secondsPerWord);
    }

    public double GetDuration(byte[] audio)
    {
        return audioProcessor.GetDuration(audio);
    }

    public bool IsSlotFilled(byte[] audio, double targetDuration)
    {
        double finalSeconds = audioProcessor.GetDuration(audio);
        double finalSpeechSeconds = _fineTuner.GetSpokenDurationIfPossible(audio);
        double trailingSilence = Math.Max(0.0, finalSeconds - finalSpeechSeconds);
        return Math.Abs(finalSeconds - targetDuration) <= _durationOptions.EffectiveToleranceSeconds
               && trailingSilence <= _durationOptions.MaxSilencePadSeconds + 1e-6;
    }

    public byte[] StitchAudioClips(IReadOnlyList<byte[]> wavClips, double pauseSecondsBetweenClips)
    {
        return PcmAudioStitcher.Concatenate(wavClips, pauseSecondsBetweenClips);
    }

    public byte[] RetimeClip(byte[] audio, double targetSeconds)
    {
        if (audio.Length == 0 || targetSeconds <= 0)
            return audio;
        return audioProcessor.AdjustSpeed(audio, targetSeconds);
    }

    public byte[] TrimToSpokenDuration(byte[] audio)
    {
        return _fineTuner.TrimToSpokenDuration(audio);
    }

    private Result<AudioSynthesisResult> LogWaterfallAndSucceed(
        byte[] audio,
        string finalScript,
        double targetDuration,
        double estimatedDuration,
        double wordCountEstimate,
        int wordCount,
        double finalSeconds,
        double finalSpeed,
        Stopwatch pipelineSw,
        Option<string> voiceIdOverride,
        Option<string> modelOverride,
        Option<VoiceSettings>? attemptSettings,
        double effectiveTolerance,
        Option<AudioAlignment> alignment = default,
        double alignmentSourceDuration = 0,
        bool bedClosed = false)
    {
        pipelineSw.Stop();

        string nonNullFinalScript = finalScript ?? string.Empty;
        // Final sample-level guarantee — ensures exact duration even after strategies or rounding.
        audio = _fineTuner.EnsureExactDurationIfPossible(audio, targetDuration);
        finalSeconds = audioProcessor.GetDuration(audio);

        // Final stage: normalize loudness so every bulletin airs at a consistent level.
        // Gain only — duration and sample count are unchanged, so the exact-duration
        // guarantee above still holds.
        if (_loudnessOptions.Enabled)
            audio = loudnessNormalizer.Normalize(audio, _loudnessOptions.TargetRmsDbFs,
                _loudnessOptions.PeakCeilingDbFs, _loudnessOptions.SilenceFloorDbFs,
                _loudnessOptions.MaxMakeupGainDb);

        double effectiveWpm = finalSeconds > 0 ? wordCount / (finalSeconds / 60.0) : 0;
        double audioSizeKb = audio.Length / 1024.0;

        var waterfall = new DurationWaterfall(
            targetDuration,
            estimatedDuration,
            wordCountEstimate,
            finalSeconds,
            wordCount,
            Math.Round(effectiveWpm, 1));

        logger.LogInformation(
            "[AudioPipeline] SUCCESS — Final: {Final:F3}s | Target: {Target:F3}s | Residual: {Residual:F6}s | Size: {SizeKB:F1} KB ({SizeBytes} bytes) | Words: {Words} | EffectiveWPM: {Wpm:F1} | Pipeline: {PipelineMs}ms",
            waterfall.FinalSeconds, waterfall.TargetSeconds, waterfall.FinalSeconds - waterfall.TargetSeconds,
            audioSizeKb, audio.Length,
            waterfall.WordCount, waterfall.EffectiveWpm, pipelineSw.ElapsedMilliseconds);

        logger.LogInformation(
            "[AudioPipeline] DurationWaterfall — Target: {Target:F3}s | SyllableEst: {SylEst:F3}s ({SylAcc:F1}%) | WordCountEst: {WcEst:F3}s ({WcAcc:F1}%) | Final: {Final:F3}s",
            waterfall.TargetSeconds,
            waterfall.EstimatedSeconds,
            waterfall.SyllableAccuracyPercent,
            waterfall.WordCountEstimatedSeconds,
            waterfall.WordCountAccuracyPercent,
            waterfall.FinalSeconds);

        if (waterfall.WordCountAccuracyPercent >= 90)
            logger.LogInformation(
                "[AudioPipeline] Word-count duration estimate is accurate ({Accuracy:F1}%).",
                waterfall.WordCountAccuracyPercent);
        else
            logger.LogWarning(
                "[AudioPipeline] Word-count duration estimate diverged ({Accuracy:F1}%).",
                waterfall.WordCountAccuracyPercent);

        // Spoken span of the FINAL delivered audio — end of the last voiced block. After a
        // slow-to-fill stretch this is ~= the stretched length; it drives the word-timing rescale
        // and the trailing-silence (SlotFilled) check below.
        double finalSpeechSeconds = _fineTuner.GetSpokenDurationIfPossible(audio);

        // Natural read span BEFORE any fill (the raw trimmed synthesis length, passed as
        // alignmentSourceDuration). Calibration and the retry word-budget key off THIS, never the
        // stretched/padded length, or the learned read rate self-poisons (the e866746b… loop).
        double naturalSpoken = alignmentSourceDuration > 0 ? alignmentSourceDuration : finalSpeechSeconds;

        // The slot is "filled" only when the file is at target AND the trailing silence left after
        // slow-to-fill is within the pad budget. A larger gap means content + slow-fill could not
        // honestly reach the slot → the orchestrator rejects and falls back.
        double trailingSilence = Math.Max(0.0, finalSeconds - finalSpeechSeconds);
        bool slotFilled = Math.Abs(finalSeconds - targetDuration) <= effectiveTolerance
                          && trailingSilence <= _durationOptions.MaxSilencePadSeconds + 1e-6;

        // Record synthesis outcome for duration prediction learning
        try
        {
            int syllableCount = CountSyllables(DeliveryTags.Strip(nonNullFinalScript));
            var record = new SynthesisRecord
            {
                VoiceId = voiceIdOverride.GetValueOr(_broadcastOptions.DefaultVoiceId),
                ModelId = modelOverride.GetValueOr(""),
                Seed = null,
                Speed = finalSpeed,
                SyllableCount = syllableCount,
                WordCount = wordCount,
                AchievedDuration = naturalSpoken,
                TargetDuration = targetDuration,
                ScriptPreview = nonNullFinalScript[..Math.Min(120, nonNullFinalScript.Length)],
                Timestamp = DateTimeOffset.UtcNow,
                WasWithinTolerance = Math.Abs(naturalSpoken - targetDuration) <= effectiveTolerance
            };
            synthesisHistoryRepository.Record(record);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record synthesis history");
        }

        // Word timings: the alignment timescale belongs to the raw (trimmed) synthesis audio.
        // Rescale onto the SPOKEN span of the final audio — a slow-to-fill stretch lengthens that
        // span, so scaling by it keeps the karaoke highlight aligned with the (now slower) speech;
        // a pure pad leaves the span unchanged (scale ~1.0). The span is measured uncapped so a
        // large fill is not miscounted as speech.
        //
        // When a non-silent bed closed the underrun (UseTrimmedBedCloser), the measurement above
        // intentionally counts the bed as "voiced" — that's how the take passes the trailing-
        // silence/SlotFilled check. But the bed leaves the actual speech byte-for-byte untouched,
        // so using that inflated span here would stretch every word timing across the bed's dead
        // time (the karaoke highlight keeps "reading" after the voice has already stopped, and the
        // audible read finishes noticeably before the served timings say it does). Use the real
        // pre-bed spoken span instead: scale 1.0, clamp at the true end of speech.
        double speechSpanForRescale = bedClosed && alignmentSourceDuration > 0
            ? alignmentSourceDuration
            : finalSpeechSeconds;
        double timingCeiling = bedClosed && alignmentSourceDuration > 0
            ? alignmentSourceDuration
            : finalSeconds;

        IReadOnlyList<WordTiming> wordTimings =
            alignment.HasValue && alignmentSourceDuration > 0 && speechSpanForRescale > 0
                ? WordTimingBuilder.Build(alignment.Value, speechSpanForRescale / alignmentSourceDuration, timingCeiling)
                : [];

        return Result<AudioSynthesisResult>.Success(
            new AudioSynthesisResult(audio, nonNullFinalScript, finalSeconds, finalSpeed)
            {
                WordTimings = wordTimings,
                SpokenDurationSeconds = naturalSpoken,
                SlotFilled = slotFilled,
                IsNaturalSynthesis = true
            });
    }

    private async Task<AudioStrategyProposal?> TryAdjustAudioWithStrategiesAsync(
        string currentScript,
        byte[] currentAudio,
        double targetDuration,
        double actualDuration,
        int attempt,
        int maxSynthesisAttempts,
        CancellationToken cancellationToken)
    {
        if (_postSynthesisStrategies.Count == 0)
            return null;

        var context = new AudioPostSynthesisAdjustmentContext(
            currentScript,
            currentAudio,
            targetDuration,
            actualDuration,
            attempt,
            maxSynthesisAttempts);

        foreach (var strategy in _postSynthesisStrategies)
        {
            var proposal = await strategy.TryAdjustAsync(context, cancellationToken);
            if (proposal is null)
                continue;

            if (proposal.Audio.Length == currentAudio.Length && proposal.Audio.SequenceEqual(currentAudio))
                continue;

            logger.LogInformation(
                "[AudioPipeline] Duration strategy '{Strategy}' produced adjusted audio. Reason: {Reason}",
                strategy.Name,
                proposal.Reason);

            return new AudioStrategyProposal(
                proposal.Audio,
                strategy.Name,
                proposal.Reason);
        }

        return null;
    }

    private sealed record AudioStrategyProposal(
        byte[] Audio,
        string StrategyName,
        string Reason);
}
