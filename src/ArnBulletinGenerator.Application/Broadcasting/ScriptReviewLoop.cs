using System.Diagnostics;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Validation;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Owns the review/refine convergence loop and the quality gates that guard it: the main
///     review-refine ratchet (LLM review + deterministic detectors + a monotone commit gate), the
///     post-exhaustion final grounding hard gate, the violation-count scoring the polish audit
///     also relies on, the mandatory style gate, and content safety (wordlist + AI safety
///     reviewer). Extracted from <see cref="ScriptGenerationEngine" /> — see that class for the
///     fields it did not need.
/// </summary>
internal sealed class ScriptReviewLoop(
    IScriptReviewer scriptReviewer,
    IScriptRefiner scriptRefiner,
    IOptions<BroadcastOptions> options,
    IBroadcastMetrics metrics,
    IContentSafetyFilter contentSafetyFilter,
    IScriptSafetyReviewer scriptSafetyReviewer,
    ILogger<BroadcastOrchestrator> logger,
    JobStatusUpdater jobStatusUpdater,
    // Needed only to resolve the daypart for the spoken-form sweep on the way out: this loop is the
    // last thing to mutate the text, so it is the last place daypart wording can be corrected.
    TimeProvider timeProvider)
{
    private const int MaxReviewRefineRetriesCeiling = 3;

    // Bounded retries for one final grounding pass when the main review-refine loop EXHAUSTS
    // without ever reaching a clean "approved" verdict (see the loop's post-exhaustion block in
    // ReviewAndRefineScriptAsync). Deliberately tighter than the main loop's own budget — this is
    // a last-chance safety net, not a second full refine cycle. Observed live 2026-07-07 (job
    // 4417add2): attempt 3's OWN review feedback told the refiner to change a CORRECT location
    // ("at Baudesert Road") into a WRONG one borrowed from a different incident ("near
    // Beenleigh") — the loop then exhausted at 3/3 without re-validating that refine, and the
    // corrupted text shipped. The six deterministic gates above never catch this class (a real
    // place name swapped for another road's real place name); only a scriptReviewer call —
    // grounded against the source data — reliably does, and it DID catch this exact error a
    // moment later inside the (narrower-scoped) word-target expansion grounding check.
    private const int MaxFinalGroundingAttempts = 2;
    private static string RepairLogSuffix(int refineCount) =>
        refineCount > 0 ? $" after {refineCount} refine(s)" : string.Empty;


    private readonly BroadcastOptions _options = options.Value;
    private static IReadOnlyList<string> StationLines(BroadcastWithTraffic context)
    {
        var lines = new List<string>();
        if (context.Bookends.HasValue)
        {
            lines.Add(context.Bookends.Value.IntroText);
            lines.Add(context.Bookends.Value.OutroText);
        }
        lines.AddRange(context.FillerStatements.Select(statement => statement.Text));
        return lines;
    }

    // Deterministic violation counts used by the review-refine ratchet: how many FIDELITY/SAFETY
    // defects (blocking) and STYLE defects a script trips. Mirrors the inline detector set used for
    // the approval gate so a refined candidate is scored on the same axes as the current script.
    internal static (int Blocking, int Style) ComputeViolationCounts(
        string script,
        BroadcastWithTraffic context,
        IReadOnlyList<DraftedSentence>? draftedSentences = null)
    {
        var rollCall = RollCallDetector.Detect(script, context);
        var dupAnchor = DuplicateAnchorDetector.Detect(script, context);
        var metaLeak = MetaLeakDetector.Detect(script);
        var typeStack = IncidentTypeStackDetector.Detect(script);
        var repeatedPrep = RepeatedPrepositionDetector.Detect(script);
        var impactStack = ImpactStackDetector.Detect(script);
        var fakeLocation = FakeLocationDetector.Detect(script);
        var sentenceClone = SentenceCloneDetector.Detect(script, context);
        var unselectedSource = UnselectedSourceDetector.Detect(script, context);
        var requiredSource = RequiredSourceDetector.Detect(script, context);
        var sectionOrder = SectionOrderDetector.Detect(script, context);
        var suburbRetention = SuburbRetentionDetector.Detect(script, context);
        var evidenceRestatement = EvidenceRestatementDetector.Detect(draftedSentences);
        var stateRep = RepeatedStatePhraseDetector.Detect(script);
        var falseClosure = FalseClosureDetector.Detect(script, context);
        int blocking = (metaLeak.HasMetaLeak ? 1 : 0) + (typeStack.HasTypeStack ? 1 : 0)
            + (fakeLocation.HasFakeLocation ? 1 : 0) + (unselectedSource.HasFabrication ? 1 : 0)
            + (requiredSource.HasMissingSource ? 1 : 0)
            + (suburbRetention.HasMissingSuburb ? 1 : 0)
            + (evidenceRestatement.HasRestatement ? 1 : 0)
            + (sentenceClone.HasClone ? 1 : 0)
            + (falseClosure.HasFalseClosure ? 1 : 0);
        var splitClear = SplitClearRunDetector.Detect(script);
        var connectorFreq = ConnectorFrequencyDetector.Detect(script);
        var redundantTail = RedundantTailDetector.Detect(script);
        var vagueness = VaguenessDetector.Detect(script);
        var secondaryCap = SecondaryItemCapDetector.Detect(script, context);
        // Shape defects the writer prompt already forbids but nothing enforced: a run-on carrying
        // three roads, a semicolon (no spoken equivalent), a staccato run of "road is state"
        // sentences, or two sentences opening on the same frame. All four aired on 2026-08-23 past
        // every existing gate, because each read was factually correct and fitted its slot. Every
        // instance is now pinned in test/corpus/subpar-reads.json.
        var sentenceShape = SentenceShapeDetector.Detect(script, StationLines(context));
        // A cause stated once per item instead of once per GROUP (2026-08-23): the Gold Coast read
        // aired four closures each restating "for the Gold Coast Marathon", where a presenter names
        // the cause once and sweeps the roads. Counted as style so the conditional audit can never
        // skip the LLM review on a read carrying it — and because it is RELATIONAL (a repetition
        // across sentences) the sentence-scoped refiner cannot repair it, so the value here is
        // routing it to a fresh draft via the final grounding gate's now-Retryable disposition
        // rather than pretending a local repair exists.
        var repeatedCause = RepeatedCauseDetector.Detect(script, context);
        int style = (rollCall.HasRollCall ? 1 : 0) + (dupAnchor.HasDuplicateAnchor ? 1 : 0)
            + (secondaryCap.HasExcessItems ? 1 : 0)
            + (sentenceShape.HasIssue ? 1 : 0)
            + (repeatedCause.HasRepeatedCause ? 1 : 0)
            + (repeatedPrep.HasRepeatedPreposition ? 1 : 0) + (impactStack.HasImpactStack ? 1 : 0)
            + (sectionOrder.HasOutOfOrderSection ? 1 : 0)
            + (stateRep.HasExcessRepetition ? 1 : 0) + (splitClear.HasSplitClearRun ? 1 : 0)
            + (connectorFreq.HasOveruse ? 1 : 0) + (redundantTail.HasRedundantTail ? 1 : 0)
            + (vagueness.HasVagueness ? 1 : 0);
        return (blocking, style);
    }

    /// <summary>
    ///     Everything the assembled read needs AFTER a sentence has been removed, in one place so
    ///     the two drop sites below cannot drift apart:
    ///     <list type="number">
    ///         <item>re-splice a required fixture the dropped sentence was carrying, and</item>
    ///         <item>
    ///             clear a back-reference the survivor now has nothing to point at. Dropping the
    ///             leading claim promotes the next sentence, and its additive connective ("also",
    ///             "as well") silently becomes a reference to a sentence that no longer exists —
    ///             dev job 54befeec (2026-08-24) opened "Roadworks are ALSO slowing the Bruce
    ///             Highway" as the first thing said about the roads, because the Captain Cook
    ///             Highway sentence it was adding to had just been dropped as unsupported. Every
    ///             surviving sentence was individually clean; only their order was wrong.
    ///         </item>
    ///     </list>
    /// </summary>
    private static (string Script, string LogSuffix) RepairAfterDrop(
        string withoutSentence, BroadcastWithTraffic context)
    {
        string afterFixtures = SpliceRequiredFixtures(withoutSentence, context);
        string afterConnective = OrphanedConnectiveDetector.Repair(afterFixtures, context);
        string suffix = afterFixtures.Length != withoutSentence.Length
            ? "; the required fixture it carried was re-spliced from the fetched data"
            : string.Empty;
        if (!string.Equals(afterConnective, afterFixtures, StringComparison.Ordinal))
            suffix += "; cleared the back-reference the promoted sentence had nothing to point at";

        return (afterConnective, suffix);
    }

    /// <summary>
    ///     Appends the deterministic fixture line(s) for any required source the data holds but the
    ///     script never voiced. The only defect class whose honest repair needs no LLM: the facts
    ///     are already structured. New lines go to the tail; the section-order repair then places
    ///     them in the station's on-air order in the same pass.
    ///     <para>
    ///         "The tail" means the end of the BODY, not the end of the read. A composed take that
    ///         re-enters the loop already carries the station outro, and appending after it buried
    ///         the closer mid-read — which the composer's trailing-edge duplicate guard cannot see,
    ///         so it appended the closer again. Dev sydney <c>ffcf233d</c>, 2026-08-25, aired the
    ///         KIIS sign-off twice with the weather sandwiched between them.
    ///     </para>
    /// </summary>
    private static string SpliceRequiredFixtures(string script, BroadcastWithTraffic context)
    {
        var sentences = ContentSourceSignals.SplitSentences(script);
        var additions = new List<string>();
        if (context.Content.RequiresWeather && context.Weather.HasValue
            && !sentences.Any(ContentSourceSignals.IsWeatherLine))
            additions.Add(FixtureLineComposer.WeatherLine(context.Weather.Value));
        if (context.Content.RequiresFuel && context.Fuel.HasValue
            && !sentences.Any(ContentSourceSignals.IsFuelLine))
            additions.AddRange(FixtureLineComposer.FuelLines(context.Fuel.Value));
        if (additions.Count == 0)
            return script;

        var (body, outro) = BulletinBookendComposer.SplitTrailingOutro(script, context.Bookends);
        string spliced = string.Join(" ", new[] { body }.Concat(additions));
        return outro is null ? spliced : $"{spliced} {outro}";
    }

    /// <summary>
    ///     Removes the sentence at 1-based <paramref name="order" /> and re-joins the read. Returns
    ///     null when the script has only one sentence — an empty bulletin is never an acceptable
    ///     repair, so the caller escalates instead.
    /// </summary>
    private static string? RemoveSentence(string script, int order)
    {
        var sentences = ScriptSentenceSplitter.Split(script);
        if (order <= 0 || order > sentences.Count || sentences.Count == 1)
            return null;
        return string.Join(" ", sentences.Where((_, index) => index + 1 != order));
    }

    /// <summary>
    ///     Resolves an issue with no usable order to the ONE sentence containing the reviewer's own
    ///     quoted span (the review contract requires the exact script quote in every reason).
    ///     Longest span first; zero or multiple containing sentences leave the issue unresolved so the
    ///     caller defers it to the next review pass rather than patch the wrong sentence.
    /// </summary>
    private static int? ResolveOrderFromQuotedSpan(ReviewIssue issue, IReadOnlyList<string> splitSentences)
    {
        foreach (string span in ExtractQuotedSpans(issue.Reason))
        {
            string needle = SentenceIdentity(span);
            if (needle.Length < 12)
                continue;
            int match = -1;
            int count = 0;
            for (int i = 0; i < splitSentences.Count; i++)
            {
                if (SentenceIdentity(splitSentences[i]).Contains(needle, StringComparison.Ordinal))
                {
                    match = i;
                    count++;
                }
            }
            if (count == 1)
                return match + 1;
        }
        return null;
    }

    private static IEnumerable<string> ExtractQuotedSpans(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        foreach (Match match in Regex.Matches(text, "[\"“](.+?)[\"”]"))
            if (match.Groups[1].Value.Trim().Length > 0)
                yield return match.Groups[1].Value.Trim();
    }

    private static string? SpliceSentence(string script, int order, string replacement)
    {
        var sentences = ScriptSentenceSplitter.Split(script);
        if (order <= 0 || order > sentences.Count || string.IsNullOrWhiteSpace(replacement))
            return null;

        int searchStart = 0;
        int targetStart = -1;
        int targetLength = 0;
        for (int index = 0; index < sentences.Count; index++)
        {
            int start = script.IndexOf(sentences[index], searchStart, StringComparison.Ordinal);
            if (start < 0)
                return null;
            if (index + 1 == order)
            {
                targetStart = start;
                targetLength = sentences[index].Length;
                break;
            }
            searchStart = start + sentences[index].Length;
        }

        if (targetStart < 0)
            return null;

        return string.Concat(
            script[..targetStart],
            replacement.Trim(),
            script[(targetStart + targetLength)..]);
    }

    internal readonly record struct ReviewRefineResult(
        string Script,
        IReadOnlyList<DraftedSentence>? DraftedSentences);

    internal async Task<Result<ReviewRefineResult>> ReviewAndRefineScriptAsync(
        BroadcastWithTraffic context,
        string script,
        double secondsPerWord,
        CancellationToken cancellationToken,
        IReadOnlyList<DraftedSentence>? draftedSentences = null)
    {
        int configuredRetries = _options.MaxReviewRefineRetries;
        // Honour the configured retry count up to a defensive ceiling that bounds added latency.
        int maxRetries = Math.Clamp(configuredRetries, 0, MaxReviewRefineRetriesCeiling);
        if (configuredRetries != maxRetries)
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} clamping configured retries from {Configured} to ceiling {Effective}",
                context.JobId, configuredRetries, maxRetries);

        if (maxRetries <= 0)
        {
            logger.LogInformation("[ReviewRefine] Job {JobId} review-refine disabled (MaxReviewRefineRetries=0)",
                context.JobId);
            return (await ApplyStyleGateAsync(context, script, secondsPerWord, true, cancellationToken))
                .Map(finalScript => new ReviewRefineResult(finalScript, draftedSentences));
        }
        // The deterministic detectors are the gate. When the draft trips none of them and is not
        // under-filling the slot, skip the LLM review/refine rounds entirely and finalize
        // deterministically — the LLM audit earns its cost only on a flagged draft.
        var (blocking, style) = ComputeViolationCounts(script, context, draftedSentences);
        int auditBand = context.TargetWordCountOverride.HasValue
            ? context.TargetWordCountOverride.Value
            : Math.Max(1, (int)Math.Round(context.TargetDurationSeconds / (secondsPerWord <= 0 ? 0.4 : secondsPerWord)));
        if (blocking == 0 && style == 0 && !SlotFitPlanner.NeedsExpansion(WordCounter.Count(script), auditBand))
        {
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} deterministic detectors clean — skipping LLM review/refine.",
                context.JobId);
            return (await ApplyStyleGateAsync(context, script, secondsPerWord, true, cancellationToken))
                .Map(finalScript => new ReviewRefineResult(finalScript, draftedSentences));
        }

        int refineCount = 0;
        bool approved = false;
        string? lastBlockingSignature = null;
        // Sentences the ladder removed this run (patch failed/ratchet-rejected drops) — handed to
        // every later review so the reviewer never demands their return (the 76f4f8d0 whipsaw).
        var removedSentences = new List<string>();
        // The last review's (order, defect) keys and the orders the last attempt mutated: the final
        // grounding gate acts only on persistent or mutation-adjacent issues — a defect first
        // articulated at the gate about an untouched sentence is reviewer noise, not proof.
        var lastReviewIssueKeys = new HashSet<(int? Order, string Defect)>();
        var lastMutatedOrders = new HashSet<int>();
        // Target words for the duration-deficit axis of the ratchet below. Under-filling the slot is
        // the failure a length-shaving refine causes, so a refine that only shortens an already-short
        // script without clearing a real defect is rejected.
        int targetWords = context.TargetWordCountOverride.HasValue
            ? context.TargetWordCountOverride.Value
            : Math.Max(1, (int)Math.Round(context.TargetDurationSeconds / (secondsPerWord <= 0 ? 0.4 : secondsPerWord)));
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await jobStatusUpdater.LogJobAsync(context.JobId, $"Review-refine attempt {attempt}/{maxRetries}: reviewing script...",
                cancellationToken);
            logger.LogInformation("[ReviewRefine] Job {JobId} starting review attempt {Attempt}/{MaxRetries}",
                context.JobId, attempt, maxRetries);

            string? producerFeedback = context.RejectionFeedback.HasValue
                ? context.RejectionFeedback.Value
                : null;

            var reviewResult = await scriptReviewer.ReviewScriptAsync(
                script,
                (int)context.TargetDurationSeconds,
                context.TargetWordCountOverride.HasValue ? context.TargetWordCountOverride.Value : null,
                producerFeedback,
                context,
                draftedSentences: draftedSentences,
                cancellationToken: cancellationToken,
                removedSentences: removedSentences);

            if (reviewResult.IsFailure)
            {
                logger.LogWarning("[ReviewRefine] Job {JobId} review failed on attempt {Attempt}: {Error}",
                    context.JobId, attempt, reviewResult.Error);
                await jobStatusUpdater.LogJobAsync(context.JobId, $"Review failed: {reviewResult.Error}. Using current script.",
                    cancellationToken);
                break;
            }

            lastReviewIssueKeys = reviewResult.Value.Issues
                .Select(issue => (issue.Order, issue.Defect))
                .ToHashSet();
            lastMutatedOrders.Clear();

            string scriptBeforeRepairs = script;
            var rollCall = RollCallDetector.Detect(script, context);
            var dupAnchor = DuplicateAnchorDetector.Detect(script, context);
            var metaLeak = MetaLeakDetector.Detect(script);
            var typeStack = IncidentTypeStackDetector.Detect(script);
            var repeatedPrep = RepeatedPrepositionDetector.Detect(script);
            var stateRep = RepeatedStatePhraseDetector.Detect(script);
            var sentenceShape = SentenceShapeDetector.Detect(script, StationLines(context));
            var impactStack = ImpactStackDetector.Detect(script);
            var fakeLocation = FakeLocationDetector.Detect(script);
            var sentenceClone = SentenceCloneDetector.Detect(script, context);
            var unselectedSource = UnselectedSourceDetector.Detect(script, context);
            var requiredSource = RequiredSourceDetector.Detect(script, context);
            var sectionOrder = SectionOrderDetector.Detect(script, context);
            var suburbRetention = SuburbRetentionDetector.Detect(script, context);
            var evidenceRestatement = EvidenceRestatementDetector.Detect(draftedSentences);
            var falseClosure = FalseClosureDetector.Detect(script, context);
            var roadGrounding = RoadNameGroundingDetector.Detect(script, context);
            // Mechanical repairs run before any refine decision. Re-detect each repaired gate
            // against the updated script so a deterministic fix never spends an LLM refine call.
            var splitClear = SplitClearRunDetector.Detect(script);
            var redundantTail = RedundantTailDetector.Detect(script);
            var fragment = FragmentDetector.Detect(script);
            var connectorFrequency = ConnectorFrequencyDetector.Detect(script);
            // Every attempt, not just the drop sites: a promoted sentence is orphaned by ANY
            // mutation that changes what precedes it. Test job c1b346ee (2026-08-24) proved the
            // drop-site hook alone is not enough — the drop left a healthy lead in place, and it
            // was the attempt-2 sentence PATCH that promoted "There's also a closure..." to the
            // top, so the repair never ran and the read aired opening on "also".
            var orphanedConnective = OrphanedConnectiveDetector.Detect(script, context);
            var vagueness = VaguenessDetector.Detect(script);
            var hazardBlockage = HazardBlockageDetector.Detect(script, context);
            var secondaryCap = SecondaryItemCapDetector.Detect(script, context);
            if (secondaryCap.HasExcessItems)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "secondary-item-cap", script,
                    s => SecondaryItemCapDetector.Repair(s, context), cancellationToken);
                if (!string.Equals(beforeRepair, script, StringComparison.Ordinal))
                {
                    draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                    logger.LogInformation(
                        "[ReviewRefine] Job {JobId} mechanically swept a cross-sentence roll-call ({Items} named items){RepairSuffix}.",
                        context.JobId, secondaryCap.TrafficItemCount, RepairLogSuffix(refineCount));
                }
                secondaryCap = SecondaryItemCapDetector.Detect(script, context);
            }
            if (typeStack.HasTypeStack)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "type-stack", script, s => IncidentTypeStackDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved type-stack clause{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                typeStack = IncidentTypeStackDetector.Detect(script);
            }
            if (rollCall.HasRollCall)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "roll-call", script, s => RollCallDetector.Repair(s, context), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved roll-call{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                rollCall = RollCallDetector.Detect(script, context);
            }
            if (splitClear.HasSplitClearRun)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "split-clear-run", script, s => SplitClearRunDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically merged a split clear-run{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                splitClear = SplitClearRunDetector.Detect(script);
            }
            if (dupAnchor.HasDuplicateAnchor)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "duplicate-anchor", script, s => DuplicateAnchorDetector.Repair(s, context), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved duplicate-anchor{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                dupAnchor = DuplicateAnchorDetector.Detect(script, context);
            }
            if (repeatedPrep.HasRepeatedPreposition)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "repeated-preposition", script, s => RepeatedPrepositionDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved repeated-preposition{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                repeatedPrep = RepeatedPrepositionDetector.Detect(script);
            }
            if (redundantTail.HasRedundantTail)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "redundant-tail", script, s => RedundantTailDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically dropped a redundant summarising tail{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                redundantTail = RedundantTailDetector.Detect(script);
            }
            if (impactStack.HasImpactStack)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "impact-stack", script, s => ImpactStackDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved impact-stack clause{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                impactStack = ImpactStackDetector.Detect(script);
            }
            if (requiredSource.HasMissingSource)
            {
                // The one defect class whose honest repair needs no LLM: the fixture's facts are
                // already structured, so splice the deterministic line. Before section-order in
                // the chain, so the new line lands in the station's on-air order in the same pass.
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "required-source", script, s => SpliceRequiredFixtures(s, context), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically spliced the required fixture line from the fetched data{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                requiredSource = RequiredSourceDetector.Detect(script, context);
            }
            if (fakeLocation.HasFakeLocation)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "fake-location", script, s => FakeLocationDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved fake-location{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                fakeLocation = FakeLocationDetector.Detect(script);
            }
            if (suburbRetention.HasMissingSuburb)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "suburb-retention", script, s => SuburbRetentionDetector.Repair(s, context), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved suburb-retention gap{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                suburbRetention = SuburbRetentionDetector.Detect(script, context);
            }
            if (evidenceRestatement.HasRestatement)
            {
                string beforeRestatementRepair = script;
                script = await ApplyRepairAsync(
                    context, "evidence-restatement", script,
                    s => EvidenceRestatementDetector.Repair(s, draftedSentences), cancellationToken);
                if (!string.Equals(beforeRestatementRepair, script, StringComparison.Ordinal))
                    draftedSentences = EvidenceRestatementDetector.RepairSentences(draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically dropped an evidence-restatement sentence{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                evidenceRestatement = EvidenceRestatementDetector.Detect(draftedSentences);
            }
            if (sectionOrder.HasOutOfOrderSection)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "section-order", script, s => SectionOrderDetector.Repair(s, context), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically relocated an out-of-order supplementary section{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                sectionOrder = SectionOrderDetector.Detect(script, context);
            }
            if (fragment.HasFragment)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "fragment", script, s => FragmentDetector.Repair(s), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically repaired an unresolved fragment{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                fragment = FragmentDetector.Detect(script);
            }
            // LAST of the mechanical repairs: section-order RELOCATES sentences and the drop/patch
            // paths REPLACE them, so whether the lead sentence is orphaned is only settled once
            // every other repair has moved what it is going to move. Re-detected against the
            // repaired script for exactly that reason.
            orphanedConnective = OrphanedConnectiveDetector.Detect(script, context);
            if (orphanedConnective.HasOrphanedConnective)
            {
                string beforeRepair = script;
                script = await ApplyRepairAsync(
                    context, "orphaned-connective", script,
                    s => OrphanedConnectiveDetector.Repair(s, context), cancellationToken);
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically cleared a back-reference the lead sentence "
                    + "had nothing to point at{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
                orphanedConnective = OrphanedConnectiveDetector.Detect(script, context);
            }

            // Repairs can clear blocking/style defects, or expose a second deterministic gate.
            // Re-run the complete detector set before deciding whether an LLM refine is justified.
            rollCall = RollCallDetector.Detect(script, context);
            dupAnchor = DuplicateAnchorDetector.Detect(script, context);
            metaLeak = MetaLeakDetector.Detect(script);
            typeStack = IncidentTypeStackDetector.Detect(script);
            repeatedPrep = RepeatedPrepositionDetector.Detect(script);
            stateRep = RepeatedStatePhraseDetector.Detect(script);
            sentenceShape = SentenceShapeDetector.Detect(script, StationLines(context));
            impactStack = ImpactStackDetector.Detect(script);
            fakeLocation = FakeLocationDetector.Detect(script);
            sentenceClone = SentenceCloneDetector.Detect(script, context);
            unselectedSource = UnselectedSourceDetector.Detect(script, context);
            requiredSource = RequiredSourceDetector.Detect(script, context);
            sectionOrder = SectionOrderDetector.Detect(script, context);
            suburbRetention = SuburbRetentionDetector.Detect(script, context);
            evidenceRestatement = EvidenceRestatementDetector.Detect(draftedSentences);
            falseClosure = FalseClosureDetector.Detect(script, context);
            splitClear = SplitClearRunDetector.Detect(script);
            roadGrounding = RoadNameGroundingDetector.Detect(script, context);
            redundantTail = RedundantTailDetector.Detect(script);
            bool mechanicalRepairApplied =
                !string.Equals(scriptBeforeRepairs, script, StringComparison.Ordinal);
            fragment = FragmentDetector.Detect(script);
            connectorFrequency = ConnectorFrequencyDetector.Detect(script);
            vagueness = VaguenessDetector.Detect(script);
            hazardBlockage = HazardBlockageDetector.Detect(script, context);

            var deterministicIssues = new List<ReviewIssue>();
            deterministicIssues.AddRange(rollCall.Issues);
            deterministicIssues.AddRange(dupAnchor.Issues);
            deterministicIssues.AddRange(metaLeak.Issues);
            deterministicIssues.AddRange(typeStack.Issues);
            deterministicIssues.AddRange(repeatedPrep.Issues);
            deterministicIssues.AddRange(sentenceShape.ReviewIssues);
            deterministicIssues.AddRange(stateRep.Issues);
            deterministicIssues.AddRange(impactStack.Issues);
            deterministicIssues.AddRange(fakeLocation.Issues);
            deterministicIssues.AddRange(sentenceClone.Issues);
            deterministicIssues.AddRange(unselectedSource.Issues);
            deterministicIssues.AddRange(requiredSource.Issues);
            deterministicIssues.AddRange(sectionOrder.Issues);
            deterministicIssues.AddRange(suburbRetention.Issues);
            deterministicIssues.AddRange(evidenceRestatement.Issues);
            deterministicIssues.AddRange(falseClosure.Issues);
            deterministicIssues.AddRange(splitClear.Issues);
            deterministicIssues.AddRange(redundantTail.Issues);
            deterministicIssues.AddRange(fragment.Issues);
            deterministicIssues.AddRange(connectorFrequency.Issues);
            deterministicIssues.AddRange(vagueness.Issues);
            deterministicIssues.AddRange(hazardBlockage.Issues);

            bool blockingIssue = metaLeak.HasMetaLeak || typeStack.HasTypeStack
                || fakeLocation.HasFakeLocation || unselectedSource.HasFabrication
                || requiredSource.HasMissingSource
                || suburbRetention.HasMissingSuburb || evidenceRestatement.HasRestatement
                || sentenceClone.HasClone || falseClosure.HasFalseClosure;
            bool styleIssue = rollCall.HasRollCall || dupAnchor.HasDuplicateAnchor
                || sentenceShape.HasIssue || repeatedPrep.HasRepeatedPreposition
                || impactStack.HasImpactStack || sectionOrder.HasOutOfOrderSection;
            bool deterministicClean = deterministicIssues.Count == 0;

            // Mechanical repairs are already reflected in the detector results above. If the
            // reviewer approved and those re-run detectors are clean, this is a complete approval:
            // do not spend a refine call or a redundant re-review on the repair.
            if (reviewResult.Value.IsApproved && deterministicClean && reviewResult.Value.Issues.Count == 0)
            {
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} script approved on attempt {Attempt}{RepairNote}{StyleNote}",
                    context.JobId, attempt,
                    mechanicalRepairApplied ? " after deterministic repair" : string.Empty,
                    styleIssue ? " (style nits handled by mechanical repair)" : string.Empty);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Script approved by review (attempt {attempt})", cancellationToken);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    "Refine routing: no actionable issues; no refine call.", cancellationToken);
                approved = true;
                break;
            }

            // Convergence guard (job 4417add2, 2026-07-07): if the SAME blocking defect(s) survive
            // a refine — fired last attempt and again now — the refiner is not converging, and
            // another round on the same feedback risks corrupting a correct fact. Stop the loop and
            // let the final grounding hard-gate own the outcome.
            string blockingSignature = string.Join("|", new[]
            {
                metaLeak.HasMetaLeak ? "meta" : null,
                typeStack.HasTypeStack ? "type" : null,
                fakeLocation.HasFakeLocation ? "fake" : null,
                unselectedSource.HasFabrication ? "unselected" : null,
                requiredSource.HasMissingSource ? "required" : null,
                suburbRetention.HasMissingSuburb ? "suburb" : null,
                evidenceRestatement.HasRestatement ? "restatement" : null,
                sentenceClone.HasClone ? "clone" : null
            }.Where(s => s is not null));
            if (blockingIssue && attempt > 1 && blockingSignature == lastBlockingSignature)
            {
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} blocking defect(s) [{Signature}] persisted across a refine on attempt {Attempt} — stopping loop (no convergence).",
                    context.JobId, blockingSignature, attempt);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Refine not converging on [{blockingSignature}] (attempt {attempt}); stopping and handing to final grounding gate.",
                    cancellationToken);
                break;
            }
            lastBlockingSignature = blockingSignature;

            if (rollCall.HasRollCall)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic roll-call gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (dupAnchor.HasDuplicateAnchor)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic duplicate-anchor gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (metaLeak.HasMetaLeak)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic meta-leak gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (typeStack.HasTypeStack)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic type-stack gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (repeatedPrep.HasRepeatedPreposition)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic repeated-preposition gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (impactStack.HasImpactStack)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic impact-stack gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (fakeLocation.HasFakeLocation)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic fake-location gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (sentenceClone.HasClone)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic cloned-shape gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (unselectedSource.HasFabrication)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic unselected-source gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (requiredSource.HasMissingSource)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic required-source gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (sectionOrder.HasOutOfOrderSection)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic section-order gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (suburbRetention.HasMissingSuburb)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic suburb-retention gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            if (evidenceRestatement.HasRestatement)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} deterministic evidence-restatement gate triggered on attempt {Attempt}",
                    context.JobId, attempt);
            // OBSERVE-ONLY (2026-08-19). Road-name grounding is deliberately NOT blocking yet, and
            // is excluded from the violation counts, the blocking signature and the refine feedback.
            // Fabrication is already prevented structurally on the structured path: each sentence
            // must cite ids from a per-request strict enum of real EvidenceUnits. A replay eval
            // confirmed fabrication=ok on all three
            // markets WITHOUT this gate. This detector is a second net whose false-positive rate is
            // unmeasured — it fired on 27 existing test fixtures on introduction — so it logs for a
            // dev observation period first. Promote to blocking only with measured evidence that it
            // does not reject grounded copy.
            if (roadGrounding.HasUngroundedRoad)
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} road-grounding OBSERVATION (non-blocking) on attempt {Attempt}: {Feedback}",
                    context.JobId, attempt, roadGrounding.Feedback);

            // Keep the reviewer's verdict separate from deterministic detector feedback. In
            // particular, an approved review must not become "Approved. SUBURB DROPPED ..." and
            // send the already-repaired deterministic finding back to the refiner.
            string? reviewerFeedback = reviewResult.Value.IsApproved
                ? null
                : reviewResult.Value.Feedback;
            string deterministicFeedback = string.Join(" ", new[]
            {
                rollCall.HasRollCall ? rollCall.Feedback : null,
                dupAnchor.HasDuplicateAnchor ? dupAnchor.Feedback : null,
                metaLeak.HasMetaLeak ? metaLeak.Feedback : null,
                typeStack.HasTypeStack ? typeStack.Feedback : null,
                sentenceClone.HasClone ? sentenceClone.Feedback : null,
                sentenceShape.HasIssue ? sentenceShape.Feedback : null,
                requiredSource.HasMissingSource ? requiredSource.Feedback : null,
                sectionOrder.HasOutOfOrderSection ? sectionOrder.Feedback : null,
                suburbRetention.HasMissingSuburb ? suburbRetention.Feedback : null,
                evidenceRestatement.HasRestatement ? evidenceRestatement.Feedback : null,
                falseClosure.HasFalseClosure ? falseClosure.Feedback : null,
            }.Where(f => !string.IsNullOrWhiteSpace(f)));
            string feedback = string.Join(" ", new[] { reviewerFeedback, deterministicFeedback }
                .Where(f => !string.IsNullOrWhiteSpace(f)));
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} review feedback (attempt {Attempt}, mechanicalRepairApplied={MechanicalRepairApplied}): {Feedback}",
                context.JobId, attempt, mechanicalRepairApplied, feedback);
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Review feedback (attempt {attempt}): {feedback}", cancellationToken);

            // A reviewer can reject without leaving any actionable instruction. Calling a refiner
            // in that state caused 3/7 live refines to rewrite most sentences without a defect to
            // fix (measured 2026-08-20). Keep this guard at the loop boundary, not in a prompt.
            if (string.IsNullOrWhiteSpace(feedback))
            {
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} empty actionable feedback — skipping refine call on attempt {Attempt}.",
                    context.JobId, attempt);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Refine routing: empty actionable feedback; skipping refine call (attempt {attempt}).",
                    cancellationToken);
                break;
            }

            // Resolve each issue to its sentence. Detector orders are always fresh — the detectors
            // re-ran on the repaired script. A reviewer's order is only fresh when no mechanical
            // repair ran: repairs can move the flagged sentence (observed in tests and in the
            // 02:03 test job — a section-order repair shifted the flagged weather sentence, and
            // the stale order patched the WRONG sentence). A stale reviewer order is re-resolved
            // from the reviewer's own quoted span, never kept by position and never guessed.
            // Anything still unresolved has no sentence target; defer it to the next review rather than
            // widening the repair to the whole script.
            var reviewerIssues = reviewResult.Value.Issues.ToList();
            // Provenance must survive order remapping: a `with { Order }` copy is a new value, so
            // match on the content the remap never changes.
            var reviewerIssueKeys = reviewerIssues
                .Select(i => (i.Defect, i.Reason))
                .ToHashSet();
            var allIssues = reviewerIssues.Concat(deterministicIssues).ToList();
            var splitSentences = ScriptSentenceSplitter.Split(script);
            int splitCount = splitSentences.Count;

            var mappedIssues = new List<ReviewIssue>();
            bool hasUnresolvedIssue = false;
            foreach (var issue in allIssues)
            {
                bool reviewerSourced = reviewerIssueKeys.Contains((issue.Defect, issue.Reason));
                if (reviewerSourced && mechanicalRepairApplied)
                {
                    int? refreshed = ResolveOrderFromQuotedSpan(issue, splitSentences);
                    if (refreshed.HasValue)
                        mappedIssues.Add(issue with { Order = refreshed.Value });
                    else
                        hasUnresolvedIssue = true;
                    continue;
                }
                if (issue.Order is > 0 && issue.Order.Value <= splitCount)
                {
                    mappedIssues.Add(issue);
                    continue;
                }
                int? resolved = ResolveOrderFromQuotedSpan(issue, splitSentences);
                if (resolved.HasValue)
                    mappedIssues.Add(issue with { Order = resolved.Value });
                else
                    hasUnresolvedIssue = true;
            }

            var patchGroups = mappedIssues
                .GroupBy(issue => issue.Order!.Value)
                .OrderBy(group => group.Key)
                .ToList();

            // The sentence-scoped path is the only LLM repair: whole-script refinement is removed
            // (2026-08-21). An issue that never resolves to a sentence defers to the next review.
            if (patchGroups.Count > 0)
            {
                string requestedOrders = string.Join(", ", patchGroups.Select(group => group.Key));
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Refine routing: sentence-scoped; affected sentence orders [{requestedOrders}].",
                    cancellationToken);
                var currentDrafts = draftedSentences?.ToList()
                    ?? ScriptSentenceSplitter.Split(script)
                        .Select((text, index) => new DraftedSentence(index + 1, text, []))
                        .ToList();
                var successfulOrders = new List<int>();
                bool droppedFlaggedSentence = false;
                foreach (var group in patchGroups)
                {
                    int order = group.Key;
                    DraftedSentence? sentence = currentDrafts.FirstOrDefault(s => s.Order == order);
                    if (sentence is null)
                    {
                        if (order > splitSentences.Count)
                            continue;
                        sentence = new DraftedSentence(order, splitSentences[order - 1], []);
                        currentDrafts.Add(sentence);
                    }

                    string instruction = string.Join(" ", group
                        .Select(issue => string.IsNullOrWhiteSpace(issue.Reason) ? issue.Defect : issue.Reason)
                        .Where(reason => !string.IsNullOrWhiteSpace(reason))
                        .Distinct(StringComparer.Ordinal));
                    if (string.IsNullOrWhiteSpace(instruction))
                        continue;

                    // A reviewer-sourced defect is a fidelity claim about THIS sentence (an
                    // unsupported or contradicted fact). When the sentence cannot be repaired, the
                    // defective sentence must not survive verbatim: the next review finds the same
                    // defect and the job burns its whole budget on it (observed test 2026-08-21 —
                    // one sentence failed patch on word tolerance twice, then failed the job at the
                    // final grounding gate). Drop it; when it carries a required fixture,
                    // RepairAfterDrop deterministically splices that fixture back from fetched data.
                    bool reviewerFlagged = group.Any(i => reviewerIssueKeys.Contains((i.Defect, i.Reason)));

                    var patchResult = await scriptRefiner.PatchSentenceAsync(
                        context, sentence, instruction, [], WordCounter.Count(sentence.Text), cancellationToken);
                    if (patchResult is null || patchResult.IsFailure
                        || string.IsNullOrWhiteSpace(patchResult.Value.Text))
                    {
                        string error = patchResult is null
                            ? "no result"
                            : patchResult.IsFailure ? patchResult.Error : "empty result";
                        if (reviewerFlagged)
                        {
                            string? withoutSentence = RemoveSentence(script, order);
                            if (withoutSentence is not null)
                            {
                                // A required fixture the dropped sentence carried is rebuilt
                                // deterministically from the fetched data — never a reason to
                                // retain a proven defect (whole-script LLM refine was removed
                                // 2026-08-21; see decisions.md).
                                var repaired = RepairAfterDrop(withoutSentence, context);
                                string afterDrop = repaired.Script;
                                await jobStatusUpdater.LogJobAsync(context.JobId,
                                    $"Sentence refine failed for order {order} ({error}); dropped the flagged sentence rather than retain a claim review proved unsupported"
                                    + repaired.LogSuffix + ".",
                                    cancellationToken);
                                string beforeDrop = script;
                                removedSentences.Add(sentence.Text);
                                lastMutatedOrders.Add(order);
                                script = afterDrop;
                                draftedSentences = PreserveMechanicalCitations(beforeDrop, script, draftedSentences);
                                refineCount++;
                                droppedFlaggedSentence = true;
                                break;
                            }
                            await jobStatusUpdater.LogJobAsync(context.JobId,
                                $"Sentence refine failed for order {order} ({error}) and it is the only sentence; leaving it for the final grounding gate.",
                                cancellationToken);
                            continue;
                        }
                        await jobStatusUpdater.LogJobAsync(context.JobId,
                            $"Sentence refine failed for order {order}: {error}. Keeping original sentence.",
                            cancellationToken);
                        continue;
                    }

                    // The patch is allowed to change wording only. The sentence's original
                    // evidence ids remain authoritative, even if a provider returns a different
                    // citation list.
                    DraftedSentence patchedSentence = patchResult.Value with
                    {
                        Order = sentence.Order,
                        EvidenceIds = sentence.EvidenceIds
                    };
                    string? candidateScript = SpliceSentence(script, order, patchedSentence.Text);
                    if (candidateScript is null)
                        continue;

                    var candidateDrafts = currentDrafts
                        .Select(existing => existing.Order == order ? patchedSentence : existing)
                        .ToList();
                    var (candidateBlocking, candidateStyle) =
                        ComputeViolationCounts(candidateScript, context, candidateDrafts);
                    var (currentBlocking, currentStyle) =
                        ComputeViolationCounts(script, context, currentDrafts);
                    int candidateDeficit = Math.Max(0, targetWords - WordCounter.Count(candidateScript));
                    int currentDeficit = Math.Max(0, targetWords - WordCounter.Count(script));
                    int candidateExcess = ScriptWordCeiling.Excess(candidateScript, targetWords);
                    int currentExcess = ScriptWordCeiling.Excess(script, targetWords);
                    // The deficit axis exists to stop a refine shaving a healthy read. A patch runs
                    // on a PROVEN defect, so it gets a small length tolerance — otherwise a
                    // defect-clearing fix one word shorter is rejected and the flagged sentence
                    // ships anyway (observed in the 2026-08-21 patch-fallback tests).
                    int patchDeficitTolerance = Math.Max(3, targetWords / 10);
                    int adjustedCandidateDeficit = Math.Max(0, candidateDeficit - patchDeficitTolerance);
                    bool patchNotWorse =
                        (candidateBlocking, candidateStyle, adjustedCandidateDeficit, candidateExcess)
                            .CompareTo((currentBlocking, currentStyle, currentDeficit, currentExcess)) <= 0;
                    if (!patchNotWorse)
                    {
                        if (reviewerFlagged)
                        {
                            string? withoutSentence = RemoveSentence(script, order);
                            if (withoutSentence is not null)
                            {
                                var repaired = RepairAfterDrop(withoutSentence, context);
                                string afterDrop = repaired.Script;
                                await jobStatusUpdater.LogJobAsync(context.JobId,
                                    $"Sentence refine order {order} rejected by ratchet; dropped the flagged sentence rather than retain a claim review proved unsupported"
                                    + repaired.LogSuffix + ".",
                                    cancellationToken);
                                string beforeDrop = script;
                                removedSentences.Add(sentence.Text);
                                lastMutatedOrders.Add(order);
                                script = afterDrop;
                                draftedSentences = PreserveMechanicalCitations(beforeDrop, script, draftedSentences);
                                refineCount++;
                                droppedFlaggedSentence = true;
                                break;
                            }
                        }
                        await jobStatusUpdater.LogJobAsync(context.JobId,
                            $"Sentence refine order {order} rejected by ratchet; keeping original sentence.",
                            cancellationToken);
                        continue;
                    }

                    script = candidateScript;
                    currentDrafts = candidateDrafts;
                    draftedSentences = currentDrafts;
                    refineCount++;
                    successfulOrders.Add(order);
                    lastMutatedOrders.Add(order);
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        $"Sentence refined and committed (order {order}), {WordCounter.Count(script)} words",
                        cancellationToken);
                }

                if (droppedFlaggedSentence)
                {
                    // Orders shifted with the drop; the next attempt re-reviews the new script and
                    // re-computes them rather than risking a patch against a stale order.
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        "Sentence-scoped refine completed by dropping the flagged sentence.",
                        cancellationToken);
                    continue;
                }

                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Sentence-scoped refine completed; patched sentence orders [{string.Join(", ", successfulOrders)}].",
                    cancellationToken);
                continue;
            }

            // Whole-script LLM refinement is deliberately GONE (2026-08-21): measured over the
            // eval corpus and live jobs, a whole-bulletin rewrite degraded more reads than it
            // fixed — 3/7 rewrote most sentences with no defect addressed, Darwin recap padding,
            // and the test-env provisioning mixup that ran the sentence contract here for a day.
            // An issue that cannot be resolved to one sentence defers to the next attempt's
            // review; the attempt budget and the final grounding gate bound it, and a proven
            // defect that survives the budget is removed sentence-by-sentence there.
            if (hasUnresolvedIssue)
            {
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    "Refine routing: issue(s) not resolvable to one sentence; no whole-script rewrite — deferring to the next review.",
                    cancellationToken);
            }
            continue;
        }
        if (refineCount > 0)
            metrics.TrackRefineCount(context.Location, refineCount);

        // The loop above exhausted without ever reaching a clean approval — its last action may
        // have been a refine that was never re-validated, and that refine followed feedback from
        // the SAME reviewer call, which can itself be wrong (see MaxFinalGroundingRetries). None
        // of the deterministic gates below catch a real place name borrowed from a different
        // road's incident; only a source-grounded review call reliably does. Run a bounded hard
        // grounding gate now. If it still says NEEDS_REVISION after its repair budget, fail this
        // generation attempt so the outer retry/fallback policy owns the next state instead of
        // sending a known-unapproved script to WaitingApproval.
        if (!approved)
        {
            var finalGrounding = await RunFinalGroundingCheckAsync(
                context, script, secondsPerWord, cancellationToken, draftedSentences,
                removedSentences, lastReviewIssueKeys, lastMutatedOrders);
            if (finalGrounding.IsFailure)
            {
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Final grounding hard gate failed: {finalGrounding.Error}", cancellationToken);
                return finalGrounding.PropagateFailure<ReviewRefineResult>();
            }

            script = finalGrounding.Value.Script;
            draftedSentences = finalGrounding.Value.DraftedSentences;
        }

        // Final deterministic re-check (no LLM call): catches the specific TEXTUAL patterns the
        // grounding check above doesn't target (roll-calls, duplicate anchors, meta-leaks, etc.) on
        // whatever script state the loop and the grounding check above left behind. Ship the
        // script regardless — a defect here is a quality gap, not a hard failure — but make it
        // visible in telemetry/logs rather than silently shipping unchecked.
        var finalRollCall = RollCallDetector.Detect(script, context);
        var finalDupAnchor = DuplicateAnchorDetector.Detect(script, context);
        var finalMetaLeak = MetaLeakDetector.Detect(script);
        var finalTypeStack = IncidentTypeStackDetector.Detect(script);
        var finalSplitClear = SplitClearRunDetector.Detect(script);
        var finalRepeatedPrep = RepeatedPrepositionDetector.Detect(script);
        var finalRepeatedFuelSuburb = RepeatedFuelSuburbDetector.Detect(script, context);
        var finalImpactStack = ImpactStackDetector.Detect(script);
        var finalFakeLocation = FakeLocationDetector.Detect(script);
        var finalSuburbRetention = SuburbRetentionDetector.Detect(script, context);
        var finalRequiredSource = RequiredSourceDetector.Detect(script, context);
        var finalSectionOrder = SectionOrderDetector.Detect(script, context);
        var finalFragment = FragmentDetector.Detect(script);
        var finalHazardBlockage = HazardBlockageDetector.Detect(script, context);
        var finalFalseClosure = FalseClosureDetector.Detect(script, context);
        var finalStateRep = RepeatedStatePhraseDetector.Detect(script);
        var finalSentenceShape = SentenceShapeDetector.Detect(script, StationLines(context));
        var finalRedundantTail = RedundantTailDetector.Detect(script);
        var finalConnectorFreq = ConnectorFrequencyDetector.Detect(script);
        if (finalConnectorFreq.HasOveruse)
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} ships with residual connector overuse: \"while\"/\"whereas\" x{Count} (no safe mechanical fix).",
                context.JobId, finalConnectorFreq.Count);
        if (finalStateRep.HasExcessRepetition)
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} ships with residual repetition: \"{Word}\" x{Count} (no mechanical fix; refine budget exhausted).",
                context.JobId, finalStateRep.Word, finalStateRep.Count);
        if (finalSentenceShape.HasIssue)
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} ships with residual sentence-shape issue: {Feedback}",
                context.JobId, finalSentenceShape.Feedback);

        // Type-stack, roll-call (Rule B), duplicate-anchor (the safe bare-self-reference shape),
        // repeated-preposition, impact-stack, and fake-location all have a SAFE mechanical fix —
        // the offending text is either an appended afterthought (type-stack/impact-stack), a
        // contiguous run of bare-listed road names joined only by list connectors (roll-call), a
        // trailing "at/on/near/along ANCHOR" self-reference with nothing else after it
        // (duplicate-anchor), an identical preposition repeated to introduce a second anchor
        // (repeated-preposition, swapped for "and"), or a fabricated pseudo-location made from the
        // incident type itself (fake-location) — stripping/collapsing/swapping it can't break the
        // sentence or remove a fact stated earlier, so apply directly rather than shipping a known,
        // fixable defect.
        // Provenance framing ("with no delays reported", "the reported road closure") aired on 3 of
        // 10 panel reads on 2026-08-25 despite the meta-leak gate blocking on it, because blocking
        // only buys a re-draft and the read ships once attempts run out. Repair.Detect keeps every
        // other meta phrase blocking; only the clause-removable shapes are swept here.
        // Detected at line 1057 with the rest of the final gates.
        if (finalMetaLeak.HasMetaLeak)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "meta-provenance", script, s => MetaLeakDetector.Repair(s), cancellationToken);
            if (!string.Equals(beforeRepair, script, StringComparison.Ordinal))
            {
                draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
                logger.LogInformation(
                    "[ReviewRefine] Job {JobId} mechanically stripped unresolved provenance framing{RepairSuffix}.",
                    context.JobId, RepairLogSuffix(refineCount));
            }
        }

        if (finalTypeStack.HasTypeStack)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "type-stack", script, s => IncidentTypeStackDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved type-stack clause{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalTypeStack = IncidentTypeStackDetector.Detect(script);
        }

        if (finalRollCall.HasRollCall)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "roll-call", script, s => RollCallDetector.Repair(s, context), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved roll-call{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalRollCall = RollCallDetector.Detect(script, context);
        }

        if (finalSplitClear.HasSplitClearRun)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "split-clear-run", script, s => SplitClearRunDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically merged a split clear-run{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalSplitClear = SplitClearRunDetector.Detect(script);
        }

        if (finalDupAnchor.HasDuplicateAnchor)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "duplicate-anchor", script, s => DuplicateAnchorDetector.Repair(s, context), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved duplicate-anchor{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalDupAnchor = DuplicateAnchorDetector.Detect(script, context);
        }

        if (finalRepeatedPrep.HasRepeatedPreposition)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "repeated-preposition", script, s => RepeatedPrepositionDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved repeated-preposition{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalRepeatedPrep = RepeatedPrepositionDetector.Detect(script);
        }

        // ARN, on a live Star 102.7 read: "Cheapest unleaded is a dollar ninety-eight at Manoora,
        // while diesel is two dollars forty in Manoora." The suburb must be said once. The old prompt
        // hint ('say "there too", never the suburb name twice') did prevent this, but it also locked
        // the writer into one frame every bulletin — ARN's other complaint — so it was replaced with
        // rotatable frames plus softer guidance, and the softer guidance did not hold under sampling.
        // Same resolution as ClosureVerbNormalizer: let the prompt vary the frame, make the rule
        // mechanical. Dropping the LATER location keeps the fact and the first mention.
        if (finalRepeatedFuelSuburb.HasRepeatedFuelSuburb)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "repeated-fuel-suburb", script,
                s => RepeatedFuelSuburbDetector.Repair(s, context), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically dropped a repeated fuel suburb{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalRepeatedFuelSuburb = RepeatedFuelSuburbDetector.Detect(script, context);
        }

        if (finalRedundantTail.HasRedundantTail)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "redundant-tail", script, s => RedundantTailDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically dropped a redundant summarising tail{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalRedundantTail = RedundantTailDetector.Detect(script);
        }

        if (finalImpactStack.HasImpactStack)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "impact-stack", script, s => ImpactStackDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved impact-stack clause{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalImpactStack = ImpactStackDetector.Detect(script);
        }

        if (finalFakeLocation.HasFakeLocation)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "fake-location", script, s => FakeLocationDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved fake-location{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalFakeLocation = FakeLocationDetector.Detect(script);
        }

        if (finalSuburbRetention.HasMissingSuburb)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "suburb-retention", script, s => SuburbRetentionDetector.Repair(s, context), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved suburb-retention gap{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalSuburbRetention = SuburbRetentionDetector.Detect(script, context);
        }
        if (finalSectionOrder.HasOutOfOrderSection)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "section-order", script, s => SectionOrderDetector.Repair(s, context), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically relocated an out-of-order supplementary section{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalSectionOrder = SectionOrderDetector.Detect(script, context);
        }
        if (finalFragment.HasFragment)
        {
            string beforeRepair = script;
            script = await ApplyRepairAsync(
                context, "fragment", script, s => FragmentDetector.Repair(s), cancellationToken);
            draftedSentences = PreserveMechanicalCitations(beforeRepair, script, draftedSentences);
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} mechanically repaired an unresolved fragment{RepairSuffix}.",
                context.JobId, RepairLogSuffix(refineCount));
            finalFragment = FragmentDetector.Detect(script);
        }

        if (finalRollCall.HasRollCall || finalDupAnchor.HasDuplicateAnchor || finalMetaLeak.HasMetaLeak
            || finalTypeStack.HasTypeStack || finalRepeatedPrep.HasRepeatedPreposition
            || finalImpactStack.HasImpactStack || finalFakeLocation.HasFakeLocation
            || finalSuburbRetention.HasMissingSuburb || finalFragment.HasFragment
            || finalRequiredSource.HasMissingSource || finalSectionOrder.HasOutOfOrderSection
            || finalHazardBlockage.HasHazardBlockage
            || finalFalseClosure.HasFalseClosure)
        {
            logger.LogWarning(
                "[ReviewRefine] Job {JobId} shipping with an unresolved deterministic gate{RepairSuffix}: {Gates}",
                context.JobId, RepairLogSuffix(refineCount),
                string.Join("; ", new[] { finalRollCall.HasRollCall ? "roll-call" : null,
                    finalDupAnchor.HasDuplicateAnchor ? "duplicate-anchor" : null,
                    finalMetaLeak.HasMetaLeak ? "meta-leak" : null,
                    finalTypeStack.HasTypeStack ? "type-stack" : null,
                    finalRepeatedPrep.HasRepeatedPreposition ? "repeated-preposition" : null,
                    finalImpactStack.HasImpactStack ? "impact-stack" : null,
                    finalFakeLocation.HasFakeLocation ? "fake-location" : null,

    finalSuburbRetention.HasMissingSuburb? "suburb-retention" : null,
                    finalFragment.HasFragment? "fragment" : null,
                    finalRequiredSource.HasMissingSource? "required-source" : null,
                    finalSectionOrder.HasOutOfOrderSection? "section-order" : null,
                    finalHazardBlockage.HasHazardBlockage? "hazard-blockage" : null,
                    finalFalseClosure.HasFalseClosure? "false-closure" : null }.Where(g => g != null)));
            await jobStatusUpdater.LogJobAsync(context.JobId,
                "Warning: script shipped with an unresolved style gate (see logs) — retry budget exhausted.",
                cancellationToken);
        }

        return (await ApplyStyleGateAsync(context, script, secondsPerWord, true, cancellationToken))
            .Map(finalScript => new ReviewRefineResult(finalScript, draftedSentences));
    }
    /// <summary>
    ///     Which word target the ship-time word ceiling is measured against, and why.
    ///     <para>
    ///         Pure so it can be tested without driving the whole review loop. Three paths, in
    ///         precedence order: an explicit caller target always wins; a calibrated prosody
    ///         prediction is used when one exists; otherwise the legacy 150-WPM derivation is
    ///         reproduced exactly, which is the path every uncalibrated voice still takes.
    ///     </para>
    ///     <para>
    ///         The calibrated path exists because the old derivation failed at its own boundary.
    ///         <see cref="ScriptWordCeiling.For" /> adds ~15% to a word target, and 150 WPM is a
    ///         genuinely accurate average (measured 152 WPM over 30 clean ARN Sarah bulletins) — but
    ///         63 words of real copy costs 25.6s against a 22.0s slot, 16.4% over, just outside the
    ///         1.15 speed-up absorber. The fitter then dropped sentences instead of compressing, the
    ///         trimmed read fell under the slot, and the attempt was retried as content-limited: the
    ///         overrun/trim/underrun oscillation seen live on job 43395fe5 (143s, 3 synthesis rounds).
    ///         Denominating the allowance in predicted SECONDS instead of words × 1.15 removes it.
    ///     </para>
    /// </summary>
    internal static CeilingTargetChoice ResolveCeilingTarget(
        Option<int> explicitTarget,
        double targetDurationSeconds,
        double maxSpeedUpFactor,
        double maxSilencePadSeconds,
        double maxSlowDownFactor,
        double legacySecondsPerWord,
        int currentWords,
        Option<PredictedDuration> prediction)
    {
        // Precedence, and the reason it is NOT "caller override first": TargetWordCountOverride is
        // set by the PIPELINE on every attempt, not by an operator — ScriptGenerationEngine lines 262
        // and 453 and WordTargetEnforcer line 47 all populate it with that attempt's own word target.
        // Ranking it above the model therefore made the calibrated path unreachable in the normal
        // flow: verified live 2026-08-20 on job dfb11ab8, which logged `explicit-target` with 47
        // words, exactly the attempt target. A measured duration is strictly better evidence about
        // whether a read fits its slot than any word count, so when a calibrated prediction exists it
        // decides; the word target is the proxy used only when it does not.
        double secondsPerMeasuredWord = prediction.HasValue && currentWords > 0
            ? prediction.Value.Seconds / currentWords
            : 0;
        int floorWords = WordTargetCalculator.FloorWords(
            targetDurationSeconds, maxSilencePadSeconds, maxSlowDownFactor, secondsPerMeasuredWord);
        if (!prediction.HasValue || currentWords <= 0)
        {
            if (explicitTarget.HasValue)
                return new CeilingTargetChoice(explicitTarget.Value, "attempt-target", 0, 0, 0);
            int legacy = Math.Max(1, (int)Math.Round(
                targetDurationSeconds / (legacySecondsPerWord <= 0 ? 0.4 : legacySecondsPerWord)));
            return new CeilingTargetChoice(legacy, "legacy-150wpm", 0, 0, 0);
        }

        var predicted = prediction.Value;
        // One residual sigma of margin absorbs the model's own error: measured cross-slot bias is
        // +0.17s and the same-input ElevenLabs floor is ±0.21s at 22s, so a bare point estimate sits
        // over the line about half the time. Clamped at the bare target: a sigma wide enough to pull
        // the allowance BELOW the slot would start trimming reads that legitimately fit, which is the
        // same defect in the opposite direction.
        double allowance = Math.Max(
            targetDurationSeconds, targetDurationSeconds * maxSpeedUpFactor - predicted.ResidualSdSec);
        if (predicted.Seconds <= allowance)
            return new CeilingTargetChoice(
                currentWords, "prosody-fits", predicted.Seconds, allowance, floorWords);

        // Trim quantum comes from THIS script's own measured pace, not a global constant, so the
        // number of words removed reflects the copy actually being cut.
        int desiredCeiling = Math.Max(1, currentWords -
            (int)Math.Ceiling((predicted.Seconds - allowance) / secondsPerMeasuredWord));
        return new CeilingTargetChoice(
            BaseTargetForCeiling(desiredCeiling), "prosody-trim", predicted.Seconds, allowance, floorWords);
    }

    /// <summary>The resolved ceiling target plus the measured slot floor and its evidence, for the job log.</summary>
    internal readonly record struct CeilingTargetChoice(
        int TargetWords, string Path, double PredictedSeconds, double AllowanceSeconds, int FloorWords);

    private static int BaseTargetForCeiling(int desiredCeiling)
    {
        int low = 1;
        int high = desiredCeiling;
        while (low < high)
        {
            int mid = low + (high - low + 1) / 2;
            if (ScriptWordCeiling.For(mid) <= desiredCeiling)
                low = mid;
            else
                high = mid - 1;
        }
        return low;
    }
    internal readonly record struct CeilingVariantSelection(
        string Script,
        IReadOnlyList<DraftedSentence> Sentences);

    internal static CeilingVariantSelection? SelectCeilingVariants(
        string script,
        IReadOnlyList<DraftedSentence>? draftedSentences,
        int ceiling,
        IEnumerable<string>? stationLines = null)
    {
        if (draftedSentences is null || draftedSentences.Count == 0)
            return null;

        var selected = draftedSentences.OrderBy(sentence => sentence.Order).ToList();

        ScriptBodySplit? split = null;
        string bodyScript = script;
        if (stationLines is not null)
        {
            split = StationLineSplitter.Split(script, stationLines);
            if (split.PreservedLineCount > 0)
                bodyScript = split.Body;
        }

        if (ScriptSentenceSplitter.Split(bodyScript).Count != selected.Count)
            return null;

        int fixedPreservedWords = split is not null
            ? split.Leading.Concat(split.Trailing).Sum(WordCounter.Count)
            : 0;
        int bodyCeiling = Math.Max(1, ceiling - fixedPreservedWords);

        // We want to find a phrasing combination that:
        // 1. Fits under bodyCeiling (Assemble(combination) <= bodyCeiling).
        // 2. Maximises word count under the ceiling (fill the slot, longest phrasing combination).
        // 3. Represents a change from the current phrasing (or replaces at least one sentence with a shorter variant).
        var choices = new List<List<string>>();
        for (int i = 0; i < selected.Count; i++)
        {
            var phrasings = new[] { selected[i].Text }
                .Concat((selected[i].Variants ?? []).Select(variant => variant.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(WordCounter.Count)
                .ToList();
            choices.Add(phrasings);
        }

        List<string>? bestCombo = null;
        int bestWordCount = -1;

        void Permute(int index, List<string> current)
        {
            if (index == choices.Count)
            {
                int count = WordCounter.Count(string.Join(" ", current));
                if (count <= bodyCeiling && count > bestWordCount)
                {
                    bestWordCount = count;
                    bestCombo = [.. current];
                }
                return;
            }

            foreach (string option in choices[index])
            {
                current.Add(option);
                Permute(index + 1, current);
                current.RemoveAt(current.Count - 1);
            }
        }

        Permute(0, []);

        if (bestCombo is null)
            return null;

        bool changed = false;
        for (int i = 0; i < selected.Count; i++)
        {
            if (!string.Equals(selected[i].Text, bestCombo[i], StringComparison.Ordinal))
            {
                selected[i] = selected[i] with { Text = bestCombo[i] };
                changed = true;
            }
        }

        if (!changed)
            return null;

        string assembledBody = Assemble(selected);
        if (WordCounter.Count(assembledBody) > bodyCeiling)
            return null;

        string finalScript = split is not null && split.PreservedLineCount > 0
            ? split.Recompose(assembledBody)
            : assembledBody;

        return new CeilingVariantSelection(finalScript, selected);
    }

    private static string Assemble(IReadOnlyList<DraftedSentence> sentences) =>
        string.Join(" ", sentences.OrderBy(sentence => sentence.Order).Select(sentence => sentence.Text.Trim()));

    /// <summary>
    ///     Re-pairs each sentence of a mechanically repaired script with the evidence the DRAFT
    ///     committed for it, and renumbers <see cref="DraftedSentence.Order"/> to the repaired
    ///     script's own positions.
    ///     <para>
    ///         This map is what the reviewer is handed as the STRUCTURED DRAFT, so a mis-pairing
    ///         here does not degrade the review — it inverts it. Dev job 63684f38 (2026-08-24)
    ///         handed the reviewer "A road closure is in place on Sir Reginald Ansett Drive at
    ///         Mascot" cited to <c>r2</c> (the M4 at Eastern Creek), the M5 sentence cited to
    ///         <c>r4</c> (Mitchell Road), and orders starting at 2 with no order 1. The reviewer
    ///         correctly proved all three "fabrications", the refiner then cited invented ids, and
    ///         the job burned four generation attempts and failed. The draft had cited perfectly.
    ///     </para>
    ///     <para>
    ///         Two rules make that unrepresentable. First, matching is ORDER-INDEPENDENT: the
    ///         previous forward-only cursor assumed survivors keep their relative order, which the
    ///         section-order repair exists to violate. Second, a pairing must be EARNED — a single
    ///         shared token ("closure", a road word) used to be enough to bind one sentence's facts
    ///         to another's. An unmatched sentence is emitted with NO evidence, never with a
    ///         neighbour's: the review contract treats an unverifiable citation as approve-by
    ///         -default, so an empty list costs a little grounding strength, while a wrong list is a
    ///         guaranteed false rejection that destroys a correct sentence.
    ///     </para>
    /// </summary>
    internal static IReadOnlyList<DraftedSentence>? PreserveMechanicalCitations(
        string before,
        string after,
        IReadOnlyList<DraftedSentence>? draftedSentences)
    {
        if (draftedSentences is null || string.Equals(before, after, StringComparison.Ordinal))
            return draftedSentences;

        var drafts = draftedSentences.OrderBy(sentence => sentence.Order).ToList();
        var afterSentences = ScriptSentenceSplitter.Split(after);
        var aligned = AlignByContent(afterSentences, drafts);

        return aligned.Count == 0 ? null : aligned;
    }

    /// <summary>Fewest shared meaningful tokens that can bind a sentence to a draft's evidence.</summary>
    private const int MinBindingOverlap = 2;

    private static List<DraftedSentence> AlignByContent(
        IReadOnlyList<string> afterSentences,
        IReadOnlyList<DraftedSentence> drafts)
    {
        var matches = new DraftedSentence?[afterSentences.Count];
        var used = new bool[drafts.Count];

        // Pass 1 — exact identity. Unambiguous, so it claims its draft before any scoring runs and
        // a reordered-but-unchanged sentence keeps its own citations wherever it moved to.
        for (int i = 0; i < afterSentences.Count; i++)
        {
            string identity = SentenceIdentity(afterSentences[i]);
            for (int d = 0; d < drafts.Count; d++)
            {
                if (used[d] || !string.Equals(identity, SentenceIdentity(drafts[d].Text), StringComparison.Ordinal))
                    continue;

                matches[i] = drafts[d];
                used[d] = true;
                break;
            }
        }

        // Pass 2 — a rewritten sentence, matched on content across ALL remaining drafts. Requires a
        // clear winner: enough shared tokens to be meaningful, and strictly more than the runner-up,
        // so two sentences about the same road never silently swap evidence.
        for (int i = 0; i < afterSentences.Count; i++)
        {
            if (matches[i] is not null)
                continue;

            int bestIndex = -1;
            int bestScore = 0;
            int runnerUp = 0;
            for (int d = 0; d < drafts.Count; d++)
            {
                if (used[d])
                    continue;

                int score = TokenOverlap(afterSentences[i], drafts[d].Text);
                if (score > bestScore)
                {
                    runnerUp = bestScore;
                    bestScore = score;
                    bestIndex = d;
                }
                else if (score > runnerUp)
                    runnerUp = score;
            }

            if (bestIndex < 0 || bestScore < MinBindingOverlap || bestScore == runnerUp)
                continue;

            matches[i] = drafts[bestIndex];
            used[bestIndex] = true;
        }

        // Every sentence in the repaired script appears in the map exactly once, at its own order.
        // An unmatched one carries no evidence rather than inheriting a neighbour's.
        var aligned = new List<DraftedSentence>(afterSentences.Count);
        for (int i = 0; i < afterSentences.Count; i++)
        {
            var match = matches[i];
            aligned.Add(match is null
                ? new DraftedSentence(i + 1, afterSentences[i], [])
                : match with { Order = i + 1, Text = afterSentences[i] });
        }

        return aligned;
    }

    private static string SentenceIdentity(string text) =>
        string.Join(" ",
            new string(text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray())
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private static int TokenOverlap(string left, string right)
    {
        var leftTokens = MeaningfulTokens(left);
        var rightTokens = MeaningfulTokens(right);
        return leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
    }

    private static HashSet<string> MeaningfulTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = new(raw.Where(char.IsLetterOrDigit).ToArray());
            if (token.Length >= 2 && !CommonSentenceWords.Contains(token))
                tokens.Add(token);
        }
        return tokens;
    }

    private static readonly HashSet<string> CommonSentenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "at", "by", "for", "from", "has", "have", "in", "is", "near",
        "of", "on", "or", "the", "to", "was", "were", "with"
    };
    private async Task<string> ApplyRepairAsync(
        BroadcastWithTraffic context,
        string repairName,
        string script,
        Func<string, string> repair,
        CancellationToken cancellationToken)
    {
        string after;
        try
        {
            after = repair(script);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[ReviewRefine] Job {JobId} {RepairName} repair threw {ExceptionType}; shipping the unrepaired script.",
                context.JobId, repairName, ex.GetType().Name);
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Mechanical {repairName} repair failed ({ex.GetType().Name}); shipped the unrepaired script.",
                cancellationToken);
            return script;
        }

        await LogPostLlmRepairAsync(context, repairName, script, after, cancellationToken);
        return after;
    }

    private async Task LogPostLlmRepairAsync(
        BroadcastWithTraffic context,
        string repairName,
        string before,
        string after,
        CancellationToken cancellationToken)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Post-LLM deterministic repair applied ({repairName}): {WordCounter.Count(before)} -> {WordCounter.Count(after)} words.",
            cancellationToken);
    }

    /// <summary>
    ///     Runs ONE bounded extra grounding pass when the main review-refine loop exhausted without
    ///     approval. Its returned draft structure keeps final refine citations in the outer loop.
    /// </summary>
    private async Task<Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>>
        RunFinalGroundingCheckAsync(
            BroadcastWithTraffic context,
            string script,
            double secondsPerWord,
            CancellationToken cancellationToken,
            IReadOnlyList<DraftedSentence>? draftedSentences,
            List<string> removedSentences,
            HashSet<(int? Order, string Defect)> lastReviewIssueKeys,
            HashSet<int> lastMutatedOrders)
    {
        string lastFeedback = "";
        for (int attempt = 1; attempt <= MaxFinalGroundingAttempts; attempt++)
        {
            var review = await scriptReviewer.ReviewScriptAsync(
                script,
                (int)context.TargetDurationSeconds,
                context.TargetWordCountOverride.HasValue ? context.TargetWordCountOverride.Value : null,
                null,
                context,
                cancellationToken: cancellationToken,
                draftedSentences: draftedSentences,
                removedSentences: removedSentences);

            if (review.IsFailure)
            {
                logger.LogWarning(
                    "[ReviewRefine] Job {JobId} final grounding hard gate unavailable on attempt {Attempt}: {Error}",
                    context.JobId, attempt, review.Error);
                return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Failure(
                    $"Final grounding check unavailable: {review.Error}");
            }

            if (review.Value.IsApproved)
            {
                if (attempt > 1)
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        $"Final grounding check passed after {attempt - 1} extra refine(s) — retry budget had exhausted without approval.",
                        cancellationToken);
                return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Success(
                    (script, draftedSentences));
            }

            lastFeedback = review.Value.Feedback;
            logger.LogInformation(
                "[ReviewRefine] Job {JobId} final grounding hard gate failed on attempt {Attempt}: {Feedback}",
                context.JobId, attempt, lastFeedback);
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Final grounding check failed (retry budget exhausted without approval): {lastFeedback}",
                cancellationToken);

            if (string.IsNullOrWhiteSpace(lastFeedback))
            {
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Final grounding refine skipped (attempt {attempt}): empty actionable feedback.",
                    cancellationToken);
                return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Failure(
                    "Final grounding refine skipped: empty actionable feedback.");
            }

            var splitNow = ScriptSentenceSplitter.Split(script);
            var (actionable, persistentScriptScoped, novelCount) = PartitionGateIssues(
                review.Value.Issues, splitNow, lastReviewIssueKeys, lastMutatedOrders);
            if (novelCount > 0)
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Final grounding: {novelCount} continuity issue(s) first raised at the gate about untouched content — noted, not acted on.",
                    cancellationToken);

            // The novelty screen governs the failure decision too: when nothing actionable or
            // persistent remains, the rejection rests on a first-time continuity judgment about
            // untouched content — ship the loop's script rather than fail on reviewer noise.
            if (actionable.Count == 0 && persistentScriptScoped.Count == 0)
            {
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    "Final grounding: nothing actionable remained after the novelty screen — shipping the loop's script.",
                    cancellationToken);
                return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Success(
                    (script, draftedSentences));
            }

            if (attempt == MaxFinalGroundingAttempts)
            {
                string error = $"Final grounding check failed after {MaxFinalGroundingAttempts - 1} extra refine(s): {lastFeedback}";
                logger.LogWarning(
                    "[ReviewRefine] Job {JobId} failing generation attempt at final grounding hard gate: {Error}",
                    context.JobId, error);
                // Retryable, NOT Permanent (2026-08-23). "Permanent" means retrying cannot help, and
                // this is the one failure where it demonstrably can: the defect is a property of the
                // sample the draft agent produced, not of the input. cairns-4ca aired on 1 of 3
                // replays of byte-identical evidence, and died on generation attempt 1 of 4 with
                // three attempts unspent, because Permanent breaks the outer loop
                // (ScriptGenerationEngine's attempt loop). The retry path also already feeds this
                // exact error string forward as draft guidance ("Prior error: ..."), so the reviewer's
                // own complaint conditions the next attempt for free.
                //
                // The TERMINAL disposition after the attempt budget is exhausted is unchanged and
                // must stay that way: ScriptGenerationEngine converts an exhausted grounding failure
                // back to Permanent. Leaking Retryable out of the engine would make QueueFailurePolicy
                // redeliver the whole job and stop BroadcastOrchestrator covering the slot — the exact
                // asymmetry the 2026-07-26 TEST load burst exposed.
                return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Failure(
                    OperationalFailure.Retryable("broadcast.final_grounding_failed", error));
            }

            // No whole-script rewrite here anymore (2026-08-21): the gate's extra attempt removes
            // the reviewer-proven defective sentences deterministically — drop each one, and
            // re-splice any required fixture it carried from the fetched data. A read that still
            // fails after that fails the generation attempt and the fallback covers the slot.

            // Resolve every issue against the SAME pre-drop split, then drop highest-order first:
            // an issue's order is anchored to the REVIEWED script, so a low-order drop would shift
            // every later order onto the wrong sentence.
            var toDrop = actionable
                .Select(item => item.Order)
                .Distinct()
                .OrderByDescending(order => order)
                .ToList();
            bool droppedAny = false;
            foreach (int order in toDrop)
            {
                string? withoutSentence = RemoveSentence(script, order);
                if (withoutSentence is null)
                    continue;
                string beforeDrop = script;
                removedSentences.Add(splitNow[order - 1]);
                script = withoutSentence;
                draftedSentences = PreserveMechanicalCitations(beforeDrop, script, draftedSentences);
                droppedAny = true;
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Final grounding removed flagged sentence {order} rather than rewrite the bulletin.",
                    cancellationToken);
            }
            if (droppedAny)
            {
                string beforeSplice = script;
                script = SpliceRequiredFixtures(script, context);
                if (script.Length != beforeSplice.Length)
                    await jobStatusUpdater.LogJobAsync(context.JobId,
                        "Final grounding re-spliced a required fixture the dropped sentence(s) carried, from the fetched data.",
                        cancellationToken);
            }
            if (!droppedAny)
            {
                // Retryable for the same reason as the hard-gate site above: an issue that cannot be
                // isolated to one sentence is almost always a RELATIONAL defect — the same fact voiced
                // twice, a cause restated across sentences, a repeated frame — and a relational defect
                // is a property of this sample, which a fresh draft may simply not have. This is the
                // exact path cairns-4ca took ("repeats the Edge Hill fuel prices and the clear-skies
                // weather"), and it is unfixable HERE by construction: sentence-scoped refine cannot
                // repair a relation between sentences, and whole-script rewrite is deliberately gone
                // (2026-08-21, degraded 3/7 reads). Regeneration is the only repair mechanism that
                // both exists and is proven, so route to it instead of killing the job.
                return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Failure(
                    OperationalFailure.Retryable("broadcast.final_grounding_failed",
                        $"Final grounding could not isolate the defect to a sentence: {lastFeedback}"));
            }
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Final grounding dropped flagged sentence(s), {WordCounter.Count(script)} words", cancellationToken);
        }

        return Result<(string Script, IReadOnlyList<DraftedSentence>? DraftedSentences)>.Failure(
            $"Final grounding check failed: {lastFeedback}");
    }

    /// <summary>
    ///     Partitions final-grounding issues into actionable drops, persistent script-scoped
    ///     complaints, and screened novel-continuity noise. Novelty screen (continuity-class only):
    ///     a CONTINUITY issue first articulated at the gate about a sentence the loop never touched
    ///     is reviewer noise — continuity judges a prior read the reviewer never heard, and the loop
    ///     already enforced it (observed live on dev jobs 76f4f8d0/b56a431c, 2026-08-21). Factual
    ///     classes are re-verifiable against the supplied evidence at the gate, so a first-time
    ///     factual claim still acts.
    /// </summary>
    private static (List<(ReviewIssue Issue, int Order)> Actionable, List<ReviewIssue> ScriptScoped, int NovelContinuity)
        PartitionGateIssues(
            IReadOnlyList<ReviewIssue> issues,
            IReadOnlyList<string> splitNow,
            HashSet<(int? Order, string Defect)> lastReviewIssueKeys,
            HashSet<int> lastMutatedOrders)
    {
        var actionable = new List<(ReviewIssue, int)>();
        var scriptScoped = new List<ReviewIssue>();
        int novelContinuity = 0;
        foreach (var issue in issues)
        {
            int? order = issue.Order is > 0 && issue.Order.Value <= splitNow.Count
                ? issue.Order.Value
                : ResolveOrderFromQuotedSpan(issue, splitNow);
            bool persistent = lastReviewIssueKeys.Contains((issue.Order, issue.Defect))
                || (order is int resolved && lastReviewIssueKeys.Contains((resolved, issue.Defect)));
            bool mutationAdjacent = order is int o && lastMutatedOrders.Contains(o);
            if (!persistent && !mutationAdjacent
                && string.Equals(issue.Defect, "CONTINUITY", StringComparison.OrdinalIgnoreCase))
            {
                novelContinuity++;
                continue;
            }
            if (order is int ord)
                actionable.Add((issue, ord));
            else
                scriptScoped.Add(issue);
        }
        return (actionable, scriptScoped, novelContinuity);
    }
    /// <summary>
    ///     The review loop's single exit, and therefore the last chance to make the text safe to speak.
    /// </summary>
    /// <remarks>
    ///     Re-normalises spoken forms on the way out (2026-08-23). The price normaliser runs in the
    ///     Infrastructure generator, but this loop then mutates the text repeatedly — sentence refines,
    ///     mechanical repairs (roll-call, type-stack, split-clear), fixture splices — and any of those
    ///     can reinstate a raw numeral the generator had already converted. Measured on the live panel:
    ///     cairns-4ca read "a dollar ninety-six" while adelaide read "$1.93" and portlincoln "$2.23"
    ///     from the same build, all three dev-published markets.
    ///     This is the general rule the pipeline kept violating in different places: a gate that runs
    ///     BEFORE the last mutation does not hold. Normalisation is idempotent, so running it last
    ///     costs nothing and removes the whole class rather than one path.
    /// </remarks>
    private async Task<Result<string>> ApplyStyleGateAsync(
        BroadcastWithTraffic context,
        string script,
        double secondsPerWord,
        bool allowAutoRefine,
        CancellationToken cancellationToken)
    {
        script = SpokenFormNormalizer.Normalize(
            script, SpokenFormNormalizer.ResolveDaypart(context.TimezoneId, timeProvider.GetUtcNow()));

        var validation = TrafficScriptStyleValidator.Validate(script);
        if (validation.IsValid)
            return Result<string>.Success(script);

        if (!allowAutoRefine)
            return Result<string>.Failure(string.Join(" ", validation.Issues));

        string feedback = string.Join(" ", validation.Issues);
        logger.LogInformation(
                "[StyleGate] Job {JobId} detected {IssueCount} issue(s). Running one targeted refine pass.",
                context.JobId, validation.Issues.Count);
        await jobStatusUpdater.LogJobAsync(context.JobId, $"Style gate corrections requested: {feedback}", cancellationToken);

        var sentences = ScriptSentenceSplitter.Split(script)
            .Select((text, index) => new DraftedSentence(index + 1, text, []))
            .ToList();
        bool anyRepaired = false;
        for (int i = 0; i < sentences.Count; i++)
        {
            var s = sentences[i];
            var sVal = TrafficScriptStyleValidator.Validate(s.Text);
            if (!sVal.IsValid)
            {
                string inst = $"Fix style issue: {string.Join(" ", sVal.Issues)}";
                var patch = await scriptRefiner.PatchSentenceAsync(
                    context, s, inst, [], WordCounter.Count(s.Text), cancellationToken);
                if (patch is { IsSuccess: true } && !string.IsNullOrWhiteSpace(patch.Value.Text))
                {
                    sentences[i] = patch.Value;
                    anyRepaired = true;
                }
            }
        }

        string candidate = anyRepaired ? string.Join(" ", sentences.Select(s => s.Text.Trim())) : script;
        var revalidation = TrafficScriptStyleValidator.Validate(candidate);
        if (revalidation.IsValid)
        {
            await jobStatusUpdater.LogJobAsync(context.JobId, "Style gate passed after targeted refine.", cancellationToken);
            return Result<string>.Success(candidate);
        }

        // No whole-script fallback anymore (2026-08-21): sentence patches are the style gate's
        // only LLM repair. The remaining rules are hard defects (speaker labels, broken fragments,
        // advice closers, and cloned scaffolding), so an unfixable issue fails the attempt. The numeric
        // cap was removed on 2026-09-03 because it contradicted the conversational writer.
        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Style gate did not clear after sentence patches: {string.Join(" ", revalidation.Issues)}", cancellationToken);
        return Result<string>.Failure(string.Join(" ", revalidation.Issues));
    }

    internal async Task<Result<string>> CheckContentSafetyAsync(
        string jobId, Option<string> market, string script, CancellationToken cancellationToken)
    {
        var result = await contentSafetyFilter.CheckAsync(
            script, market.Match(m => (string?)m, () => null), cancellationToken);
        if (!result.IsAllowed)
        {
            string violationSummary = string.Join("; ", result.Violations);
            logger.LogWarning(
                "[ContentSafety] Job {JobId} script blocked: {Violations}",
                jobId, violationSummary);
            await jobStatusUpdater.LogJobAsync(jobId, $"Content safety blocked: {violationSummary}", cancellationToken);
            return Result<string>.Failure(OperationalFailure.SafetyRejected("broadcast.content_safety_rejected", $"Content safety violation: {violationSummary}"));
        }

        if (result.Warnings.Length > 0)
        {
            logger.LogInformation(
                "[ContentSafety] Job {JobId} script warnings: {Warnings}",
                jobId, string.Join("; ", result.Warnings));
        }

        // ── AI safety agent layer (SoW: AI-based pre-review of script content) ──
        // Catches nuance the wordlist cannot: suicide/self-harm references (Mindframe),
        // graphic injury detail, masked profanity, victim identification.
        var safetySw = Stopwatch.StartNew();
        var llmReview = await scriptSafetyReviewer.ReviewScriptSafetyAsync(script, jobId, cancellationToken);
        safetySw.Stop();

        if (llmReview.IsFailure)
        {
            // Fail-open: the deterministic wordlist passed and the mandatory human approval
            // gate remains. A transient LLM outage must not kill a scheduled bulletin.
            logger.LogWarning(
                "[ContentSafety] Job {JobId} AI safety review unavailable ({ElapsedMs}ms): {Error}",
                jobId, safetySw.ElapsedMilliseconds, llmReview.Error);
            await jobStatusUpdater.LogJobAsync(jobId,
                "AI safety review unavailable — banned-word check passed; relying on human review",
                cancellationToken);
            return Result<string>.Success(script);
        }

        if (!llmReview.Value.IsSafe)
        {
            string summary = string.Join("; ", llmReview.Value.Violations);
            logger.LogWarning(
                "[ContentSafety] Job {JobId} AI safety review blocked script: {Violations}",
                jobId, summary);
            await jobStatusUpdater.LogJobAsync(jobId, $"AI safety review blocked: {summary}", cancellationToken);
            return Result<string>.Failure(OperationalFailure.SafetyRejected("broadcast.content_safety_rejected", $"Content safety violation: {summary}"));
        }

        logger.LogInformation(
            "[ContentSafety] Job {JobId} AI safety review passed in {ElapsedMs}ms",
            jobId, safetySw.ElapsedMilliseconds);
        await jobStatusUpdater.LogJobAsync(jobId, "AI safety review passed", cancellationToken);

        return Result<string>.Success(script);
    }
}
