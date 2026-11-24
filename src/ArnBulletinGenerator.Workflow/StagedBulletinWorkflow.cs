using System.Diagnostics;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow;

/// <summary>
///     The staged workflow: <c>EVIDENCE -> MEANING -> SELECTION -> DISCOURSE -> WORDS -> VERIFICATION</c>.
///     <para>
///         Its one governing rule is that <b>the fitter fits FACTS, never finished prose</b>. Content is
///         chosen while it is still a semantic plan, so adding or removing it costs nothing: there is no
///         connective to strand, no ordering to invalidate, no phrasing to re-pick. Only once the content
///         and the order are final does a single composer write the read, and it is told — truthfully —
///         that nothing downstream will reorder, split or rewrite what it produces.
///     </para>
///     <para>
///         The predecessor pipeline inverted this. It wrote sentences first and assembled a bulletin from
///         them afterwards, which forced the writer to produce interchangeable stand-alone units; a
///         reviewer was then instructed to flag any sentence that "only reads correctly in one position"
///         as a defect. Between them those two instructions prohibit every linguistic mechanism that
///         creates flow, and the measured result was a database read-out: road, suburb, condition,
///         repeated down the bulletin.
///     </para>
/// </summary>
public sealed class StagedBulletinWorkflow(
    IWorkflowLlm llm,
    IDurationOracle duration,
    StagedWorkflowOptions? options = null,
    TimeProvider? timeProvider = null) : IBulletinWorkflow
{
    private readonly StagedWorkflowOptions _options = options ?? new StagedWorkflowOptions();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Planner _planner = new(llm);
    private readonly Composer _composer = new(llm);
    private readonly Grounder _grounder = new(llm);

    private static int _runOrdinal;

    /// <summary>
    ///     Distinguishes runs that start inside the same second. Without it a test suite that
    ///     exercises the workflow several times per second silently overwrites its own traces, and
    ///     the evidence the run existed to produce is lost.
    /// </summary>
    private static int NextRunOrdinal() => Interlocked.Increment(ref _runOrdinal);

    public string Id => "staged-v1";

    public string Description =>
        "Plan (LLM) -> fit facts (code) -> compose whole read (LLM) -> verify (code + LLM entailment)";

    public async Task<WorkflowOutcome> RunAsync(WorkflowRequest request, CancellationToken cancellationToken = default)
    {
        var trace = new TraceRecorder(
            runId: $"{_time.GetUtcNow():yyyyMMdd-HHmmss}-{NextRunOrdinal():D3}-{request.Market}-{Id}",
            workflowId: Id,
            workflowDescription: Description,
            jobId: request.JobId,
            market: request.Market,
            station: request.Station,
            targetSeconds: request.TargetSeconds,
            timeProvider: _time);

        try
        {
            return await ExecuteAsync(request, trace, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A workflow that throws loses its trace, and a run nobody can inspect teaches nothing.
            trace.Failure("Workflow", "Unhandled exception", ex.ToString());
            return WorkflowOutcome.Failure(ex.Message, trace.Freeze("failed", ex.Message));
        }
    }

    public Task<WorkflowOutcome> ReviseForMeasuredDurationAsync(
        WorkflowRequest request,
        string composerThreadId,
        string currentRead,
        double measuredSeconds,
        CancellationToken cancellationToken = default)
    {
        var trace = new TraceRecorder(
            runId: $"{_time.GetUtcNow():yyyyMMdd-HHmmss}-{NextRunOrdinal():D3}-{request.Market}-{Id}-revision",
            workflowId: Id,
            workflowDescription: Description,
            jobId: request.JobId,
            market: request.Market,
            station: request.Station,
            targetSeconds: request.TargetSeconds,
            timeProvider: _time);
        const string error = "The staged workflow does not own a composer conversation to continue.";
        trace.Failure("Revision", error, $"Measured {measuredSeconds:F1}s; thread {composerThreadId}.");
        return Task.FromResult(WorkflowOutcome.Failure(error, trace.Freeze("failed", error)));
    }

    private async Task<WorkflowOutcome> ExecuteAsync(
        WorkflowRequest request, TraceRecorder trace, CancellationToken cancellationToken)
    {
        // MEANING. Flat evidence lines become events, so every later stage reasons about things
        // rather than about rows.
        var input = Normalizer.Normalize(request, trace);
        if (input.Events.Count == 0)
        {
            trace.Failure("Normalize", "No events in the supplied evidence", "empty evidence");
            return WorkflowOutcome.Failure("No events in the supplied evidence", trace.Freeze("failed"));
        }

        // The plan is authored ONCE. Duration pressure changes which parts of it are voiced, never
        // what the editorial judgement was: re-planning per attempt would let a slot overshoot
        // silently rewrite the running order, which is exactly the instability being designed out.
        var plan = await _planner.PlanAsync(request, input, trace, cancellationToken);
        if (plan.Stories.Count == 0)
        {
            trace.Failure("Plan", "Planner produced no stories", "empty plan");
            return WorkflowOutcome.Failure("Planner produced no stories", trace.Freeze("failed"));
        }

        int targetWords = duration.WordsForSeconds(request.TargetSeconds);
        trace.Decision("Fit", $"Slot {request.TargetSeconds:F1}s budgets {targetWords} words",
            new Dictionary<string, string>
            {
                ["targetSeconds"] = request.TargetSeconds.ToString("F2"),
                ["targetWords"] = targetWords.ToString()
            });

        FittedPlan fitted = FittedPlan.Empty;
        ComposedRead best = ComposedRead.Empty;
        ComposedRead candidate = ComposedRead.Empty;
        double bestDistance = double.MaxValue;
        string? bestError = null;
        string? lastError = null;

        for (int iteration = 1; iteration <= _options.MaxFitIterations; iteration++)
        {
            trace.BeginIteration(iteration);
            cancellationToken.ThrowIfCancellationRequested();

            // SELECTION. Pure code spends the budget on semantic content.
            fitted = iteration == 1
                ? Fitter.Fit(plan, input, targetWords, trace)
                : Fitter.Refit(fitted, plan, input, targetWords, trace);

            if (fitted.Stories.Count == 0)
            {
                lastError = "Nothing fits the slot";
                trace.Failure("Fit", lastError, lastError);
                break;
            }

            // DISCOURSE + WORDS. One model writes the whole read, then gets at most a bounded number
            // of whole-script revisions. Repair is never sentence-scoped: a listy read cannot be
            // fixed one sentence at a time, because the defect lives between the sentences.
            var read = await _composer.ComposeAsync(request, input, fitted, trace, cancellationToken);

            // Surface under-production explicitly. A read materially below its fitted budget is a
            // distinct fault with the OPPOSITE fix from a duration miss: either the fitter did not
            // buy enough (fix the selection) or the composer did not voice what it bought (fix the
            // writing). Confusing them is what drove the live treadmill on 2026-08-26: budgets of
            // 82, then 74, then 59 words came back as 69, 66, 52 — each shrink was read as a
            // budget signal, the fitter re-bought less, and the read collapsed pass by pass. Name
            // the gap in the trace so the two diagnoses stay separable.
            if (read.Sentences.Count > 0 && read.WordCount < 0.85 * fitted.TargetWords)
            {
                trace.Decision("Compose",
                    $"Under-produced: fitted budget {fitted.TargetWords} words, composed {read.WordCount}",
                    new Dictionary<string, string>
                    {
                        ["budgetedWords"] = fitted.TargetWords.ToString(),
                        ["actualWords"] = read.WordCount.ToString()
                    });
            }

            for (int revision = 0; revision <= _options.MaxRevisions; revision++)
            {
                if (read.Sentences.Count == 0)
                {
                    lastError = "Composer produced no sentences";
                    break;
                }

                candidate = read;

                // VERIFICATION, gated: HARD CHECKS -> (fail) revise, re-entering here;
                // (pass) GROUND -> (fail) revise, back to hard checks; (pass) accept. Entailment
                // is the expensive stage, so it runs only on a read the cheap stage already
                // cleared. Measured live 2026-08-26: iterations 1 and 3 each grounded a read that
                // had already failed four deterministic checks — 9s and 14s of entailment spent on
                // scripts the code knew could not ship. WordTargetBand is informational only and
                // must not gate grounding; MaxRevisions bounds the combined loop either way.
                var checks = HardChecks.Run(request, input, fitted, read, trace);
                var failedChecks = checks
                    .Where(c => !c.Passed && !string.Equals(c.Name, "WordTargetBand", StringComparison.Ordinal))
                    .ToList();

                var problems = new List<string>();
                if (failedChecks.Count > 0)
                {
                    problems.AddRange(failedChecks.Select(c => c.SentenceOrder is null
                        ? $"{c.Name}: {c.Detail}"
                        : $"Sentence {c.SentenceOrder} {c.Name}: {c.Detail}"));

                    // The trace must show why a stage did not run; otherwise every later reader
                    // pays again to rediscover that grounding was skipped deliberately.
                    trace.Decision("Verify",
                        $"Grounding skipped: {failedChecks.Count} deterministic check(s) already failed",
                        failedChecks.Select((c, i) => (Key: i, Text: $"{c.Name}: {c.Detail}"))
                            .ToDictionary(x => $"failed-{x.Key + 1}", x => x.Text));
                }
                else
                {
                    var grounding = await _grounder.GroundAsync(request, input, read, trace, cancellationToken);
                    problems.AddRange(grounding.Contradicted
                        .Select(c => $"Sentence {c.SentenceOrder} states \"{c.Claim}\", which the data contradicts. Remove or correct it."));
                    problems.AddRange(grounding.Unsupported
                        .Select(c => $"Sentence {c.SentenceOrder} states \"{c.Claim}\", which no supplied evidence supports. Drop the claim."));
                }

                if (problems.Count == 0)
                {
                    lastError = null;
                    break;
                }

                if (revision == _options.MaxRevisions)
                {
                    lastError = $"{problems.Count} unresolved problem(s) after {revision} revision(s)";
                    trace.Decision("Verify", lastError);
                    break;
                }

                // Repair is whole-script and re-enters at the cheap gate above. A defect that lives
                // BETWEEN sentences cannot be fixed one sentence at a time.
                trace.Decision("Verify", $"Revising whole read: {problems.Count} problem(s)",
                    problems.Select((p, i) => (i, p)).ToDictionary(x => $"problem-{x.i + 1}", x => x.p));
                read = await _composer.ReviseAsync(request, input, fitted, read, problems, trace, cancellationToken);
            }

            if (candidate.Sentences.Count == 0)
                break;

            // Duration is the ONLY reason to re-fit. Correctness never buys more words, and a
            // timing miss never triggers a rewrite from scratch: it adjusts the fact budget and
            // recomposes, which is the cheap direction.
            double predicted = duration.PredictSeconds(candidate.Script);
            double drift = predicted - request.TargetSeconds;
            trace.Measurement("Measure", $"Predicted {predicted:F1}s against a {request.TargetSeconds:F1}s slot",
                predicted, new Dictionary<string, string>
                {
                    ["predictedSeconds"] = predicted.ToString("F2"),
                    ["targetSeconds"] = request.TargetSeconds.ToString("F2"),
                    ["driftSeconds"] = drift.ToString("F2"),
                    ["words"] = candidate.WordCount.ToString()
                });

            // Measured live 2026-08-26: adelaide produced 104 words on iteration 2 and 46 on
            // iteration 3, and the loop shipped the 46. Keeping the LAST candidate throws away a
            // better one the run already paid for, so score every candidate and retain the closest
            // to the slot, breaking ties toward the one with no outstanding problems.
            double distance = Math.Abs(drift);
            bool cleaner = lastError is null && bestError is not null;
            if (best.Sentences.Count == 0 || cleaner || (lastError is null) == (bestError is null) && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
                bestError = lastError;
            }

            if (distance <= _options.DurationToleranceSeconds)
            {
                if (lastError is null)
                {
                    trace.Decision("Measure", $"Landed the slot on iteration {iteration}");
                    EmitFinalScript(trace, candidate);
                    return WorkflowOutcome.Success(candidate.Script, candidate.Sentences, trace.Freeze("succeeded"));
                }
                break;
            }

            // Correct ASYMMETRICALLY, because the two directions are not the same fault.
            //
            // Too LONG: the budget genuinely bought too much. Aim at what the composer actually
            // produced and damp the step — correcting from the nominal target re-applies the whole
            // miss on top, which measured live as an undamped oscillation (75 -> 104 -> 56 words,
            // never converging). This is the existing rule and it is correct.
            //
            // Too SHORT: the slot is a physical fact and under-production is a composer failure,
            // NOT a budget signal, so the target NEVER falls on an underrun. Raise it instead,
            // anchored on the CURRENT TARGET rather than on achieved words so the fitter buys MORE
            // content, clamped to what the slot can hold at the oracle's rate.
            //
            // Measured live 2026-08-26, the symmetric rule moved the goalposts toward the failure:
            // 85 budgeted words came back as 54, the controller aimed at 73, the fitter bought less
            // for 73, and the read shrank 85 -> 73 -> 71 words with Barwell Avenue dropped at 71.
            double secondsPerWord = Math.Max(0.0001, duration.SecondsForWords(1));
            int achieved = candidate.WordCount;
            int correction = (int)Math.Round(_options.CorrectionDamping * drift / secondsPerWord);
            int stepped;
            int ceiling;
            if (drift > 0)
            {
                stepped = achieved - correction;
                ceiling = duration.WordsForSeconds(request.TargetSeconds * 2);
            }
            else
            {
                stepped = targetWords - correction; // drift < 0, so this RAISES the target

                // The ceiling must exceed nominal slot capacity. The composer writes FEWER words
                // than it is budgeted - measured live 2026-08-26, brisbane returned 56 against 85 -
                // so landing a 30-second slot needs a budget above what 30 seconds nominally holds.
                // Clamping the raise to WordsForSeconds(TargetSeconds) capped it at the value it
                // already had, which made the whole underrun branch a no-op: adelaide, brisbane and
                // melbourne each re-ran an IDENTICAL fit three times and paid four extra LLM calls
                // to reach the same selection. The clamp is a safety rail, not the control law.
                ceiling = duration.WordsForSeconds(request.TargetSeconds * 2);
            }

            int previousTarget = targetWords;
            targetWords = Math.Clamp(stepped, 1, ceiling);

            // A correction that changes nothing cannot change the fit, and recomposing an identical
            // plan only resamples the same prompt: brisbane's three passes produced 56, 56 and 59
            // words. Stop and keep the best candidate rather than paying for the resample.
            if (targetWords == previousTarget)
            {
                trace.Decision("Measure",
                    $"{(drift > 0 ? "Over" : "Under")} by {distance:F1}s at {achieved} words, but the budget "
                    + $"is already {targetWords} words and cannot move. Keeping the best read.");
                break;
            }

            trace.Decision("Measure",
                $"{(drift > 0 ? "Over" : "Under")} by {distance:F1}s at {achieved} words - refitting to {targetWords} words");
        }

        if (best.Sentences.Count == 0)
        {
            string error = lastError ?? "Workflow produced no read";
            return WorkflowOutcome.Failure(error, trace.Freeze("failed", error));
        }

        // A read that missed the band or kept a problem is still the best evidence of what this
        // configuration does, so it is returned rather than discarded — the caller decides whether
        // it airs, and the trace explains exactly what it settled for.
        lastError = bestError;
        trace.Decision("Workflow", lastError is null
            ? "Returning best read: duration band not met"
            : $"Returning best read with unresolved problems: {lastError}");
        EmitFinalScript(trace, best);
        return new WorkflowOutcome(best.Script, best.Sentences,
            trace.Freeze(lastError is null ? "succeeded-out-of-band" : "succeeded-with-problems", lastError));
    }

    /// <summary>
    ///     Publishes the finished read under a key the trace viewer renders prominently as copy.
    ///     Radio writing is judged by reading it, not by scanning a data table.
    /// </summary>
    private static void EmitFinalScript(TraceRecorder trace, ComposedRead read) =>
        trace.Artifact("Compose", "Final script", new Dictionary<string, string>
        {
            ["finalScript"] = read.Script,
            ["words"] = read.WordCount.ToString(),
            ["sentences"] = read.Sentences.Count.ToString()
        });

}

/// <summary>Bounds on the workflow's loops. Every one exists to stop a bad run costing unbounded money.</summary>
public sealed record StagedWorkflowOptions
{
    /// <summary>Re-fits allowed for duration. Each costs one compose call.</summary>
    public int MaxFitIterations { get; init; } = 3;

    /// <summary>Whole-script revisions allowed per fit, for correctness problems.</summary>
    public int MaxRevisions { get; init; } = 1;

    /// <summary>How far off the slot is acceptable before the fact budget is adjusted.</summary>
    public double DurationToleranceSeconds { get; init; } = 1.0;

    /// <summary>
    ///     Fraction of a measured duration error fed back into the next word budget. Below 1.0
    ///     because the estimator and the composer both carry error: applying the whole correction
    ///     assumes the budget alone caused the miss, and measured live that produced an undamped
    ///     oscillation rather than convergence.
    /// </summary>
    public double CorrectionDamping { get; init; } = 0.6;
}
