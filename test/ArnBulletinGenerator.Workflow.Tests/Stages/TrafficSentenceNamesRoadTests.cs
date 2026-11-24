using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

public sealed class TrafficSentenceNamesRoadTests
{
    [Fact]
    public void A_traffic_sentence_and_weather_fixture_pass()
    {
        var input = Normalize(
            new EvidenceUnit("road", "corridor:Craig Street", "road", "Craig Street"),
            new EvidenceUnit("weather-condition", "weather", "condition", "Partly sunny"),
            new EvidenceUnit("weather-temperature", "weather", "temperature", "16 degrees"));
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "craig-closure", "Craig Street closure", ["corridor:Craig Street"], 100,
                "the road is closed"),
            new StoryBrief(2, "weather", "Weather", ["weather"], 0, "fixture", Required: true)
        ]);

        Check(input, briefs,
                "Craig Street is closed northbound at Canning Highway in Burswood, with nothing getting through. "
                + "Weather is partly sunny, 16 degrees.")
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_corridor_label_supplies_the_story_road_when_no_road_fact_exists()
    {
        var input = Normalize(
            new EvidenceUnit("suburb", "corridor:South Road#1", "suburb", "Torrensville"));
        var briefs = new BulletinBriefs([
            new StoryBrief(
                1,
                "south-road-closure",
                "South Road closure",
                ["corridor:South Road#1"],
                100,
                "the road is closed",
                Condition: "Southbound traffic cannot pass through.")
        ]);

        // The join here is incidental to what this test is for (the story road coming from the
        // corridor LABEL, there being no road fact) — it used to be a semicolon, which
        // A_condition_split_off_by_any_break_is_rejected now correctly refuses.
        Check(
                input,
                briefs,
                "South Road is closed southbound at Henley Beach Road, and southbound traffic cannot pass through.")
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_drive_time_event_id_supplies_its_road_name()
    {
        var input = Normalize(
            new EvidenceUnit(
                "drive-time",
                "route:M4",
                "driveTime",
                "M4 - 31 min run (typical 27 min, 4 min slower than typical)"));
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "m4-drive-time", "M4 drive time", ["route:M4"], 100, "the run is delayed")
        ]);

        Check(input, briefs, "The M4 run is taking thirty-one minutes.")
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_condition_sentence_without_its_story_road_is_rejected()
    {
        var input = Normalize(
            new EvidenceUnit("road", "corridor:Craig Street", "road", "Craig Street"));
        var briefs = new BulletinBriefs([
            new StoryBrief(
                1,
                "craig-closure",
                "Craig Street closure",
                ["corridor:Craig Street"],
                100,
                "the closed northbound section",
                Condition: "Northbound traffic cannot pass through, though local access is retained.")
        ]);

        var result = Check(
            input,
            briefs,
            "Craig Street is closed northbound at Canning Highway. "
            + "Northbound traffic cannot pass through, though local access is retained.");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("craig-closure");
        result.Detail.ShouldContain("Northbound traffic cannot pass through");
        result.Detail.ShouldContain("merge it into the sentence that names its road; a sentence with no road cannot be placed by a listener and can be orphaned by the slot fitter");
    }

    [Fact]
    public void A_traffic_sentence_without_a_road_or_condition_is_rejected()
    {
        var input = Normalize(
            new EvidenceUnit("road", "corridor:Craig Street", "road", "Craig Street"));
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "craig-closure", "Craig Street closure", ["corridor:Craig Street"], 100,
                "the closed northbound section")
        ]);

        var result = Check(input, briefs, "Through traffic cannot use the northbound section.");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("Through traffic cannot use the northbound section");
    }

    /// <summary>
    ///     Dev goldcoast job <c>de2d1b60</c> (2026-09-03), verbatim: the composer appended the desk
    ///     condition behind a SEMICOLON, this check passed it because its own splitter broke on
    ///     <c>.!?</c> only, and the delivery director then rewrote the semicolon to an ellipsis —
    ///     which the canonical splitter DOES break on — so a road-less orphan aired.
    /// </summary>
    [Theory]
    [InlineData("half a kay; Northbound", "semicolon")]
    [InlineData("half a kay: Northbound", "colon")]
    [InlineData("half a kay… Northbound", "ellipsis")]
    [InlineData("half a kay. Northbound", "full stop")]
    public void A_condition_split_off_by_any_break_is_rejected(string join, string _)
    {
        var input = Normalize(
            new EvidenceUnit("road", "corridor:Country Club Drive", "road", "Country Club Drive"));
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "country-club-drive-closure", "Country Club Drive closure",
                ["corridor:Country Club Drive"], 200, "a northbound closure",
                Condition: "Northbound traffic cannot use the closed section.")
        ]);

        var result = Check(
            input,
            briefs,
            "Country Club Drive at Helensvale is closed northbound for roadworks over about "
            + join + " traffic cannot use the closed section.");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("Northbound traffic cannot use the closed section");
    }

    /// <summary>
    ///     The counterpart the composer IS asked for: the condition woven into the road's sentence
    ///     behind a comma. Splitting comma clauses would reject exactly the prescribed copy.
    /// </summary>
    [Fact]
    public void A_condition_woven_into_the_road_sentence_passes()
    {
        var input = Normalize(
            new EvidenceUnit("road", "corridor:Country Club Drive", "road", "Country Club Drive"));
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "country-club-drive-closure", "Country Club Drive closure",
                ["corridor:Country Club Drive"], 200, "a northbound closure",
                Condition: "Northbound traffic cannot use the closed section.")
        ]);

        var result = Check(
            input,
            briefs,
            "Country Club Drive at Helensvale is closed northbound for roadworks, with "
            + "northbound traffic cannot use the closed section.");

        result.Passed.ShouldBeTrue(result.Detail);
    }

    [Fact]
    public void Clear_run_names_its_road_while_weather_and_fuel_fixtures_are_ignored()
    {
        var input = Normalize(
            new EvidenceUnit("clear-road", "goodrun:M7", "clear road", "M7"),
            new EvidenceUnit("weather-condition", "weather", "condition", "Partly sunny"),
            new EvidenceUnit("weather-temperature", "weather", "temperature", "16 degrees"),
            new EvidenceUnit("fuel-unleaded", "fuel", "unleaded", "a dollar ninety"),
            new EvidenceUnit("fuel-diesel", "fuel", "diesel", "two dollars"));
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "clear-run", "M7 clear run", ["goodrun:M7"], 100, "a useful alternative"),
            new StoryBrief(2, "weather", "Weather", ["weather"], 0, "fixture", Required: true),
            new StoryBrief(3, "fuel", "Fuel", ["fuel"], 0, "fixture", Required: true)
        ]);

        Check(
                input,
                briefs,
                "M7 is running well. Partly sunny, 16 degrees. Unleaded is a dollar ninety, diesel is two dollars.")
            .Passed.ShouldBeTrue();
    }

    private static CheckResult Check(NormalizedInput input, BulletinBriefs briefs, string read) =>
        ConversationalChecks
            .Run(input, read, briefs, NewTrace())
            .Single(check => check.Name == "TrafficSentenceNamesRoad");

    private static NormalizedInput Normalize(params EvidenceUnit[] evidence) =>
        Normalizer.Normalize(
            new WorkflowRequest("job", "perth", "Station", "Perth", 30, evidence, []),
            NewTrace());

    private static TraceRecorder NewTrace() =>
        new("run", "wf", "traffic sentence road check", "job", "perth", "Station", 30.0);
}
