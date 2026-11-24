using System.Diagnostics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Finalizes a validated script into an aired broadcast: saves the synthesised audio,
///     re-voices an operator-edited script verbatim, resolves the ScriptOnly result, and persists
///     the operator-facing final result (WaitingApproval). Extracted from
///     <see cref="BroadcastOrchestrator" /> — see that class for the fields it did not need.
/// </summary>
internal sealed class AudioFinalizer(
    IDurationEstimator durationEstimator,
    IBroadcastOutput broadcastOutput,
    IJobRepository jobRepository,
    INotificationService notificationService,
    TimeProvider timeProvider,
    IVoiceCalibrationService voiceCalibrationService,
    IAudioPipelineService audioPipeline,
    IScriptGenerator scriptGenerator,
    IOptions<BroadcastOptions> options,
    IOptions<SpeechDurationOptions> speechDurationOptions,
    IAudioTranscriber? audioTranscriber,
    IVerifiedPronunciationRepository? verifiedPronunciations,
    ILogger<BroadcastOrchestrator> logger,
    JobStatusUpdater jobStatusUpdater,
    ScriptReviewLoop scriptReviewLoop,
    DeliveryDirector deliveryDirector)
{
    private readonly BroadcastOptions _options = options.Value;
    private readonly SpeechDurationOptions _durationOptions = speechDurationOptions.Value;

    /// <summary>
    ///     Saves the synthesised audio and finalises the job (WaitingApproval). Shared by the full
    ///     generation path and the re-voice path.
    /// </summary>
    internal async Task<Result<BroadcastResult>> FinalizeAudioAsync(
        BroadcastWithTraffic context,
        string script,
        string taggedScript,
        byte[] audio,
        IReadOnlyList<WordTiming> wordTimings,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress = null)
    {
        byte[] processedAudio = audio;
        double processedDuration = durationEstimator.GetDuration(processedAudio);
        logger.LogInformation(
            "[FineTune] Job {JobId} audio finalized — Duration={Duration:F1}s, TargetDuration={TargetSec}s, AudioBytes={Bytes}",
            context.JobId, processedDuration, context.TargetDurationSeconds, processedAudio.Length);
        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Audio finalized at {processedDuration:F1}s (target {context.TargetDurationSeconds}s)", cancellationToken);

        // JobId suffix makes the name collision-proof: same-second generations across markets must
        // never overwrite each other's audio.
        string fileName = $"broadcast_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss}_{context.JobId}";

        await jobStatusUpdater.LogJobAsync(context.JobId, "Saving broadcast audio...", cancellationToken);

        var saveSw = Stopwatch.StartNew();
        var saveResult = await broadcastOutput.SaveAsync(processedAudio, fileName, cancellationToken);
        saveSw.Stop();

        logger.LogInformation(
            "[Save] Job {JobId} save completed in {ElapsedMs}ms — FileName={FileName}, Success={IsSuccess}",
            context.JobId, saveSw.ElapsedMilliseconds, fileName, saveResult.IsSuccess);
        await jobStatusUpdater.LogJobAsync(context.JobId,
            saveResult.IsSuccess
                ? $"Audio saved as {fileName}.wav in {saveSw.ElapsedMilliseconds}ms"
                : $"Audio save failed: {saveResult.Error}", cancellationToken);

        if (saveResult.IsFailure)
        {
            await jobStatusUpdater.MarkJobFailedAsync(context.JobId,
                $"{ErrorMessages.Validation.SavingFailed}: {saveResult.Error}", cancellationToken);
            return Result<BroadcastResult>.Failure($"{ErrorMessages.Validation.SavingFailed}: {saveResult.Error}");
        }

        return await SaveFinalBroadcastResultAsync(context, script, taggedScript, processedAudio, fileName,
            wordTimings, cancellationToken, progress);
    }

    /// <summary>
    ///     Keeps the strongest script/audio produced before generation exhausted its retries.
    ///     The candidate is deliberately not approval-ready: its audio is stored unpublished and
    ///     the job remains on the failure path. Operators can inspect what nearly worked, then
    ///     retry or choose the fallback without losing the useful evidence.
    /// </summary>
    internal async Task PersistFailedCandidateAsync(
        BroadcastWithTraffic context,
        ScriptGenerationEngine.GenerationCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.Script))
            return;

        var jobResult = await jobRepository.GetJobAsync(context.JobId, cancellationToken);
        if (jobResult is null || jobResult.IsFailure)
        {
            logger.LogWarning(
                "[FailedCandidate] Job {JobId} could not be loaded: {Error}",
                context.JobId, jobResult?.Error ?? "repository returned no result");
            return;
        }

        var job = jobResult.Value;
        if (job.Status is JobStatus.Rejected or JobStatus.Approved or JobStatus.Published
            or JobStatus.FallbackUsed or JobStatus.Missed)
        {
            logger.LogInformation(
                "[FailedCandidate] Job {JobId} is already {Status}; not mutating the terminal record.",
                context.JobId, job.Status);
            return;
        }

        string? fileName = null;
        double? measuredDuration = null;
        byte[] candidateAudio = candidate.Audio ?? [];
        if (candidateAudio.Length > 0)
        {
            double duration = durationEstimator.GetDuration(candidateAudio);
            if (duration > 0)
            {
                fileName = $"failed_candidate_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss}_{context.JobId}";
                var saveResult = await broadcastOutput.SaveAsync(
                    candidateAudio, fileName, cancellationToken);
                if (saveResult is { IsSuccess: true })
                {
                    measuredDuration = duration;
                    await jobStatusUpdater.LogJobAsync(
                        context.JobId,
                        $"Best failed candidate audio saved as {fileName}.wav ({duration:F1}s); it is unpublished and not airable.",
                        cancellationToken);
                }
                else
                {
                    logger.LogWarning(
                        "[FailedCandidate] Job {JobId} audio save failed: {Error}",
                        context.JobId, saveResult?.Error ?? "output returned no result");
                }
            }
        }

        string taggedScript = candidate.TaggedScript ?? string.Empty;
        IReadOnlyList<WordTiming> candidateWordTimings = candidate.WordTimings ?? [];
        job.ScriptText = Option<string>.Some(candidate.Script);
        job.TaggedScriptText = DeliveryTags.HasTags(taggedScript)
            ? Option<string>.Some(taggedScript)
            : Option<string>.None;
        job.DeliveryStyleId = DeliveryTags.HasTags(taggedScript)
            ? context.DeliveryStyleId
            : Option<string>.None;
        job.DeliveryStyleVersion = DeliveryTags.HasTags(taggedScript)
            ? context.DeliveryStyleVersion
            : Option<int>.None;
        job.AudioUrl = measuredDuration.HasValue
            ? Option<string>.Some($"{fileName}.wav")
            : Option<string>.None;
        // Clear any stale hash: the audio bytes have just been replaced, so a hash computed for
        // the previous take would misidentify what actually shipped. `AudioSha256` is a plain
        // nullable string, not an Option.
        job.AudioSha256 = null;
        job.MeasuredDurationSec = measuredDuration.HasValue
            ? Option<double>.Some(measuredDuration.Value)
            : Option<double>.None;
        job.LastEstimatedDurationSec = candidate.DurationSeconds > 0
            ? Option<double>.Some(candidate.DurationSeconds)
            : Option<double>.None;
        job.WordTimings = measuredDuration.HasValue && candidateWordTimings.Count > 0
            ? Option<IReadOnlyList<WordTiming>>.Some(candidateWordTimings)
            : Option<IReadOnlyList<WordTiming>>.None;
        // A failed candidate is an inspection aid, not a verified bulletin. Do not leave sentence
        // lineage from a different attempt attached to it.
        job.SetScriptSentences([]);
        job.LastUpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        var saveJobResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveJobResult is null || saveJobResult.IsFailure)
        {
            logger.LogWarning(
                "[FailedCandidate] Job {JobId} could not persist candidate metadata: {Error}",
                context.JobId, saveJobResult?.Error ?? "repository returned no result");
            return;
        }

        await jobStatusUpdater.LogJobAsync(
            context.JobId,
            measuredDuration.HasValue
                ? $"Best failed candidate retained for inspection: {candidate.Script.Length} characters and {measuredDuration.Value:F1}s audio."
                : "Best failed candidate script retained for inspection; no usable audio was produced.",
            cancellationToken);
    }

    /// <summary>
    ///     Re-voice only: synthesise the operator's existing/edited script verbatim — no traffic
    ///     re-fetch, no LLM generation/review. The script still passes content safety and the delivery
    ///     director (so v3 tags/redirection apply), then the audio is filled to slot and finalised.
    /// </summary>
    internal async Task<Result<BroadcastResult>> RevoiceExistingScriptAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress = null)
    {
        string script = context.PreviousScript.GetValueOr(string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(script))
        {
            const string error = "Re-voice requested but there is no existing script to synthesise.";
            await jobStatusUpdater.MarkJobFailedAsync(context.JobId, error, cancellationToken);
            return Result<BroadcastResult>.Failure(error);
        }

        // Persist the operator's edited script to the job record BEFORE attempting safety
        // review/synthesis (RISK-31). Every failure path below only calls MarkJobFailedAsync,
        // which never touches ScriptText — without this, a failed re-voice left job.ScriptText
        // holding the pre-edit script, so any later retry (which falls back to job.ScriptText
        // when no editedScript is resubmitted) silently discarded the operator's edit.
        await jobStatusUpdater.UpdateJobAsync(context.JobId, j => j.ScriptText = Option<string>.Some(script), null, cancellationToken);

        await jobStatusUpdater.LogJobAsync(context.JobId, "Re-voicing the existing script (no regeneration)...", cancellationToken);

        // Compliance still applies to a verbatim re-voice.
        var safetyResult = await scriptReviewLoop.CheckContentSafetyAsync(context.JobId, context.MarketId, script, cancellationToken);
        if (safetyResult.IsFailure)
        {
            await jobStatusUpdater.MarkJobFailedAsync(context.JobId, safetyResult.Error, cancellationToken);
            return safetyResult.PropagateFailure<BroadcastResult>();
        }
        script = safetyResult.Value;

        // The operator's own tags win outright: applying the station's delivery style on top
        // would give one read two authors (decision 2026-08-02). The tags are accepted only
        // when stripping them reproduces the approved words exactly, so a malformed or
        // tampered payload can never change what is said — it just loses the tone.
        string synthScript = await ResolveTonedOrDirectedAsync(context, script, cancellationToken);

        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Synthesizing audio ({WordCounter.Count(script)} words, target {context.TargetDurationSeconds}s)...",
            cancellationToken);

        // Re-voice keeps the operator's exact words — PaceBoostWpm cannot grow the script here,
        // but if the fixed read overruns the slot, honour the voice's configured compression
        // headroom rather than clamping to the unboosted default. The same applies in the other
        // direction: an operator edit that lands SHORT gets the boost's earned slow-down headroom,
        // so a re-voice is judged against the same band generation used.
        // The editorial value is the base for the deliberate pace-boost derivation below; the
        // derived re-voice factor wins downstream so this override cannot be applied twice.
        double resolvedMaxSpeedUpFactor =
            context.EditorialSettings.MaxSpeedUpFactor ?? _durationOptions.MaxSpeedUpFactor;
        double resolvedMaxSlowDownFactor =
            context.EditorialSettings.MaxSlowDownFactor ?? _durationOptions.MaxSlowDownFactor;
        // Seeded from the EDITORIAL value, which is null when ARN has configured nothing — the
        // pipeline then applies its own configured option exactly as it did before the console
        // existed. Seeding from the resolved double instead would send an explicit factor on every
        // re-voice, changing the call for stations that set nothing (11 Application tests pin the
        // old contract; 2026-08-24). The pace-boost branch below still derives from the resolved
        // base, and that derived value deliberately wins.
        double? revoiceMaxSpeedUpFactor = context.EditorialSettings.MaxSpeedUpFactor;
        double? revoiceMaxSlowDownFactor = context.EditorialSettings.MaxSlowDownFactor;
        if (context.PaceBoostWpm > 0)
        {
            var revoiceSettings = context.VoiceSettings.GetValueOr(VoiceSettings.Default);
            var revoiceMetrics = await voiceCalibrationService.GetCalibrationMetricsAsync(
                context.VoiceId.GetValueOr(_options.DefaultVoiceId), Option<VoiceSettings>.Some(revoiceSettings),
                false, cancellationToken, context.Model);
            double revoiceWpm =
                revoiceMetrics.WordsPerMinute > 0 ? revoiceMetrics.WordsPerMinute : _options.WordsPerMinute;
            // Fitter ceiling, not the aim ceiling: nothing here sizes a script, so the only job of
            // this number is to absorb render variance around the operator's fixed word count.
            revoiceMaxSpeedUpFactor = WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(
                revoiceWpm, context.PaceBoostWpm, resolvedMaxSpeedUpFactor,
                _durationOptions.AbsolutePaceBoostSpeedUpCeiling);
            revoiceMaxSlowDownFactor = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(
                revoiceWpm, context.PaceBoostWpm, resolvedMaxSlowDownFactor,
                _durationOptions.AbsolutePaceBoostSlowDownCeiling);
        }

        Task<Result<AudioSynthesisResult>> SynthesizeAsync(string text) =>
            audioPipeline.SynthesizeWithDurationTargetAsync(
                text,
                context.TargetDurationSeconds,
                context.VoiceId,
                context.VoiceSettings,
                context.Model,
                cancellationToken,
                0,
                Option<string>.Some(context.MarketId.GetValueOr(context.Location)),
                context.PronunciationStack,
                revoiceMaxSpeedUpFactor,
                maxSlowDownFactorOverride: revoiceMaxSlowDownFactor,
                // Same lock the generation path passes (ScriptGenerationEngine): an overrun is
                // fitted by dropping trailing sentences, and the station's closer sits at the very
                // end. A re-voice onto a slower voice is the EASIEST way to overrun a read that
                // used to fit, so without this the voice change deleted the bookend first. The
                // operator's edited words carry no workflow lineage, so no fixture lines are
                // pinned here: the words are exactly what the operator approved.
                lockedSuffix: BulletinBookendComposer.OutroOf(context.Bookends),
                lockedPrefix: BulletinBookendComposer.LockedIntroOf(context.Bookends));

        var audioResult = await SynthesizeAsync(synthScript);

        if (audioResult.IsFailure)
        {
            await jobStatusUpdater.MarkJobFailedAsync(context.JobId,
                $"Re-voice synthesis failed: {audioResult.Error}", cancellationToken);
            return audioResult.PropagateFailure<BroadcastResult>();
        }

        // A tone may cost time, never content. If the toned read overruns, the pipeline's last
        // resort is to DROP trailing sentences and re-synthesise (AudioPipelineService CASE 3),
        // which would let a delivery choice silently change what airs — the same harm the
        // word-immutability guard exists to prevent.
        //
        // Reduce in MEASURED cost order rather than discarding the tone (2026-08-05 device study,
        // 4.9% duration noise floor): pause punctuation is the expensive part (ellipsis +5.3%, em
        // dash +8.1%, extra sentence breaks +12.3%), and among tags only the sound-inserting ones
        // cost anything ([short pause] +13.0%, [exhales] +10.9%, [sighs] +5.0%). Manner tags are
        // free or FASTER than plain ([brightly] −6.2%, [firmly] −5.5%, [excitedly] −3.2%), so the
        // old "drop every tag" step bought almost no time on a manner-tagged read and cost the
        // operator their register for nothing (job de45b38b).
        if (DeliveryTags.HasTags(synthScript) &&
            !DeliveryTags.WordsPreserved(script, audioResult.Value.FinalScript))
        {
            var attempted = new HashSet<string>(StringComparer.Ordinal) { synthScript };
            foreach ((string candidate, string note) in TonedFallbacks(synthScript, script))
            {
                // A rung that reduces to something already synthesised buys nothing.
                if (!attempted.Add(candidate))
                    continue;

                logger.LogWarning(
                    "[Tone] Job {JobId} toned read did not fit the slot; retrying with {Note}.",
                    context.JobId, note);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Toned read overran the slot — retrying with {note}.", cancellationToken);

                var retry = await SynthesizeAsync(candidate);
                if (retry.IsFailure)
                {
                    await jobStatusUpdater.MarkJobFailedAsync(context.JobId,
                        $"Re-voice synthesis failed: {retry.Error}", cancellationToken);
                    return retry.PropagateFailure<BroadcastResult>();
                }

                audioResult = retry;
                if (DeliveryTags.WordsPreserved(script, retry.Value.FinalScript))
                {
                    int kept = DeliveryTags.Extract(retry.Value.FinalScript).Count;
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        kept > 0
                            ? $"Fitted the slot with {note}; {kept} audio tag(s) kept."
                            : "Fitted the slot with the plain read; delivery tones dropped.",
                        cancellationToken);
                    break;
                }
            }
        }

        // A re-voice promises the operator's exact words, but the pipeline's overrun fit is
        // allowed to drop trailing sentences (CASE 3) — on a slower voice the same words take
        // longer, so a read that fitted on the old voice can overrun on the new one. When that
        // happens the trim must be LOUD: on dev (2026-08-07, job 0c1c11e8) a re-voice silently
        // deleted the station's drafted sign-off and the only trace was a shorter script. The
        // review transcript check can't catch it — it compares against the trimmed script.
        string spokenPlain = DeliveryTags.Strip(audioResult.Value.FinalScript);
        if (!DeliveryTags.WordsPreserved(script, spokenPlain))
        {
            int keptWords = WordCounter.Count(spokenPlain);
            int askedWords = WordCounter.Count(script);
            logger.LogWarning(
                "[Revoice] Job {JobId} slot fit dropped trailing content: {Kept} of {Asked} words kept on voice {VoiceId}.",
                context.JobId, keptWords, askedWords, context.VoiceId.GetValueOr("(default)"));
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Slot fit dropped the end of the read on this voice ({keptWords} of {askedWords} words kept) — " +
                "the full text was too long even at maximum compression. Review the script before approving; " +
                "regenerate to have the words rewritten to fit.",
                cancellationToken);
        }

        var synthesis = audioResult.Value;
        string taggedFinalScript = synthesis.FinalScript;
        string finalScript = DeliveryTags.Strip(taggedFinalScript);
        double spokenDuration = synthesis.SpokenDurationSeconds > 0
            ? synthesis.SpokenDurationSeconds
            : durationEstimator.GetDuration(synthesis.Audio);
        await jobStatusUpdater.LogJobAsync(context.JobId,
            synthesis.SlotFilled
                ? $"Re-voiced read fitted to the {context.TargetDurationSeconds:F0}s slot"
                : $"Re-voiced read is {spokenDuration:F1}s for the {context.TargetDurationSeconds:F0}s slot — content-limited",
            cancellationToken);

        return await FinalizeAudioAsync(
            context, finalScript, taggedFinalScript, synthesis.Audio, synthesis.WordTimings,
            cancellationToken, progress);
    }

    /// <summary>
    ///     Ways to make an overrunning toned read shorter, cheapest loss of intent first. Ordered
    ///     by MEASURED time cost, not by assumption: pause punctuation is the expensive part, the
    ///     sound-inserting tags come next, and only then is the tone abandoned. See
    ///     <see cref="DeliveryTimeCost" /> for the numbers and why the old single step — strip
    ///     every tag — was both too blunt and, on a manner-tagged read, nearly free of benefit.
    /// </summary>
    private static IEnumerable<(string Script, string Note)> TonedFallbacks(string toned, string plain)
    {
        yield return (DeliveryTimeCost.WithoutPausePunctuation(toned), "the pauses flattened");
        yield return (
            DeliveryTimeCost.WithoutPausePunctuation(DeliveryTimeCost.WithoutTimeCostingTags(toned)),
            "the pauses flattened and the breath tags removed");
        yield return (plain, "the plain approved read");
    }

    /// <summary>
    ///     The text to synthesise: the operator's own tagged read when they set one, otherwise
    ///     the station's delivery style via the director. Never both — see the caller.
    /// </summary>
    private async Task<string> ResolveTonedOrDirectedAsync(
        BroadcastWithTraffic context, string script, CancellationToken cancellationToken)
    {
        string? toned = context.TonedScript.Match<string?>(v => v, () => null);
        // A toned script carrying no tags directs nothing, so it must not cost the station
        // its configured delivery style — suppressing the director here would strip the
        // style and add nothing back (found on dev, 2026-08-02).
        if (string.IsNullOrWhiteSpace(toned) || !DeliveryTags.HasTags(toned))
            return await deliveryDirector.ApplyDeliveryDirectionAsync(context, script, cancellationToken);

        // The operator's tagged read can only air on a voice that interprets tags. On any other
        // model the tags would be SPOKEN, so fall through to the director (which is itself gated
        // and will direct with punctuation only). Told plainly in the job log, because the operator
        // deliberately placed those tags and is entitled to know they were not used.
        string? ttsModel = context.Model.Match<string?>(v => v, () => null);
        if (!DeliveryTags.SupportsTags(ttsModel))
        {
            logger.LogWarning(
                "[Tone] Job {JobId} has operator tags but model '{Model}' is not v3; tags cannot air.",
                context.JobId, ttsModel ?? "(none)");
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Delivery tones not applied: this voice ({ttsModel ?? "unknown model"}) cannot use " +
                "audio tags - it would read them aloud. Airing the read without tags.",
                cancellationToken);
            return await deliveryDirector.ApplyDeliveryDirectionAsync(
                context, DeliveryTags.Strip(script), cancellationToken);
        }

        if (!DeliveryTags.WordsPreserved(script, toned))
        {
            logger.LogWarning(
                "[Tone] Job {JobId} toned script altered the approved words; airing the plain read.",
                context.JobId);
            await jobStatusUpdater.LogJobAsync(context.JobId,
                "Delivery tones ignored: the tagged read did not match the approved words.",
                cancellationToken);
            return script;
        }

        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Operator delivery tones applied: {DeliveryTags.Extract(toned).Count} tag(s); station delivery style skipped.",
            cancellationToken);
        return toned;
    }

    internal async Task<Result<BroadcastResult>> HandleScriptOnlyResultAsync(
        BroadcastWithTraffic context,
        string script,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress = null)
    {
        string voiceId = context.VoiceId.GetValueOr(_options.DefaultVoiceId);
        var settings = context.VoiceSettings.GetValueOr(VoiceSettings.Default);

        var metrics = await voiceCalibrationService.GetCalibrationMetricsAsync(
            voiceId,
            Option<VoiceSettings>.Some(settings),
            false,
            cancellationToken,
            context.Model);
        double secondsPerWord = metrics.WordsPerMinute > 0 ? 60.0 / metrics.WordsPerMinute : 0.4;

        double estimatedDuration = durationEstimator.EstimateDuration(script, secondsPerWord);
        int wordCount = WordCounter.Count(script);

        logger.LogInformation(
            "[ScriptOnly] Job {JobId} complete — WordCount={WordCount}, EstimatedDuration={EstDuration:F1}s",
            context.JobId, wordCount, estimatedDuration);

        var updateResult = await jobStatusUpdater.UpdateJobAsync(context.JobId, job =>
        {
            // ScriptOnly produces a script and no audio, so the job never passes through review
            // on its own. Move it there explicitly before approving: Job.Approve now requires
            // WaitingApproval (it previously accepted any in-flight state, which is what let a
            // job with no script be approved). Two honest transitions beat a weak guard.
            var utcNow = timeProvider.GetUtcNow().UtcDateTime;
            job.ScriptText = Option<string>.Some(script);
            job.MeasuredDurationSec = Option<double>.Some(estimatedDuration);
            job.TransitionTo(JobStatus.WaitingApproval, "system", Option<string>.None, utcNow);
            job.Approve(utcNow, "system", Option<string>.Some("Script generation complete (ScriptOnly mode)"));
        }, "Script generation complete (ScriptOnly mode)", cancellationToken);

        if (updateResult.IsFailure)
            return Result<BroadcastResult>.Failure(updateResult.Error);
        if (progress is not null)
            await progress.Ready($"Script ready for approval: {wordCount} words.");

        return new BroadcastResult(
            string.Empty,
            "Script generated successfully (ScriptOnly mode).",
            new BroadcastDebugInfo(
                scriptGenerator.GetType().Name,
                "None (ScriptOnly)",
                estimatedDuration));
    }

    private async Task<Result<BroadcastResult>> SaveFinalBroadcastResultAsync(
        BroadcastWithTraffic context,
        string script,
        string taggedScript,
        byte[] processedAudio,
        string fileName,
        IReadOnlyList<WordTiming> wordTimings,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress = null)
    {
        string jobId = context.JobId;
        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result<BroadcastResult>.Failure($"Failed to retrieve job: {jobResult.Error}");

        var job = jobResult.Value;
        if (job.Status is JobStatus.WaitingApproval or JobStatus.Rejected
            or JobStatus.FallbackUsed or JobStatus.Published or JobStatus.Approved or JobStatus.Missed)
            return Result<BroadcastResult>.Failure(OperationalFailure.Conflict("broadcast.job_already_terminal", $"Job already holds an operator-facing result: {job.Status}"));
        double duration = durationEstimator.GetDuration(processedAudio);

        logger.LogInformation(
            "[FinalResult] Job {JobId} — MeasuredDuration={Duration:F1}s, AudioSizeBytes={AudioBytes}, FileName={FileName}",
            jobId, duration, processedAudio.Length, fileName);

        if (duration is 0)
        {
            const string failureMessage = "Audio synthesis produced empty audio (0s duration).";
            logger.LogError("[FinalResult] Job {JobId} FAILED: {Reason}", jobId, failureMessage);
            await jobStatusUpdater.UpdateJobStatus(jobId, JobStatus.Failed, failureMessage, cancellationToken);
            return Result<BroadcastResult>.Failure(failureMessage);
        }

        // Pre-approval audio review (SoW "AI-based pre-review of generated audio"): transcribe
        // the FINAL audio and compare against the script. Fail-soft — an unavailable transcriber
        // degrades the review to the deterministic checks, never the bulletin.
        AudioPreReview.Result? preReview = null;
        AudioPreReviewReport? preReviewReport = null;
        if (audioTranscriber is { Enabled: true })
        {
            var transcription = await audioTranscriber.TranscribeAsync(processedAudio, cancellationToken);
            if (transcription.IsSuccess)
            {
                preReview = AudioPreReview.Compare(DeliveryTags.Strip(script), transcription.Value);
                preReviewReport = AudioPreReviewReport.For(
                    preReview,
                    await ClearedPronunciationsAsync(
                        context.MarketId.GetValueOr(context.Location), cancellationToken));
                if (preReviewReport.IsWarning)
                    logger.LogWarning(
                        "[AudioPreReview] Job {JobId}: cut off: {Tail}; {Count} suspect pronunciation(s): {Words}; number mismatches: {Numbers} (match {Ratio:P0})",
                        jobId,
                        preReviewReport.UnheardTailWords.Count > 0
                            ? string.Join(", ", preReviewReport.UnheardTailWords)
                            : "none",
                        preReviewReport.OutstandingSuspects.Count,
                        string.Join(", ", preReviewReport.OutstandingSuspects),
                        preReview.NumberMismatches.Count > 0 ? string.Join(", ", preReview.NumberMismatches) : "none",
                        preReview.MatchRatio);
            }
            else
            {
                logger.LogWarning("[AudioPreReview] Job {JobId}: transcription unavailable — {Error}", jobId, transcription.Error);
            }
        }

        logger.LogInformation(
            "[FinalResult] Job {JobId} broadcast ready — transitioning to WaitingApproval",
            jobId);

        job.TransitionTo(JobStatus.WaitingApproval, utcNow: timeProvider.GetUtcNow().UtcDateTime);
        job.ScriptText = Option<string>.Some(script);
        // Delivery-style provenance (audio tags actually sent to synthesis) — set independently
        // of the earlier ScriptReady-stage save, rather than trusting it survived the round trip
        // (empirically it can silently arrive here as neutral even after a real director pass;
        // see the 2026-07-23 decision). Mirrors PersistScriptOnJob's exact tagged/untagged logic.
        bool tagged = DeliveryTags.HasTags(taggedScript);
        job.TaggedScriptText = tagged ? Option<string>.Some(taggedScript) : Option<string>.None;
        job.DeliveryStyleId = tagged ? context.DeliveryStyleId : Option<string>.None;
        job.DeliveryStyleVersion = tagged ? context.DeliveryStyleVersion : Option<int>.None;
        // Pronunciation provenance, recorded here for the same reason as the delivery style
        // above: only ScriptGenerationEngine writes it, and a re-voice never runs that. So a
        // re-voice that refreshed the dictionary synthesised with a NEWER version than the job
        // pinned, and leaving the pin stale makes the record claim it aired with rules it did
        // not use — and makes a byte-faithful republish impossible. Mirrors the context the
        // synthesis call at the top of this class was actually handed. Both layers are recorded
        // separately because they are edited by different people under different rules: an
        // operator's word fix targets the station layer, never the platform-wide one.
        job.PronunciationDictionaryId = context.PronunciationStack.Station.Map(l => l.DictionaryId);
        job.PronunciationVersionId = context.PronunciationStack.Station.Map(l => l.VersionId);
        job.GlobalPronunciationDictionaryId = context.PronunciationStack.Global.Map(l => l.DictionaryId);
        job.GlobalPronunciationVersionId = context.PronunciationStack.Global.Map(l => l.VersionId);
        job.MeasuredDurationSec = Option<double>.Some(duration);
        job.AudioUrl = Option<string>.Some($"{fileName}.wav");
        job.WordTimings = wordTimings.Count > 0
            ? Option<IReadOnlyList<WordTiming>>.Some(wordTimings)
            : Option<IReadOnlyList<WordTiming>>.None;
        job.PreReview = preReview is not null
            ? Option<AudioPreReviewOutcome>.Some(new AudioPreReviewOutcome(
                preReview.MatchRatio, preReview.SuspectWords, preReview.NumberMismatches))
            : Option<AudioPreReviewOutcome>.None;
        // Evidence provenance belongs to the CONTENT, and a re-voice re-renders words that are
        // already fixed — it never re-fetches traffic, weather or fuel, so its context carries
        // none of them. Writing it through anyway blanked the whole evidence trail: a re-voiced
        // bulletin reciting "Passing clouds, 14 degrees" reported no weather, no fuel and
        // "0 incidents cited · heuristic matching" in the cockpit, leaving the operator asked to
        // approve eight road claims with nothing to check them against (dev, 2026-07-28).
        //
        // The replay snapshot is the SAME class of data and belongs under the same guard: while it
        // sat outside, every re-voice overwrote GeneratorInputJson with the contextless re-voice
        // context, so a job an operator had edited and re-voiced could no longer be replayed at all
        // (job fa754637, Gold Coast: GoodRunRoadsJson kept its six roads while the snapshot recorded
        // zero, and a regression fixture built from it produced an empty script).
        if (!context.RevoiceOnly)
        {
            job.Weather = context.Weather;
            job.Fuel = context.Fuel;
            job.GoodRunRoads = context.GoodRunRoads;
            // Named arguments deliberately: this call previously passed `f.FunctionalClass` as the
            // 8th positional argument, which is `BaseScore` (double?), not `FunctionalClass` (int).
            // The int converted implicitly so it compiled silently, writing the functional class into
            // BaseScore, never persisting BoostAmount at all, and leaving FunctionalClass on its
            // default of 3 — which made the review rundown render a bare "(+)" instead of the boost
            // amount, and lost the real functional class on every persisted flow road.
            job.SetFlowRoads(context.FlowPool.Select(f => new JobFlowRoad(
                f.RoadName, f.JamFactor, f.LengthMeters, f.IsWeighted,
                context.GoodRunRoads.Any(r => string.Equals(r, f.RoadName, StringComparison.OrdinalIgnoreCase)),
                Score: f.Score,
                PriorityBoosted: f.PriorityBoosted,
                BaseScore: f.BaseScore,
                BoostAmount: f.BoostAmount,
                FunctionalClass: f.FunctionalClass)));
            try { job.GeneratorInput = GeneratorInputSnapshot.FromContext(context); }
            catch (Exception ex) { logger.LogWarning(ex, "[FinalResult] Job {JobId} failed to capture generator-input snapshot", jobId); }
        }

        job.LastUpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
        {
            logger.LogWarning("[FinalResult] Job {JobId} failed to save: {Error}", jobId, saveResult.Error);
            return Result<BroadcastResult>.Failure(saveResult.Error);
        }

        // Append the "broadcast ready" log only AFTER the status save succeeds. AddLogAsync
        // runs its own read-modify-write that bumps the job entity's ETag; doing it before the
        // save (as it was) invalidated this save's optimistic-concurrency ETag, returning
        // Conflict and stranding the job at SCRIPT_READY even though the audio uploaded fine.
        await jobStatusUpdater.LogJobAsync(jobId, $"Broadcast ready — {duration:F1}s duration, awaiting approval", cancellationToken);
        if (progress is not null)
            await progress.Ready($"Broadcast ready: {duration:F1}s duration, awaiting approval.");

        if (preReviewReport is not null)
            await jobRepository.AddLogAsync(jobId, preReviewReport.Message,
                preReviewReport.IsWarning ? JobLogLevel.Warn : JobLogLevel.Info, cancellationToken);

        await notificationService.SendJobNotificationAsync(job, NotificationKind.ReadyForReview, cancellationToken);

        return new BroadcastResult(
            $"{fileName}.wav",
            SuccessMessages.BroadcastGenerated,
            new BroadcastDebugInfo(
                scriptGenerator.GetType().Name,
                audioPipeline.GetType().Name,
                duration));
    }

    /// <summary>
    ///     The words this market's operators have already listened to and cleared.
    ///     <para>
    ///         The list persisted on the job stays RAW on purpose (see
    ///         <see cref="VerifiedPronunciation" />): the review cockpit applies verdicts as a
    ///         read-time lens, so withdrawing one re-exposes the word on every bulletin ever
    ///         generated rather than only on future ones. A job LOG line is the opposite kind of
    ///         artefact — a point-in-time note telling THIS operator what to go and listen to —
    ///         and it is written once, at Warn, where the audit timeline renders it amber and
    ///         auto-expanded. Leaving verdicts out of it meant a word a human cleared months ago
    ///         kept raising a warning on every bulletin that named the road (melbourne cleared
    ///         "Toorak" on 2026-07-28 and still logged it 27 times).
    ///     </para>
    ///     <para>
    ///         What a clearance may mute is <see cref="AudioPreReviewReport" />'s decision, and it
    ///         is deliberately narrow: suspect PRONUNCIATIONS only, never an unheard tail. Prod job
    ///         c2894ffb109f4c218c73e274e43665b1 (2026-08-18, cairns-4ca) aired a read cut off
    ///         mid-"Manoora" and logged only "1 suspect pronunciation(s) — hole", because an
    ///         earlier clearance recorded about how Manoora SOUNDS also muted the evidence that the
    ///         word was absent from the audio entirely. Those are two different failures.
    ///     </para>
    ///     <para>Fail-soft: an unreadable verdict list warns about too much, never too little.</para>
    /// </summary>
    private async Task<IReadOnlyList<string>> ClearedPronunciationsAsync(
        string market, CancellationToken cancellationToken)
    {
        if (verifiedPronunciations is null)
            return [];

        var stored = await verifiedPronunciations.GetForMarketAsync(market, cancellationToken);
        if (stored.IsFailure)
        {
            logger.LogWarning(
                "[AudioPreReview] Market {Market}: could not read verified pronunciations ({Error}); reporting every suspect",
                market, stored.Error);
            return [];
        }

        return stored.Value.Where(v => v.Enabled).Select(v => v.Word).ToList();
    }
}
