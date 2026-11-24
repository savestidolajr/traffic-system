using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class DuplicateAnchorDetectorTests
{
    private static BroadcastWithTraffic Ctx(IReadOnlyList<TrafficCorridor>? corridors = null) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Corridors = corridors ?? []
        };

    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, 0, 1, 0, 0, "congestion", string.Empty, []);

    [Fact]
    public void Flags_same_road_named_twice_in_one_sentence()
    {
        var ctx = Ctx([Corridor("Kwinana Freeway")]);
        const string script =
            "Roe Highway westbound is heavy at the Kwinana Freeway, with the Kwinana Freeway slow at Berrigan Drive.";

        var result = DuplicateAnchorDetector.Detect(script, ctx);

        result.HasDuplicateAnchor.ShouldBeTrue();
        result.Feedback.ShouldContain("DUPLICATE ANCHOR");
        result.Feedback.ShouldContain("Kwinana Freeway");
    }

    [Fact]
    public void Passes_anchor_that_is_the_tail_of_a_longer_compound_name()
    {
        // "Coast Highway" appears once as itself and once INSIDE "Gold Coast Highway" — a
        // different, longer road named by the corridor's own extent text. The writer voiced the
        // data faithfully; the tail match must not count (observed live false-flag: atn2 pack,
        // Gold Coast — every best-of candidate penalized for the data's own extent).
        var ctx = Ctx([Corridor("Coast Highway")]);
        const string script =
            "Coast Highway southbound is closed for roadworks from Gold Coast Highway to Pacific Motorway.";

        DuplicateAnchorDetector.Detect(script, ctx).HasDuplicateAnchor.ShouldBeFalse();
    }

    [Fact]
    public void Flags_cross_item_duplicate_within_one_sentence_gold_coast_case()
    {
        var ctx = Ctx([Corridor("Pacific Motorway")]);
        const string script =
            "Coast Highway is shut southbound at the Pacific Motorway ramp for roadworks, from Gold Coast Highway to Pacific Motorway.";

        DuplicateAnchorDetector.Detect(script, ctx).HasDuplicateAnchor.ShouldBeTrue();
    }

    [Fact]
    public void Passes_single_mention_per_sentence()
    {
        var ctx = Ctx([Corridor("Kwinana Freeway"), Corridor("Roe Highway")]);
        const string script =
            "Roe Highway westbound is heavy at the Kwinana Freeway. Traffic is slow at Berrigan Drive.";

        DuplicateAnchorDetector.Detect(script, ctx).HasDuplicateAnchor.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_confuse_a_road_with_its_longer_variant_as_a_second_distinct_mention()
    {
        // "Smith Street" and "Smith Street Motorway" are two DIFFERENT known roads (real HERE case).
        // Each named once — no repeat of the SAME name — must not trigger.
        var ctx = Ctx([Corridor("Smith Street"), Corridor("Smith Street Motorway")]);
        const string script = "Smith Street is at a standstill, and Smith Street Motorway has minor delays.";

        DuplicateAnchorDetector.Detect(script, ctx).HasDuplicateAnchor.ShouldBeFalse();
    }

    [Fact]
    public void Flags_the_longer_variant_repeated_twice()
    {
        var ctx = Ctx([Corridor("Smith Street"), Corridor("Smith Street Motorway")]);
        const string script =
            "Smith Street Motorway is heavy citybound, and Smith Street Motorway is also slow near Regency Place.";

        DuplicateAnchorDetector.Detect(script, ctx).HasDuplicateAnchor.ShouldBeTrue();
    }

    [Fact]
    public void Passes_empty_script()
    {
        DuplicateAnchorDetector.Detect(string.Empty, Ctx()).HasDuplicateAnchor.ShouldBeFalse();
    }

    [Fact]
    public void Repair_collapses_the_exact_live_shipped_defect()
    {
        // Observed live (pass 13, Brisbane): retry budget exhausted, script shipped with a bare
        // self-reference — "is at a standstill at Seventeen Mile Rocks Road" restates the road the
        // sentence already opened with, zero new information.
        var ctx = Ctx([Corridor("Seventeen Mile Rocks Road"), Corridor("Centenary Highway")]);
        const string script =
            "Seventeen Mile Rocks Road is at a standstill at Seventeen Mile Rocks Road, and Centenary Highway citybound has minor delays at the same spot.";

        DuplicateAnchorDetector.Detect(script, ctx).HasDuplicateAnchor.ShouldBeTrue();

        string repaired = DuplicateAnchorDetector.Repair(script, ctx);

        DuplicateAnchorDetector.Detect(repaired, ctx).HasDuplicateAnchor.ShouldBeFalse();
        repaired.ShouldBe(
            "Seventeen Mile Rocks Road is at a standstill, and Centenary Highway citybound has minor delays at the same spot.");
    }

    [Fact]
    public void Repair_leaves_a_second_mention_with_new_information_untouched()
    {
        // The second "Kwinana Freeway" mention introduces a genuinely new fact (Berrigan Drive) via
        // "with" — not a bare "at/on/near/along" self-reference — so this mechanical pass must not
        // touch it; it ships with visibility via the retry-exhaustion warning instead.
        var ctx = Ctx([Corridor("Kwinana Freeway")]);
        const string script =
            "Roe Highway westbound is heavy at the Kwinana Freeway, with the Kwinana Freeway slow at Berrigan Drive.";

        DuplicateAnchorDetector.Repair(script, ctx).ShouldBe(script);
    }

    [Fact]
    public void Repair_leaves_a_genuine_from_x_to_y_extent_span_untouched()
    {
        // "from Gold Coast Highway to Pacific Motorway" is a real extent span, not a self-reference
        // — the repeated anchor is preceded by "to", outside the safe at/on/near/along set.
        var ctx = Ctx([Corridor("Pacific Motorway")]);
        const string script =
            "Coast Highway is shut southbound at the Pacific Motorway ramp for roadworks, from Gold Coast Highway to Pacific Motorway.";

        DuplicateAnchorDetector.Repair(script, ctx).ShouldBe(script);
    }

    [Fact]
    public void Repair_leaves_a_clean_script_unchanged()
    {
        var ctx = Ctx([Corridor("Kwinana Freeway"), Corridor("Roe Highway")]);
        const string script =
            "Roe Highway westbound is heavy at the Kwinana Freeway. Traffic is slow at Berrigan Drive.";

        DuplicateAnchorDetector.Repair(script, ctx).ShouldBe(script);
    }
}
