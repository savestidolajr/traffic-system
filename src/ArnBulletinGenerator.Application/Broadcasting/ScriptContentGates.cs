using System.Diagnostics;
using ArnBulletinGenerator.Application.Validation;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     The deterministic quality boundary between the workflow drafter and synthesis, plus the
///     single content-safety exit. Three ideas, none of them an LLM call:
///     <list type="number">
///         <item>
///         <b>Observe judgement, enforce membership.</b> Judgement-based fidelity/style detectors run in
///         two tiers. The factual detectors for fabricated locations, false closure claims, content from
///         an unselected source, and production-language meta leaks are blocking: the first three assert
///         facts contradicted by structured evidence, while a meta leak exposes our own production
///         vocabulary rather than a broadcast fact. A failed read must be re-drafted rather than aired.
///         This is affordable because measured rejection rates were FakeLocation 0/93 historical and
///         0/14 current bulletins, FalseClosure 0/93 and 0/14, UnselectedSource 1/93 and 0/14, and
///         MetaLeak 1/14. The remaining fidelity defects are advisory: suburb dropped from an incident,
///         required source missing, cloned sentence, evidence restated verbatim, and incident-type stack
///         are omissions or style concerns rather than evidence-contradicting factual assertions. Every
///         style detector likewise remains advisory. The advisory detectors re-run without the workflow's
///         full context (selection flags, prior read, phrasing menus), so they must not resurrect the
///         false-positive rejections the deleted LLM review loop caused.
///         Candidate membership is the explicit exception: <see cref="CandidateGrounding" /> performs
///         judgement-free set membership over our own vocabulary in both directions, comparing the
///         candidate roads the writer was handed with the full fetched road pool. It has one correct
///         answer and is precisely the check the workflow's own per-candidate checks cannot make.
///         An earlier prose-extraction road-name check, tried on 2026-08-28, proved unsafe:
///         sentence-initial common noun “Traffic” and market name “Sydney” as roads in the all-clear
///         fixture “Traffic is flowing smoothly across all major roads in Sydney today”. That
///         extraction false-positive is why this guard must remain membership-based, rather than
///         being simplified back onto a prose extractor. The candidate-membership guard and the
///         mechanical style validator therefore fail an attempt.
///         </item>
///         <item>
///             <b>One honest repair.</b> A required weather/fuel fixture the read omitted is
///             spliced from the fetched data — the only defect class whose repair needs no
///             judgement, because the facts are already structured.
///         </item>
///         <item>
///             <b>Fit selection is pure.</b> The ceiling-target and phrasing-menu selectors are
///             static functions shared by every fit path, so the over-length and under-length
///             cases can never disagree about what fits or what is untouchable.
///         </item>
///     </list>
/// </summary>
internal sealed class ScriptContentGates(
    IOptions<BroadcastOptions> options,
    IBroadcastMetrics metrics,
    IContentSafetyFilter contentSafetyFilter,
    IScriptSafetyReviewer scriptSafetyReviewer,
    ILogger<BroadcastOrchestrator> logger,
    JobStatusUpdater jobStatusUpdater,
    TimeProvider timeProvider)
{
    private readonly BroadcastOptions _options = options.Value;

    /// <summary>The gated read plus the phrasing menu, realigned to whatever the gates changed.</summary>
    internal readonly record struct GatedScript(
        string Script,
        IReadOnlyList<DraftedSentence>? DraftedSentences);

    /// <summary>
    ///     Runs every deterministic content gate over a fresh draft, in order: fixture splice,
    ///     <see cref="CandidateGrounding" /> membership guard (blocking), factual fidelity detectors
    ///     for fabricated locations, false closure claims, unselected-source content, and production
    ///     language meta leaks (blocking), remaining fidelity detectors (advisory), spoken-form
    ///     normalisation, style detectors, and style validation. The first three factual classes assert
    ///     facts contradicted by evidence; a meta leak instead exposes our own production vocabulary,
    ///     which is never valid on air. They trigger a re-draft. Measured rejection rates make this
    ///     affordable: FakeLocation 0/93 historical and 0/14 current bulletins, FalseClosure 0/93 and
    ///     0/14, UnselectedSource 1/93 and 0/14, and MetaLeak 1/14. Suburb dropped from an incident
    ///     remains advisory (2/14 current bulletins); the other fidelity remainder is also advisory
    ///     because it is omission/style, not a contradicted fact or production-language leak. Every style
    ///     item remains advisory. Advisory detectors re-run without workflow context (selection flags,
    ///     prior read, phrasing menus), while CandidateGrounding has one correct set-membership answer.
    ///     Only CandidateGrounding, the four blocking fidelity detectors, and the style validator fail
    ///     an attempt.
    /// </summary>
    internal async Task<Result<GatedScript>> EnforceAsync(
        BroadcastWithTraffic context,
        string script,
        CancellationToken cancellationToken,
        IReadOnlyList<DraftedSentence>? draftedSentences = null)
    {
        // ── Required-fixture splice ──
        // The station promised weather/fuel air whenever fetched. A read that omitted one gets the
        // deterministic fixture line appended to the BODY (before any outro) rather than failing —
        // the facts are structured, so no judgement is involved. The splice invalidates the
        // phrasing menu (sentence counts no longer match), which downstream menu fitting detects
        // and skips on its own.
        string spliced = SpliceRequiredFixtures(script, context);
        if (!string.Equals(spliced, script, StringComparison.Ordinal))
        {
            await jobStatusUpdater.LogJobAsync(context.JobId,
                "Required weather/fuel fixture was missing from the read — spliced from the fetched data.",
                cancellationToken);
            script = spliced;
        }
        // ── Candidate membership guard (blocking) ──
        // Run after the fixture splice: supplementary weather/fuel lines may mention places, but
        // CandidateGrounding only considers fetched road vocabulary. Unlike judgement-based fidelity
        // detectors, membership has one correct answer and must be checked outside the workflow,
        // whose per-candidate checks can only see candidates and therefore cannot detect that the
        // writer was handed the wrong collection.
        var grounding = CandidateGrounding.Check(script, context);
        if (grounding.HasNonCandidateRoad)
        {
            string offending = string.Join("; ", grounding.NonCandidateRoads);
            string failure = $"Candidate grounding rejected: {offending}";
            await jobStatusUpdater.LogJobAsync(context.JobId, failure, cancellationToken);
            metrics.TrackPipelineStage("candidate_grounding_reject", 0, context.Location, false);
            logger.LogWarning(
                "[ContentGates] Job {JobId} candidate grounding rejected: {Offending}",
                context.JobId, offending);
            return Result<GatedScript>.Failure(failure);
        }

        // ── Factual fidelity detectors (blocking) / remaining fidelity (advisory) ──
        var (blocking, advisory, style) = DetectViolations(script, context, draftedSentences);
        if (blocking.Count > 0)
        {
            string named = string.Join(", ", blocking);
            string failure = $"Factual fidelity detectors rejected the read: {named}.";
            await jobStatusUpdater.LogJobAsync(context.JobId, failure, cancellationToken);
            metrics.TrackPipelineStage("fidelity_reject", 0, context.Location, false);
            logger.LogWarning(
                "[ContentGates] Job {JobId} factual fidelity detectors rejected the read: {Named}",
                context.JobId, named);
            return Result<GatedScript>.Failure(failure);
        }
        if (style.Count > 0)
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Style detectors noted (advisory): {string.Join(", ", style)}.",
                cancellationToken);
        if (advisory.Count > 0)
        {
            string named = string.Join(", ", advisory);
            metrics.TrackPipelineStage("fidelity_detector_flag", 0, context.Location, false);
            logger.LogWarning(
                "[ContentGates] Job {JobId} advisory fidelity detectors flagged: {Named}",
                context.JobId, named);
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Fidelity detectors flagged (advisory): {named}.",
                cancellationToken);
        }

        // ── Spoken forms, then style validation ──
        // Normalisation runs LAST-BEFORE-VALIDATION because a gate that runs before the last
        // mutation does not hold (2026-08-23: "$1.96" vs "a dollar ninety-six" split across
        // markets on the same build). Idempotent, so the synthesis path may normalise again.
        script = SpokenFormNormalizer.Normalize(
            script, SpokenFormNormalizer.ResolveDaypart(context.TimezoneId, timeProvider.GetUtcNow()));
        var validation = TrafficScriptStyleValidator.Validate(script);
        if (!validation.IsValid)
        {
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Style gate found {validation.Issues.Count} issue(s); re-drafting rather than patching.",
                cancellationToken);
            return Result<GatedScript>.Failure(string.Join(" ", validation.Issues));
        }

        return Result<GatedScript>.Success(new GatedScript(script, draftedSentences));
    }

    /// <summary>
    ///     Deterministic defect roll-call split into fidelity defects that block (fabricated location,
    ///     false closure claim, content from an unselected source, and production-language meta leak),
    ///     fidelity defects that remain advisory (suburb dropped from an incident, required source
    ///     missing, cloned sentence, evidence restated verbatim, incident-type stack), and STYLE defects
    ///     that remain advisory. The first three blocking classes assert facts contradicted by evidence;
    ///     a meta leak exposes our own production vocabulary rather than an on-air fact. Measured rates
    ///     are FakeLocation 0/93 historical and 0/14 current, FalseClosure 0/93 and 0/14,
    ///     UnselectedSource 1/93 and 0/14, and MetaLeak 1/14, making blocking affordable. Suburb dropped
    ///     is 2/14, so that lower-stakes omission stays advisory.
    /// </summary>
    internal static (
        IReadOnlyList<string> Blocking,
        IReadOnlyList<string> Advisory,
        IReadOnlyList<string> Style) DetectViolations(
        string script,
        BroadcastWithTraffic context,
        IReadOnlyList<DraftedSentence>? draftedSentences = null)
    {
        var blocking = new List<string>();
        if (FakeLocationDetector.Detect(script).HasFakeLocation)
            blocking.Add("fabricated location");
        if (UnselectedSourceDetector.Detect(script, context).HasFabrication)
            blocking.Add("content from an unselected source");
        if (FalseClosureDetector.Detect(script, context).HasFalseClosure)
            blocking.Add("false closure claim");
        if (MetaLeakDetector.Detect(script).HasMetaLeak)
            blocking.Add("meta leak (production language on air)");

        var advisory = new List<string>();
        if (IncidentTypeStackDetector.Detect(script).HasTypeStack)
            advisory.Add("incident-type stack");
        if (RequiredSourceDetector.Detect(script, context).HasMissingSource)
            advisory.Add("required source missing");
        if (SuburbRetentionDetector.Detect(script, context).HasMissingSuburb)
            advisory.Add("suburb dropped from an incident");
        if (EvidenceRestatementDetector.Detect(draftedSentences).HasRestatement)
            advisory.Add("evidence restated verbatim");
        if (SentenceCloneDetector.Detect(script, context).HasClone)
            advisory.Add("cloned sentence");

        var style = new List<string>();
        if (RollCallDetector.Detect(script, context).HasRollCall)
            style.Add("road roll-call");
        if (DuplicateAnchorDetector.Detect(script, context).HasDuplicateAnchor)
            style.Add("duplicate anchor");
        if (SecondaryItemCapDetector.Detect(script, context).HasExcessItems)
            style.Add("secondary item overrun");
        if (SentenceShapeDetector.Detect(script, StationLines(context)).HasIssue)
            style.Add("sentence shape");
        if (RepeatedCauseDetector.Detect(script, context).HasRepeatedCause)
            style.Add("repeated cause");
        if (RepeatedPrepositionDetector.Detect(script).HasRepeatedPreposition)
            style.Add("repeated preposition");
        if (ImpactStackDetector.Detect(script).HasImpactStack)
            style.Add("impact stack");
        if (SectionOrderDetector.Detect(script, context).HasOutOfOrderSection)
            style.Add("section out of order");
        if (RepeatedStatePhraseDetector.Detect(script).HasExcessRepetition)
            style.Add("repeated state phrase");
        if (SplitClearRunDetector.Detect(script).HasSplitClearRun)
            style.Add("split clear-run");
        if (ConnectorFrequencyDetector.Detect(script).HasOveruse)
            style.Add("connector overuse");
        if (RedundantTailDetector.Detect(script).HasRedundantTail)
            style.Add("redundant tail");
        if (VaguenessDetector.Detect(script).HasVagueness)
            style.Add("vagueness");
        return (blocking, advisory, style);
    }

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

    /// <summary>
    ///     Appends the deterministic fixture line(s) for any required source the data holds but the
    ///     script never voiced. New lines go to the tail of the BODY, not the end of the read: a
    ///     composed take may already carry the station outro, and appending after it buried the
    ///     closer mid-read (dev sydney <c>ffcf233d</c>, 2026-08-25, aired the KIIS sign-off twice
    ///     with the weather sandwiched between them).
    /// </summary>
    internal static string SpliceRequiredFixtures(string script, BroadcastWithTraffic context)
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


    internal readonly record struct CeilingVariantSelection(
        string Script,
        IReadOnlyList<DraftedSentence> Sentences);

    /// <summary>
    ///     Selects, from the draft's own phrasing menu, the LONGEST sentence-variant combination at
    ///     or under <paramref name="ceiling" /> words. Used by the slot-fit loop, pointed at an
    ///     extend target to GROW a short read — every phrasing in the menu is already drafted,
    ///     reviewed and grounded, so filling the slot from it is strictly better than asking a
    ///     model to invent words (measured 2026-08-25: 87% of production menus reach the slot
    ///     window on phrasing alone). Returns null when the menu does not cover the read or
    ///     nothing changes.
    /// </summary>
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

        // Find the phrasing combination that (1) fits under bodyCeiling, (2) maximises word count
        // under it (fill the slot), and (3) actually changes at least one sentence.
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
    ///     The single content-safety exit, and therefore the last chance to make the text safe to
    ///     speak: the deterministic wordlist first, then the AI safety agent (SoW: AI-based
    ///     pre-review of script content) for nuance the wordlist cannot catch — suicide/self-harm
    ///     references (Mindframe), graphic injury detail, masked profanity, victim identification.
    /// </summary>
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
