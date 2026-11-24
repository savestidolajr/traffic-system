using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

/// <summary>
///     Guards the sentence-to-evidence map handed to the reviewer as the STRUCTURED DRAFT.
///     <para>
///         Dev job 63684f38 (2026-08-24) is the case these tests replay. The draft cited perfectly:
///         r3 = M5/Kingsgrove, r2 = M4/Eastern Creek, r4 = Mitchell Road, r5 = Sir Reginald Ansett.
///         After a drop plus a section-order relocation, the reviewer was handed the Sir Reginald
///         sentence cited to r2, the M5 sentence cited to r4, and orders running 2..5 with no
///         order 1. It proved three "fabrications" — correctly, against the map it was given — the
///         refiner then invented ids, and the job burned four attempts and failed.
///     </para>
/// </summary>
public sealed class CitationAlignmentTests
{
    private const string M5 = "Westbound on the M5 at Kingsgrove, heavy delays are adding about 18 minutes.";
    private const string M4 = "The M4 through Eastern Creek is backed up.";
    private const string Mitchell = "There's a road closure on Mitchell Road between Sydney Park Road and Maddox Street, Erskineville.";
    private const string SirReginald = "A road closure is in place on Sir Reginald Ansett Drive at Mascot.";
    private const string Weather = "Partly cloudy, 15 degrees.";

    private static DraftedSentence[] DevJobDraft() =>
    [
        new(1, M5, ["r3:suburb", "r3:direction", "r3:delayMinutes"]),
        new(2, M4, ["r2:suburb", "r2:impact"]),
        new(3, Mitchell, ["r4:suburb", "r4:location:0", "r4:detail"]),
        new(4, SirReginald, ["r5:suburb", "r5:detail"]),
        new(5, Weather, ["weather:condition", "weather:temperature"])
    ];

    /// <summary>The exact shape that shipped the corrupted map: one sentence gone, the rest moved.</summary>
    [Fact]
    public void Drop_plus_reorder_keeps_every_sentence_with_its_own_evidence()
    {
        string before = string.Join(" ", M5, M4, Mitchell, SirReginald, Weather);
        // Mitchell dropped by the failed refine; Sir Reginald promoted ahead of the motorways.
        string after = string.Join(" ", SirReginald, M5, M4, Weather);

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(before, after, DevJobDraft())!;

        preserved.Select(s => s.Text).ShouldBe([SirReginald, M5, M4, Weather]);
        // Each sentence keeps the ids the draft committed for IT — the whole point.
        preserved[0].EvidenceIds.ShouldBe(["r5:suburb", "r5:detail"]);
        preserved[1].EvidenceIds.ShouldBe(["r3:suburb", "r3:direction", "r3:delayMinutes"]);
        preserved[2].EvidenceIds.ShouldBe(["r2:suburb", "r2:impact"]);
        preserved[3].EvidenceIds.ShouldBe(["weather:condition", "weather:temperature"]);
        // Orders address the script the reviewer is looking at, so a cited order patches the
        // sentence the reviewer meant.
        preserved.Select(s => s.Order).ShouldBe([1, 2, 3, 4]);
    }

    /// <summary>
    ///     A pure relocation — what SectionOrderDetector.Repair does. The old forward-only cursor
    ///     assumed survivors keep their relative order, so moving the weather line to the front
    ///     re-bound every citation after it.
    /// </summary>
    [Fact]
    public void Pure_reorder_does_not_shift_citations()
    {
        string before = string.Join(" ", M5, M4, Weather);
        string after = string.Join(" ", Weather, M5, M4);

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(
            before, after,
            [
                new DraftedSentence(1, M5, ["r3:suburb"]),
                new DraftedSentence(2, M4, ["r2:suburb"]),
                new DraftedSentence(3, Weather, ["weather:condition"])
            ])!;

        preserved.Select(s => s.EvidenceIds.Single())
            .ShouldBe(["weather:condition", "r3:suburb", "r2:suburb"]);
        preserved.Select(s => s.Order).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void A_rewritten_sentence_still_matches_on_content()
    {
        string before = string.Join(" ", M5, Weather);
        // Suburb-retention style repair: same claim, more words.
        const string rewritten = "Westbound on the M5 at Kingsgrove, heavy delays are adding about 18 minutes for drivers.";
        string after = string.Join(" ", rewritten, Weather);

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(
            before, after,
            [
                new DraftedSentence(1, M5, ["r3:suburb"]),
                new DraftedSentence(2, Weather, ["weather:condition"])
            ])!;

        preserved[0].Text.ShouldBe(rewritten);
        preserved[0].EvidenceIds.ShouldBe(["r3:suburb"]);
    }

    /// <summary>
    ///     The binding rule that stops the corruption at source. A single shared token used to be
    ///     enough, so an unrelated new sentence inherited a neighbour's facts — and the reviewer
    ///     then proved a fabrication that the writer never committed.
    /// </summary>
    [Fact]
    public void A_weakly_overlapping_sentence_gets_no_evidence_rather_than_a_neighbours()
    {
        string before = string.Join(" ", M5, Weather);
        // Shares only "closure" with nothing in the draft, and "road" is not meaningful enough.
        const string spliced = "A road closure is in place on Gardeners Road.";
        string after = string.Join(" ", M5, spliced, Weather);

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(
            before, after,
            [
                new DraftedSentence(1, M5, ["r3:suburb"]),
                new DraftedSentence(2, Weather, ["weather:condition"])
            ])!;

        preserved.Count.ShouldBe(3);
        preserved[0].EvidenceIds.ShouldBe(["r3:suburb"]);
        preserved[1].EvidenceIds.ShouldBeEmpty();
        preserved[2].EvidenceIds.ShouldBe(["weather:condition"]);
    }

    /// <summary>
    ///     Two drafts about the same road score identically against a rewrite. Binding either one
    ///     is a coin flip, so bind neither: an uncited sentence reviews as approve-by-default,
    ///     a mis-cited one is a guaranteed false rejection.
    /// </summary>
    [Fact]
    public void An_ambiguous_tie_binds_nothing()
    {
        const string first = "The M5 at Kingsgrove is heavy westbound.";
        const string second = "The M5 at Kingsgrove is heavy eastbound.";
        string before = string.Join(" ", first, second);
        string after = "The M5 at Kingsgrove is heavy.";

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(
            before, after,
            [
                new DraftedSentence(1, first, ["r3:direction"]),
                new DraftedSentence(2, second, ["r7:direction"])
            ])!;

        preserved.Count.ShouldBe(1);
        preserved[0].EvidenceIds.ShouldBeEmpty();
    }

    [Fact]
    public void Exact_final_sentence_matches_preserve_weather_and_fuel_evidence()
    {
        const string fuel = "Unleaded is cheapest at one dollar eighty a litre.";
        var persisted = ScriptGenerationEngine.ApplyLoopSentenceEvidence(
            [
                new JobScriptSentence(Weather, []),
                new JobScriptSentence(fuel, [])
            ],
            [
                new DraftedSentence(1, Weather, ["weather:condition", "weather:temperature"]),
                new DraftedSentence(2, fuel, ["fuel:unleaded:price"])
            ]);

        persisted.AppliedCount.ShouldBe(2);
        persisted.Sentences.Select(sentence => sentence.EvidenceIds).ShouldBe([
            ["weather:condition", "weather:temperature"],
            ["fuel:unleaded:price"]
        ]);
    }

    [Fact]
    public void Empty_authored_evidence_is_not_counted_but_keeps_deterministic_keys()
    {
        const string text = "The M5 into town is running freely.";
        var sentence = ScriptProvenanceMapper.Map(
            text,
            [],
            [
                new MonitoredRouteStatus(
                    "the M5 into town",
                    new CoveragePoint(-33.8, 151.0),
                    new CoveragePoint(-33.9, 151.2),
                    DriveTimeMinutes: 12,
                    NormalDriveTimeMinutes: 12,
                    DistanceKm: 25)
            ],
            [new FlowRoad("M5", JamFactor: 1, LengthMeters: 1_000)]).Single();
        var authored = new DraftedSentence(1, text, []);

        var exact = ScriptGenerationEngine.ApplyLoopSentenceEvidence([sentence], [authored]);
        var fallback = ScriptGenerationEngine.AlignDraftedSentenceEvidence([sentence], [authored]);

        exact.AppliedCount.ShouldBe(0);
        exact.Sentences.Single().EvidenceIds.ShouldBeEmpty();
        exact.Sentences.Single().CorridorKeys.ShouldBe(["M5"]);
        exact.Sentences.Single().RouteKeys.ShouldBe(["the M5 into town"]);
        fallback.AlignedCount.ShouldBe(0);
        fallback.Sentences.Single().EvidenceIds.ShouldBeEmpty();
        fallback.Sentences.Single().CorridorKeys.ShouldBe(["M5"]);
        fallback.Sentences.Single().RouteKeys.ShouldBe(["the M5 into town"]);
    }

    // Dev job 75639f66 (2026-09-02): the loop alignment REPLACED the mapper's evidence with the
    // workflow's authored list, so a good-run id found on the final read was erased by an empty
    // authored list. Authored citations (now including the workflow's own weather:line/fuel:line
    // section lineage) merge with the mapper's road evidence; neither side erases the other.
    [Fact]
    public void Authored_evidence_merges_with_deterministic_good_run_evidence()
    {
        const string goodRun = "The M4 from Parramatta to the city is moving a treat.";
        const string weather = "Looking at the weather, clear and 22 for Sydney.";
        const string fuel = "At Silverwater, unleaded is a dollar ninety-three.";
        var mapped = ScriptProvenanceMapper.Map(
            string.Join(" ", goodRun, weather, fuel),
            [],
            goodRunRoads: ["M4"]);
        mapped[0].EvidenceIds.ShouldBe(["goodrun:M4"]);
        mapped[1].EvidenceIds.ShouldBeEmpty();

        DraftedSentence[] authored =
        [
            new(1, goodRun, []),
            new(2, weather, ["weather:line"]) { Section = BulletinSegment.Weather },
            new(3, fuel, ["fuel:line", "fuel:unleaded:price"]) { Section = BulletinSegment.Fuel }
        ];

        var exact = ScriptGenerationEngine.ApplyLoopSentenceEvidence(mapped, authored);
        var fallback = ScriptGenerationEngine.AlignDraftedSentenceEvidence(mapped, authored);

        foreach (var result in new[] { exact.Sentences, fallback.Sentences })
        {
            result[0].EvidenceIds.ShouldBe(["goodrun:M4"]);
            result[1].EvidenceIds.ShouldBe(["weather:line"]);
            result[2].EvidenceIds.ShouldBe(["fuel:line", "fuel:unleaded:price"]);
        }

        exact.AppliedCount.ShouldBe(2);
        fallback.AlignedCount.ShouldBe(2);
    }

    [Fact]
    public void Every_script_sentence_appears_in_the_map_exactly_once()
    {
        string before = string.Join(" ", M5, M4, Mitchell, SirReginald, Weather);
        string after = string.Join(" ", SirReginald, M5, M4, Weather);

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(before, after, DevJobDraft())!;

        preserved.Count.ShouldBe(4);
        preserved.Select(s => s.Order).ShouldBeUnique();
        // No survivor may carry a dropped sentence's ids.
        preserved.SelectMany(s => s.EvidenceIds).ShouldNotContain("r4:detail");
    }
}
