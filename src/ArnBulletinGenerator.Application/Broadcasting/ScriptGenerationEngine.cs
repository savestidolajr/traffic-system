using System.Diagnostics;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Validation;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Owns the generation retry loop (drafting, duration-fit re-attempts, filler top-up) and TTS
///     synthesis for one broadcast. Review/refine convergence and delivery direction/polish are
///     owned by <see cref="ScriptReviewLoop" /> and <see cref="DeliveryDirector" /> — this class
///     drives the loop and calls through to them. Extracted from
///     <see cref="BroadcastOrchestrator" /> — see that class for the fields it did not need.
/// </summary>
internal sealed class ScriptGenerationEngine(
    IScriptGenerator scriptGenerator,
    IAudioPipelineService audioPipeline,
    IDurationEstimator durationEstimator,
    IFillerAudioCacheService fillerAudioCacheService,
    IVoiceCalibrationService voiceCalibrationService,
    IOptions<BroadcastOptions> options,
    IOptions<SpeechDurationOptions> speechDurationOptions,
    IBroadcastMetrics metrics,
    IDurationPredictor? durationPredictor,
    ISpokenDurationModel? spokenDurationModel,
    ILogger<BroadcastOrchestrator> logger,
    TimeProvider timeProvider,
    JobStatusUpdater jobStatusUpdater,
    ScriptReviewLoop scriptReviewLoop,
    DeliveryDirector deliveryDirector,
    ISpokenDurationSampleRepository? durationSamples = null,
    SpokenDurationCalibration? durationCalibration = null)
{
    // Inter-clip pause when stitching cached filler audio onto the traffic-only take — an
    // audio-engineering constant, not a business lever.
    private const double FillerPauseSeconds = 0.35;

    // Below this the pace match is not worth a WSOLA pass: a sub-50ms move on a 3s clip is inaudible,
    // and skipping it keeps the clip byte-identical to the cached bytes (so a well-matched voice pays
    // nothing and the audio is provably untouched). Four of the six measured aired jobs sat inside
    // 9% of the read's rate; this deadband is what stops them being re-encoded for no gain.
    private const double FillerRetimeDeadbandSeconds = 0.05;

    // Post-synthesis filler rescue: when no statement fits WITHIN the measured residual gap,
    // allow closing it with one that overshoots by at most this much — the overrun is compressed
    // back to the slot by FineTuneToTargetDuration within the speed-up budget (MaxSpeedUpFactor
    // 1.1 → a 30s slot absorbs ~3.0s of overshoot), while an underrun beyond the silence pad fails
    // the slot outright and costs the whole attempt. Kept just under the ~3.0s compression headroom
    // so a ~4s station filler can bridge the common 1.5–2.7s gaps by overshoot-then-compress (only
    // ever speeding up), rather than letting the read go short into slow-down/fallback.
    private const double FillerRescueOvershootToleranceSeconds = 2.5;

    private readonly BroadcastOptions _options = options.Value;
    private readonly SpeechDurationOptions _durationOptions = speechDurationOptions.Value;

    // ── Generation retry loop ────────────────────────────────────────────
    internal async Task<GenerationRunOutcome> TryGenerateValidBroadcastAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken,
        BroadcastWorkBudget workBudget,
        IGenerationProgressReporter? progress = null)
    {
        using var budgetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, workBudget.Token);
        cancellationToken = budgetCancellation.Token;
        workBudget.ThrowIfCancellationRequested();
        string voiceId = context.VoiceId.GetValueOr(_options.DefaultVoiceId);
        var settings = context.VoiceSettings.GetValueOr(VoiceSettings.Default);

        var metrics = await voiceCalibrationService.GetCalibrationMetricsAsync(
            voiceId,
            Option<VoiceSettings>.Some(settings),
            false,
            cancellationToken,
            context.Model);

        double calibratedSps = metrics.SyllablesPerSecond;
        if (calibratedSps <= 0)
            calibratedSps = _options.SyllablesPerSecond;

        double calibratedSpw = metrics.SyllablesPerWord;
        if (calibratedSpw <= 0)
            calibratedSpw = CalibrationDefaults.SyllablesPerWord;

        double calibratedWpm = metrics.WordsPerMinute;
        if (calibratedWpm <= 0)
            calibratedWpm = calibratedSpw > 0
                ? calibratedSps * 60.0 / calibratedSpw
                : _options.WordsPerMinute;

        double calibratedSecondsPerWord = 60.0 / calibratedWpm;
        // Resolve the bulletin's slot envelope once. A null editorial field means inherit the
        // environment's configured option, preserving the pre-console behaviour exactly.
        double resolvedMaxSpeedUpFactor =
            context.EditorialSettings.MaxSpeedUpFactor ?? _durationOptions.MaxSpeedUpFactor;
        double resolvedMaxSlowDownFactor =
            context.EditorialSettings.MaxSlowDownFactor ?? _durationOptions.MaxSlowDownFactor;
        double resolvedMaxSilencePadSeconds =
            context.EditorialSettings.MaxSilencePadSeconds ?? _durationOptions.MaxSilencePadSeconds;

        // ── Bookend budget ──
        // The station's fixed opener/closer are unconditional and ride the SAME take as the body,
        // so the words the writer may spend are the slot MINUS the bookends. Filler is topped up
        // afterwards against whatever gap is left; bookends are reserved up front. Without this
        // every bookended bulletin drafts to the full slot, overshoots, and burns duration-fit
        // retries it cannot win. Floors at 1s so a pathologically long pair cannot invert the
        // target — the operator sees the squeeze in the studio's resolved preview, and there is
        // deliberately no cap here (2026-08-04 decision).
        double bookendSeconds = BulletinBookendComposer.EstimateSeconds(
            context.Bookends, calibratedSecondsPerWord);
        double bodyTargetSeconds = Math.Max(1.0, context.TargetDurationSeconds - bookendSeconds);

        // ── Bookend slot-fit pre-check (bug-bash 2026-08-05 finding 4) ──
        // A pair that cannot share the slot with a minimum viable traffic read is dropped BEFORE
        // any drafting or synthesis. Keeping it would size the writer for a body too small to be
        // a real read; the duration-fit loop then cannot converge (measured live on job
        // f9907416a3: 15s slot + 17-word pair → 4 generations, 4+ TTS renders, 14.0 → 17.4 →
        // 13.0 → 11.0s, then the filler cart with NO traffic at all), burning LLM calls and TTS
        // renders to discover an arithmetic impossibility knowable before the first draft. The
        // product policy is decided: PREFER THE TRAFFIC CONTENT. Airing traffic without the
        // station's top-and-tail is a mild degradation; airing a generic filler cart instead of
        // the traffic is a product failure. The ordinary case (a pair that DOES fit — the
        // standard 30s-slot-with-18-word-pair, and every portlincoln-style 15s bulletin with no
        // or a small pair) is untouched: the pre-draft budget subtraction above still applies.
        if (BulletinBookendComposer.HasAny(context.Bookends))
        {
            var fit = BookendSlotFitPlanner.Evaluate(
                context.Bookends, context.TargetDurationSeconds, calibratedSecondsPerWord);
            if (!fit.Fits)
            {
                string dropReason =
                    $"Bookend pair dropped: its {BulletinBookendComposer.WordCount(context.Bookends)} words need " +
                    $"{fit.BookendSeconds:F1}s of the {context.TargetDurationSeconds:F0}s slot, leaving only " +
                    $"{Math.Max(0, context.TargetDurationSeconds - fit.BookendSeconds):F1}s for traffic — less than " +
                    $"the {fit.MinimumViableTrafficSeconds:F1}s minimum viable traffic read. Airing the traffic " +
                    "without the station's top-and-tail.";
                logger.LogWarning("[Generation] Job {JobId} {DropReason}", context.JobId, dropReason);
                await jobStatusUpdater.LogJobAsync(context.JobId, dropReason, cancellationToken);
                context = context with { Bookends = Option<ResolvedBookends>.None };
                bookendSeconds = 0;
                bodyTargetSeconds = context.TargetDurationSeconds;
            }
        }

        double? effectiveMaxSpeedUpFactor = context.PaceBoostWpm > 0
            ? WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(
                calibratedWpm, context.PaceBoostWpm, resolvedMaxSpeedUpFactor,
                _durationOptions.AbsolutePaceBoostSpeedUpCeiling)
            : null;
        // A boost past what the compression ceiling can realise is not an error — it just stops
        // buying anything, and silently, because every downstream number clamps. Say so on the job
        // rather than leaving an operator to wonder why +60 read the same as +48.
        if (context.PaceBoostWpm > 0)
        {
            double saturating = WordTargetCalculator.SaturatingPaceBoostWpm(
                calibratedWpm, _durationOptions.AbsolutePaceBoostSpeedUpCeiling);
            if (saturating > 0 && context.PaceBoostWpm > saturating + 0.5)
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Pace boost +{context.PaceBoostWpm:0} exceeds what this voice can realise: at "
                    + $"{calibratedWpm:0} wpm the compression ceiling tops out at +{saturating:0} wpm "
                    + $"({calibratedWpm * _durationOptions.AbsolutePaceBoostSpeedUpCeiling:0} wpm on air). "
                    + "Reading at the ceiling.",
                    cancellationToken);
        }

        // What the AIM ceiling above permits is exactly what the boost needs, leaving a take that
        // lands on target zero room for eleven_v3's render-to-render duration spread (measured ±12%
        // at a fixed word count on dev job a1d40b4c). The fitter therefore gets its own, slightly
        // wider cap — the target is unchanged, the extra slack only absorbs variance instead of
        // spending a re-render on it.
        double? fitterMaxSpeedUpFactor = context.PaceBoostWpm > 0
            ? WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(
                calibratedWpm, context.PaceBoostWpm, resolvedMaxSpeedUpFactor,
                _durationOptions.AbsolutePaceBoostSpeedUpCeiling)
            : null;

        // A pace boost widens the speed-up ceiling; grant half of that widening back as slow-down
        // headroom so the acceptance band stays roughly symmetric. Without it the band is narrower
        // than one sentence for a boosted voice, and the measure-then-trim fitter's only escape from
        // an overrun is to step one sentence too far and undershoot the floor — which costs a whole
        // extra generation pass (verified live: job b6e03354 trimmed 102 -> 63 words -> 29.3s
        // against a 30.0s floor). Unboosted voices get the configured value unchanged.
        double? effectiveMaxSlowDownFactor = context.PaceBoostWpm > 0
            ? WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(
                calibratedWpm, context.PaceBoostWpm, resolvedMaxSlowDownFactor,
                _durationOptions.AbsolutePaceBoostSlowDownCeiling)
            : null;
        int maxAttempts = workBudget.MaxGenerationAttempts;
        int slotWordTarget = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(
            calibratedWpm, bodyTargetSeconds, settings.Speed, context.PaceBoostWpm);
        int aimWordTarget = context.PaceBoostWpm > 0
            ? WordTargetCalculator.ComputeBoostedWordTarget(calibratedWpm, bodyTargetSeconds,
                settings.Speed, effectiveMaxSpeedUpFactor!.Value, effectiveMaxSlowDownFactor!.Value)
            : WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(
                calibratedWpm,
                WordTargetCalculator.MidpointAimSeconds(bodyTargetSeconds, resolvedMaxSpeedUpFactor),
                settings.Speed, context.PaceBoostWpm);
        int baseWordTarget = context.TargetWordCountOverride.GetValueOr(aimWordTarget);
        if (context.PaceBoostWpm <= 0 && !context.TargetWordCountOverride.HasValue)
        {
            double ceilingSeconds = bodyTargetSeconds * resolvedMaxSpeedUpFactor;
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Word aim: {aimWordTarget} words for the {bodyTargetSeconds:F1}s body, midpoint of the "
                + $"{bodyTargetSeconds:F1}s-{ceilingSeconds:F1}s fitter band ({slotWordTarget} words at the slot).",
                cancellationToken);
        }
        int currentTargetWords = Math.Max(baseWordTarget, 10);
        var originalRejectionFeedback = context.RejectionFeedback;
        var currentFeedback = context.RejectionFeedback;
        var currentRetryContext = Option<string>.None;
        var lastResult = Result<(string script, string taggedScript, byte[] audio, IReadOnlyList<WordTiming> wordTimings)>.Failure(
            "No generation attempts executed.");
        double lastSpeed = 0.0;
        bool hadContentLimitedUnderrun = false;
        double bestUnderrunDuration = 0;
        string? bestUnderrunScript = null;
        IReadOnlyList<DraftedSentence>? bestUnderrunSentences = null;

        GenerationCandidate? bestCandidate = null;

        void RememberCandidate(GenerationAttemptOutcome outcome)
        {
            GenerationCandidate? candidate = null;
            if (outcome.Result.IsSuccess)
            {
                var value = outcome.Result.Value;
                if (!string.IsNullOrWhiteSpace(value.script))
                    candidate = new GenerationCandidate(
                        value.script, value.taggedScript, value.audio, value.wordTimings,
                        outcome.DurationSeconds);
            }
            else
            {
                string script = string.IsNullOrWhiteSpace(outcome.Script)
                    ? outcome.ValidatedScript
                    : outcome.Script;
                if (!string.IsNullOrWhiteSpace(script))
                    candidate = new GenerationCandidate(
                        script, string.Empty, [], [], outcome.DurationSeconds);
            }

            if (candidate is null)
                return;

            bool candidateHasAudio = candidate.Audio.Length > 0;
            bool existingHasAudio = bestCandidate?.Audio.Length > 0;
            bool replace = bestCandidate is null
                || (candidateHasAudio && !existingHasAudio)
                || (candidateHasAudio == existingHasAudio
                    && candidate.DurationSeconds > bestCandidate.DurationSeconds);
            if (replace)
            {
                if (bestCandidate is not null
                    && !ReferenceEquals(candidate.Audio, bestCandidate.Audio))
                    bestCandidate.Dispose();
                bestCandidate = candidate;
            }
            else if (!ReferenceEquals(candidate.Audio, bestCandidate?.Audio))
            {
                candidate.Dispose();
            }
        }

        // Single exit for the whole run, and therefore the ONE place the retry semantics of a
        // grounding failure are allowed to end (2026-08-23). A grounding failure is Retryable while
        // the attempt budget lasts — a fresh draft may simply not carry the defect — but Retryable
        // must never escape this engine: QueueFailurePolicy would redeliver the entire job, and
        // BroadcastOrchestrator's cover branch only fires for ContentLimited/SafetyRejected/
        // Permanent, so the slot would sit uncovered until the watchdog swept it. That is precisely
        // the asymmetry the 2026-07-26 TEST load burst exposed.
        //
        // Converting here rather than at each terminal return is deliberate: the loop below has
        // several exits and every one of them must pass through this conversion. An earlier version
        // converted only one tail and shipped Retryable straight to the orchestrator from another —
        // caught by
        // BroadcastOrchestratorTests.GroundingGateExhausted_NoFallbackAvailable_CarriesPermanentDisposition,
        // which exists for exactly this reason. One exit, one conversion.
        async Task<GenerationRunOutcome> Outcome(Result<(string script, string taggedScript, byte[] audio,
            IReadOnlyList<WordTiming> wordTimings)> result)
        {
            // ── Content safety: ONCE, on the take that can actually air ──
            // This used to run inside every generation attempt, so a job that re-fitted three times
            // paid three AI safety reviews at ~9s each and threw two of them away with the drafts
            // they judged (dev brisbane aafc505a, 2026-08-26: safety=4, 156s wall). The filter is a
            // pure GATE - every success path returns the script unchanged - so nothing downstream
            // depends on it having run early, and a block is rare enough that paying for synthesis
            // first is the cheaper expectation. Deliberately inside the single exit: the 2026-08-23
            // note above records why every path must funnel through here.
            if (result.IsSuccess)
            {
                var safety = await scriptReviewLoop.CheckContentSafetyAsync(
                    context.JobId, context.MarketId, result.Value.script, cancellationToken);
                if (safety.IsFailure)
                {
                    if (progress is not null)
                        await progress.Failed(safety.Error);
                    return new GenerationRunOutcome(
                        safety.PropagateFailure<(string script, string taggedScript, byte[] audio,
                            IReadOnlyList<WordTiming> wordTimings)>(),
                        bestCandidate);
                }

            }

            if (result.IsFailure)
            {
                if (progress is not null)
                    await progress.Failed(result.Error);

                // Any failure escaping the engine after exhausting all generation attempts is permanent
                // for this job run. Returning Permanent ensures BroadcastOrchestrator immediately covers
                // the slot with standby fallback in-process, preventing a 5-minute queue retry timeout.
                string code = result.FailureMetadata.HasValue && !string.IsNullOrWhiteSpace(result.FailureMetadata.Value.Code)
                    ? result.FailureMetadata.Value.Code
                    : "broadcast.generation_exhausted";
                var permanentFailure = result.FailureMetadata.HasValue
                    && result.FailureMetadata.Value.Disposition is FailureDisposition.Permanent
                    or FailureDisposition.ContentLimited
                    or FailureDisposition.SafetyRejected
                    ? result
                    : Result<(string script, string taggedScript, byte[] audio, IReadOnlyList<WordTiming> wordTimings)>
                        .Failure(OperationalFailure.Permanent(code, result.Error));

                return new GenerationRunOutcome(permanentFailure, bestCandidate);
            }
            return new GenerationRunOutcome(result, bestCandidate);
        }


        // ── Duration-fit loop ──
        // Draft once, synthesise, measure. A measured miss continues the composer's own thread with
        // the shortfall or overrun (ReviseForMeasuredDurationAsync); anything else regenerates fresh
        // with the word target moved toward what the last render measured.
        int maxAdjust = (int)Math.Round(baseWordTarget * _options.MaxWordCountAdjustmentPercent / 100.0);
        int contentTargetWords = currentTargetWords;
        var contentFeedback = currentFeedback;
        var contentRetryContext = currentRetryContext;
        DraftedScript? pendingDurationRevision = null;

        // Starts neutral and tightens as renders land: the pooled model's estimate for this
        // voice, corrected by what this job's own attempts measured.
        double durationCorrection = 1.0;

        int attempt = 0;
        while (attempt < maxAttempts && workBudget.TryStartGenerationAttempt(out attempt))
        {
            var attemptContext = context with
            {
                TargetWordCountOverride = Option<int>.Some(contentTargetWords),
                RejectionFeedback = contentFeedback,
                RetryContext = contentRetryContext,
                ProgressReporter = progress
            };

            await jobStatusUpdater.LogJobAsync(
                context.JobId,
                $"Generation attempt {attempt}/{maxAttempts}: target {contentTargetWords} words...",
                cancellationToken);
            if (progress is not null)
                await progress.AttemptStarted(
                    attempt,
                    maxAttempts,
                    $"Generating around {contentTargetWords} words.");

            DraftedScript? draftedForAttempt = pendingDurationRevision;
            pendingDurationRevision = null;
            var attemptResult = await GenerateAndSynthesizeAsync(
                attemptContext, calibratedSps, calibratedSecondsPerWord, settings, lastSpeed,
                fitterMaxSpeedUpFactor,
                effectiveMaxSlowDownFactor,
                resolvedMaxSpeedUpFactor,
                resolvedMaxSlowDownFactor,
                resolvedMaxSilencePadSeconds,
                slotWordTarget,
                draftedForAttempt,
                durationCorrection,
                workBudget,
                cancellationToken,
                progress);
            lastResult = attemptResult.Result;
            RememberCandidate(attemptResult);
            if (attemptResult.Result.IsFailure && progress is not null)
                await progress.AttemptFailed(attemptResult.Result.Error);

            // This render measured the pace the ceiling had only predicted. Fold it in so the
            // next attempt trims against what the voice does rather than against the estimate
            // that just proved wrong — the difference between 6c3ebefb's four renders and one.
            if (attemptResult.PredictedSecondsPerWord > 0
                && attemptResult.DurationSeconds > 0
                && attemptResult.WordCount > 0)
            {
                double measuredSecondsPerWord = attemptResult.DurationSeconds / attemptResult.WordCount;
                double folded = SpokenDurationCorrection.Fold(
                    durationCorrection, attemptResult.PredictedSecondsPerWord, measuredSecondsPerWord);
                if (Math.Abs(folded - durationCorrection) > 0.01)
                    logger.LogInformation(
                        "[Ceiling] Job {JobId} attempt {Attempt} measured {Measured:F3}s/word against a "
                        + "predicted {Predicted:F3}s/word — correction {Before:F2}x -> {After:F2}x.",
                        context.JobId, attempt, measuredSecondsPerWord,
                        attemptResult.PredictedSecondsPerWord, durationCorrection, folded);
                durationCorrection = folded;
            }

            if (attemptResult.FinalSpeed > 0)
                lastSpeed = attemptResult.FinalSpeed;

            if (!string.IsNullOrWhiteSpace(attemptResult.Script) && attemptResult.DurationSeconds > 0)
                await voiceCalibrationService.RecordActualDurationAsync(
                    voiceId, settings, attemptResult.Script, attemptResult.DurationSeconds,
                    cancellationToken, context.Model);

            // Same lossless capture as the outer attempt path below. BOTH sites are needed: this
            // inner path is the one a first-attempt success takes, so instrumenting only the
            // outer one recorded nothing for every healthy bulletin.
            if (durationSamples is not null
                && !string.IsNullOrWhiteSpace(attemptResult.Script)
                && attemptResult.DurationSeconds > 0)
                await RecordDurationSampleAsync(
                    context, voiceId, settings, attemptResult, cancellationToken);

            if (attemptResult.Result.IsSuccess && !attemptResult.IsContentLimitedUnderrun)
                return await Outcome(lastResult);

            if (attemptResult.Result.IsSuccess && attemptResult.IsContentLimitedUnderrun)
            {
                hadContentLimitedUnderrun = true;
                string underrunScript = string.IsNullOrWhiteSpace(attemptResult.SpokenScript)
                    ? attemptResult.ValidatedScript
                    : attemptResult.SpokenScript;
                if (attemptResult.DurationSeconds < context.TargetDurationSeconds
                    && attemptResult.DurationSeconds > bestUnderrunDuration
                    && !string.IsNullOrWhiteSpace(underrunScript))
                {
                    bestUnderrunDuration = attemptResult.DurationSeconds;
                    bestUnderrunScript = underrunScript;
                    bestUnderrunSentences = attemptResult.DraftedSentences;
                }
            }

            // A measured synthesis miss is feedback about THIS composed read. When the workflow
            // returned a composer response id, keep the correction in that conversation instead
            // of paying for a fresh writer/composer pass that has forgotten the selected stories
            // and exact wording. If continuation itself fails, the existing fresh-regeneration
            // path immediately below remains the fallback.
            bool isMeasuredDurationMiss = attemptResult.IsContentLimitedUnderrun
                || (attemptResult.Result.IsFailure
                    && attemptResult.Result.FailureMetadata.HasValue
                    && string.Equals(
                        attemptResult.Result.FailureMetadata.Value.Code,
                        "duration.mismatch",
                        StringComparison.Ordinal));
            if (isMeasuredDurationMiss
                && attemptResult.WasNaturalSynthesis
                && attemptResult.DurationSeconds > 0
                && !string.IsNullOrWhiteSpace(attemptResult.ComposerThreadId)
                && attempt < maxAttempts)
            {
                // The composer's conversation is about the BODY: no bookends, no filler, against
                // the body slot (WorkflowRequest.TargetSeconds = slot minus bookendSeconds).
                // Handing it the final read and the final read's seconds told it a 20.9s read that
                // was 1.1s SHORT of a 22s slot was "24.1s against a 19.4s slot", and it deleted the
                // only traffic sentence to fix an overrun that did not exist (dev daf615b5,
                // 2026-09-02). Subtract the same bookend cost the body slot was derived from, so
                // the composer sees exactly the shortfall or overrun the engine measured.
                // The read handed to the composer and the seconds handed with it must describe the
                // same audio. ValidatedScript is the pre-synthesis text and is wrong whenever the
                // fitter trimmed, so prefer the plain, filler-free SpokenScript take.
                string body = BulletinBookendComposer.StripBookendEdges(
                    !string.IsNullOrWhiteSpace(attemptResult.SpokenScript)
                        ? attemptResult.SpokenScript
                        : string.IsNullOrWhiteSpace(attemptResult.ValidatedScript)
                            ? attemptResult.Script
                            : attemptResult.ValidatedScript,
                    context.Bookends);
                double bodySeconds = Math.Max(0.1, attemptResult.DurationSeconds - bookendSeconds);
                var revised = await scriptGenerator.ReviseForMeasuredDurationAsync(
                    attemptContext,
                    attemptResult.ComposerThreadId,
                    body,
                    bodySeconds,
                    calibratedSps,
                    settings,
                    cancellationToken);
                if (revised.IsSuccess)
                {
                    pendingDurationRevision = revised.Value;
                    contentFeedback = originalRejectionFeedback;
                    continue;
                }

                logger.LogWarning(
                    "[Generation] Job {JobId} could not continue composer thread {ThreadId}: {Error}; "
                    + "falling back to fresh generation.",
                    context.JobId, attemptResult.ComposerThreadId, revised.Error);
            }

            if (attemptResult.Result.IsFailure
                && attemptResult.Result.FailureMetadata.HasValue
                && attemptResult.Result.FailureMetadata.Value.Disposition == FailureDisposition.Permanent)
                break;
            if (attempt == maxAttempts)
                break;

            // Fresh regen: move the word target toward the ideal.
            if (attemptResult.DurationSeconds > 0)
            {
                double target = context.TargetDurationSeconds;
                int prior = attemptResult.WordCount > 0 ? attemptResult.WordCount : contentTargetWords;
                int raw = (int)Math.Round(prior * target / attemptResult.DurationSeconds);
                int stepClamped = Math.Clamp(raw, contentTargetWords - _options.MaxPerRetryWordShift,
                    contentTargetWords + _options.MaxPerRetryWordShift);
                contentTargetWords = Math.Clamp(stepClamped, Math.Max(10, baseWordTarget - maxAdjust),
                    baseWordTarget + maxAdjust);
                string direction = attemptResult.DurationSeconds > target ? "shorter" : "longer";
                contentRetryContext = Option<string>.Some(
                    $"The previous script produced {attemptResult.DurationSeconds:F1}s audio; target is {target:F0}s. Make this version {direction} and land around {contentTargetWords} words.");
            }
            else if (attemptResult.WordCount > 0 && attemptResult.WordCount != contentTargetWords)
            {
                int damped = (int)Math.Round(contentTargetWords + _options.DampingFactor * (attemptResult.WordCount - contentTargetWords));
                contentTargetWords = Math.Clamp(damped, Math.Max(10, baseWordTarget / 2), baseWordTarget + maxAdjust);
                contentRetryContext = Option<string>.Some(
                    $"The previous attempt failed quality/synthesis checks. Regenerate cleanly and land around {contentTargetWords} words.");
            }
            else
            {
                contentRetryContext = Option<string>.Some(
                    $"The previous attempt failed quality/synthesis checks. Regenerate cleanly and land around {contentTargetWords} words.");
            }
            contentFeedback = originalRejectionFeedback;
        }


        if (hadContentLimitedUnderrun)
        {
            logger.LogWarning(
                "[Generation] Job {JobId} could not fill the {Target}s slot (best spoken {Spoken:F1}s) after {Attempts} attempts (decoupled) — failing to fallback.",
                context.JobId, context.TargetDurationSeconds, bestUnderrunDuration, maxAttempts);
            return new GenerationRunOutcome(
                Result<(string script, string taggedScript, byte[] audio, IReadOnlyList<WordTiming> wordTimings)>.Failure(
                    OperationalFailure.ContentLimited("broadcast.content_limited",
                        ErrorMessages.Broadcast.ContentLimited(bestUnderrunDuration, context.TargetDurationSeconds))),
                bestCandidate);
        }

        return await Outcome(lastResult);
    }
    internal async Task<GenerationAttemptOutcome> GenerateAndSynthesizeAsync(
        BroadcastWithTraffic context,
        double syllablesPerSecond,
        double secondsPerWord,
        VoiceSettings settings,
        double speedHint,
        double? maxSpeedUpFactorOverride,
        double? maxSlowDownFactorOverride,
        double resolvedMaxSpeedUpFactor,
        double resolvedMaxSlowDownFactor,
        double resolvedMaxSilencePadSeconds,
        int slotWordTarget,
        DraftedScript? preDrafted,
        double durationCorrection,
        BroadcastWorkBudget workBudget,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress = null)
    {
        using var budgetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, workBudget.Token);
        cancellationToken = budgetCancellation.Token;
        workBudget.ThrowIfCancellationRequested();
        // acted on, which the loop compares against what the render measured.
        double predictedSecondsPerWordUsed = 0;
        double effectiveSpeedUpFactor = maxSpeedUpFactorOverride ?? resolvedMaxSpeedUpFactor;
        double effectiveSlowDownFactor = maxSlowDownFactorOverride ?? resolvedMaxSlowDownFactor;
        // Handed ONWARD to the audio pipeline, which falls back to its own configured option when the
        // argument is null. These must therefore stay NULLABLE: collapsing them into the resolved
        // double would send an explicit value on every call, so a station that has configured nothing
        // would no longer make the same call it made before the console existed. Same resulting number,
        // different contract at the boundary — and 128 Application tests are pinned to the old one,
        // which is exactly the regression that caught this (2026-08-24). The non-nullable pair above
        // stays for the internal fit arithmetic, which has no "unset" case to preserve.
        double? pipelineSpeedUpOverride = maxSpeedUpFactorOverride ?? context.EditorialSettings.MaxSpeedUpFactor;
        double? pipelineSlowDownOverride = maxSlowDownFactorOverride ?? context.EditorialSettings.MaxSlowDownFactor;
        string script;
        string synthScript;
        IReadOnlyList<DraftedSentence>? draftedSentences = null;
        IReadOnlyList<DraftedSentence>? alignmentFallbackSentences = null;
        int scriptWordCount = 0;
        string? composerThreadId = preDrafted?.ComposerThreadId;
        // ── Generate Script ──
        // Write/Compose/FitWords/Edit/Gates envelopes come from the workflow itself
        // (GenerationProgressTraceRelay); wrapping them again here produced a second, empty
        // Write→Edit→Fit words→Gates set that said "gates passed" after the workflow's had.
        var scriptSw = Stopwatch.StartNew();
        bool continuedComposerThread = preDrafted is not null;
        Result<DraftedScript> scriptResult = continuedComposerThread
            ? Result<DraftedScript>.Success(preDrafted!)
            : await scriptGenerator.GenerateScriptAsync(
                context, syllablesPerSecond, settings, cancellationToken);
        scriptSw.Stop();
        metrics.TrackPipelineStage(
            continuedComposerThread ? "script_duration_revise" : "script_generate",
            scriptSw.Elapsed.TotalSeconds,
            context.Location,
            scriptResult.IsSuccess);

        logger.LogInformation(
            continuedComposerThread
                ? "[ScriptGenerate] Job {JobId} composer duration revision entered validation in {ElapsedMs}ms — Success={IsSuccess}"
                : "[ScriptGenerate] Job {JobId} script generation completed in {ElapsedMs}ms — Success={IsSuccess}",
            context.JobId, scriptSw.ElapsedMilliseconds, scriptResult.IsSuccess);
        await jobStatusUpdater.LogJobAsync(context.JobId, scriptResult.IsSuccess
            ? continuedComposerThread
                ? $"Composer duration revision ready in {scriptSw.ElapsedMilliseconds}ms"
                : $"Script generated in {scriptSw.ElapsedMilliseconds}ms"
            : $"Script generation failed: {scriptResult.Error}", cancellationToken);

        if (scriptResult.IsFailure)
        {
            logger.LogWarning(
                "[ScriptGenerate] Job {JobId} script generation FAILED — {Error}",
                context.JobId, scriptResult.Error);
            return new GenerationAttemptOutcome(
                scriptResult.PropagateFailure<(string, string, byte[], IReadOnlyList<WordTiming>)>(),
                0,
                string.Empty,
                0,
                0,
                ComposerThreadId: composerThreadId);
        }

        script = scriptResult.Value.Text;
        draftedSentences = scriptResult.Value.Sentences;
        composerThreadId = scriptResult.Value.ComposerThreadId;
        alignmentFallbackSentences = scriptResult.Value.Sentences;
        if (string.IsNullOrWhiteSpace(script))
        {
            logger.LogWarning("[ScriptGenerate] Job {JobId} generated an empty script", context.JobId);
            return new GenerationAttemptOutcome(
                Result<(string, string, byte[], IReadOnlyList<WordTiming>)>.Failure("Generated script was empty."),
                0,
                string.Empty,
                0,
                0);
        }

        scriptWordCount = WordCounter.Count(script);
        logger.LogInformation(
            "[ScriptGenerate] Job {JobId} script ready — WordCount={WordCount}, CharCount={CharCount}",
            context.JobId, scriptWordCount, script.Length);
        // ── Review-Refine Loop ──
        var reviewedScript = await scriptReviewLoop.ReviewAndRefineScriptAsync(
            context,
            script,
            secondsPerWord,
            cancellationToken,
            draftedSentences);
        if (reviewedScript.IsFailure)
        {
            logger.LogWarning("[ReviewRefine] Job {JobId} final style gate failed: {Error}", context.JobId,
                reviewedScript.Error);
            await jobStatusUpdater.LogJobAsync(context.JobId, $"Script quality gate failed: {reviewedScript.Error}", cancellationToken);
            return new GenerationAttemptOutcome(
                reviewedScript.PropagateFailure<(string, string, byte[], IReadOnlyList<WordTiming>)>(),
                0,
                script,
                WordCounter.Count(script),
                SyllableCounter.CountSyllables(script));
        }

        script = reviewedScript.Value.Script;
        draftedSentences = reviewedScript.Value.DraftedSentences;

        // Last mutation before synthesis, so the last place spoken forms can be reinstated as
        // numerals (2026-08-23). Idempotent, so the cost is nil and the class closes here rather
        // than one path at a time.
        script = SpokenFormNormalizer.Normalize(
            script,
            SpokenFormNormalizer.ResolveDaypart(context.TimezoneId, timeProvider.GetUtcNow()));

        var postWordValidation = TrafficScriptStyleValidator.Validate(script);
        if (!postWordValidation.IsValid)
        {
            string styleIssues = string.Join(" ", postWordValidation.Issues);
            logger.LogWarning(
                "[StyleRevalidation] Job {JobId} style re-validation after word compliance failed: {Issues}",
                context.JobId, styleIssues);
            return new GenerationAttemptOutcome(
                Result<(string, string, byte[], IReadOnlyList<WordTiming>)>.Failure(
                    $"Style re-validation after word compliance failed: {styleIssues}"),
                0, script, WordCounter.Count(script), SyllableCounter.CountSyllables(script));
        }

        // Content safety runs ONCE at the run's single exit, on the take that can air.

        // ── Delivery direction (ElevenLabs v3 audio tags) ──
        // The station's delivery style (resolved by the poller) wins; otherwise the config default
        // (neutral unless turned on). The director adds tags ONLY — it never changes the spoken
        // words (enforced by its word-immutability guard) and never blocks (fail-open to the plain).
        synthScript = await deliveryDirector.ApplyDeliveryDirectionAsync(context, script, cancellationToken);

        // Validated, pre-filler, PRE-BOOKEND read. Bookends are deliberately NOT baked in here: they
        // are re-applied below on every attempt.
        string validatedPlain = script;
        string validatedSynth = synthScript;
        // ── Station bookends ──
        // The fixed opener/closer resolved by the poller. Unconditional, ARN-authored, and never
        // seen by an LLM stage — the writer was SHOWN them in the draft prompt so the body reads
        // into them, but nothing downstream may reword or cut them.
        bool hasOutro = BulletinBookendComposer.HasOutro(context.Bookends);
        bool hasBookends = BulletinBookendComposer.HasAny(context.Bookends);

        // ── Filler top-up (ARN-authored, unreviewed) ──
        // Applied AFTER generation, review/refine, word-target compliance, safety, and delivery
        // direction — filler never passes through any LLM call, so it is never critiqued, reworded,
        // or cut. It is ARN's own editorial content; the pipeline appends it verbatim, in the
        // gap-covering order FillerStatementSelector picks, only to close whatever gap the real
        // traffic/weather/fuel content left in the slot. A station whose FillOrder omits Filler has
        // opted out of the cart entirely — both this pass and the post-synthesis rescue skip it.
        // The gap is measured against the BODY's share of the slot, not the whole slot: the
        // bookends already claimed their seconds before the writer drafted.
        bool fillerEligible = context.FillOrder.Contains(FillSource.Filler);
        double bookendSeconds = BulletinBookendComposer.EstimateSeconds(
            context.Bookends, secondsPerWord);
        double bodyTargetSeconds = Math.Max(1.0, context.TargetDurationSeconds - bookendSeconds);
        double trafficOnlyDuration = durationEstimator.EstimateDuration(script, secondsPerWord);
        double fillerGapSeconds = Math.Max(0, bodyTargetSeconds - trafficOnlyDuration);
        var selectedFiller = fillerEligible
            ? FillerStatementSelector.Select(
                context.FillerStatements, fillerGapSeconds,
                context.PreviousScript.HasValue ? context.PreviousScript.Value : null,
                wordsPerSecond: 1.0 / secondsPerWord)
            : [];

        // Filler is normally resolved to AUDIO (cached or freshly synthesized), not appended as
        // text — it joins the traffic-only synthesis by stitching after synthesis (below), so it
        // never passes through the TTS duration-fit loop and its measured duration is exact, not
        // estimated.
        //
        // A station with an OUTRO is the exception: stitching would land the filler after the
        // closer, so the bulletin would announce it was over and then keep talking. There the
        // filler text goes INLINE into this take, before the outro, and is spoken in one
        // continuous read. The cost is real and deliberate (2026-08-04 decision): inline filler
        // is chosen against an ESTIMATED gap and rides the duration-fit loop, forfeiting both the
        // exact-duration guarantee and the post-synthesis rescue below.
        bool fillerInline = hasOutro && selectedFiller.Count > 0;
        IReadOnlyList<string> inlineFiller = [];
        var resolvedFiller = new List<(FillerStatement Statement, ResolvedFillerAudio Audio)>();
        if (fillerInline)
        {
            inlineFiller = [.. selectedFiller.Select(s => s.Text)];
        }
        else if (selectedFiller.Count > 0)
        {
            string fillerVoiceId = context.VoiceId.GetValueOr(_options.DefaultVoiceId);
            var fillerVoiceSettings = context.VoiceSettings.GetValueOr(VoiceSettings.Default);
            // Matches ModelSelectionOptions.BroadcastModel's default; schedule-driven jobs always
            // resolve a concrete model before this point, so this branch is edge-case-only
            // (ad-hoc jobs that omit VoiceModel).
            string fillerModel = context.Model.GetValueOr("eleven_v3");
            foreach (var statement in selectedFiller)
            {
                var resolved = await fillerAudioCacheService.ResolveAsync(
                    statement, fillerVoiceId, fillerVoiceSettings, fillerModel, cancellationToken);
                if (resolved.IsSuccess)
                    resolvedFiller.Add((statement, resolved.Value));
                else
                    logger.LogWarning(
                        "[Filler] Job {JobId} could not resolve audio for filler statement {StatementId}: {Error}",
                        context.JobId, statement.Id, resolved.Error);
            }
            if (resolvedFiller.Count > 0)
                script = BulletinBookendComposer.JoinBodyAndFollowing(
                    script,
                    resolvedFiller.Select(p => p.Statement.Text));
        }

        // Wrap the read when bookends are configured. All body/filler seams use the same
        // punctuation-aware join helper, including the no-bookend path above.
        if (hasBookends)
        {
            script = BulletinBookendComposer.Compose(context.Bookends, script, inlineFiller);
            synthScript = BulletinBookendComposer.Compose(context.Bookends, synthScript, inlineFiller);
        }

        scriptWordCount = WordCounter.Count(script);
        int scriptSyllableCount = SyllableCounter.CountSyllables(script);

        if (context.ScriptOnly)
        {
            await PersistScriptOnJob(context, script, synthScript, draftedSentences, alignmentFallbackSentences, cancellationToken);
            return new GenerationAttemptOutcome(
                Result<(string, string, byte[], IReadOnlyList<WordTiming>)>.Success((script, synthScript, Array.Empty<byte>(), [])),
                0,
                script,
                scriptWordCount,
                scriptSyllableCount);
        }

        // ── Synthesize Audio ──
        // ── Phase 2: predictive fit gate ──
        // Predict the read's natural duration BEFORE synthesis and drop trailing content that
        // confidently overruns the correctable window: free string work instead of a discarded
        // eleven_v3 render (measured 13-16s each for a ~600-char bulletin). A predicted UNDERRUN is
        // not acted on here: the pipeline cannot lengthen a read, so the take is synthesised,
        // measured, and the composer is asked to revise against the measured shortfall.
        if (durationPredictor is not null && !context.ScriptOnly && selectedFiller.Count == 0)
        {
            var predictionContext = new PredictionContext
            {
                VoiceId = context.VoiceId.GetValueOr(_options.DefaultVoiceId),
                ModelId = context.Model.GetValueOr(string.Empty),
                Speed = context.VoiceSettings.GetValueOr(VoiceSettings.Default).Speed
            };
            var prediction = durationPredictor.PredictDuration(DeliveryTags.Strip(synthScript), predictionContext);
            if (prediction.HasCalibratedData)
            {
                var plan = DurationController.Plan(
                    prediction.ConfidenceLower, prediction.ConfidenceUpper, context.TargetDurationSeconds,
                    effectiveSlowDownFactor,
                    effectiveSpeedUpFactor,
                    hasFiller: false, canExtend: false, canTrim: true);
                if (plan == FitPlan.TrimBeforeSynthesis)
                {
                    // Identical arithmetic to the POST-synthesis trim in AudioPipelineService
                    // (CASE 3) so a trim decided before synthesis and one decided after it can never
                    // disagree about what fits or about protecting the weather/fuel close.
                    double effSpeedUp = effectiveSpeedUpFactor;
                    double overrunAllowance = Math.Max(
                        0.0, (context.TargetDurationSeconds * effSpeedUp) - context.TargetDurationSeconds - 0.75);
                    // DraftedSentence variants are available here only when the composed read still
                    // matches the body sentence list (bookends/filler add locked sentences). Reuse
                    // the existing ceiling selector rather than inventing a second variant policy;
                    // it chooses the longest phrasing that fits the derived duration ceiling.
                    // Spans are built AFTER variant selection below, because selecting shorter
                    // phrasings rewrites the script: computing them here left the fitter reasoning
                    // about the pre-variant sentences and then REPLACING the variant-selected script
                    // with a subset of the originals, discarding the variant work outright.
                    if (draftedSentences is { Count: > 0 })
                    {
                        int currentWords = WordCounter.Count(script);
                        int durationCeiling = Math.Max(1, (int)Math.Floor(
                            currentWords * (context.TargetDurationSeconds + overrunAllowance)
                            / Math.Max(0.1, prediction.ExpectedDuration)));
                        var stationLines = StationLinesFor(context);

                        var variantFit = ScriptReviewLoop.SelectCeilingVariants(
                            script, draftedSentences, durationCeiling, stationLines);
                        if (variantFit is not null)
                        {
                            script = variantFit.Value.Script;
                            // If synthScript already had tags applied (e.g. leading [tag]), carry the leading tag over
                            // rather than firing a whole second LLM director round-trip.
                            var existingTags = DeliveryTags.Extract(synthScript);
                            if (existingTags.Count > 0 && synthScript.TrimStart().StartsWith(existingTags[0], StringComparison.Ordinal))
                            {
                                synthScript = $"{existingTags[0]} {script}";
                            }
                            else
                            {
                                synthScript = await deliveryDirector.ApplyDeliveryDirectionAsync(
                                    context, script, cancellationToken);
                            }
                            draftedSentences = variantFit.Value.Sentences;
                            logger.LogInformation(
                                "[PredictiveFit] Job {JobId} selected shorter authored variants before sentence removal ({Before}->{After} words).",
                                context.JobId, currentWords, WordCounter.Count(script));
                        }
                    }

                    // One whole-script prediction for whatever the script is NOW (variants may have
                    // rewritten it), then per-sentence shares rescaled to match it. PredictDuration
                    // is a per-utterance model: called once per sentence its fixed per-read constant
                    // is charged N times, so the raw span sum sits far above this single figure and
                    // fitting to the budget cut roughly twice what the overrun required.
                    double wholeScriptSeconds = durationPredictor
                        .PredictDuration(DeliveryTags.Strip(synthScript), predictionContext).ExpectedDuration;
                    var spans = BulletinDurationFitter.RescaleTo(
                        BulletinDurationFitter.EstimateSentenceSpans(
                            synthScript,
                            sentence => durationPredictor
                                .PredictDuration(DeliveryTags.Strip(sentence), predictionContext).ExpectedDuration),
                        wholeScriptSeconds);
                    string? trimmed = BulletinDurationFitter.FitByTrimming(
                        spans, context.TargetDurationSeconds, overrunAllowance,
                        lockedSuffix: BulletinBookendComposer.OutroOf(context.Bookends),
                        fixtures: FixtureLines.From(draftedSentences, FixturesRequired(context)),
                        lockedPrefix: BulletinBookendComposer.LockedIntroOf(context.Bookends));

                    if (!string.IsNullOrWhiteSpace(trimmed)
                        && !string.Equals(trimmed, synthScript, StringComparison.Ordinal))
                    {
                        int wordsBefore = scriptWordCount;
                        synthScript = trimmed;
                        script = DeliveryTags.Strip(trimmed);
                        scriptWordCount = WordCounter.Count(script);
                        scriptSyllableCount = SyllableCounter.CountSyllables(script);
                        // Any later extend/retry must measure against what was ACTUALLY synthesized,
                        // not the pre-trim script — mixing the two is the defect that cost job
                        // b6e03354 a whole extra generation pass.
                        validatedPlain = script;
                        validatedSynth = synthScript;

                        logger.LogInformation(
                            "[PredictiveFit] Job {JobId} predicted natural {Expected:F1}s (p-lo {Lower:F1}s) overruns the {Target}s slot's {Factor:F2}x compression budget — trimmed {Before}->{After} words before synthesis.",
                            context.JobId, prediction.ExpectedDuration, prediction.ConfidenceLower,
                            context.TargetDurationSeconds, effSpeedUp, wordsBefore, scriptWordCount);
                        await jobStatusUpdater.LogJobAsync(context.JobId,
                            $"Predicted {prediction.ExpectedDuration:F1}s overruns the {context.TargetDurationSeconds:F0}s slot — "
                            + $"trimmed {wordsBefore} -> {scriptWordCount} words before synthesis (no wasted TTS); "
                            + "the weather/fuel close is kept where it fits.",
                            cancellationToken);
                        metrics.TrackPipelineStage("predictive_fit_trim", 0, context.Location, true);
                    }
                    else
                    {
                        logger.LogInformation(
                            "[PredictiveFit] Job {JobId} predicted an overrun but nothing was droppable — synthesizing and measuring.",
                            context.JobId);
                    }
                }
            }
        }

        // ── Ship-time word ceiling ──
        // This is deliberately at the synthesis choke point, after bookends, inline filler, and
        // delivery direction have been composed. Predict the exact text sent to ElevenLabs (tags
        // included so the calibrated model can account for their prosodic cost); word counts and
        // the deterministic trim operate on the tag-free spoken read.
        string voicedSynthScript = DeliveryTags.Strip(synthScript);
        int voicedWordCount = WordCounter.Count(voicedSynthScript);
        // Correct the model with what THIS job already measured. Every earlier attempt rendered
        // this voice reading this copy, which is better evidence about its pace than the pooled
        // model, and the loop used to discard it: the calibrated rate is read once before the first
        // attempt (line 74) and never revisited, so job 6c3ebefb predicted 27.7s, 26.1s and 26.7s
        // for reads that measured 19.3s, 19.3s and 18.5s, trimmed each one into an underrun, and
        // spent four renders and 95 seconds shipping the words attempt 1 was already holding.
        var rawPrediction = spokenDurationModel?.Predict(
            synthScript,
            new DurationVoiceKey(
                context.VoiceId.GetValueOr(_options.DefaultVoiceId),
                context.Model.GetValueOr("eleven_v3"),
                context.VoiceSettings.GetValueOr(VoiceSettings.Default).Speed,
                context.PaceBoostWpm)) ?? default;
        var correctedPrediction = rawPrediction.HasValue && durationCorrection is > 0 and not 1.0
            ? Option<PredictedDuration>.Some(new PredictedDuration(
                rawPrediction.Value.Seconds * durationCorrection,
                rawPrediction.Value.ResidualSdSec * durationCorrection,
                rawPrediction.Value.MatchTier))
            : rawPrediction;
        if (correctedPrediction.HasValue && durationCorrection is > 0 and not 1.0)
            logger.LogInformation(
                "[Ceiling] Job {JobId} corrected the predicted read by {Factor:F2}x from this job's own "
                + "measured pace: {Raw:F1}s -> {Corrected:F1}s.",
                context.JobId, durationCorrection, rawPrediction.Value.Seconds,
                correctedPrediction.Value.Seconds);

        // The rate the ceiling actually decided on, handed back so the next attempt can measure
        // against it rather than against a number no one kept.
        predictedSecondsPerWordUsed = correctedPrediction.HasValue && voicedWordCount > 0
            ? correctedPrediction.Value.Seconds / voicedWordCount
            : 0;

        var ceilingChoice = ScriptReviewLoop.ResolveCeilingTarget(
            Option<int>.Some(slotWordTarget),
            context.TargetDurationSeconds,
            effectiveSpeedUpFactor,
            resolvedMaxSilencePadSeconds,
            resolvedMaxSlowDownFactor,
            secondsPerWord,
            voicedWordCount,
            correctedPrediction);
        int shipTargetWords = ceilingChoice.TargetWords;
        await jobStatusUpdater.LogJobAsync(context.JobId,
            ceilingChoice.Path is "legacy-150wpm" or "attempt-target"
                    ? $"Word ceiling ({ceilingChoice.Path}): {shipTargetWords} target words "
                      + $"({ScriptWordCeiling.For(shipTargetWords)}-word ceiling) for the "
                      + $"{context.TargetDurationSeconds:F0}s slot"
                      + (ceilingChoice.Path == "attempt-target" ? " (no calibrated voice prediction)." : ".")
                    : $"Word ceiling ({ceilingChoice.Path}): predicted {ceilingChoice.PredictedSeconds:F1}s "
                      + $"against a {ceilingChoice.AllowanceSeconds:F1}s allowance for the "
                      + $"{context.TargetDurationSeconds:F0}s slot; {shipTargetWords} target words "
                      + $"({ScriptWordCeiling.For(shipTargetWords)}-word ceiling).",
            cancellationToken);
        if (ScriptWordCeiling.Excess(voicedSynthScript, shipTargetWords) > 0)
        {
            var stationLines = StationLinesFor(context);

            var variantFit = ScriptReviewLoop.SelectCeilingVariants(
                voicedSynthScript, draftedSentences, ScriptWordCeiling.For(shipTargetWords), stationLines);
            if (variantFit is not null)
            {
                int before = WordCounter.Count(voicedSynthScript);
                voicedSynthScript = variantFit.Value.Script;
                synthScript = voicedSynthScript;
                script = voicedSynthScript;
                draftedSentences = variantFit.Value.Sentences;
                logger.LogInformation(
                    "[AudioSynthesize] Job {JobId} selected shorter sentence variants {Before}->{After} before word-ceiling dropping.",
                    context.JobId, before, WordCounter.Count(voicedSynthScript));
            }
            if (ScriptWordCeiling.Excess(voicedSynthScript, shipTargetWords) > 0)
            {
                int before = WordCounter.Count(voicedSynthScript);
                // The station's bookends are NOT the trim's to spend. ScriptWordCeiling.TrimToCeiling
                // drops TRAILING sentences and has no concept of a locked prefix or suffix — only of
                // required weather/fuel fixtures — so the closer, being last, was always the first
                // thing cut. Its sibling on the predictive-fit path (BulletinDurationFitter
                // .FitByTrimming) has protected lockedPrefix/lockedSuffix all along; the two trims
                // simply disagreed about what is untouchable, and this is the one that runs at the
                // synthesis choke point. ARN, 2026-08-24: three consecutive reads shipped with the
                // KIIS closer missing, every one of them on a `prosody-trim` attempt.
                //
                // Peel the station lines, trim only the BODY against a ceiling reduced by what they
                // cost, then put them back — the same split SelectCeilingVariants already uses above,
                // so both steps agree on what a station line is.
                var split = StationLineSplitter.Split(voicedSynthScript, stationLines);
                bool preserved = split.PreservedLineCount > 0;
                string bodyToTrim = preserved ? split.Body : voicedSynthScript;
                int stationWords = preserved
                    ? split.Leading.Concat(split.Trailing).Sum(WordCounter.Count)
                    : 0;
                int bodyCeiling = Math.Max(1, shipTargetWords - stationWords);
                int bodyFloor = Math.Max(0, ceilingChoice.FloorWords - stationWords);

                // Weather/fuel lines are protected by identity, as the workflow that wrote them
                // identified them, not by vocabulary: the word list this replaced cut "clear and 22
                // for Sydney" as traffic after the gates had passed the read (dev 2e158de3).
                string trimmedBody = ScriptWordCeiling.TrimToCeiling(bodyToTrim, bodyCeiling,
                    FixtureLines.From(draftedSentences, FixturesRequired(context)),
                    minimumWords: bodyFloor);
                string trimmed = preserved ? split.Recompose(trimmedBody) : trimmedBody;
                if (!string.Equals(trimmed, voicedSynthScript, StringComparison.Ordinal))
                {
                    synthScript = trimmed;
                    script = trimmed;
                    voicedSynthScript = trimmed;
                    logger.LogInformation(
                        "[AudioSynthesize] Job {JobId} trimmed {Before}->{After} words back to the {Ceiling}-word ceiling before synthesis ({StationWords} station-line word(s) held back).",
                        context.JobId, before, WordCounter.Count(voicedSynthScript),
                        ScriptWordCeiling.For(shipTargetWords), stationWords);
                }
                else if (ceilingChoice.FloorWords > 0
                    && ScriptWordCeiling.Excess(voicedSynthScript, shipTargetWords) > 0)
                {
                    logger.LogInformation(
                        "[AudioSynthesize] Job {JobId} held the ceiling trim: dropping the next sentence would put the read under the {FloorWords}-word slot floor; leaving {Words} words for the fitter.",
                        context.JobId, ceilingChoice.FloorWords, WordCounter.Count(voicedSynthScript));
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        "Word ceiling held: trimming further would fall under the slot floor; the fitter compresses the read.",
                        cancellationToken);
                }
            }

            scriptWordCount = WordCounter.Count(voicedSynthScript);
            scriptSyllableCount = SyllableCounter.CountSyllables(voicedSynthScript);
            validatedPlain = script;
            validatedSynth = synthScript;
        }
        else
        {
            // The synthesis log and any post-synthesis comparison must describe the read ElevenLabs
            // will actually receive, not a plain form that may include stitched filler.
            scriptWordCount = voicedWordCount;
        }

        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Synthesizing audio ({scriptWordCount} words, target {context.TargetDurationSeconds}s)...",
            cancellationToken);
        logger.LogInformation(
            "[AudioSynthesize] Job {JobId} starting synthesis — TargetDuration={TargetSec}s, ScriptWords={WordCount}",
            context.JobId, context.TargetDurationSeconds, scriptWordCount);

        if (!workBudget.TryStartSynthesisAttempt(out _))
        {
            const string exhausted = "Broadcast synthesis attempt budget exhausted.";
            logger.LogWarning("[AudioSynthesize] Job {JobId} {Reason}", context.JobId, exhausted);
            return new GenerationAttemptOutcome(
                Result<(string script, string taggedScript, byte[] audio,
                    IReadOnlyList<WordTiming> wordTimings)>.Failure(
                    OperationalFailure.Retryable("broadcast.synthesis_budget_exhausted", exhausted)),
                0,
                script,
                scriptWordCount,
                scriptSyllableCount,
                ValidatedScript: validatedPlain,
                ValidatedSynthScript: validatedSynth);
        }

        workBudget.ThrowIfCancellationRequested();
        var synthSw = Stopwatch.StartNew();
        // Reserve the space the filler will occupy AFTER pace-matching, not the raw cached duration
        // — the clips are retimed to the read's rate at the stitch below, so reserving the cached
        // figure would leave the total short (or long) by exactly the retime. Both sides here are
        // GROSS rates (silence included): the calibration is a gross figure and the clip's stored
        // duration is a gross figure, and mixing a gross rate with an articulation one inverts the
        // correction. The stitch re-measures both sides as articulation, which is stricter; the
        // final fine-tune absorbs the difference, the same residual it already absorbs today.
        double predictedReadGrossSps = syllablesPerSecond > 0
            ? syllablesPerSecond * (maxSpeedUpFactorOverride is > 0
                ? maxSpeedUpFactorOverride.Value * (1.0 - WordTargetCalculator.BoostedCeilingMarginFraction)
                : 1.0)
            : 0;
        double totalFillerDuration = resolvedFiller.Sum(p => FillerPaceMatcher.RetimedSeconds(
            p.Audio.DurationSeconds,
            FillerPaceMatcher.GrossRate(
                SyllableCounter.CountSyllables(p.Statement.Text), p.Audio.DurationSeconds),
            predictedReadGrossSps));
        double totalPauseSeconds = resolvedFiller.Count > 0 ? FillerPauseSeconds * resolvedFiller.Count : 0;
        double trafficTargetDuration = Math.Max(1.0, context.TargetDurationSeconds - totalFillerDuration - totalPauseSeconds);
        if (progress is not null)
        {
            // The voice id is an ElevenLabs key, not operator copy (a live run printed
            // "EF2rFRNH..." on the rail). Words and the target are what the operator reads.
            int synthWords = WordCounter.Count(synthScript);
            await progress.StageStarted(
                ProgressStage.Voice,
                $"Synthesizing {synthWords} words for a {trafficTargetDuration:F0}s read.",
                new Dictionary<string, double>
                {
                    ["words"] = synthWords,
                    ["targetSeconds"] = trafficTargetDuration
                });
        }
        var audioResult = await audioPipeline.SynthesizeWithDurationTargetAsync(
            synthScript,
            trafficTargetDuration,
            context.VoiceId,
            context.VoiceSettings,
            context.Model,
            cancellationToken,
            speedHint,
            Option<string>.Some(context.MarketId.GetValueOr(context.Location)),
            context.PronunciationStack,
            pipelineSpeedUpOverride,
            // Dynamic slot only re-targets the full-slot (no-filler) case; when filler was pre-sized
            // the synth target is already reduced, so leave it to the original slot.
            allowDynamicSlot: selectedFiller.Count == 0,
            maxSlowDownFactorOverride: pipelineSlowDownOverride,
            // An overrun is fitted by dropping trailing sentences. The station's closer sits at the
            // very end, so it is the first thing that trim would delete — lock it. A required
            // weather/fuel close is protected on the same journey.
            lockedSuffix: BulletinBookendComposer.OutroOf(context.Bookends),
            fixtures: FixtureLines.From(draftedSentences, FixturesRequired(context)),
            lockedPrefix: BulletinBookendComposer.LockedIntroOf(context.Bookends));
        synthSw.Stop();
        if (progress is not null)
        {
            await progress.StageCompleted(
                ProgressStage.Voice,
                audioResult.IsSuccess ? "Voice synthesis completed." : "Voice synthesis failed.",
                new Dictionary<string, double> { ["durationMs"] = synthSw.ElapsedMilliseconds });
            await progress.StageStarted(ProgressStage.FitAudio, "Fitting the read to the bulletin slot.");
        }
        metrics.TrackPipelineStage("tts_synthesize", synthSw.Elapsed.TotalSeconds,
            context.Location, audioResult.IsSuccess);
        metrics.TrackTtsCharacters(context.Location, synthScript.Length);
        // ElevenLabs eleven_v3 pricing is roughly $0.30 per 1000 chars (eyeballed).
        metrics.TrackTtsCostEstimateUsd(context.Location, synthScript.Length / 1000.0 * 0.30);

        if (audioResult.IsSuccess)
        {
            var synthesis = audioResult.Value;
            // The pipeline MAY shorten the text: an overrun beyond the pitch-preserving speed-up
            // budget is fitted by BulletinDurationFitter dropping sentences and re-synthesizing, so
            // synthesis.FinalScript is the (possibly tagged, possibly TRIMMED) read that was actually
            // spoken. Persist the PLAIN form of THAT as ScriptText — never the pre-synthesis script,
            // which would leave the record claiming content the listener never heard.
            string taggedFinalScript = synthesis.FinalScript;
            byte[] finalAudio = synthesis.Audio;
            var finalWordTimings = synthesis.WordTimings;
            double naturalSpokenDuration = synthesis.SpokenDurationSeconds > 0
                ? synthesis.SpokenDurationSeconds
                : durationEstimator.GetDuration(synthesis.Audio);
            // ONE fill verdict, owned by the audio pipeline: the file is at slot length and any
            // trailing silence is inside the pad. Filler below re-asks the same question of the
            // stitched file. Nothing else in this method decides whether the read is short — the
            // engine used to hold four more rulers here (a slow-down-derived tolerance, a
            // pre-trim excuse, a trim-overshoot bound, the pipeline verdict) and on dev 34402fbb
            // (2026-09-02) they disagreed with each other about a 22.0s file, which failed.
            bool contentLimitedUnderrun = !synthesis.SlotFilled;

            // A trim changes what airs, so it belongs in the operator-visible job log. Without this
            // the only record is the pre-synthesis "Synthesizing audio (N words)" line above, which
            // is the PRE-trim count — so the timeline actively overstated the read (observed live on
            // jobs 19be05d7, 89b2dbf2, 4d2324d5, 79788128, where the weather/fuel close vanished with
            // nothing in LogsJson to explain it).
            int spokenWordCount = WordCounter.Count(DeliveryTags.Strip(taggedFinalScript));
            if (spokenWordCount < scriptWordCount)
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Read overran the {context.TargetDurationSeconds:F0}s slot — trimmed to fit "
                    + $"({scriptWordCount} -> {spokenWordCount} words); the weather/fuel close"
                    + (hasOutro ? " and the station's closer are" : " is") + " kept where it fits.",
                    cancellationToken);

            // ── Post-synthesis filler rescue ──
            // The pre-synthesis filler gap above is an ESTIMATE from voice calibration; when
            // calibration runs slow (observed live 2026-07-06: melbourne calibrated ~129 wpm vs
            // 161 wpm measured) the estimated gap collapses to ~0, no filler is selected, and an
            // otherwise-fillable take is discarded into the content-limited retry/fallback path.
            // Here the MEASURED read exists: re-derive the true gap and the voice's true pace from
            // it, and top up from the remaining filler pool before declaring the attempt short.
            // Skipped entirely for a station with an outro: every rescue statement is STITCHED as
            // audio onto the end of the take, which is precisely where the closer already is. The
            // underrun instead falls through to the content-limited retry, which re-drafts with a
            // bigger target and re-composes the whole read in order.
            if (contentLimitedUnderrun && fillerEligible && !hasOutro && context.FillerStatements.Count > 0)
            {
                double naturalSpoken = synthesis.SpokenDurationSeconds > 0
                    ? synthesis.SpokenDurationSeconds
                    : durationEstimator.GetDuration(synthesis.Audio);
                double alreadyStitched = resolvedFiller.Sum(p => p.Audio.DurationSeconds + FillerPauseSeconds);
                double trueGap = context.TargetDurationSeconds - naturalSpoken - alreadyStitched;
                int trafficWords = WordCounter.Count(DeliveryTags.Strip(synthesis.FinalScript));
                double measuredWps = naturalSpoken > 0 && trafficWords > 0
                    ? Math.Clamp(trafficWords / naturalSpoken, 1.0, 6.0)
                    : 1.0 / secondsPerWord;
                var usedIds = selectedFiller.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
                var rescuePool = context.FillerStatements.Where(s => !usedIds.Contains(s.Id)).ToList();
                var rescue = FillerStatementSelector.Select(
                    rescuePool, trueGap,
                    context.PreviousScript.HasValue ? context.PreviousScript.Value : null,
                    maxStatements: Math.Max(0, 3 - resolvedFiller.Count),
                    wordsPerSecond: measuredWps,
                    overshootToleranceSeconds: FillerRescueOvershootToleranceSeconds,
                    acceptableResidualSeconds: _durationOptions.MaxSilencePadSeconds);
                if (rescue.Count == 0)
                {
                    logger.LogInformation(
                        "[Filler] Job {JobId} rescue found no statement for the measured {Gap:F1}s gap " +
                        "(pool {Pool}, {Used} used/excluded, measured {Wps:F2} wps)",
                        context.JobId, trueGap, rescuePool.Count, usedIds.Count, measuredWps);
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        $"Filler rescue: no statement fits the measured {trueGap:F1}s gap ({rescuePool.Count} eligible).",
                        cancellationToken);
                }
                else
                {
                    string rescueVoiceId = context.VoiceId.GetValueOr(_options.DefaultVoiceId);
                    var rescueVoiceSettings = context.VoiceSettings.GetValueOr(VoiceSettings.Default);
                    string rescueModel = context.Model.GetValueOr("eleven_v3");
                    int resolvedBefore = resolvedFiller.Count;
                    foreach (var statement in rescue)
                    {
                        var resolved = await fillerAudioCacheService.ResolveAsync(
                            statement, rescueVoiceId, rescueVoiceSettings, rescueModel, cancellationToken);
                        if (resolved.IsSuccess)
                            resolvedFiller.Add((statement, resolved.Value));
                        else
                            logger.LogWarning(
                                "[Filler] Job {JobId} rescue could not resolve audio for statement {StatementId}: {Error}",
                                context.JobId, statement.Id, resolved.Error);
                    }
                    int added = resolvedFiller.Count - resolvedBefore;
                    if (added > 0)
                        await jobStatusUpdater.LogJobAsync(context.JobId,
                            $"Filler rescue: +{added} statement(s) to cover the measured {trueGap:F1}s gap.",
                            cancellationToken);
                }
            }

            // Stitch cached/freshly-synthesized filler audio onto the traffic-only take, then
            // fine-tune the STITCHED result to the FULL slot — this is what makes the final file
            // exactly context.TargetDurationSeconds long when filler was used.
            if (resolvedFiller.Count > 0)
            {
                // synthesis.Audio may already be padded to the FULL slot with raw trailing
                // silence (SynthesizeWithDurationTargetAsync's underrun path pads toward its
                // target even when it can't reach it, correctly reporting SlotFilled=false on
                // that padded clip alone). Stitching filler onto that padded clip buries it
                // behind dead air that the final uniform speed-up only shrinks, never removes —
                // trim back to the real spoken content first so filler lands right after real
                // speech, with just the intended pause (2026-07-07 job 4417add2 dead-air fix).
                byte[] trafficAudio = audioPipeline.TrimToSpokenDuration(synthesis.Audio);
                var clips = new List<byte[]> { trafficAudio };

                // Pace-match each cached clip to the read it is about to follow. The clip was
                // rendered standalone and keeps whatever rate that gave it, while the read has been
                // time-compressed into the slot — measured across six aired dev bulletins the clip
                // articulates a median 8% faster, worst case 35%. Retiming is against the read's
                // compression, not with it (FillerPaceMatcher explains why), and clamped, so a clip
                // already in step is left byte-identical and only a genuine mismatch is touched.
                // BOTH rates are articulation (silence excluded) — the clip's own stored timings
                // give it, and mixing units here inverts the correction outright.
                double readSps = FillerPaceMatcher.ArticulationRate(synthesis.WordTimings);
                var retimedFiller = new List<(FillerStatement Statement, byte[] Audio, double Duration,
                    IReadOnlyList<WordTiming> WordTimings)>(resolvedFiller.Count);
                foreach (var pair in resolvedFiller)
                {
                    double clipSps = FillerPaceMatcher.ArticulationRate(pair.Audio.WordTimings);
                    double target = FillerPaceMatcher.RetimedSeconds(
                        pair.Audio.DurationSeconds, clipSps, readSps);
                    if (Math.Abs(target - pair.Audio.DurationSeconds) <= FillerRetimeDeadbandSeconds)
                    {
                        retimedFiller.Add((pair.Statement, pair.Audio.Audio, pair.Audio.DurationSeconds,
                            pair.Audio.WordTimings));
                        continue;
                    }

                    byte[] retimed = audioPipeline.RetimeClip(pair.Audio.Audio, target);
                    double actual = durationEstimator.GetDuration(retimed);
                    double clipScale = pair.Audio.DurationSeconds > 0 ? actual / pair.Audio.DurationSeconds : 1.0;
                    logger.LogInformation(
                        "[Filler] Job {JobId} pace-matched {StatementId}: {Cached:F2}s -> {Actual:F2}s "
                        + "({Scale:F3}x) to meet the read's {ReadSps:F2} syl/s.",
                        context.JobId, pair.Statement.Id, pair.Audio.DurationSeconds, actual, clipScale, readSps);
                    retimedFiller.Add((pair.Statement, retimed, actual,
                        [.. pair.Audio.WordTimings.Select(wt => new WordTiming(
                            wt.Word, wt.StartSeconds * clipScale, wt.EndSeconds * clipScale))]));
                }

                clips.AddRange(retimedFiller.Select(p => p.Audio));
                byte[] stitched = audioPipeline.StitchAudioClips(clips, FillerPauseSeconds);
                double preFineTuneDuration = durationEstimator.GetDuration(stitched);

                var stitchedTimings = new List<WordTiming>(synthesis.WordTimings);
                double cumulativeOffset = durationEstimator.GetDuration(trafficAudio);
                foreach (var pair in retimedFiller)
                {
                    cumulativeOffset += FillerPauseSeconds;
                    foreach (var wt in pair.WordTimings)
                        stitchedTimings.Add(new WordTiming(wt.Word, wt.StartSeconds + cumulativeOffset, wt.EndSeconds + cumulativeOffset));
                    cumulativeOffset += pair.Duration;
                }

                // Same boundary rule as the synthesis call: the pipeline applies its own configured
                // option when an argument is null, so an unset console must still send null here.
                finalAudio = audioPipeline.FineTuneToTargetDuration(
                    stitched, context.TargetDurationSeconds, pipelineSpeedUpOverride, pipelineSlowDownOverride);
                double finalDuration = durationEstimator.GetDuration(finalAudio);
                // The trimmed-bed closer appends a bed AFTER the speech (read + filler) without
                // stretching it, so the stitched word timings are already correct — only the uniform
                // fine-tune stretch rescales them.
                double scale = _durationOptions.UseTrimmedBedCloser
                    ? 1.0
                    : preFineTuneDuration > 0 ? finalDuration / preFineTuneDuration : 1.0;
                finalWordTimings = stitchedTimings
                    .Select(wt => new WordTiming(wt.Word,
                        Math.Round(Math.Clamp(wt.StartSeconds * scale, 0, finalDuration), 3),
                        Math.Round(Math.Clamp(wt.EndSeconds * scale, 0, finalDuration), 3)))
                    .ToList();

                contentLimitedUnderrun = !audioPipeline.IsSlotFilled(finalAudio, context.TargetDurationSeconds);
            }

            // Stripping the delivery tags is where the plain script's punctuation can collapse: a
            // tag sitting between the last body sentence and the station outro leaves ". . That's
            // KIIS traffic and weather together." behind, which the operator then sees in the console
            // and which lands in the word timings (observed on dev jobs 027729aac1 and d94648edf6).
            // The generator's own normaliser chain runs BEFORE this strip, so it cannot see the
            // artifact; normalise again here, on the text that actually persists.
            string spokenPlain = SpokenPunctuationNormalizer.Normalize(DeliveryTags.Strip(taggedFinalScript));
            string finalScript = spokenPlain;
            string taggedWithFiller = taggedFinalScript;
            if (resolvedFiller.Count > 0)
            {
                string fillerText = string.Join(" ", resolvedFiller.Select(p => p.Statement.Text));
                finalScript = BulletinBookendComposer.JoinBodyAndFollowing(finalScript, [fillerText]);
                taggedWithFiller = BulletinBookendComposer.JoinBodyAndFollowing(
                    taggedFinalScript,
                    [fillerText],
                    normalizePunctuation: false);
            }

            // The measured figures handed back are the NATURAL READ: the traffic take's own spoken
            // seconds and words, before filler, pad or stretch. Calibration, the retry word budget
            // and the composer's duration revision all divide one by the other, so they must
            // describe the same audio. Reporting the slot-length file here instead told the
            // composer a 17.5s read had "measured 22.0s" (dev 34402fbb, 2026-09-02) and it cut.
            double fileDuration = durationEstimator.GetDuration(finalAudio);
            int finalWordCount = WordCounter.Count(finalScript);
            int spokenSyllableCount = SyllableCounter.CountSyllables(DeliveryTags.Strip(taggedFinalScript));

            logger.LogInformation(
                "[AudioSynthesize] Job {JobId} synthesis succeeded in {ElapsedMs}ms — File={File:F1}s, NaturalSpoken={Spoken:F1}s, SlotFilled={Filled}, AudioBytes={Bytes}",
                context.JobId, synthSw.ElapsedMilliseconds, fileDuration, naturalSpokenDuration,
                !contentLimitedUnderrun, finalAudio.Length);

            string fillerNote = resolvedFiller.Count > 0
                ? $" plus {resolvedFiller.Count} filler statement(s)"
                : string.Empty;
            await jobStatusUpdater.LogJobAsync(context.JobId,
                contentLimitedUnderrun
                    ? $"Short bulletin: {naturalSpokenDuration:F1}s of natural speech{fillerNote} could not fill the {context.TargetDurationSeconds:F0}s slot — retrying for more"
                    : $"Audio filled to slot: {naturalSpokenDuration:F1}s natural read{fillerNote} fitted to {fileDuration:F1}s ({synthSw.ElapsedMilliseconds}ms)",
                cancellationToken);
            await PersistScriptOnJob(context, finalScript, taggedWithFiller, draftedSentences, alignmentFallbackSentences, cancellationToken);

            if (progress is not null)
                await progress.StageCompleted(
                    ProgressStage.FitAudio,
                    contentLimitedUnderrun ? "Audio fit needs another attempt." : "Audio fitted to the slot.",
                    new Dictionary<string, double>
                    {
                        ["durationSeconds"] = fileDuration,
                        ["words"] = finalWordCount
                    });
            return new GenerationAttemptOutcome(
                Result<(string, string, byte[], IReadOnlyList<WordTiming>)>.Success((finalScript, taggedWithFiller, finalAudio, finalWordTimings)),
                naturalSpokenDuration,
                finalScript,
                spokenWordCount,
                spokenSyllableCount,
                synthesis.FinalSpeed,
                IsContentLimitedUnderrun: contentLimitedUnderrun,
                ValidatedScript: validatedPlain,
                ValidatedSynthScript: validatedSynth,
                PredictedSecondsPerWord: predictedSecondsPerWordUsed,
                SpokenScript: spokenPlain,
                DraftedSentences: draftedSentences,
                ComposerThreadId: composerThreadId,
                WasNaturalSynthesis: synthesis.IsNaturalSynthesis);
        }

        double extractedDuration = JobStatusUpdater.ExtractDurationFromError(audioResult.Error).GetValueOr(0);
        if (progress is not null)
            await progress.StageCompleted(
                ProgressStage.FitAudio,
                "Audio fitting failed.",
                new Dictionary<string, double> { ["durationSeconds"] = extractedDuration });

        logger.LogWarning(
                "[AudioSynthesize] Job {JobId} synthesis FAILED in {ElapsedMs}ms — ExtractedDuration={Duration:F1}s, Error={Error}",
                context.JobId, synthSw.ElapsedMilliseconds, extractedDuration, audioResult.Error);
        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Audio synthesis failed after {synthSw.ElapsedMilliseconds}ms: {audioResult.Error}", cancellationToken);

        return new GenerationAttemptOutcome(
            audioResult.PropagateFailure<(string, string, byte[], IReadOnlyList<WordTiming>)>(),
            extractedDuration,
            script,
            scriptWordCount,
            scriptSyllableCount,
            ComposerThreadId: composerThreadId,
            WasNaturalSynthesis: extractedDuration > 0);
    }

    /// <summary>
    ///     The station-authored lines a fit pass must peel off before it touches phrasing: bookend
    ///     intro/outro and the market's standing filler. Shared by both variant-fit paths so the
    ///     over-length and under-length cases can never disagree about what is untouchable.
    /// </summary>
    private static List<string> StationLinesFor(BroadcastWithTraffic context)
    {
        var lines = new List<string>();
        if (context.Bookends.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(context.Bookends.Value.IntroText))
                lines.Add(context.Bookends.Value.IntroText);
            if (!string.IsNullOrWhiteSpace(context.Bookends.Value.OutroText))
                lines.Add(context.Bookends.Value.OutroText);
        }
        if (context.FillerStatements.Count > 0)
            lines.AddRange(context.FillerStatements.Select(statement => statement.Text));
        return lines;
    }

    private async Task PersistScriptOnJob(BroadcastWithTraffic context, string script, string? taggedScript,
        IReadOnlyList<DraftedSentence>? loopDraftedSentences,
        IReadOnlyList<DraftedSentence>? alignmentFallbackSentences,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Road lineage is derived deterministically from the evidence the read was built on:
        // incident corridors, selected good runs, the full flow pool and monitored drive-time
        // runs. Weather/fuel lineage arrives from the workflow on the drafted sentences and is
        // merged below. No attribution LLM call or post-hoc model pass.
        IReadOnlyList<JobScriptSentence>? sentences = ScriptProvenanceMapper.Map(
            script,
            context.Corridors,
            context.MonitoredRoutes,
            context.FlowPool,
            context.GoodRunRoads);

        if (sentences is not null)
        {
            int alignedCitations = 0;
            string citationSource = "none";
            if (loopDraftedSentences is { Count: > 0 })
            {
                (sentences, alignedCitations) = ApplyLoopSentenceEvidence(sentences, loopDraftedSentences);
                citationSource = "loop";
            }
            else if (alignmentFallbackSentences is { Count: > 0 })
            {
                (sentences, alignedCitations) = AlignDraftedSentenceEvidence(sentences, alignmentFallbackSentences);
                citationSource = "alignment-fallback";
            }

            logger.LogInformation(
                "[ScriptGen] Job {JobId} persisted {Aligned}/{Total} sentence citations (source={CitationSource})",
                context.JobId, alignedCitations, sentences.Count, citationSource);
        }

        var persistResult = await jobStatusUpdater.UpdateJobAsync(context.JobId, job =>
        {
            job.ScriptText = Option<string>.Some(script);
            bool tagged = DeliveryTags.HasTags(taggedScript);
            job.TaggedScriptText = tagged ? Option<string>.Some(taggedScript!) : Option<string>.None;
            job.DeliveryStyleId = tagged ? context.DeliveryStyleId : Option<string>.None;
            job.DeliveryStyleVersion = tagged ? context.DeliveryStyleVersion : Option<int>.None;
            job.BookendId = context.Bookends.Map(b => b.Id);
            job.BookendVersion = context.Bookends.Map(b => b.Version);
            job.PronunciationDictionaryId = context.PronunciationStack.Station.Map(l => l.DictionaryId);
            job.PronunciationVersionId = context.PronunciationStack.Station.Map(l => l.VersionId);
            job.GlobalPronunciationDictionaryId = context.PronunciationStack.Global.Map(l => l.DictionaryId);
            job.GlobalPronunciationVersionId = context.PronunciationStack.Global.Map(l => l.VersionId);
            if (sentences is not null)
                job.SetScriptSentences(sentences);
            job.TransitionTo(JobStatus.ScriptReady, "system", Option<string>.Some("Script generated successfully"),
                timeProvider.GetUtcNow().UtcDateTime);
        }, "Script generated successfully", cancellationToken);

        if (persistResult.IsFailure)
            logger.LogWarning("[PersistScript] Job {JobId} failed to persist script: {Error}",
                context.JobId, persistResult.Error);
    }

    /// <summary>
    ///     Applies citations authored for the final loop output. Unlike the legacy alignment path,
    ///     this only accepts an exact sentence identity (after harmless punctuation normalisation);
    ///     it never transfers evidence across a fuzzy text match.
    /// </summary>
    internal static (IReadOnlyList<JobScriptSentence> Sentences, int AppliedCount) ApplyLoopSentenceEvidence(
        IReadOnlyList<JobScriptSentence> sentences, IReadOnlyList<DraftedSentence> authoredSentences)
    {
        if (sentences.Count == 0 || authoredSentences.Count == 0)
            return (sentences, 0);

        var authored = authoredSentences.OrderBy(sentence => sentence.Order).ToList();
        var used = new bool[authored.Count];
        var applied = new List<JobScriptSentence>(sentences.Count);
        int appliedCount = 0;
        foreach (JobScriptSentence sentence in sentences)
        {
            int match = -1;
            for (int index = 0; index < authored.Count; index++)
            {
                if (!used[index]
                    && string.Equals(
                        NormalizeSentenceForAlignment(sentence.Text),
                        NormalizeSentenceForAlignment(authored[index].Text),
                        StringComparison.Ordinal))
                {
                    match = index;
                    break;
                }
            }

            if (match < 0)
            {
                applied.Add(sentence);
                continue;
            }

            used[match] = true;
            IReadOnlyList<string> authoredEvidence = authored[match].EvidenceIds ?? [];
            applied.Add(sentence with
            {
                EvidenceIds = MergeEvidenceIds(sentence.EvidenceIds, authoredEvidence),
                CorridorKeys = MergeCorridorKeys(sentence.CorridorKeys, authored[match].CorridorKeys)
            });
            // A text match with no authored evidence is still useful for carrying deterministic
            // road keys, but it is not a persisted citation.
            if (authoredEvidence.Any(id => !string.IsNullOrWhiteSpace(id)))
                appliedCount++;
        }

        return (applied, appliedCount);
    }

    internal static (IReadOnlyList<JobScriptSentence> Sentences, int AlignedCount) AlignDraftedSentenceEvidence(
        IReadOnlyList<JobScriptSentence> sentences, IReadOnlyList<DraftedSentence> draftedSentences)
    {
        if (sentences.Count == 0 || draftedSentences.Count == 0)
            return (sentences, 0);

        var alignedSentences = new List<JobScriptSentence>(sentences.Count);
        var orderedDrafts = draftedSentences.OrderBy(d => d.Order).ToList();
        int nextDraftIndex = 0;
        int alignedCount = 0;

        foreach (JobScriptSentence sentence in sentences)
        {
            int match = FindDraftMatch(sentence.Text, orderedDrafts, nextDraftIndex);
            if (match < 0)
            {
                alignedSentences.Add(sentence);
                continue;
            }

            DraftedSentence draft = orderedDrafts[match];
            IReadOnlyList<string> authoredEvidence = draft.EvidenceIds ?? [];
            alignedSentences.Add(sentence with
            {
                EvidenceIds = MergeEvidenceIds(sentence.EvidenceIds, authoredEvidence),
                CorridorKeys = MergeCorridorKeys(sentence.CorridorKeys, draft.CorridorKeys)
            });
            nextDraftIndex = match + 1;
            if (authoredEvidence.Any(id => !string.IsNullOrWhiteSpace(id)))
                alignedCount++;
        }

        return (alignedSentences, alignedCount);
    }

    /// <summary>
    ///     Authored citations never replace the deterministic lineage the mapper derived from the
    ///     final read (synthetic <c>goodrun:</c>/<c>weather:line</c>/<c>fuel:line</c> ids). The
    ///     conversational workflow emits sentences with no evidence ids at all, and replacing the
    ///     mapped list with that empty one is exactly how dev job 75639f66 lost its weather and
    ///     fuel lineage after the mapper had found it.
    /// </summary>
    private static IReadOnlyList<string> MergeEvidenceIds(
        IReadOnlyList<string>? mapped,
        IReadOnlyList<string> authored) =>
        (mapped ?? [])
            .Concat(authored)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>True when the station marks weather or fuel "always include" for this bulletin.</summary>
    private static bool FixturesRequired(BroadcastWithTraffic context) =>
        context.Content.RequiresWeather || context.Content.RequiresFuel;

    private static IReadOnlyList<string> MergeCorridorKeys(
        IReadOnlyList<string> existing,
        IReadOnlyList<string> authored) =>
        existing
            .Concat(authored)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int FindDraftMatch(
        string finalText, IReadOnlyList<DraftedSentence> drafts, int firstCandidate)
    {
        for (int tier = 0; tier < 3; tier++)
        {
            for (int i = firstCandidate; i < drafts.Count; i++)
            {
                string draftText = drafts[i].Text;
                bool matches = tier switch
                {
                    0 => string.Equals(draftText.Trim(), finalText.Trim(), StringComparison.Ordinal),
                    1 => string.Equals(NormalizeSentenceForAlignment(draftText),
                        NormalizeSentenceForAlignment(finalText), StringComparison.Ordinal),
                    _ => StrongSentenceMatch(draftText, finalText)
                };
                if (matches)
                    return i;
            }
        }

        return -1;
    }

    private static bool StrongSentenceMatch(string draftText, string finalText)
    {
        string draftNormalized = NormalizeSentenceForAlignment(draftText);
        string finalNormalized = NormalizeSentenceForAlignment(finalText);
        if (draftNormalized.Length == 0 || finalNormalized.Length == 0)
            return false;

        if (ContainsWholeSentence(draftNormalized, finalNormalized)
            || ContainsWholeSentence(finalNormalized, draftNormalized))
            return true;

        string[] draftWords = SignificantWords(draftNormalized);
        string[] finalWords = SignificantWords(finalNormalized);
        if (draftWords.Length < 3 || finalWords.Length < 3)
            return false;

        int shared = draftWords.Intersect(finalWords, StringComparer.Ordinal).Count();
        int smaller = Math.Min(draftWords.Length, finalWords.Length);
        int larger = Math.Max(draftWords.Length, finalWords.Length);
        return shared * 4 >= smaller * 3 && shared * 2 >= larger;
    }

    private static bool ContainsWholeSentence(string containing, string contained) =>
        containing == contained
        || containing.StartsWith(contained + " ", StringComparison.Ordinal)
        || containing.EndsWith(" " + contained, StringComparison.Ordinal)
        || containing.Contains(" " + contained + " ", StringComparison.Ordinal);

    private static string[] SignificantWords(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3 || word.Any(char.IsDigit))
            .Where(word => !AlignmentStopWords.Contains(word))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeSentenceForAlignment(string text)
    {
        string normalized = FillerStatementSelector.Normalize(DeliveryTags.Strip(text));
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly HashSet<string> AlignmentStopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "at", "be", "by", "for", "from", "in", "is", "it",
        "of", "on", "or", "the", "this", "to", "was", "with"
    };

    internal sealed record GenerationRunOutcome(
        Result<(string script, string taggedScript, byte[] audio, IReadOnlyList<WordTiming> wordTimings)> Result,
        GenerationCandidate? Candidate);

    internal sealed class GenerationCandidate : IDisposable
    {
        private readonly AudioByteOwner _audioOwner;

        public GenerationCandidate(
            string script,
            string taggedScript,
            byte[] audio,
            IReadOnlyList<WordTiming> wordTimings,
            double durationSeconds)
        {
            Script = script;
            TaggedScript = taggedScript;
            _audioOwner = new AudioByteOwner(audio);
            WordTimings = wordTimings;
            DurationSeconds = durationSeconds;
        }

        public string Script { get; }
        public string TaggedScript { get; }
        public byte[] Audio => _audioOwner.Buffer;
        public IReadOnlyList<WordTiming> WordTimings { get; }
        public double DurationSeconds { get; }
        public bool AudioReleased => _audioOwner.IsReleased;

        public void Dispose() => _audioOwner.Dispose();
    }

    /// <param name="SpokenScript">The plain, tag-stripped, filler-free text that the natural-spoken seconds were measured on; the fitter may have trimmed it below the validated script.</param>
    internal sealed record GenerationAttemptOutcome(
        Result<(string script, string taggedScript, byte[] audio, IReadOnlyList<WordTiming> wordTimings)> Result,
        double DurationSeconds,
        string Script,
        int WordCount,
        int SyllableCount,
        double FinalSpeed = 0,
        bool IsContentLimitedUnderrun = false,
        string ValidatedScript = "",
        string ValidatedSynthScript = "",
        double PredictedSecondsPerWord = 0,
        // The phrasing menu this take was assembled from. Carried out of the attempt so an underrun
        // can be closed by RE-SELECTING longer variants — free, deterministic, already written —
        // before spending LLM calls asking the writer to grow sentences. Measured 2026-08-25 on
        // Foundry traces: 38 of 54 refine calls (70%) were "extend this sentence toward the word
        // target", and 87% of production menus can already reach the slot window by swapping
        // phrasings. Most of that spend re-derives a combination we were already holding.
        IReadOnlyList<DraftedSentence>? DraftedSentences = null,
        string? ComposerThreadId = null,
        bool WasNaturalSynthesis = false,
        string SpokenScript = "");

    /// <summary>
    ///     Records one measured (shape → natural seconds) sample for the voice fingerprint that
    ///     produced this read. Fail-soft at the repository; guarded here only against the cases
    ///     where a sample would be meaningless (no shape coefficients loaded, or a read whose shape
    ///     evaluates to zero).
    /// </summary>
    private async Task RecordDurationSampleAsync(
        BroadcastWithTraffic context,
        string voiceId,
        VoiceSettings settings,
        GenerationAttemptOutcome attemptResult,
        CancellationToken cancellationToken)
    {
        if (durationSamples is null || durationCalibration is null)
            return;

        // Tags are never spoken, so the shape must be measured on the read the voice actually
        // performed — the same strip the predictor applies before Extract.
        string spoken = DeliveryTags.Strip(attemptResult.Script);
        double shapeTotal = durationCalibration.Shape.Total(SpokenDurationFeatures.Extract(spoken));
        if (shapeTotal <= 0)
            return;

        await durationSamples.RecordAsync(
            new SpokenDurationSample
            {
                VoiceId = voiceId,
                ModelId = context.Model.GetValueOr("eleven_v3"),
                Speed = settings.Speed,
                PaceBoostWpm = context.PaceBoostWpm,
                ShapeTotal = shapeTotal,
                NaturalSeconds = attemptResult.DurationSeconds,
                TargetSeconds = context.TargetDurationSeconds,
                WordCount = attemptResult.WordCount,
                SyllableCount = attemptResult.SyllableCount,
                SpokenText = spoken,
                JobId = context.JobId,
                CapturedAt = timeProvider.GetUtcNow()
            },
            cancellationToken);
    }
}
