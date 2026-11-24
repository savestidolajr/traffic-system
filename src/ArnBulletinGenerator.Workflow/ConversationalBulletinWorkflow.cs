using System.Diagnostics;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow;

/// <summary>
///     The conversational shape: a writer offers a menu, a composer assembles it, and an editor
///     reviews the read like a colleague. It avoids the former pipeline's measured 36-second,
///     roughly 20,000-token citation apparatus by letting plain-words justifications carry provenance.
/// </summary>
public sealed class ConversationalBulletinWorkflow(
    IWorkflowLlm llm,
    IDurationOracle duration,
    ConversationalWorkflowOptions? options = null,
    TimeProvider? timeProvider = null) : IBulletinWorkflow
{
    // A field initializer cannot read another instance field, so the composer cannot be built from
    // _options. Both read the primary-constructor parameter instead, falling back to one SHARED
    // default so the workflow and the composer always hold the same instance rather than two
    // equivalent copies that could drift if the options ever stop being immutable.
    private static readonly ConversationalWorkflowOptions DefaultOptions = new();
    private readonly ConversationalWorkflowOptions _options = options ?? DefaultOptions;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly BulletinWriter _writer = new(llm);
    private readonly BulletinComposer _composer = new(llm, options ?? DefaultOptions);
    private readonly BulletinEditor _editor = new(llm);

    private static int _runOrdinal;

    /// <summary>
    ///     A monotonic suffix keeps traces distinct when several runs begin within the same second.
    /// </summary>
    private static int NextRunOrdinal() => Interlocked.Increment(ref _runOrdinal);

    public string Id => "conversational-v1";

    public string Description => "Writer offers a menu, composer assembles it, editor reviews like a colleague";

    public async Task<WorkflowOutcome> RunAsync(
        WorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        GenerationProgressTraceRelay? progressRelay = request.ProgressReporter is { } reporter
            ? new GenerationProgressTraceRelay(reporter)
            : null;
        var trace = new TraceRecorder(
            runId: $"{_time.GetUtcNow():yyyyMMdd-HHmmss}-{NextRunOrdinal():D3}-{request.Market}-{Id}",
            workflowId: Id,
            workflowDescription: Description,
            jobId: request.JobId,
            market: request.Market,
            station: request.Station,
            targetSeconds: request.TargetSeconds,
            timeProvider: _time,
            onEvent: progressRelay is null ? null : progressRelay.Observe);

        try
        {
            return await ExecuteAsync(request, trace, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            trace.Failure("Workflow", "Unhandled exception", exception.ToString());
            return WorkflowOutcome.Failure(exception.Message, trace.Freeze("failed", exception.Message));
        }
    }

    private async Task<WorkflowOutcome> ExecuteAsync(
        WorkflowRequest request,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        NormalizedInput input = Normalizer.Normalize(request, trace);
        if (input.Events.Count == 0)
        {
            const string error = "No events in the supplied evidence";
            trace.Failure("Normalize", error, "empty evidence");
            trace.Decision("Write", "Skipped: normalization produced no events");
            trace.Decision("Compose", "Skipped: writer had no source events");
            trace.Decision("Edit", "Skipped: writer had no source events");
            trace.Decision("Measure", "Skipped: no read was composed");
            return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
        }

        // The caller's calibrated number when it supplied one, and only then the oracle. See
        // WorkflowRequest.SlotWords: the oracle has no text to price before a read exists, so it
        // prices a probe, and the probe was being answered with a full bulletin's duration.
        int oracleWords = duration.WordsForSeconds(request.TargetSeconds);
        int targetWords = request.SlotWords > 0 ? request.SlotWords : oracleWords;

        // Both numbers answer one question, so a disagreement means one of them is wrong and the
        // read is about to be sized against it. Recorded rather than reconciled: the caller's value
        // wins, and the gap is the evidence needed to tell which side drifted.
        string agreement = request.SlotWords > 0 && oracleWords > 0
            ? $"{100.0 * (oracleWords - request.SlotWords) / request.SlotWords:+0;-0}%"
            : "n/a";
        trace.Decision("Workflow", $"Slot {request.TargetSeconds:F1}s budgets {targetWords} words",
            new Dictionary<string, string>
            {
                ["targetSeconds"] = request.TargetSeconds.ToString("F2"),
                ["targetWords"] = targetWords.ToString(),
                ["source"] = request.SlotWords > 0 ? "caller (calibrated)" : "oracle (probe)",
                ["oracleWords"] = oracleWords.ToString(),
                ["oracleDrift"] = agreement
            });

        trace.BeginIteration(1);
        BulletinBriefs briefs;
        try
        {
            briefs = await _writer.WriteBriefsAsync(
                request, input, targetWords, trace, cancellationToken).ConfigureAwait(false);
        }
        catch (BriefingDeskLostEventsException lost)
        {
            // Fail the attempt rather than air a read that omits what the desk was given. The
            // engine retries and falls back; a filler cart beats a bulletin that contradicts the
            // evidence (dev adelaide 369b0c37, 2026-08-30: three closures dropped, "clear runs"
            // aired in their place).
            trace.Failure("Write", lost.Message, $"{lost.Role} lost {lost.EventCount} event(s)");
            trace.Decision("Compose", "Skipped: a briefing desk lost its events");
            trace.Decision("Edit", "Skipped: a briefing desk lost its events");
            trace.Decision("Measure", "Skipped: no read was composed");
            return WorkflowOutcome.Failure(lost.Message, trace.Freeze("failed", lost.Message));
        }

        if (briefs.Stories.Count == 0)
        {
            const string error = "Writer produced no story briefs";
            trace.Failure("Write", error, "empty briefs");
            trace.Decision("Compose", "Skipped: writer briefed no stories");
            trace.Decision("Edit", "Skipped: writer briefed no stories");
            trace.Decision("Measure", "Skipped: no read was composed");
            return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
        }

        // Three reads at once, at different lengths, then pick — rather than write one and rewrite
        // it until it fits. The ladder is centred on the exact body budget: one under, one on and
        // one over. The model's word aim is sampling guidance; the audio duration band decides
        // whether a returned read fits.
        //
        // Length is a GUESS until the prose exists. Within a fixed board the measured aim-to-output
        // slope is only +0.083 and board identity explains about 89% of length variance. Candidate
        // selection therefore prices the prose that actually came back instead of trusting which
        // rung produced it.
        //
        // And the checks are FREE. Every candidate is scored by the same deterministic checks the
        // loop below applies, at no model cost, so three samples buy three chances of a clean read
        // where one bought one. Sequentially that cost 5-8s per extra attempt; concurrently the
        // whole set costs one call's latency.
        // One Compose envelope spans the candidates AND their selection, so the per-candidate
        // measurements land inside it rather than after "Compose completed".
        trace.StageStart("Compose", "Composing the read");
        var composeSw = Stopwatch.StartNew();
        var candidates = await ComposeCandidatesAsync(
            request, input, briefs, targetWords, trace, cancellationToken).ConfigureAwait(false);
        var chosen = SelectCandidate(candidates, request.TargetSeconds, request.EffectiveMaxSeconds, trace);
        composeSw.Stop();
        trace.StageEnd("Compose",
            chosen is null
                ? "No candidate returned a read"
                : $"Composed {candidates.Count} candidate(s); chose {chosen.Words} words",
            composeSw.Elapsed.TotalMilliseconds);
        if (chosen is null)
        {
            const string error = "Composer produced no read";
            trace.Failure("Compose", error, "no candidate returned a read");
            trace.Decision("Edit", "Skipped: composer produced no read");
            trace.Decision("Measure", "Skipped: composer produced no read");
            return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
        }

        // Correct the winner only when its predicted speech is beyond the fitter's compression
        // ceiling. A candidate already inside that duration band is usable even when it ignored its
        // prompt's word band; forcing it back to its original aim discards the very duration margin
        // selection chose. Dev job ba48fe7b (2026-09-03) selected a clean 56-word read, then this
        // word correction cut it to 46 because its rung aimed at 47. The first synthesis consequently
        // landed 0.7s short and paid for another TTS render.
        string read = chosen.Read;
        string? composerThread = chosen.ResponseId;
        if (chosen.PredictedSeconds > request.EffectiveMaxSeconds)
        {
            var corrected = await _composer.CorrectChosenReadAsync(
                    request, chosen.Read, chosen.AimedWords, chosen.ResponseId, trace, cancellationToken)
                .ConfigureAwait(false);
            read = corrected.Text;
            composerThread = corrected.ResponseId;
        }
        else
        {
            trace.Decision("FitWords",
                $"Skipped: selected read predicts {chosen.PredictedSeconds:F1}s inside the "
                + $"{request.TargetSeconds:F1}s–{request.EffectiveMaxSeconds:F1}s fitter band");
        }

        // ONE loop, deliberately. These used to be two: the editor ran to a verdict, and only then
        // did a separate length loop rewrite the read. That ordering meant the script that aired
        // was never the script that was reviewed - a rejection could be followed by a length
        // rewrite that shipped unexamined, and even an approval was invalidated by the rewrite that
        // came after it. Correctness and length are now conditions on the SAME read: nothing ships
        // until one read satisfies every check, the editor, and the slot at once.
        int iteration = 1;
        int revision = 0;
        int maxRevisions = Math.Max(0, _options.MaxRevisions);
        var unresolved = new List<string>();
        bool inDurationBand = false;
        double predictedSeconds = 0;
        // At most one revision may be spent on a ceiling breach, no matter how many revisions the
        // editor itself earns. A length loop that cannot converge is the documented failure mode.
        bool spentCeilingRevision = false;
        bool editorUnavailable = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Code first, model second. The deterministic checks answer questions with one correct
            // answer, and asking a model to re-derive them is both slower and less reliable: on the
            // first live conversational run the editor approved "about a kay slower" against data
            // reading "about half a kay" and a one-minute delay. A failure here is folded into the
            // editor's own verdict shape so the composer receives one coherent note from one voice
            // rather than two channels of correction.
            var checks = ConversationalChecks.Run(
                input, read, briefs, trace, request.PreviousScript, traceStage: "Gates");
            var checkFailures = checks.Where(c => !c.Passed).Select(c => c.Detail).ToList();

            EditorVerdict verdict = checkFailures.Count > 0
                ? new EditorVerdict(false, checkFailures)
                : await ReviewAsync(request, input, briefs, read, trace, cancellationToken).ConfigureAwait(false);

            if (checkFailures.Count > 0)
            {
                trace.Decision("Edit",
                    $"Editor review skipped: {checkFailures.Count} deterministic check(s) already failed");
            }
            if (verdict.IsUnreviewed)
            {
                editorUnavailable = true;
                string reason = string.Join("; ", verdict.Feedback);
                trace.Failure("Edit", "Editor unavailable - read shipped unreviewed", reason);
                break;
            }

            predictedSeconds = duration.PredictSeconds(read);
            double drift = predictedSeconds - request.TargetSeconds;
            inDurationBand = Math.Abs(drift) <= _options.DurationToleranceSeconds;
            trace.Measurement("Measure",
                $"Predicted {predictedSeconds:F1}s against a {request.TargetSeconds:F1}s slot",
                predictedSeconds,
                new Dictionary<string, string>
                {
                    ["predictedSeconds"] = predictedSeconds.ToString("F2"),
                    ["targetSeconds"] = request.TargetSeconds.ToString("F2"),
                    ["driftSeconds"] = drift.ToString("F2"),
                    ["words"] = CountWords(read).ToString(),
                    ["revision"] = revision.ToString()
                });

            // Every reason this exact read cannot air, gathered before anything is rewritten.
            //
            // Length is asymmetric here, because the two directions have different repairs.
            //
            // OVERRUN never earns a revision. A long read is compressed by the deterministic slot
            // fitter, which is exactly what the one-sided candidate ladder already assumes, so
            // treating overrun as an LLM problem contradicted the design's own premise and funded
            // the same defect twice. It also could not win: measured 2026-08-29 across 13 dev jobs,
            // 31 of 42 revision calls listed length as their ONLY reason and 8 of 13 jobs spent
            // both revisions and shipped out-of-band regardless. Job 07a833d1 asked four times over
            // at 102, 93, 98 then 83 words and the read priced at 33.6s every time. The target is
            // what is wrong, not the read: on job 4ac7095e the syllable predictor priced a 92-word
            // read at 38.8s and the voice delivered 38.7s - 0.3% error - against a slot handed to
            // the workflow as 34.0s for a 30-second bulletin.
            //
            // UNDERRUN still earns one. Dead air can only be filled with content, and one extra
            // compose-plus-review round is far cheaper than the alternative the engine is forced
            // into: re-drafting the ENTIRE workflow at a corrected target. Cairns job 2f4660c4
            // spent four full workflow runs and 26 LLM calls escalating 70 -> 77 -> 74 -> 83 words
            // before giving up content-limited, and the writer did add words each time it was
            // asked, so the cheap in-workflow attempt is worth making first.
            unresolved = verdict.Approved ? [] : [.. verdict.Feedback];
            var problems = new List<string>(unresolved);

            // The underrun trigger is TIGHT, and deliberately tighter than DurationToleranceSeconds.
            //
            // That knob (1.5s) describes a read close enough to report as on-target. It is not what
            // the audio path will ACCEPT: slowing is disabled, the trimmed-bed closer is off, and the
            // silence pad covers only 0.3s, so anything more than ~0.3s short of the slot is
            // content-limited and burns a whole generation attempt. Judging underrun at 1.5s left a
            // 1.2s band where the workflow declared the read fine and the engine then failed it —
            // dev brisbane lived there: 29.3-29.5s reads against a 30s slot, four attempts, failed.
            // It passed 4 of 4 before this change only because the target it was handed was the
            // OVERSHOT one, which made the same read look 5s short and bought a revision by accident.
            // Now the target is honest, the trigger has to be honest too.
            bool underruns = drift < -EngineSilencePadSeconds;
            if (underruns)
                problems.Add(LengthNote(CountWords(read), predictedSeconds, request.TargetSeconds));

            // ── Ceiling breach earns exactly ONE revision ──
            // Overrun WITHIN the band still earns nothing: the fitter compresses it for free, and
            // spending a model call there is the mistake the 2026-08-29 measurement caught (31 of
            // 42 revision calls listed length as their only reason and 8 of 13 jobs shipped
            // out-of-band anyway).
            //
            // Past the fitter's ceiling the arithmetic is different, because the alternative is no
            // longer free — it is a full TTS re-render, measured at 14.4s median on 8 of 20 dev
            // jobs (2026-08-30). One directive revision costs ~2.4s, so it is worth about six
            // re-renders, and revisions are the accurate call shape: measured adherence sigma 11.9%
            // with median 0.0%, against 19.3% for an initial compose, because the note names an
            // explicit delta instead of a target.
            //
            // Strictly once, and only for a breach. The failure the earlier measurement recorded
            // was a LOOP that never converged (job 07a833d1 asked four times at 102, 93, 98 then 83
            // words and priced 33.6s every time) against a target that was itself wrong. The target
            // is now the true slot, and this cannot loop.
            bool breachesCeiling = predictedSeconds > request.EffectiveMaxSeconds;
            if (breachesCeiling && !spentCeilingRevision && !underruns)
            {
                spentCeilingRevision = true;
                problems.Add(LengthNote(CountWords(read), predictedSeconds, request.TargetSeconds));
                trace.Decision("Measure",
                    $"Read is {predictedSeconds - request.EffectiveMaxSeconds:F1}s past the "
                    + $"{request.EffectiveMaxSeconds:F1}s compression ceiling — spending one revision "
                    + "rather than a re-render");
            }

            if (problems.Count == 0)
            {
                trace.Decision("Edit", $"Editor approved the read after {revision} revision(s)");
                trace.Decision("Measure", inDurationBand
                    ? $"Read is within the duration band after {revision} revision(s)"
                    : $"Read is {drift:F1}s over the slot after {revision} revision(s); inside the "
                      + $"{request.EffectiveMaxSeconds:F1}s compression ceiling, so left to the slot fitter");
                trace.Decision("Compose", "Revision skipped: the reviewed read can air as written");
                break;
            }

            if (!verdict.Approved)
            {
                trace.Decision("Edit", $"Editor rejected the read after {revision} revision(s)",
                    new Dictionary<string, string> { ["feedback"] = string.Join(" | ", verdict.Feedback) });
            }

            if (revision >= maxRevisions)
            {
                // Say plainly what is shipping unfixed. An out-of-band read is a scheduling problem;
                // an unresolved editor concern is an editorial one, and conflating them in the log
                // is how the second becomes invisible.
                trace.Decision("Workflow",
                    $"Revisions exhausted after {revision}; shipping with {unresolved.Count} unresolved editorial concern(s) and a read {Math.Abs(drift):F1}s from the slot",
                    new Dictionary<string, string>
                    {
                        ["unresolved"] = unresolved.Count == 0 ? "none" : string.Join(" | ", unresolved),
                        ["inDurationBand"] = inDurationBand.ToString()
                    });
                break;
            }

            revision++;
            trace.Decision("Compose",
                $"Revising: {problems.Count} problem(s) to fix, {maxRevisions - revision} revision(s) remaining");
            iteration++;
            trace.BeginIteration(iteration);
            // The revision is deliberately NOT re-corrected. Correcting it as well was measured on
            // dev 2026-09-01 and inverted the failure: with up to two revisions each running its
            // own drop round, the read shrank monotonically and three 75-word targets came back at
            // 43, 51 and 50 words. A short read cannot be repaired - the audio path compresses an
            // overrun but can only pad silence under a short one - so an occasional overrun the
            // deterministic trimmer can cut is the better failure, and length is re-imposed once,
            // on the chosen candidate, before the editor ever sees it.
            int revisionAim = targetWords;

            // Continue the composer's own thread where one exists. The fresh re-render is the
            // fallback, not the default: it resent 18,560 characters of identical briefs, lessons
            // and board state per revision and quoted the model's own output back to it.
            Stages.Conversational.ComposerReply revised;
            if (composerThread is { Length: > 0 } thread)
            {
                trace.StageStart("Compose", "Revising the read");
                var reviseSw = Stopwatch.StartNew();
                revised = await _composer.ReviseInThreadAsync(
                        request, thread, revisionAim,
                        new EditorVerdict(false, problems), trace, cancellationToken)
                    .ConfigureAwait(false);
                reviseSw.Stop();
                trace.StageEnd("Compose",
                    string.IsNullOrWhiteSpace(revised.Text)
                        ? "Composer returned no revision"
                        : $"Revised to a {CountWords(revised.Text)}-word read",
                    reviseSw.Elapsed.TotalMilliseconds);
            }
            else
            {
                revised = await ComposeReadAsync(
                        request, input, briefs, revisionAim, read,
                        new EditorVerdict(false, problems), trace, cancellationToken)
                    .ConfigureAwait(false);
            }
            read = revised.Text;
            composerThread = revised.ResponseId ?? composerThread;
            if (string.IsNullOrWhiteSpace(read))
            {
                string error = revised.Error is { Length: > 0 } cause
                    ? $"Composer produced no read while revising: {cause}"
                    : "Composer produced no read while revising";
                trace.Failure("Compose", error, "empty revision");
                trace.Decision("Measure", "Skipped: revision produced no read");
                return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
            }
        }

        IReadOnlyList<ComposedSentence> sentences = ToSentences(read, briefs, input);
        if (sentences.Count == 0)
        {
            const string error = "Final read contained no sentences";
            trace.Failure("Workflow", error, "empty sentence list");
            return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
        }

        var finalRead = new ComposedRead(sentences);
        EmitFinalScript(trace, finalRead);
        // An unavailable editor is distinct from an unresolved rejection: no actionable feedback
        // existed to justify a revision, and the operator must be told the read shipped unreviewed.
        string outcome = editorUnavailable
            ? "succeeded-unreviewed"
            : unresolved.Count > 0
                ? "succeeded-with-notes"
                : !inDurationBand
                    ? "succeeded-out-of-band"
                    : "succeeded";
        trace.Decision("Workflow", $"Returning {outcome}");
        return WorkflowOutcome.Success(finalRead.Script, finalRead.Sentences, trace.Freeze(outcome))
            with
        { ComposerThreadId = composerThread };
    }

    /// <summary>One composed read, with everything needed to choose between it and its siblings.</summary>
    /// <param name="ResponseId">
    ///     The provider conversation this candidate came from, kept so the word loop can continue
    ///     THAT conversation after selection. Candidates are composed in parallel, so the id has to
    ///     travel with each one rather than living on the composer.
    /// </param>
    private sealed record Candidate(
        int AimedWords,
        string Read,
        int Words,
        double PredictedSeconds,
        IReadOnlyList<string> Failures,
        string? ResponseId = null);

    /// <summary>
    ///     Composes several reads concurrently around the caller's exact body-word budget. One
    ///     candidate is on target and its neighbours are slightly under and over; compression
    ///     headroom remains an acceptance limit, never a second word target.
    ///     <para>
    ///         A candidate that throws does not sink the round. One transport failure out of three
    ///         still leaves a read to air, and failing all three is already handled by the caller.
    ///     </para>
    /// </summary>
    private async Task<IReadOnlyList<Candidate>> ComposeCandidatesAsync(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        int targetWords,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        int[] aims = CandidateWordTargets(targetWords);
        trace.Decision("Compose",
            $"Composing {aims.Length} candidate read(s) at {string.Join(", ", aims)} body words "
            + $"around the {targetWords}-word budget");

        var tasks = aims.Select(async aim =>
        {
            try
            {
                var composed = await ComposeReadAsync(
                    request, input, briefs, aim, null, null, trace, cancellationToken).ConfigureAwait(false);
                string read = composed.Text;
                if (string.IsNullOrWhiteSpace(read))
                    return null;

                // Scored with the SAME checks the loop below applies, so a candidate chosen here
                // cannot be rejected by a different standard a moment later.
                var failures = ConversationalChecks.Run(
                        input, read, briefs, trace, request.PreviousScript, traceStage: "CandidateChecks")
                    .Where(c => !c.Passed)
                    .Select(c => c.Detail)
                    .ToList();

                return new Candidate(aim, read, CountWords(read), duration.PredictSeconds(read), failures, composed.ResponseId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                trace.Failure("Compose", $"Candidate aimed at {aim} words failed", exception.Message);
                return null;
            }
        });

        return [.. (await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<Candidate>()];
    }

    /// <summary>
    ///     Symmetric aim points around the exact body budget supplied by the engine. The engine has
    ///     already subtracted the station bookends, so multiplying this target by the compression
    ///     ceiling or an over-aim factor spends those fixed words again. With the default three
    ///     candidates this yields one under, one on target, and one over.
    /// </summary>
    private int[] CandidateWordTargets(int targetWords)
    {
        int count = Math.Max(1, _options.ComposeCandidateCount);
        int safeTarget = Math.Max(1, targetWords);
        if (count == 1)
            return [safeTarget];

        double spread = Math.Max(0, _options.CandidateWordSpread);
        double step = 2 * spread / (count - 1);
        return [.. Enumerable.Range(0, count)
            .Select(i => (int)Math.Round(
                safeTarget * (1 - spread + i * step),
                MidpointRounding.AwayFromZero))
            .Select(words => Math.Max(1, words))
            .Distinct()];
    }

    /// <summary>
    ///     The only shortfall the audio path can absorb without a fresh generation: the silence pad,
    ///     0.3s. Mirrored here as a constant rather than plumbed through, because the Workflow
    ///     project is a clean room over event-shaped evidence and must not take a dependency on
    ///     <c>SpeechDurationOptions</c>. If that pad or the slow-down cap changes, this changes with
    ///     it — a workflow that judges underrun more loosely than the engine hands the engine reads
    ///     it cannot use.
    /// </summary>
    private const double EngineSilencePadSeconds = 0.3;

    /// <summary>
    ///     Picks the read to review.
    ///     <para>
    ///         Clean beats close, always. A candidate that failed a check has said something the
    ///         evidence does not support, and no amount of landing on the slot redeems that; a
    ///         well-fitted wrong read is the worse of the two failures because it looks correct.
    ///     </para>
    ///     <para>
    ///         Among candidates that pass, fit is judged against the BAND, not a single point. The
    ///         band runs from the slot to the fitter's ceiling, and anything inside it is equally
    ///         airworthy: the fitter compresses it deterministically for free. So the winner is the
    ///         LONGEST read inside the band — longest because a read near the ceiling used the slot
    ///         most fully, and inside because a read past the ceiling costs a full re-render.
    ///     </para>
    ///     <para>
    ///         When nothing lands inside the band the two directions are no longer equivalent, and
    ///         the old asymmetric cost decides it: overrun is preferred to underrun by
    ///         <see cref="UnderrunSecondWeight" />, because an overrun is repaired downstream at
    ///         known cost while a short read is dead air only a fresh generation can fix.
    ///     </para>
    ///     <para>
    ///         This replaced minimising distance to the target. That was correct when the target
    ///         was the only number available, but the target handed in was the OVERSHOT aim, so
    ///         "closest to target" meant "closest to the ceiling" — every winner sat at the fail
    ///         line with none of the composer's measured 19.3% variance absorbed. Measured 2026-08-30:
    ///         8 of 20 dev jobs re-rendered, every one of them above the ceiling.
    ///     </para>
    /// </summary>
    private static Candidate? SelectCandidate(
        IReadOnlyList<Candidate> candidates,
        double targetSeconds,
        double maxSeconds,
        TraceRecorder trace)
    {
        if (candidates.Count == 0)
            return null;

        var winner = candidates
            .OrderBy(c => c.Failures.Count == 0 ? 0 : 1)
            .ThenBy(c => c.Failures.Count)
            .ThenBy(c => InBand(c.PredictedSeconds, targetSeconds, maxSeconds) ? 0 : 1)
            .ThenBy(c => Math.Abs(c.PredictedSeconds - targetSeconds))
            .ThenBy(c => FitCost(c.PredictedSeconds, targetSeconds))
            .First();

        foreach (var c in candidates)
        {
            string verdict = c.Failures.Count == 0 ? "clean" : $"{c.Failures.Count} check failure(s)";
            trace.Measurement("Compose",
                $"Candidate aimed at {c.AimedWords} words: {c.Words} words, {c.PredictedSeconds:F1}s, {verdict}"
                + (ReferenceEquals(c, winner) ? " — CHOSEN" : string.Empty),
                c.PredictedSeconds,
                new Dictionary<string, string>
                {
                    ["aimedWords"] = c.AimedWords.ToString(),
                    ["words"] = c.Words.ToString(),
                    ["predictedSeconds"] = c.PredictedSeconds.ToString("F2"),
                    ["driftSeconds"] = (c.PredictedSeconds - targetSeconds).ToString("F2"),
                    ["checkFailures"] = c.Failures.Count == 0 ? "none" : string.Join(" | ", c.Failures),
                    ["chosen"] = ReferenceEquals(c, winner).ToString()
                });
        }

        trace.Decision("Compose",
            $"Chose the candidate aimed at {winner.AimedWords} words: {winner.Words} words, "
            + $"{winner.PredictedSeconds:F1}s against a {targetSeconds:F1}s–{maxSeconds:F1}s band "
            + (InBand(winner.PredictedSeconds, targetSeconds, maxSeconds)
                ? "(inside the band, the fitter absorbs it)"
                : winner.PredictedSeconds > maxSeconds
                    ? "(ABOVE the band — a re-render unless a revision pulls it back)"
                    : "(BELOW the slot — dead-air risk)")
            + ", "
            + (winner.Failures.Count == 0 ? "no check failures" : $"{winner.Failures.Count} check failure(s) to repair"));

        return winner;
    }

    /// <summary>
    ///     True when a predicted read lands in the compressible band: at or above the slot, and at
    ///     or below what the fitter can compress into it. Everything inside is equally airworthy,
    ///     so this is a membership test rather than a distance.
    /// </summary>
    private static bool InBand(double predictedSeconds, double targetSeconds, double maxSeconds) =>
        predictedSeconds >= targetSeconds && predictedSeconds <= maxSeconds;

    /// <summary>
    ///     How much worse a predicted second of underrun is than a second of overrun when choosing
    ///     between candidate reads. Two: a read must be predicted twice as far over the slot as a
    ///     rival is under it before the short rival wins, which within a one-sided six-per-cent
    ///     ladder means the short read practically never wins while genuinely absurd overshoot
    ///     (which the ladder cannot produce) would still lose.
    /// </summary>
    private const double UnderrunSecondWeight = 2.0;

    /// <summary>Asymmetric slot-fit cost: seconds under the slot count double.</summary>
    private static double FitCost(double predictedSeconds, double targetSeconds) =>
        predictedSeconds >= targetSeconds
            ? predictedSeconds - targetSeconds
            : (targetSeconds - predictedSeconds) * UnderrunSecondWeight;

    private async Task<Stages.Conversational.ComposerReply> ComposeReadAsync(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        int aimWords,
        string? previousRead,
        EditorVerdict? correction,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        bool recomposing = previousRead is not null && correction is not null;
        string summary = recomposing ? "Recomposing the read" : "Composing the read";
        trace.StageStart("Compose", summary);
        Stages.Conversational.ComposerReply? reply = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            reply = recomposing
                ? await _composer.RecomposeAsync(
                    request, input, briefs, aimWords, previousRead!, correction!, trace, cancellationToken).ConfigureAwait(false)
                : await _composer.ComposeAsync(
                    request, input, briefs, aimWords, trace, cancellationToken).ConfigureAwait(false);
            return reply;
        }
        finally
        {
            stopwatch.Stop();
            trace.StageEnd("Compose",
                reply is null ? $"{summary} failed"
                : string.IsNullOrWhiteSpace(reply.Text) ? "Composer returned no read"
                : $"{(recomposing ? "Recomposed" : "Composed")} a {CountWords(reply.Text)}-word read",
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<EditorVerdict> ReviewAsync(
        WorkflowRequest request,
        NormalizedInput input,
        BulletinBriefs briefs,
        string read,
        TraceRecorder trace,
        CancellationToken cancellationToken)
    {
        const string summary = "Reviewing the read as a colleague";
        trace.StageStart("Edit", summary);
        Stopwatch stopwatch = Stopwatch.StartNew();
        EditorVerdict? verdict = null;
        try
        {
            verdict = await _editor.ReviewAsync(
                request, input, briefs, read, trace, cancellationToken).ConfigureAwait(false);
            return verdict;
        }
        finally
        {
            stopwatch.Stop();
            trace.StageEnd("Edit",
                verdict is null ? "Editor review failed"
                : verdict.IsUnreviewed ? "Editor unavailable; read left unreviewed"
                : verdict.Approved ? "Editor approved the read"
                : $"Editor asked for a revision ({verdict.Feedback.Count} note(s))",
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    ///     Tells the composer how far off it is, and in which direction to move.
    ///     <para>
    ///         Direction and magnitude both come from SECONDS, never from word counts. The oracle's
    ///         two derivations disagree by design — <c>WordsForSeconds</c> prices a probe sentence
    ///         at a flat rate while <c>PredictSeconds</c> prices the actual text through the shape
    ///         model — so a 37-word read can measure 15.3s against a slot whose word budget said 36
    ///         (19.1s). Comparing the word counts there told a 3.8-second-SHORT read to cut a word,
    ///         the composer obliged, and the loop churned to exhaustion (dev job a6b75762,
    ///         2026-08-27). The read's own measured pace converts the seconds gap into a word count
    ///         the composer can act on.
    ///     </para>
    ///     <para>
    ///         What this cannot leave implicit is the ORDER of the two moves on a big cut: shedding
    ///         a whole story and shaving every sentence both reach the target, and only one of them
    ///         keeps the read speakable. A small cut must say the opposite — dropping a story over a
    ///         couple of words is the larger error.
    ///     </para>
    /// </summary>
    private static string LengthNote(int words, double predictedSeconds, double targetSeconds)
    {
        double drift = predictedSeconds - targetSeconds;
        double secondsPerWord = words > 0 && predictedSeconds > 0 ? predictedSeconds / words : 0;
        int deltaWords = secondsPerWord > 0
            ? Math.Max(1, (int)Math.Round(Math.Abs(drift) / secondsPerWord))
            : Math.Max(1, (int)Math.Round(Math.Abs(drift) * 2.5));
        string measured =
            $"This read is {words} words and prices at about {predictedSeconds:F1} seconds against a {targetSeconds:F1}-second slot.";

        if (drift > 0)
            return deltaWords >= 8
                ? $"{measured} Cut about {deltaWords} words, shedding whole stories before you shed detail."
                : $"{measured} Trim about {deltaWords} words by tightening phrasing; do not drop a story over this.";

        // Naming ONE move as the fix used to strand the composer whenever it had already voiced
        // every story: measured on the Cairns 27.9s slot, it was told to voice a story it had
        // not left out, and returned the same read reworded by two words on 4 of 6 runs. The
        // ladder below is ordered by what costs the least risk, and every rung is reachable.
        return $"{measured} Add about {deltaWords} words: voice a story you left out, or facts you "
            + "left unvoiced, or give the lead a fuller sentence. Do not pad.";
    }

    /// <summary>Publishes the finished read as the prominent viewer artifact, alongside its size.</summary>
    private static void EmitFinalScript(TraceRecorder trace, ComposedRead read) =>
        trace.Artifact("Compose", "Final script", new Dictionary<string, string>
        {
            ["finalScript"] = read.Script,
            ["words"] = read.WordCount.ToString(),
            ["sentences"] = read.Sentences.Count.ToString()
        });

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    ///     Splits the finished read into sentences and tags each with the segment it voices, decided
    ///     from the supplied facts it carries (see <see cref="ConversationalChecks.SegmentOfSentence" />).
    ///     A weather or fuel sentence also carries the matching synthetic evidence id so the
    ///     persisted lineage names its source without a downstream word list.
    /// </summary>
    private static IReadOnlyList<ComposedSentence> ToSentences(
        string script, BulletinBriefs briefs, NormalizedInput input)
    {
        var sentences = new List<ComposedSentence>();
        int start = 0;
        for (int index = 0; index < script.Length; index++)
        {
            if (script[index] is not ('.' or '!' or '?'))
                continue;

            AddSentence(script[start..(index + 1)], sentences, briefs, input);
            start = index + 1;
        }

        AddSentence(script[start..], sentences, briefs, input);
        return sentences;
    }

    private static void AddSentence(
        string text, List<ComposedSentence> sentences, BulletinBriefs briefs, NormalizedInput input)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;

        BulletinSegment? section = ConversationalChecks.SegmentOfSentence(trimmed, briefs, input);
        IReadOnlyList<string> evidence = section switch
        {
            BulletinSegment.Weather => ["weather:line"],
            BulletinSegment.Fuel => ["fuel:line"],
            _ => []
        };
        sentences.Add(new ComposedSentence(sentences.Count + 1, trimmed, evidence, Section: section));
    }
    /// <summary>
    ///     Corrects a measured slot miss by continuing the composer response that authored the
    ///     current read. This is intentionally a narrow retry: it does not call either writer desk
    ///     or compose candidate ladder, then applies the same deterministic checks and one editor
    ///     pass used by <see cref="RunAsync" /> before returning the revised read.
    /// </summary>
    /// <param name="request">The evidence and station configuration used for the original run.</param>
    /// <param name="composerThreadId">Provider response id returned by the original composer call.</param>
    /// <param name="currentRead">The read whose natural duration missed the slot.</param>
    /// <param name="measuredSeconds">Natural spoken duration measured for <paramref name="currentRead" />.</param>
    /// <param name="cancellationToken">Cancellation requested by the workflow host.</param>
    public async Task<WorkflowOutcome> ReviseForMeasuredDurationAsync(
        WorkflowRequest request,
        string composerThreadId,
        string currentRead,
        double measuredSeconds,
        CancellationToken cancellationToken = default)
    {
        GenerationProgressTraceRelay? progressRelay = request.ProgressReporter is { } reporter
            ? new GenerationProgressTraceRelay(reporter)
            : null;
        var trace = new TraceRecorder(
            runId: $"{_time.GetUtcNow():yyyyMMdd-HHmmss}-{NextRunOrdinal():D3}-{request.Market}-{Id}-duration-revision",
            workflowId: Id,
            workflowDescription: Description,
            jobId: request.JobId,
            market: request.Market,
            station: request.Station,
            targetSeconds: request.TargetSeconds,
            timeProvider: _time,
            onEvent: progressRelay is null ? null : progressRelay.Observe);

        try
        {
            if (string.IsNullOrWhiteSpace(composerThreadId)
                || string.IsNullOrWhiteSpace(currentRead)
                || measuredSeconds <= 0
                || !double.IsFinite(measuredSeconds))
            {
                const string error = "Measured-duration revision requires a composer thread, read and positive duration.";
                trace.Failure("Compose", error, "invalid revision input");
                return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
            }

            trace.BeginIteration(1);
            NormalizedInput input = Normalizer.Normalize(request, trace);
            if (input.Events.Count == 0)
            {
                const string error = "No events in the supplied evidence";
                trace.Failure("Normalize", error, "empty evidence");
                trace.Decision("Write", "Skipped: measured-duration revision never invokes writer desks");
                trace.Decision("Compose", "Skipped: normalized evidence was empty");
                trace.Decision("Edit", "Skipped: normalized evidence was empty");
                return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
            }

            // The original writer briefs are intentionally not regenerated. This compact,
            // evidence-derived view supplies the same checks/editor boundary without paying for
            // either writer desk, while the continuation itself still has the original briefs in
            // its provider conversation.
            BulletinBriefs briefs = RevisionBriefs(request, input);
            trace.Decision("Write", "Skipped: continuing a measured-duration composer revision");
            trace.Decision("Compose",
                $"Continuing the existing composer thread for a {measuredSeconds:F1}s read against a "
                + $"{request.TargetSeconds:F1}s slot");

            ComposerReply revised = await _composer.ReviseForMeasuredDurationAsync(
                request, composerThreadId, currentRead, measuredSeconds, trace, cancellationToken)
                .ConfigureAwait(false);
            string read = revised.Text;
            if (string.IsNullOrWhiteSpace(read))
            {
                string error = revised.Error is { Length: > 0 } cause
                    ? $"Composer produced no read while correcting measured duration: {cause}"
                    : "Composer produced no read while correcting measured duration";
                trace.Failure("Compose", error, "empty continuation");
                trace.Decision("Edit", "Skipped: composer continuation produced no read");
                return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));

            }
            var checks = ConversationalChecks.Run(
                input, read, briefs, trace, request.PreviousScript, traceStage: "Gates");
            var checkFailures = checks.Where(check => !check.Passed).Select(check => check.Detail).ToList();

            // Unlike the normal loop, this entry point has exactly one correction turn and exactly
            // one editor pass. The deterministic failures remain part of the final notes rather
            // than silently suppressing the independent editor call.
            EditorVerdict verdict = await ReviewAsync(
                request, input, briefs, read, trace, cancellationToken).ConfigureAwait(false);

            var unresolved = new List<string>(checkFailures);
            if (!verdict.Approved)
                unresolved.AddRange(verdict.Feedback);

            if (verdict.IsUnreviewed)
            {
                string reason = string.Join("; ", verdict.Feedback);
                trace.Failure("Edit", "Editor unavailable - read shipped unreviewed", reason);
            }
            else if (unresolved.Count > 0)
            {
                trace.Decision("Edit",
                    $"Measured-duration revision returned with {unresolved.Count} unresolved concern(s)",
                    new Dictionary<string, string>
                    {
                        ["feedback"] = string.Join(" | ", unresolved)
                    });
            }
            else
            {
                trace.Decision("Edit", "Editor approved the measured-duration revision");
            }

            IReadOnlyList<ComposedSentence> sentences = ToSentences(read, briefs, input);
            if (sentences.Count == 0)
            {
                const string error = "Measured-duration revision contained no sentences";
                trace.Failure("Workflow", error, "empty sentence list");
                return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
            }

            var finalRead = new ComposedRead(sentences);
            EmitFinalScript(trace, finalRead);
            string outcome = verdict.IsUnreviewed
                ? "succeeded-unreviewed"
                : unresolved.Count > 0
                    ? "succeeded-with-notes"
                    : "succeeded";
            trace.Decision("Workflow", $"Returning {outcome} from measured-duration revision");
            return WorkflowOutcome.Success(finalRead.Script, finalRead.Sentences, trace.Freeze(outcome))
                with
            { ComposerThreadId = revised.ResponseId ?? composerThreadId };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            trace.Failure("Workflow", "Unhandled measured-duration revision exception", exception.ToString());
            return WorkflowOutcome.Failure(exception.Message, trace.Freeze("failed", exception.Message));
        }
    }

    private static BulletinBriefs RevisionBriefs(WorkflowRequest request, NormalizedInput input)
    {
        static string SegmentOf(BulletinEventKind kind) => kind switch
        {
            BulletinEventKind.Weather => "weather",
            BulletinEventKind.Fuel => "fuel",
            _ => "traffic"
        };

        int SegmentIndex(BulletinEvent evt)
        {
            string segment = SegmentOf(evt.Kind);
            if (request.SegmentOrder is not { Count: > 0 } configured)
                return int.MaxValue;

            for (int index = 0; index < configured.Count; index++)
            {
                if (string.Equals(configured[index], segment, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return int.MaxValue;
        }

        var stories = input.Events
            .Select((evt, index) => (Event: evt, OriginalIndex: index))
            .OrderBy(item => SegmentIndex(item.Event))
            .ThenBy(item => item.OriginalIndex)
            .Select((item, index) => new StoryBrief(
                index + 1,
                item.Event.EventId,
                item.Event.Label,
                [item.Event.EventId],
                item.Event.Rank,
                string.Empty,
                input.RequiredEventIds.Contains(item.Event.EventId, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        return new BulletinBriefs(stories);
    }
}

/// <summary>
///     Bounds on the conversational revision loop.
///     <para>
///         There is deliberately ONE budget. Separate editor and length budgets described a
///         pipeline where the two ran in sequence, and that sequence was the defect: a length
///         rewrite spent from its own budget after the editor had finished, so the read that aired
///         had never been reviewed. A revision is a revision, whatever prompted it, and each one
///         costs the same compose-plus-review round trip.
///     </para>
/// </summary>
public sealed record ConversationalWorkflowOptions
{
    /// <summary>
    ///     How many times a read may be rewritten before it ships as it stands. Zero means the
    ///     first composition is reviewed and then aired whatever the verdict.
    /// </summary>
    public int MaxRevisions { get; init; } = 2;

    public double DurationToleranceSeconds { get; init; } = 1.5;

    /// <summary>
    ///     How many reads to compose concurrently before choosing one.
    ///     <para>
    ///         One restores the old behaviour exactly. Three is the default because the choice is
    ///         made against checks that cost nothing to run, so the only price is tokens, and it is
    ///         paid once instead of on every sequential retry it replaces.
    ///     </para>
    /// </summary>
    /// <para>
    ///     Held at THREE, not five. Five was deployed to dev on 2026-08-30 and reverted the same
    ///     day: with four markets firing together it put 20 concurrent completions on the model
    ///     instead of 9, and empty completions appeared for the first time — 2 of 42 compose2 calls
    ///     and 1 of 3 writeIncidents, against ZERO empties in 221 captured calls beforehand. The
    ///     compose2 empties were harmless (a null candidate is filtered out), but the empty
    ///     writeIncidents silently dropped three road closures from an aired Adelaide bulletin.
    ///     Raise this again only once an empty desk reply can no longer degrade a read silently.
    /// </para>
    public int ComposeCandidateCount { get; init; } = 3;

    /// <summary>
    ///     Maximum number of same-conversation whole-item deletion turns the composer may use before
    ///     the editor sees a read. The default of two reflects the measured correction loop: naming
    ///     specific items landed in band 4 of 5 times, then 12 of 12 in validation. Short reads are
    ///     never sent through this loop because continuation could not reliably extend them.
    /// </summary>
    public int MaxWordLoopRounds { get; init; } = 2;

    /// <summary>
    ///     How far the outer candidate aims sit below and above the exact body-word budget.
    ///     Proportional rather than absolute: three words is a useful spread on a 47-word body but
    ///     too wide on a 14-second bulletin and too narrow on a long one.
    /// </summary>
    public double CandidateWordSpread { get; init; } = 0.06;
}
