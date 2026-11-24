using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Contracts;
using Azure.Core;
using Azure.Identity;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     The configuration the 2026-08-26 trials converged on: structured output with evidence ids
///     KEPT, the four length-styles and the <c>continuation</c> style DELETED, one finished sentence
///     per fact, and a deterministic completion predicate.
///     <para>
///         What the two earlier arms established, each by failing differently:
///         <list type="bullet">
///             <item><b>The style dial was the defect, not the model.</b> The menu's <c>concise</c>
///                 variant dropped cross-street anchors ("London Circuit is closed eastbound"),
///                 because in a traffic sentence the only compressible material is the locative. One
///                 finished sentence per fact, plus an explicit "never delete a location" rule,
///                 dropped zero anchors across 39 real calls.</item>
///             <item><b>The evidence-id enum is load-bearing grounding, not decoration.</b> The
///                 plain-text arm removed it and fabricated road closures in 3 of 13 bulletins -
///                 adelaide invented five roads in one read ("Main North Road's closed citybound at
///                 Regency Road"), and brisbane anonymised every road into "the motorway" / "the main
///                 highway". The structured arm fabricated nothing in 39 calls.</item>
///             <item><b>A revision turn must carry the evidence.</b> Sending only the previous script
///                 plus a delta produced three bulletins that were apologies ("I'm missing the
///                 evidence needed to revise the bulletin") - which a schema physically cannot
///                 emit, and plain text happily airs.</item>
///             <item><b>Re-drafting is not revising.</b> Rebuilding the prompt each iteration without
///                 the prior text made the delta unanchored: mildura went 101 -> 101 -> 67 against a
///                 target of 112.</item>
///         </list>
///     </para>
///     <para>
///         <b>Starvation is a terminal success, not a loop condition.</b> Every earlier run wasted
///         iterations on markets that cannot fill their slot - Port Lincoln has 0 corridors and 0
///         incidents against a 121-syllable target. The predicate now distinguishes "short because
///         the evidence is exhausted" from "short because the writer left material unused" by
///         measuring CITATION COVERAGE: if the writer cited essentially every available source and
///         is still under the floor, that is the honest ceiling and the remainder belongs to filler.
///     </para>
///     <para>
///         The gate is denominated in SYLLABLES because that is what predicts duration (6.38% vs
///         7.09% within-voice CV, measured over 1,000 dev syntheses), but the feedback is expressed
///         in WORDS because that is the unit the model can actually count. Conversion uses the
///         measured 1.61 syllables per word.
///     </para>
///     <para>Opt-in via <c>ARN_SYLLABLE_PROBE=1</c>; makes real model calls.</para>
/// </summary>
public sealed class SyllableLoopProbe(ITestOutputHelper output)
{
    /// <summary>Exit band = 1 sigma of measured within-voice syllable-rate variance.</summary>
    private const double BandFraction = 0.0638;

    /// <summary>Measured pooled ratio: 4.06 syllables/sec over 2.52 words/sec.</summary>
    private const double SyllablesPerWord = 1.61;

    /// <summary>Cited-source fraction above which a short read counts as evidence-exhausted.</summary>
    private const double StarvationCoverage = 0.85;

    private const int MaxIterations = 3;

    [Fact]
    public async Task StructuredSingleRead_ClampedTarget_RevisionLoop()
    {
        if (Environment.GetEnvironmentVariable("ARN_SYLLABLE_PROBE") != "1")
        {
            output.WriteLine("skipped: set ARN_SYLLABLE_PROBE=1 (makes real model calls)");
            return;
        }

        var fixtures = FallbackReplayFixture.LoadAll()
            .Where(f => !string.Equals(f.Market, "TEST STATION", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Snapshot.FillOrder is { Count: > 0 })
            .GroupBy(f => (f.Market, f.Snapshot.TargetDurationSeconds))
            .Select(g => g.First())
            .ToList();
        // ARN_PROBE_MARKET trials a single fixture, so a wording change can be read directly
        // instead of inferred from an aggregate.
        string? only = Environment.GetEnvironmentVariable("ARN_PROBE_MARKET");
        if (!string.IsNullOrWhiteSpace(only))
            fixtures = fixtures.Where(f => f.Market.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(fixtures);

        // DeployedPipeline() is the single source of truth for which agents and endpoint the
        // live pipeline uses; hardcoding them here would create a second one, which
        // HarnessFidelityTests exists to prevent.
        var configuration = TestConfigurationHelper.BuildConfiguration(
            TestConfigurationHelper.DeployedPipeline());
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TokenCredential>(new AzureCliCredential());
        await using var provider = services.BuildServiceProvider();

        var transport = new ResponsesTransport(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<AzureAIOptions>>(),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<LlmScriptGenerator>>(),
            provider.GetRequiredService<ILlmCallCapture>(),
            provider.GetRequiredService<TokenCredential>());

        var reviewer = provider.GetRequiredService<IScriptReviewer>();
        var auth = await transport.GetAuthHeaderAsync(CancellationToken.None);
        Assert.True(auth.IsSuccess, $"auth failed: {auth.Error}");

        var rates = SpeechRates.FromSeed(FallbackReplayFixture.SynthesisHistorySeedPath());
        var rows = new List<ProbeRow>();
        foreach (var fixture in fixtures)
        {
            try
            {
                rows.Add(await RunOneAsync(fixture, transport, reviewer, auth.Value, rates));
            }
            catch (Exception ex)
            {
                output.WriteLine($"{fixture.Market}: EXCEPTION {ex.GetType().Name} {ex.Message}");
            }
        }

        Report(rows);
    }

    private async Task<ProbeRow> RunOneAsync(
        FallbackReplayFixture fixture,
        ResponsesTransport transport,
        IScriptReviewer reviewer,
        (string Name, string Value) auth,
        SpeechRates rates)
    {
        var context = fixture.Snapshot.ToContext(fixture.JobId);
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);
        string evidence = PromptLibrary.RenderEvidenceUnits(units);
        var allowed = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var suburbById = units
            .Where(u => u.Field.Equals("suburb", StringComparison.OrdinalIgnoreCase))
            .GroupBy(u => u.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        var availableSources = units.Select(u => SourcePrefix(u.Id)).ToHashSet(StringComparer.Ordinal);

        double slot = fixture.Snapshot.TargetDurationSeconds;
        double speed = fixture.VoiceConfig.Settings?.Speed ?? 1.0;
        double sps = rates.SyllablesPerSecond(fixture.VoiceConfig.VoiceId, fixture.VoiceConfig.Model, speed);

        var bookend = PlainBookends.For(fixture.Market);
        int bookendSyl = bookend is null
            ? 0
            : SyllableCounter.CountSyllables($"{bookend.Intro} {bookend.Outro}");

        double slotSyl = slot * sps;
        double targetSyl = slotSyl - bookendSyl;
        int lo = (int)Math.Floor(targetSyl * (1 - BandFraction));
        int hi = (int)Math.Ceiling(targetSyl * (1 + BandFraction));

        var attempts = new List<int>();
        var trips = new List<string>();
        string script = string.Empty;
        double coverage = 0;
        (double Density, int ColdRun) flow = (0, 0);
        IReadOnlyList<DraftItem> finalItems = [];

        int itemBudget = 0;

        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            string prompt = iteration == 1
                ? BuildDraftPrompt(context.Location, evidence, targetSyl, lo, hi)
                : BuildRevisionPrompt(context.Location, evidence, script, attempts[^1], targetSyl,
                    lo, hi, trips, itemBudget);

            var result = await transport.DispatchStructuredLlmAsync(
                "Draft", prompt, 4096, CancellationToken.None, auth,
                jsonSchema: SingleReadSchema(allowed),
                systemInstructionsOverride: PromptLibrary.SystemInstructions + SingleReadContract,
                jobId: $"probe-{fixture.JobId}",
                hasProducerFeedback: false);

            if (result.IsFailure)
                return await Row(Outcome.Error, [$"dispatch: {result.Error}"], iteration);

            var items = ParseItems(result.Value);
            script = string.Join(" ", items.Select(i => i.Text));
            int syllables = SyllableCounter.CountSyllables(script);
            attempts.Add(syllables);

            var cited = items.SelectMany(i => i.EvidenceIds).Select(SourcePrefix)
                .ToHashSet(StringComparer.Ordinal);
            coverage = availableSources.Count == 0 ? 1 : (double)cited.Count / availableSources.Count;

            finalItems = items;
            flow = FlowOf(items);
            trips = ContentGateTrips(items, allowed, suburbById);
            if (trips.Count == 0)
            {
                if (syllables >= lo && syllables <= hi)
                    return await Row(Outcome.Fit, [], iteration);

                // Layer 1: the writer used essentially everything and is still short. No further
                // call can honestly close the gap - the slot is longer than the market's news.
                if (syllables < lo && coverage >= StarvationCoverage)
                    return await Row(Outcome.Starved, [], iteration);

                // Layer 1, the other half: the draft itself measures what an item costs in THIS
                // market's phrasing, so the item budget needs no maintained constant and
                // self-calibrates per read. Every flood in the previous run was the writer voicing
                // 5-6 roads into a slot that holds 2-3, then refusing to cut because the required
                // weather/fuel fixtures are not droppable. Telling it a COUNT converts an
                // unsatisfiable soft instruction ("as many as the length allows") into arithmetic.
                if (syllables > hi && items.Count > 1)
                    itemBudget = Math.Max(1, (int)Math.Floor(targetSyl / (syllables / (double)items.Count)));
            }
        }

        return await Row(attempts.Count > 0 && attempts[^1] > hi ? Outcome.Flooded : Outcome.Miss,
            trips, MaxIterations);

        async Task<(string Verdict, int Issues, string Defects)> ReviewAsync()
        {
            if (finalItems.Count == 0)
                return ("-", 0, "");
            var drafted = finalItems
                .Select((i, n) => new DraftedSentence(n + 1, i.Text, i.EvidenceIds))
                .ToList();
            var verdict = await reviewer.ReviewScriptAsync(
                string.Join(" ", finalItems.Select(i => i.Text)),
                (int)slot, context: context, cancellationToken: CancellationToken.None,
                draftedSentences: drafted);
            if (verdict.IsFailure)
                return ("ERROR", 0, verdict.Error ?? "");
            var issues = verdict.Value.Issues ?? [];
            return (verdict.Value.IsApproved ? "APPROVED" : "NEEDS_REVISION", issues.Count,
                string.Join(",", issues.Select(i => i.Defect).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct()));
        }

        async Task<ProbeRow> Row(Outcome outcome, IReadOnlyList<string> gateTrips, int iterations)
        {
            var (verdict, issues, defects) = await ReviewAsync();
            // Bookend duplication: the composer adds intro+outro AROUND the body, so the body must
            // not sign off by itself. Dev job a4275be0 (2026-08-25) aired the KIIS outro twice.
            bool duplicated = bookend is not null && (
                script.Contains(bookend.Outro[..Math.Min(18, bookend.Outro.Length)], StringComparison.OrdinalIgnoreCase)
                || script.Contains(bookend.Intro[..Math.Min(18, bookend.Intro.Length)], StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(script, @"that'?s (it for |your |the )?\w+ (traffic|and weather)", RegexOptions.IgnoreCase)
                || script.Contains("powered by", StringComparison.OrdinalIgnoreCase));
            return new ProbeRow(
                fixture.Market, slot, sps, (int)slotSyl, bookendSyl, (int)targetSyl, lo, hi,
                attempts, outcome, gateTrips, iterations, script, bookend, coverage,
                flow.Density, flow.ColdRun, verdict, issues, defects, duplicated);
        }
    }

    /// <summary>
    ///     Fraction of non-opening sentences that join to what precedes them, and the longest run of
    ///     consecutive sentences that open cold on a subject.
    ///     <para>
    ///         <see cref="SentenceShapeDetector" /> already owns read shape, but its staccato gate
    ///         only counts sentences of 9 words or fewer (<c>StaccatoWords</c>), so it cannot see the
    ///         defect this measures: run 5's goldcoast read was four well-formed 15-18 word sentences
    ///         with ZERO connectives between them - grammatical, and a database read-out on air. The
    ///         menu contract caused it by requiring every sentence to stand alone "because any item
    ///         may end up leading"; once position is fixed that requirement is void.
    ///     </para>
    /// </summary>
    private static (double Density, int ColdRun) FlowOf(IReadOnlyList<DraftItem> items)
    {
        string[] joins =
        [
            "meanwhile", "elsewhere", "also", "still", "though", "while", "and", "but",
            "over on", "closer in", "further out", "that's after", "as for", "on top of that",
            "similarly", "likewise", "there's also", "in the meantime", "at the same time",
            "too", "as well", "either", "plus"
        ];
        int joined = 0, cold = 0, worstCold = 0;
        for (int i = 0; i < items.Count; i++)
        {
            string text = items[i].Text;
            bool hasJoin = joins.Any(j => text.Contains(j, StringComparison.OrdinalIgnoreCase));
            if (i > 0 && hasJoin)
                joined++;
            if (hasJoin)
            {
                cold = 0;
            }
            else
            {
                cold++;
                worstCold = Math.Max(worstCold, cold);
            }
        }
        return (items.Count > 1 ? (double)joined / (items.Count - 1) : 1, worstCold);
    }

    private const string SingleReadContract = """


        ## OUTPUT CONTRACT (overrides any menu/variant instruction above)

        Return ONE finished spoken sentence per fact - not alternatives, not the same fact phrased
        at several lengths. There is no style dial.

        POSITION IS KNOWN. The sentences you return ARE the bulletin, in the order you return them.
        Nothing downstream reorders them and nothing drops one out of the middle. So IGNORE any
        instruction above about a sentence having to stand alone or read correctly in any
        combination - that requirement does not apply to you.

        Because you know the order, the read may flow as continuous speech rather than a list of
        separate facts.

        Every sentence MUST keep the road AND the location the evidence supplies for it - the suburb
        or the cross-street. Shortening a sentence by dropping where it is makes the item unairable:
        a driver cannot act on "London Circuit is closed eastbound", and a generic name like Church
        Street is ambiguous across suburbs. Never replace a road's name with a description either
        ("the motorway", "the main highway") - name it. Buy length by voicing fewer facts, NEVER by
        deleting an identity.

        If a sentence names more than one road, it MUST cite the evidence ids for EVERY road it
        names. Never assert a shared condition ("all clear", "and other roads are clear") over roads
        you have not each cited - say only what the cited ids support, for exactly the roads named.

        If the evidence does not contain enough to fill the length, voice everything worth voicing
        and STOP. A short honest bulletin is correct; padding and invention are not.
        """;

    /// <summary>Editorial style, as one configurable line. Override with ARN_PROBE_STYLE to trial wording.</summary>
    private static string StyleLine =>
        Environment.GetEnvironmentVariable("ARN_PROBE_STYLE")
        ?? "Find a nice, flowing way to connect the sentences so it sounds like a presenter talking.";

    private static string BuildDraftPrompt(string location, string evidence, double targetSyl, int lo, int hi)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Write one traffic bulletin for {location} radio as a single coherent read.");
        sb.AppendLine();
        sb.AppendLine("EVIDENCE - cite by the bracketed ids exactly as printed:");
        sb.AppendLine(evidence);
        sb.AppendLine();
        sb.AppendLine($"LENGTH: about {Words(targetSyl)} words ({(int)Math.Round(targetSyl)} syllables).");
        sb.AppendLine("Voice the most important roads first, and only as many as that length allows.");
        // Deliberately in the INPUT prompt, not the system instructions: this is editorial taste,
        // so it belongs on the surface ARN can already configure per station rather than baked
        // into the shipped kernel. Kept to one line on purpose - the previous attempt enumerated
        // connectives ("meanwhile", "elsewhere", "also") and the writer used them verbatim, in the
        // same order, in 6 of 10 bulletins.
        sb.AppendLine(StyleLine);
        return sb.ToString();
    }

    /// <summary>
    ///     The revision turn. Carries BOTH the previous script and the evidence: sending the script
    ///     alone made "add detail from the evidence" impossible, and three bulletins came back as
    ///     apologies. The delta is expressed in words because that is the unit the model can count,
    ///     while the gate stays in syllables because that is what predicts duration.
    /// </summary>
    private static string BuildRevisionPrompt(
        string location, string evidence, string previous, int actualSyl,
        double targetSyl, int lo, int hi, IReadOnlyList<string> trips, int itemBudget)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Revise this {location} bulletin. It is your own previous draft:");
        sb.AppendLine();
        sb.AppendLine(previous);
        sb.AppendLine();
        sb.AppendLine("THE SAME EVIDENCE - cite by the bracketed ids exactly as printed:");
        sb.AppendLine(evidence);
        sb.AppendLine();
        int deltaWords = Math.Abs(Words(targetSyl) - Words(actualSyl));
        if (actualSyl > hi)
        {
            sb.AppendLine($"The draft runs about {deltaWords} words long.");
            if (itemBudget > 0)
                sb.AppendLine($"Return EXACTLY {itemBudget} item(s) this time. Keep the "
                              + $"{itemBudget} most important and drop every other one COMPLETELY - "
                              + "including any required weather or fuel line if it does not make the "
                              + "cut. Do not try to keep everything by shortening sentences, and do "
                              + "not remove any road's location.");
            else
                sb.AppendLine("Drop the LEAST important road entirely rather than shaving detail "
                              + "off several, and keep every road's location.");
        }
        else if (actualSyl < lo)
            sb.AppendLine($"The draft runs about {deltaWords} words short. Add another road from the "
                          + "evidence that you have not voiced yet, or add real detail the evidence "
                          + "already supplies. Invent nothing and pad with nothing.");
        if (trips.Count > 0)
            sb.AppendLine($"Also fix these: {string.Join("; ", trips.Distinct())}.");
        sb.AppendLine($"Aim for about {Words(targetSyl)} words in total.");
        return sb.ToString();
    }

    private static int Words(double syllables) => (int)Math.Round(syllables / SyllablesPerWord);

    private static string SourcePrefix(string id)
    {
        int colon = id.IndexOf(':');
        return colon > 0 ? id[..colon] : id;
    }

    /// <summary>
    ///     The content half of the predicate. Without it a length-only loop buys syllables by
    ///     deleting the locative - the defect the menu's <c>concise</c> style produced.
    /// </summary>
    private static List<string> ContentGateTrips(
        IReadOnlyList<DraftItem> items,
        IReadOnlySet<string> allowed,
        IReadOnlyDictionary<string, string> suburbById)
    {
        var trips = new List<string>();
        foreach (var item in items)
        {
            foreach (string id in item.EvidenceIds.Where(id => !allowed.Contains(id)))
                trips.Add($"fabricated-id:{id}");
            foreach (string id in item.EvidenceIds)
            {
                if (!suburbById.TryGetValue(id, out string? suburb) || string.IsNullOrWhiteSpace(suburb))
                    continue;
                if (!item.Text.Contains(suburb, StringComparison.OrdinalIgnoreCase))
                    trips.Add($"anchor-dropped:{suburb}");
            }
        }
        return trips;
    }

    /// <summary>
    ///     The BARE schema object - the transport supplies the name and strict flag itself, so
    ///     wrapping this in {name, strict, schema} is one envelope too many. The evidence-id enum is
    ///     the grounding that stopped the plain-text arm's fabricated closures.
    /// </summary>
    private static BinaryData SingleReadSchema(IReadOnlySet<string> allowed)
    {
        var ids = allowed.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "items" },
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "category", "evidenceIds", "text" },
                        properties = new
                        {
                            category = new
                            {
                                type = "string",
                                @enum = new[]
                                {
                                    "closure", "incident", "congestion", "works",
                                    "general_flow", "weather", "fuel"
                                }
                            },
                            evidenceIds = new { type = "array", items = new { type = "string", @enum = ids } },
                            text = new { type = "string" }
                        }
                    }
                }
            }
        };
        return BinaryData.FromString(JsonSerializer.Serialize(schema));
    }

    private static IReadOnlyList<DraftItem> ParseItems(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("items", out var items))
            return [];
        var parsed = new List<DraftItem>();
        foreach (var item in items.EnumerateArray())
        {
            string text = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var ids = item.TryGetProperty("evidenceIds", out var e)
                ? e.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : [];
            if (!string.IsNullOrWhiteSpace(text))
                parsed.Add(new DraftItem(text.Trim(), ids));
        }
        return parsed;
    }

    private void Report(IReadOnlyList<ProbeRow> rows)
    {
        output.WriteLine("");
        output.WriteLine("| market | slot | bookend | body target | band | attempts | iters | outcome | REVIEW | issues | dup bookend |");
        output.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            output.WriteLine($"| {r.Market} | {r.Slot:F0}s | {(r.BookendSyllables == 0 ? "-" : $"{r.BookendSyllables}")} | "
                             + $"{r.TargetSyllables} | {r.Lo}-{r.Hi} | {string.Join(" -> ", r.Attempts)} | "
                             + $"{r.Iterations} | {r.Outcome} | {r.ReviewVerdict} | "
                             + $"{(r.ReviewIssues == 0 ? "-" : $"{r.ReviewIssues}: {r.ReviewDefects}")} | "
                             + $"{(r.BookendDuplicated ? "**YES**" : "no")} |");

        int fit = rows.Count(r => r.Outcome == Outcome.Fit);
        int starved = rows.Count(r => r.Outcome == Outcome.Starved);
        int resolved = fit + starved;
        int firstCall = rows.Count(r => r.Outcome is Outcome.Fit or Outcome.Starved && r.Iterations == 1);
        int anchor = rows.Count(r => r.GateTrips.Any(t => t.StartsWith("anchor-dropped", StringComparison.Ordinal)));
        int fabricated = rows.Count(r => r.GateTrips.Any(t => t.StartsWith("fabricated-id", StringComparison.Ordinal)));
        double meanIters = rows.Count > 0 ? rows.Average(r => r.Iterations) : 0;

        output.WriteLine("");
        output.WriteLine($"PROBE SUMMARY | fixtures={rows.Count} resolved={resolved}/{rows.Count} "
                         + $"(fit={fit} starved={starved}) firstCall={firstCall}/{rows.Count} "
                         + $"flooded={rows.Count(r => r.Outcome == Outcome.Flooded)} "
                         + $"miss={rows.Count(r => r.Outcome == Outcome.Miss)} "
                         + $"| anchorDropped={anchor} fabricatedId={fabricated} "
                         + $"| reviewApproved={rows.Count(r => r.ReviewVerdict == "APPROVED")}/{rows.Count} bookendDuplicated={rows.Count(r => r.BookendDuplicated)} "
                         + $"| llmCallsPerBulletin={meanIters:F1} (vs 7.1-11.5 today)");

        foreach (var r in rows.Where(r => r.Script.Length > 0))
        {
            output.WriteLine("");
            output.WriteLine($"### {r.Market} ({r.Slot:F0}s | bookend {r.BookendSyllables} syl | "
                             + $"target {r.TargetSyllables} | got {r.Attempts.LastOrDefault()} | {r.Outcome})");
            output.WriteLine(r.Bookend is null
                ? r.Script
                : $"[{r.Bookend.Intro}] {r.Script} [{r.Bookend.Outro}]");
        }
    }

    private enum Outcome { Fit, Starved, Flooded, Miss, Error }

    private sealed record DraftItem(string Text, IReadOnlyList<string> EvidenceIds);

    private sealed record ProbeRow(
        string Market,
        double Slot,
        double Sps,
        int SlotSyllables,
        int BookendSyllables,
        int TargetSyllables,
        int Lo,
        int Hi,
        IReadOnlyList<int> Attempts,
        Outcome Outcome,
        IReadOnlyList<string> GateTrips,
        int Iterations,
        string Script,
        PlainBookend? Bookend,
        double Coverage,
        double FlowDensity,
        int ColdRun,
        string ReviewVerdict,
        int ReviewIssues,
        string ReviewDefects,
        bool BookendDuplicated);
}
