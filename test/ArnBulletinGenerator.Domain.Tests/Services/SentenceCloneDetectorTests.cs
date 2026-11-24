using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class SentenceCloneDetectorTests
{
    private static BroadcastWithTraffic Ctx(
        IReadOnlyList<string>? goodRuns = null,
        IReadOnlyList<TrafficCorridor>? corridors = null,
        IReadOnlyList<FlowRoad>? flowPool = null) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Corridors = corridors ?? [],
            GoodRunRoads = goodRuns ?? [],
            FlowPool = flowPool ?? []
        };

    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, 0, 1, 0, 0, "congestion", string.Empty, []);

    private static FlowRoad Flow(string roadName) => new(roadName, 0, 0);

    // ── THE HEADLINE DEFECT (live Melbourne CBD): three sentences, one template, road swapped ──
    // "King Street" + "Collins Street" ride the good-run list; "St Kilda Road" sits only in the
    // FlowPool — so this also proves KnownRoads folds the flow pool in (otherwise St Kilda Road
    // would not skeletonise and the third clone would slip the count).
    private static BroadcastWithTraffic MelbourneCbdCtx() =>
        Ctx(goodRuns: ["King Street", "Collins Street"], flowPool: [Flow("St Kilda Road")]);

    private const string MelbourneCbdClone =
        "King Street is moving freely through the CBD. " +
        "Collins Street is moving freely through the CBD. " +
        "St Kilda Road is moving freely through the CBD.";

    [Fact]
    public void Flags_the_live_three_sentence_moving_freely_template()
    {
        var result = SentenceCloneDetector.Detect(MelbourneCbdClone, MelbourneCbdCtx());

        result.HasClone.ShouldBeTrue();
        result.Feedback.ShouldContain("CLONED SHAPE");
        // The frame it names is the road-agnostic skeleton, not any one road.
        result.Feedback.ShouldContain("[road] is moving freely through the cbd");
    }

    [Fact]
    public void Repair_keeps_the_first_shape_and_drops_the_cloned_restatements()
    {
        var ctx = MelbourneCbdCtx();

        string repaired = SentenceCloneDetector.Repair(MelbourneCbdClone, ctx);

        // First sentence survives verbatim; the two later clones (incl. the FlowPool road) are gone.
        repaired.ShouldContain("King Street is moving freely through the CBD");
        repaired.ShouldNotContain("Collins Street");
        repaired.ShouldNotContain("St Kilda Road");
        // And the repair actually cleared the clone: re-detecting on the fix finds nothing.
        SentenceCloneDetector.Detect(repaired, ctx).HasClone.ShouldBeFalse();
    }

    // ── Two occurrences, but the shared frame is substantial (4+ non-road words) ──
    [Fact]
    public void Flags_two_clones_when_the_shared_frame_is_four_or_more_words()
    {
        var ctx = Ctx(goodRuns: ["Pitt Street", "George Street"]);
        const string script = "Pitt Street is running well tonight. George Street is running well tonight.";

        // Skeleton "[road] is running well tonight" = 4 shared words, seen twice → flagged.
        SentenceCloneDetector.Detect(script, ctx).HasClone.ShouldBeTrue();
    }

    // ── Two occurrences of a SHORT frame (<4 shared words) is not enough ──
    [Fact]
    public void Does_not_flag_two_clones_of_a_short_frame()
    {
        var ctx = Ctx(goodRuns: ["Pitt Street", "George Street"]);
        const string script = "Pitt Street is clear. George Street is clear.";

        // Skeleton "[road] is clear" = 2 shared words, only twice → below both thresholds.
        SentenceCloneDetector.Detect(script, ctx).HasClone.ShouldBeFalse();
    }

    // ── Varied prose: three distinct closures, each its own shape — must not false-positive ──
    [Fact]
    public void Does_not_flag_varied_prose_with_distinct_shapes()
    {
        var ctx = Ctx(corridors:
        [
            Corridor("Monash"), Corridor("Toorak Road"),
            Corridor("Punt Road"), Corridor("Chapel Street")
        ]);
        const string script =
            "The Monash is shut at Toorak Road. " +
            "Punt Road is bumper to bumper near the bridge. " +
            "Chapel Street has a breakdown blocking a lane.";

        SentenceCloneDetector.Detect(script, ctx).HasClone.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_a_single_sentence()
    {
        var ctx = MelbourneCbdCtx();

        SentenceCloneDetector.Detect(
            "King Street is moving freely through the CBD.", ctx).HasClone.ShouldBeFalse();
    }

    [Fact]
    public void Blank_script_is_never_a_clone_and_repair_returns_it_unchanged()
    {
        var ctx = MelbourneCbdCtx();

        SentenceCloneDetector.Detect(string.Empty, ctx).HasClone.ShouldBeFalse();
        SentenceCloneDetector.Detect("   ", ctx).HasClone.ShouldBeFalse();
        SentenceCloneDetector.Repair(string.Empty, ctx).ShouldBe(string.Empty);
        SentenceCloneDetector.Repair("   ", ctx).ShouldBe("   ");
    }

    [Fact]
    public void Repair_is_a_noop_when_there_is_no_clone()
    {
        var ctx = Ctx(corridors:
        [
            Corridor("Monash"), Corridor("Toorak Road"),
            Corridor("Punt Road"), Corridor("Chapel Street")
        ]);
        const string script =
            "The Monash is shut at Toorak Road. " +
            "Punt Road is bumper to bumper near the bridge. " +
            "Chapel Street has a breakdown blocking a lane.";

        SentenceCloneDetector.Repair(script, ctx).ShouldBe(script);
    }

    // ══ SAME-ROAD ECHO ══ a SECOND, independent trigger: the SAME known road OPENING 2+ separate
    // sentences. None of the scripts below share a skeleton (their cross-streets/tails differ), so
    // the "CLONED SHAPE" path above stays silent — a fire here can ONLY be the same-road echo.

    // The live Melbourne CBD echo: one road heads two sentences, each pinned to a DIFFERENT
    // cross-street, so the skeletons diverge and the clone trigger says nothing — the repeated LEAD
    // is the tell. Third sentence is a distinct road so it neither clones nor echoes.
    [Fact]
    public void Flags_when_the_same_road_opens_two_sentences_with_different_cross_streets()
    {
        var ctx = Ctx(corridors: [Corridor("Footscray Road"), Corridor("Punt Road")]);
        const string script =
            "Footscray Road is closed at the CityLink Tollway for roadworks. " +
            "Footscray Road is closed at Sims Street for roadworks. " +
            "Punt Road is at a standstill.";

        var result = SentenceCloneDetector.Detect(script, ctx);

        result.HasClone.ShouldBeTrue();
        result.Feedback.ShouldContain("REPEATED ROAD");
        result.Feedback.ShouldContain("Footscray Road");
    }

    // A leading "The " is transparent: "The M1 …" opening two sentences is the M1 leading twice.
    // Strip the "The " handling and neither line would count as an M1 lead — this fact guards it.
    [Fact]
    public void Flags_a_repeated_lead_seen_through_a_leading_The()
    {
        var ctx = Ctx(corridors: [Corridor("M1")]);
        const string script =
            "The M1 citybound between Huntingdale Road and Burke Road has a breakdown. " +
            "The M1 citybound between Stud Road and Heatherton Road is slowed.";

        var result = SentenceCloneDetector.Detect(script, ctx);

        result.HasClone.ShouldBeTrue();
        result.Feedback.ShouldContain("REPEATED ROAD");
        result.Feedback.ShouldContain("M1");
    }

    // THE CRITICAL GUARD: three DIFFERENT roads each leading exactly one sentence is good prose.
    // The echo must tally leads PER ROAD, not total sentence-initial roads — a detector that
    // counted "2+ sentences start with a known road" would redden every well-varied bulletin.
    [Fact]
    public void Does_not_flag_three_different_roads_each_leading_one_sentence()
    {
        var ctx = Ctx(corridors:
        [
            Corridor("Monash"), Corridor("Punt Road"), Corridor("Chapel Street")
        ]);
        const string script =
            "The Monash is shut at Toorak Road. " +
            "Punt Road is bumper to bumper near the bridge. " +
            "Chapel Street has a breakdown.";

        SentenceCloneDetector.Detect(script, ctx).HasClone.ShouldBeFalse();
    }

    // A mid-sentence cross-street mention is NOT a lead: "Tonkin Highway" is named INSIDE sentence
    // one (as Reid Highway's cross-street) and only OPENS sentence two — that is one lead, not two.
    // A detector that counted any mention (contains, not starts-with) would false-positive here.
    [Fact]
    public void Does_not_count_a_mid_sentence_cross_street_as_a_lead()
    {
        var ctx = Ctx(corridors: [Corridor("Reid Highway"), Corridor("Tonkin Highway")]);
        const string script =
            "Reid Highway is shut at Tonkin Highway. " +
            "Tonkin Highway is moving freely.";

        SentenceCloneDetector.Detect(script, ctx).HasClone.ShouldBeFalse();
    }

    // Whole-token leads only: the known road "Reid" genuinely opens sentence one, but "Reidvale"
    // in sentence two merely CONTAINS "Reid" as an alphabetic prefix — a different token, so it is
    // not a second "Reid" lead. Drop the trailing non-letter boundary check and "Reidvale" would
    // count as a lead, tipping "Reid" to two and flipping this to a false flag.
    [Fact]
    public void Does_not_treat_a_road_name_prefix_of_a_longer_token_as_a_lead()
    {
        var ctx = Ctx(corridors: [Corridor("Reid")]);
        const string script =
            "Reid Highway is shut near the tunnel this evening. " +
            "Reidvale Street is blocked near the tunnel this evening.";

        SentenceCloneDetector.Detect(script, ctx).HasClone.ShouldBeFalse();
    }
}
