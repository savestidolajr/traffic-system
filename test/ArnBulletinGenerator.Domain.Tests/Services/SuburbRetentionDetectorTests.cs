using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class SuburbRetentionDetectorTests
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

    private static TrafficCorridor Corridor(string roadName, Option<string> suburb) =>
        new(roadName, Severity: 1, FunctionalClass: 2, TotalLengthMeters: 1000,
            ImpactScore: 100, PrimaryType: "accident", Description: "crash blocking one lane",
            Incidents: [], Suburb: suburb);

    // ─── Detection ──────────────────────────────────────────────────────────

    [Fact]
    public void Road_named_with_its_suburb_is_not_flagged()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Rozelle"))]);

        var result = SuburbRetentionDetector.Detect("Victoria Road is slow near Rozelle.", context);

        result.HasMissingSuburb.ShouldBeFalse();
    }

    [Fact]
    public void Road_named_without_its_suburb_is_flagged()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Rozelle"))]);

        var result = SuburbRetentionDetector.Detect("Victoria Road is slow this morning.", context);

        result.HasMissingSuburb.ShouldBeTrue();
        result.Feedback.ShouldContain("Victoria Road");
        result.Feedback.ShouldContain("Rozelle");
    }

    [Fact]
    public void Road_not_named_in_script_is_not_flagged()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Rozelle"))]);

        var result = SuburbRetentionDetector.Detect("All quiet on the roads this morning.", context);

        result.HasMissingSuburb.ShouldBeFalse();
    }

    [Fact]
    public void Corridor_with_no_resolved_suburb_is_not_flagged()
    {
        var context = Ctx([Corridor("Victoria Road", default)]);

        var result = SuburbRetentionDetector.Detect("Victoria Road is slow this morning.", context);

        result.HasMissingSuburb.ShouldBeFalse();
    }

    [Fact]
    public void Suburb_already_embedded_in_road_name_is_not_flagged()
    {
        var context = Ctx([Corridor("Neranwood", Option<string>.Some("Neranwood"))]);

        var result = SuburbRetentionDetector.Detect("Neranwood is slow this morning.", context);

        result.HasMissingSuburb.ShouldBeFalse();
    }

    [Fact]
    public void Blank_script_is_not_flagged()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Rozelle"))]);

        SuburbRetentionDetector.Detect(string.Empty, context).HasMissingSuburb.ShouldBeFalse();
    }

    // ─── Repair ─────────────────────────────────────────────────────────────

    [Fact]
    public void Repair_inserts_the_missing_suburb_after_the_road_name()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Rozelle"))]);
        const string script = "Victoria Road is slow this morning.";

        string repaired = SuburbRetentionDetector.Repair(script, context);

        repaired.ShouldContain("Victoria Road near Rozelle");
        SuburbRetentionDetector.Detect(repaired, context).HasMissingSuburb.ShouldBeFalse();
    }

    // ─── Double-qualifier guard (AlreadyPlacedRegex) ───────────────────────

    [Fact]
    public void Repair_does_not_stack_a_second_locative_when_road_already_followed_by_near_place()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Drummoyne"))]);
        const string script = "Victoria Road near Rozelle is slow.";

        string repaired = SuburbRetentionDetector.Repair(script, context);

        repaired.ShouldNotContain("near Drummoyne");
        repaired.ShouldNotContain("near Rozelle near");
        repaired.ShouldBe(script); // road already placed — the data's suburb is not stacked on
    }

    [Fact]
    public void Repair_does_not_stack_a_second_locative_when_road_already_followed_by_at_place()
    {
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Drummoyne"))]);
        const string script = "Victoria Road at Rozelle is slow.";

        string repaired = SuburbRetentionDetector.Repair(script, context);

        repaired.ShouldNotContain("near Drummoyne");
        repaired.ShouldNotContain("at Rozelle near");
        repaired.ShouldBe(script);
    }

    [Fact]
    public void Repair_still_inserts_suburb_when_road_is_followed_by_a_condition_not_a_place()
    {
        // "at a standstill" is a condition (lowercase after "at"), not a place qualifier — the
        // guard must allow the insert here, unlike the "at Rozelle" place case above.
        var context = Ctx([Corridor("Victoria Road", Option<string>.Some("Rozelle"))]);
        const string script = "Victoria Road at a standstill.";

        string repaired = SuburbRetentionDetector.Repair(script, context);

        repaired.ShouldContain("Victoria Road near Rozelle at a standstill");
    }

    // ─── Crash guard regression ─────────────────────────────────────────────

    [Fact]
    public void Empty_road_name_does_not_throw()
    {
        var context = Ctx([Corridor(string.Empty, Option<string>.Some("Rozelle"))]);
        const string script = "Traffic is moving well across town.";

        var result = SuburbRetentionDetector.Detect(script, context);
        result.HasMissingSuburb.ShouldBeFalse();

        SuburbRetentionDetector.Repair(script, context).ShouldBe(script);
    }

    [Fact]
    public void Detect_computes_individual_orders_for_multiple_dropped_suburbs()
    {
        var context = Ctx([
            Corridor("Victoria Road", Option<string>.Some("Rozelle")),
            Corridor("Military Road", Option<string>.Some("Mosman")),
            Corridor("Pacific Highway", Option<string>.Some("Chatswood"))
        ]);

        const string script =
            "Victoria Road has heavy delays. " +
            "Military Road is crawling towards the spit. " +
            "Pacific Highway is moving slowly.";

        var result = SuburbRetentionDetector.Detect(script, context);

        result.HasMissingSuburb.ShouldBeTrue();
        result.Issues.Count.ShouldBe(3);

        result.Issues[0].Order.ShouldBe(1);
        result.Issues[0].Defect.ShouldBe("SUBURB_RETENTION");
        result.Issues[0].Reason.ShouldContain("Victoria Road");
        result.Issues[0].Reason.ShouldContain("Rozelle");
        result.Issues[0].Scope.ShouldBe(RefineScope.Sentence);

        result.Issues[1].Order.ShouldBe(2);
        result.Issues[1].Defect.ShouldBe("SUBURB_RETENTION");
        result.Issues[1].Reason.ShouldContain("Military Road");
        result.Issues[1].Reason.ShouldContain("Mosman");
        result.Issues[1].Scope.ShouldBe(RefineScope.Sentence);

        result.Issues[2].Order.ShouldBe(3);
        result.Issues[2].Defect.ShouldBe("SUBURB_RETENTION");
        result.Issues[2].Reason.ShouldContain("Pacific Highway");
        result.Issues[2].Reason.ShouldContain("Chatswood");
        result.Issues[2].Scope.ShouldBe(RefineScope.Sentence);
    }
}
