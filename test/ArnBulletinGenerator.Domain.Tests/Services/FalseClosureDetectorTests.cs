using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class FalseClosureDetectorTests
{
    private static BroadcastWithTraffic Ctx(IReadOnlyList<TrafficCorridor> corridors) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Corridors = corridors,
            GoodRunRoads = []
        };

    private static TrafficCorridor Corridor(
        string roadName, string primaryType, IReadOnlyList<TrafficIncident> incidents) =>
        new(roadName, 3, 4, 700, 100, primaryType, "roadworks outbound", incidents);

    private static TrafficIncident Works(string id) =>
        new(id, "roadworks", 2, "outbound", "construction");
    private static TrafficIncident Crash(string id) =>
        new(id, "crash", 3, "M5 at Mascot", "accident");


    private static TrafficIncident ClosedIncident(string id) =>
        new(id, "road closure", 3, "outbound", "roadClosure", RoadClosed: true);

    // ── POSITIVE: a works item voiced as closed ──────────────────────────────────

    [Fact]
    public void Flags_construction_corridor_voiced_as_closed()
    {
        // The exact live failure (Brisbane 2026-07-13): Gilchrist Avenue is construction /
        // RoadClosed=false, but the script calls it closed.
        var ctx = Ctx([Corridor("Gilchrist Avenue", "construction", [Works("g1")])]);
        const string script = "Gilchrist Avenue is closed outbound due to road construction.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeTrue();
    }

    [Fact]
    public void Flags_blocked_off_on_works_item()
    {
        var ctx = Ctx([Corridor("Gilchrist Avenue", "construction", [Works("g1")])]);
        const string script = "Gilchrist Avenue outbound is blocked off for road construction.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeTrue();
    }
    [Fact]
    public void Flags_live_crash_blocking_regression_and_gives_impact_guidance()
    {
        // Observed live 2026-08-24: the authority said "1 of 2 westbound lanes closed", but the
        // aired script said "A crash is blocking the M5 at Mascot." RoadClosed was false.
        var ctx = Ctx([Corridor("M5", "accident", [Crash("m5")])]);
        const string script = "A crash is blocking the M5 at Mascot.";

        var result = FalseClosureDetector.Detect(script, ctx);

        result.HasFalseClosure.ShouldBeTrue();
        result.Feedback.ShouldContain("accident");
        result.Feedback.ShouldContain("actual delay or queue");
        result.Feedback.ShouldNotContain("works item");
    }
    [Fact]
    public void Feedback_prefers_supplied_lane_fact_over_road_blockage()
    {
        var corridor = Corridor("M5", "accident", [Crash("m5")]) with
        {
            OfficialEvidences =
            [
                new OfficialEvidence("livetraffic-nsw", "M5", true, LanesClosed: 1, LanesTotal: 2)
            ]
        };

        var result = FalseClosureDetector.Detect("A crash is blocking the M5 at Mascot.", Ctx([corridor]));

        result.Feedback.ShouldContain("specific lane impact");
        result.Feedback.ShouldContain("1 of 2 lanes closed");
    }


    [Fact]
    public void Flags_blocks_road_level_form_on_nonclosure()
    {
        var ctx = Ctx([Corridor("M5", "accident", [Crash("m5")])]);

        FalseClosureDetector.Detect("A crash blocks the M5 at Mascot.", ctx)
            .HasFalseClosure.ShouldBeTrue();
    }

    [Fact]
    public void Flags_blocked_road_level_form_on_nonclosure()
    {
        var ctx = Ctx([Corridor("M5", "accident", [Crash("m5")])]);

        FalseClosureDetector.Detect("A crash left the M5 blocked at Mascot.", ctx)
            .HasFalseClosure.ShouldBeTrue();
    }

    [Fact]
    public void Does_not_flag_one_of_two_lanes_closed()
    {
        var ctx = Ctx([Corridor("M5", "accident", [Crash("m5")])]);

        FalseClosureDetector.Detect("A crash has one of two lanes closed on the M5 at Mascot.", ctx)
            .HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_a_lane_is_blocked()
    {
        var ctx = Ctx([Corridor("M5", "accident", [Crash("m5")])]);

        FalseClosureDetector.Detect("A crash says a lane is blocked on the M5 at Mascot.", ctx)
            .HasFalseClosure.ShouldBeFalse();
    }


    [Fact]
    public void Flags_trailing_road_closed_clause_on_works_item()
    {
        // Live phrasing variant: the closure verb trails the roadworks description.
        var ctx = Ctx([Corridor("Gilchrist Avenue", "construction", [Works("g1")])]);
        const string script =
            "There are roadworks on Gilchrist Avenue outbound, with a short work zone and the road closed due to construction.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeTrue();
    }

    [Fact]
    public void Flags_closure_chained_onto_works_item_in_grouped_sentence()
    {
        // A real closure on Herston chains onto the works item with "also closed".
        var ctx = Ctx([
            Corridor("Herston Road", "roadClosure", [Works("h1")]),
            Corridor("Gilchrist Avenue", "construction", [Works("g1")])
        ]);
        const string script = "Herston Road is closed outbound, and Gilchrist Avenue is also closed.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeTrue();
    }

    // ── NEGATIVE: faithful reads must not be flagged ─────────────────────────────

    [Fact]
    public void Does_not_flag_genuine_road_closure()
    {
        var ctx = Ctx([Corridor("Herston Road", "roadClosure", [Works("h1")])]);
        const string script = "Herston Road is closed outbound.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_when_an_incident_marks_the_road_closed()
    {
        // PrimaryType isn't roadClosure, but an incident carries RoadClosed=true — genuine closure.
        var ctx = Ctx([Corridor("Foo Road", "construction", [ClosedIncident("f1")])]);
        const string script = "Foo Road is closed.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_lane_closure_inside_works()
    {
        // A lane closed through the works is legitimate roadworks language, not a road closure.
        var ctx = Ctx([Corridor("Gilchrist Avenue", "construction", [Works("g1")])]);
        const string script = "Gilchrist Avenue has a lane closed through the works.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_works_item_voiced_as_works()
    {
        var ctx = Ctx([Corridor("Gilchrist Avenue", "construction", [Works("g1")])]);
        const string script = "Gilchrist Avenue has roadworks outbound.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_works_item_beside_a_real_closure_in_grouped_sentence()
    {
        // Herston (real closure) is closed; Gilchrist (works) is only slowed — the closure verb
        // belongs to Herston's clause, so Gilchrist must not be flagged.
        var ctx = Ctx([
            Corridor("Herston Road", "roadClosure", [Works("h1")]),
            Corridor("Gilchrist Avenue", "construction", [Works("g1")])
        ]);
        const string script = "Herston Road is closed outbound, and Gilchrist Avenue has roadworks.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_overlapping_corridor_names_at_the_same_mention()
    {
        var ctx = Ctx([
            Corridor("Pacific Motorway", "construction", [Works("p1")]),
            Corridor("M1 Pacific Motorway", "construction", [Works("m1")])
        ]);

        FalseClosureDetector.Detect("Roadworks are slowing the M1 Pacific Motorway.", ctx)
            .HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_road_not_in_context()
    {
        var ctx = Ctx([Corridor("Gilchrist Avenue", "construction", [Works("g1")])]);
        const string script = "Some Other Road is closed.";

        FalseClosureDetector.Detect(script, ctx).HasFalseClosure.ShouldBeFalse();
    }
}
