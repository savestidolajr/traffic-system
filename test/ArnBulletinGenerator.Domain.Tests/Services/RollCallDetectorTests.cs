using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RollCallDetectorTests
{
    private static BroadcastWithTraffic Ctx(
        IReadOnlyList<string> goodRuns, IReadOnlyList<TrafficCorridor>? corridors = null) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Corridors = corridors ?? [],
            GoodRunRoads = goodRuns
        };

    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, 0, 1, 0, 0, "congestion", string.Empty, []);

    private static TrafficCorridor CorridorWithAnchors(
        string roadName, string extent, params string[] incidentLocations) =>
        new(roadName, 1, 1, 0, 0, "roadClosure", "Closure detail",
            incidentLocations
                .Select((loc, i) => new TrafficIncident($"{roadName}-{i}", "Closure detail", 1, loc, "roadClosure"))
                .ToList(),
            Extent: extent);

    [Fact]
    public void Ignores_anchor_roads_that_are_also_their_own_corridors()
    {
        // The frozen fb44a570 case (3AW, 2026-07-08): the Bypass closure names its own
        // cross-street/ramp anchors, and every one of those anchors is ALSO its own corridor in
        // the same feed — a dense-metro coincidence that false-flagged a faithful closure lead
        // into an unfixable refine loop before this fix.
        var ctx = Ctx([], [
            CorridorWithAnchors("Greensborough Bypass", "between Grimshaw Street and Diamond Creek Road",
                "Between Grimshaw Street and Diamond Creek Road", "Ramp to M80 Metropolitan Ring Road"),
            Corridor("M80"), Corridor("Grimshaw Street"), Corridor("Diamond Creek Road"), Corridor("Wilson Street")
        ]);
        const string script =
            "The Greensborough Bypass southbound is shut between Grimshaw Street and Diamond Creek Road, and the ramp to the M80 Metropolitan Ring Road is closed. Wilson Street has minor delays.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Passes_grouped_sentence_with_shared_condition_v53()
    {
        // v5.3 prompt explicitly asks for grouping: "X, Y, Z and W are all slow" is a legitimate
        // grouped sentence with a shared condition, NOT a bare database read-out. The detector
        // must NOT flag it as a roll-call.
        var ctx = Ctx([], [
            Corridor("Grimshaw Street"), Corridor("Diamond Creek Road"), Corridor("Wilson Street"), Corridor("High Street")
        ]);
        const string script = "Grimshaw Street, Diamond Creek Road, Wilson Street and High Street are all slow.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Still_flags_a_genuine_bare_list_with_no_condition()
    {
        // A TRUE bare list — road names with no condition keyword after the shared verb — is still
        // an AI-tell roll-call that must be flagged.
        var ctx = Ctx([], [
            Corridor("Grimshaw Street"), Corridor("Diamond Creek Road"), Corridor("Wilson Street"), Corridor("High Street")
        ]);
        const string script = "Grimshaw Street, Diamond Creek Road, Wilson Street and High Street.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeTrue();
    }

    [Fact]
    public void Counts_anchor_road_when_it_has_its_own_clause()
    {
        // The exclusion is per-sentence and role-scoped, not a global suppression: the same
        // script that carries the Bypass's anchor relationship also contains an unrelated bare
        // list of four OTHER roads elsewhere, and that second sentence must still be flagged.
        var ctx = Ctx([], [
            CorridorWithAnchors("Greensborough Bypass", "between Grimshaw Street and Diamond Creek Road",
                "Between Grimshaw Street and Diamond Creek Road"),
            Corridor("Grimshaw Street"), Corridor("Diamond Creek Road"),
            Corridor("King Street"), Corridor("Point Road"), Corridor("President Avenue"), Corridor("Crystal Street")
        ]);
        const string script =
            "The Greensborough Bypass is shut between Grimshaw Street and Diamond Creek Road. " +
            "King Street, Point Road, President Avenue and Crystal Street are all copping minor roadwork delays.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeTrue();
    }

    [Fact]
    public void Flags_clear_road_dump()
    {
        var ctx = Ctx([
            "Upper Bird Road", "Camden Hill Road", "Burnside Road", "Emily Street",
            "Jensens Road", "Ten Mile Track", "Ringarooma Road", "George Street"
        ]);
        const string script =
            "Scottsdale's pretty much clear sailing. Upper Bird Road and Camden Hill Road are both moving freely, " +
            "and Burnside Road's running well too. Over on Emily Street and Jensens Road, it's the same story. " +
            "Ten Mile Track, Ringarooma Road and George Street are all in good shape.";

        var result = RollCallDetector.Detect(script, ctx);

        result.HasRollCall.ShouldBeTrue();
        result.Feedback.ShouldContain("ROLL-CALL");
    }

    [Fact]
    public void Ignores_flow_pool_roads_never_offered_as_clear()
    {
        // FlowPool is the RAW pre-selection candidate universe (up to 18 roads); GoodRunRoads is
        // the curated subset actually offered to the writer as "clear". A road that exists ONLY
        // in FlowPool — never surfaced as a clear road — must never count toward Rule A, even
        // when the script happens to mention it for an unrelated reason (an incident cross-street,
        // a commuter aside). Regression for the live false-positive: "Huntingdale Road" (an
        // incident cross-street) and "Monash Freeway" (a commuter aside) both happened to exist
        // in FlowPool and were miscounted as 2 of a 4-clear-road roll-call that was never voiced.
        var ctx = Ctx(["Springvale Road", "EastLink"]) with
        {
            FlowPool =
            [
                new FlowRoad("Springvale Road", 1.0, 5000, false, 1, 10),
                new FlowRoad("EastLink", 1.0, 5000, false, 1, 10),
                new FlowRoad("Huntingdale Road", 1.0, 5000, false, 1, 10),
                new FlowRoad("Monash Freeway", 1.0, 5000, false, 1, 10)
            ]
        };
        const string script =
            "On the M1 citybound, roadworks and a breakdown between Huntingdale Road and Burke Road are causing minor delays. " +
            "The West Gate citybound is running to time, and the Monash Freeway to Melbourne CBD is running to time. " +
            "Springvale Road and EastLink are flowing well, along with the rest of that run.";

        var result = RollCallDetector.Detect(script, ctx);

        result.HasRollCall.ShouldBeFalse();
    }


    [Fact]
    public void Ignores_clear_road_mentioned_only_as_an_incident_anchor()
    {
        // "Gold Coast Highway" is in the clear-road pool AND is the closure's extent anchor. Its
        // mention inside the incident sentence is the data's own anchor, not a clear-run claim —
        // counting it false-flagged every candidate of a faithful read (atn6, Gold Coast).
        var ctx = Ctx(
            ["Pacific Motorway", "Stewart Road", "Henri Robert Drive", "Maudsland Road", "Cobaki Parkway", "Gold Coast Highway"],
            [Corridor("Coast Highway")]);
        const string script =
            "Coast Highway southbound is shut for roadworks between Gold Coast Highway and Pacific Motorway. " +
            "Pacific Motorway, Stewart Road and Henri Robert Drive are flowing well. " +
            "Also, Maudsland Road and Cobaki Parkway are moving freely.";

        // 5 clear roads voiced in incident-free sentences — under the quiet cap of 6; the anchor
        // mention must not push it to 6.
        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }
    [Fact]
    public void Passes_two_named_plus_area_sweep()
    {
        var ctx = Ctx(["Upper Bird Road", "Camden Hill Road", "Burnside Road", "Emily Street"]);
        const string script =
            "Scottsdale's clear this morning — Upper Bird Road and Camden Hill Road are flowing freely, " +
            "and it's an easy run right across the rest of town.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Passes_three_roads_sharing_one_concrete_condition()
    {
        // Three roads + ONE shared concrete condition is legitimate objective radio under the
        // 1-Jul factual style ("X, Y and Z are all at a standstill") — names and a real state,
        // not a bare read-out. The gate now fires at FOUR-plus names in one sentence.
        var ctx = Ctx([], [Corridor("King Street"), Corridor("Point Road"), Corridor("President Avenue")]);
        const string script =
            "King Street, Point Road and President Avenue are all copping minor roadwork delays.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Flags_four_corridor_names_in_one_sentence()
    {
        var ctx = Ctx([], [
            Corridor("King Street"), Corridor("Point Road"),
            Corridor("President Avenue"), Corridor("Crystal Street")
        ]);
        const string script =
            "King Street, Point Road, President Avenue and Crystal Street are all copping minor roadwork delays.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeTrue();
    }

    [Fact]
    public void Ignores_cross_streets_on_a_single_closure()
    {
        // Only "Warringah Freeway" is a known road; the cross-streets are location anchors, not in the set.
        var ctx = Ctx([], [Corridor("Warringah Freeway")]);
        const string script =
            "Warringah Freeway's shut at the ramp to Alfred Street North, between Miller Street and the HT Toll Plaza, " +
            "and again at Berry Street.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Passes_empty_script()
    {
        RollCallDetector.Detect(string.Empty, Ctx([])).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Passes_three_roads_each_with_their_own_verb_and_detail()
    {
        // Observed live (Sydney CBD): each road carries its OWN clause — this is the "weave the
        // items" connected-prose style the writer is asked for elsewhere, not a bare list, even
        // though it names three roads in one sentence.
        var ctx = Ctx([], [Corridor("Burlington Street"), Corridor("Eastern Distributor"), Corridor("Chalmers Street")]);
        const string script =
            "Burlington Street has minor delays, the Eastern Distributor is congested at Cathedral Street, and Chalmers Street is also slow.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Flags_seven_roads_with_a_single_shared_verb_at_the_end()
    {
        // Observed live (Sydney CBD, an earlier pass): a TRUE bare list — zero individual detail,
        // one verb tacked on for the whole group.
        var ctx = Ctx([], [
            Corridor("Crescent Street"), Corridor("O'dea Avenue"), Corridor("Stanmore Road"),
            Corridor("Parramatta Road"), Corridor("Crystal Street"), Corridor("Forbes Street")
        ]);
        const string script =
            "Crescent Street, O'dea Avenue, Stanmore Road, Parramatta Road, Crystal Street and Forbes Street are all carrying minor delays.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeTrue();
    }

    [Fact]
    public void Passes_a_compound_subject_sharing_one_verb_as_a_second_clause()
    {
        // "King Street is slow" (own clause) + "Point Road and President Avenue are both
        // congested" (a compound subject sharing ONE verb) = two connected clauses, not a
        // three-item bare list — legitimate prose, even though it names three roads.
        var ctx = Ctx([], [Corridor("King Street"), Corridor("Point Road"), Corridor("President Avenue")]);
        const string script =
            "King Street is slow, while Point Road and President Avenue are both congested.";

        RollCallDetector.Detect(script, ctx).HasRollCall.ShouldBeFalse();
    }

    [Fact]
    public void Repair_collapses_a_genuine_bare_list_with_no_condition()
    {
        // A TRUE bare list — road names dumped with no condition keyword after the verb — is still
        // a roll-call defect that Repair must collapse. The v5.3 grouping exemption only applies
        // when a shared condition follows the verb (e.g. "are all slow").
        var ctx = Ctx([], [
            Corridor("Warringah Freeway"), Corridor("Canterbury Road"), Corridor("Smithfield Road"),
            Corridor("Joyce Drive"), Corridor("Domestic Terminal"), Corridor("Liverpool Road")
        ]);
        const string script =
            "Warringah Freeway citybound is shut at the ramp to Alfred Street North, with roadworks at Brook Street. " +
            "Canterbury Road has roadworks at Bonds Road and moderate delays. " +
            "Smithfield Road is at a standstill, while Joyce Drive, Domestic Terminal and Liverpool Road, " +
            "with a few more minor hold-ups around town.";

        Detect(script, ctx).HasRollCall.ShouldBeTrue();

        string repaired = RollCallDetector.Repair(script, ctx);

        Detect(repaired, ctx).HasRollCall.ShouldBeFalse();
        repaired.ShouldContain("Smithfield Road is at a standstill");
        repaired.ShouldContain("Joyce Drive and a couple more spots");
        repaired.ShouldNotContain("Domestic Terminal");
        repaired.ShouldNotContain("Liverpool Road");
        // Every road with its own clause is untouched.
        repaired.ShouldContain("Warringah Freeway citybound is shut");
        repaired.ShouldContain("Canterbury Road has roadworks at Bonds Road and moderate delays");
    }

    [Fact]
    public void Repair_leaves_a_clean_script_unchanged()
    {
        var ctx = Ctx([], [Corridor("Burlington Street"), Corridor("Eastern Distributor"), Corridor("Chalmers Street")]);
        const string script =
            "Burlington Street has minor delays, the Eastern Distributor is congested at Cathedral Street, and Chalmers Street is also slow.";

        RollCallDetector.Repair(script, ctx).ShouldBe(script);
    }

    [Fact]
    public void Repair_is_a_safe_noop_when_a_clean_roads_clause_sits_inside_the_bare_span()
    {
        // Interleaved shape: a road with its OWN clause sits BETWEEN the first and last bare
        // road, so collapsing the span would swallow that clause. Repair must not touch it.
        var ctx = Ctx([], [
            Corridor("Alpha Street"), Corridor("Beta Road"), Corridor("Gamma Avenue"), Corridor("Delta Lane")
        ]);
        const string script =
            "Alpha Street, Beta Road is closed for repairs, and Gamma Avenue and Delta Lane are slow.";

        RollCallDetector.Repair(script, ctx).ShouldBe(script);
    }

    private static RollCallDetector.RollCallResult Detect(string script, BroadcastWithTraffic ctx) =>
        RollCallDetector.Detect(script, ctx);

    // ---------------------------------------------------------------------------
    // Overlapping corridor names. Two corridors can match OVERLAPPING spans in one
    // sentence whenever one road name contains another - "Ring Road" inside
    // "Northern Ring Road". Ordered by index, the shorter match then starts BEFORE
    // the longer one ends, so the gap slice between consecutive mentions ran
    // backwards and threw ArgumentOutOfRangeException("length ('-20')").
    //
    // Observed live: KIIS 101.1 job 306287ef3dbd4810bda4dd24a40b7711 (2026-08-03).
    // The crash escaped the review loop, the job never advanced past
    // INCIDENTS_SELECTED, and 2m43s later the watchdog swept it as 'missed-window'
    // - so a real crash was reported to the operator as a timing problem, and a
    // finished 83-word approved script was thrown away for a filler cart.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Repair_OverlappingRoadNames_DoesNotThrow()
    {
        // "Northern Ring Road" contains "Ring Road"; both are live corridors.
        var ctx = Ctx([], [
            Corridor("Princes Highway"), Corridor("Grimshaw Street"), Corridor("Northern Ring Road"),
            Corridor("Ring Road"), Corridor("Homestead Road"), Corridor("Cambridge Street")
        ]);
        const string script =
            "There are delays on the Princes Highway, Grimshaw Street, Northern Ring Road, " +
            "Homestead Road and Cambridge Street this afternoon.";

        var repaired = Should.NotThrow(() => RollCallDetector.Repair(script, ctx));
        repaired.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Repair_RoadNameContainedInAnother_PrefersTheLongerMatch()
    {
        // The longer name owns the span: collapsing must not leave a dangling "Ring Road"
        // fragment behind, which is what dropping the wrong side of the overlap would do.
        var ctx = Ctx([], [
            Corridor("Ring Road"), Corridor("Northern Ring Road"), Corridor("High Street"),
            Corridor("Wilson Street"), Corridor("Diamond Creek Road")
        ]);
        const string script =
            "Queues on the Northern Ring Road, High Street, Wilson Street and Diamond Creek Road.";

        var repaired = Should.NotThrow(() => RollCallDetector.Repair(script, ctx));
        repaired.ShouldNotContain("the Road");
        repaired.ShouldNotContain(",,");
    }

    [Fact]
    public void Repair_IdenticalRoadNameTwiceInOneSentence_DoesNotThrow()
    {
        // Degenerate case: the same corridor listed twice still yields two mentions at
        // the same index once de-duplicated by name, so the gap can be zero-length.
        var ctx = Ctx([], [
            Corridor("Bell Street"), Corridor("Bell Street"), Corridor("Sydney Road"),
            Corridor("Nicholson Street"), Corridor("Brunswick Road")
        ]);
        const string script =
            "Watch Bell Street, Sydney Road, Nicholson Street and Brunswick Road on the way in.";

        Should.NotThrow(() => RollCallDetector.Repair(script, ctx));
    }
}
