using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RoadNameGroundingDetectorTests
{
    private static BroadcastWithTraffic Context(
        IReadOnlyList<TrafficCorridor>? corridors = null,
        IReadOnlyList<TrafficIncident>? incidents = null,
        Option<WeatherReport>? weather = null,
        Option<FuelPriceReport>? fuel = null) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Melbourne",
            Traffic = string.Empty,
            Incidents = incidents ?? [],
            Corridors = corridors ?? [],
            Weather = weather ?? Option<WeatherReport>.None,
            Fuel = fuel ?? Option<FuelPriceReport>.None,
            FillOrder = [FillSource.MinorTraffic, FillSource.Weather, FillSource.Fuel, FillSource.Filler]
        };

    private static TrafficCorridor Corridor(
        string road,
        Option<string> crossStreet = default) =>
        new(road, 2, 3, 1000, 10, "congestion", $"{road} congestion", [],
            OfficialCrossStreet: crossStreet);

    [Fact]
    public void Flags_roads_that_are_not_in_the_supplied_data()
    {
        var context = Context([
            Corridor("Craigieburn Bypass"),
            Corridor("Governor Road"),
            Corridor("Gramatan Avenue")
        ]);

        var result = RoadNameGroundingDetector.Detect(
            "The Bypass is congested at Craigieburn Bypass, with a short queue. " +
            "Breakdowns on Governor Road near Braeside, Gramatan Avenue near Beaumaris and " +
            "the Metropolitan Ring Road near Bundoora are causing minor delays. " +
            "Burnley Street near Richmond also has a breakdown, with minor delays.", context);

        result.HasUngroundedRoad.ShouldBeTrue();
        result.Feedback.ShouldContain("Metropolitan Ring Road");
        result.Feedback.ShouldContain("Burnley Street");
        result.Feedback.ShouldNotContain("Craigieburn Bypass");
        result.Feedback.ShouldNotContain("Governor Road");
        result.Feedback.ShouldNotContain("Gramatan Avenue");
    }

    [Fact]
    public void Supplied_cross_street_is_voiceable()
    {
        var result = RoadNameGroundingDetector.Detect(
            "M4 is slow at Church Street.",
            Context([Corridor("M4", Option<string>.Some("Church Street"))]));

        result.HasUngroundedRoad.ShouldBeFalse();
    }

    [Theory]
    [InlineData("The Mount Alexander Road is slow.")]
    [InlineData("Mt Alexander Rd is slow.")]
    public void Leading_article_and_road_suffix_variation_are_grounded(string script)
    {
        var result = RoadNameGroundingDetector.Detect(
            script,
            Context([Corridor("Mt Alexander Rd")]));

        result.HasUngroundedRoad.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Clear skies, seventeen degrees in Melbourne.")]
    [InlineData("Unleaded is around $1.89 a litre in Melbourne.")]
    [InlineData("You're listening to KIIS 101.1 in Melbourne.")]
    [InlineData("That's KIIS traffic and weather together.")]
    public void Supplementary_and_branding_lines_do_not_create_road_false_positives(string script)
    {
        var result = RoadNameGroundingDetector.Detect(
            script,
            Context([Corridor("M4")]));

        result.HasUngroundedRoad.ShouldBeFalse();
    }

    [Fact]
    public void Incident_location_is_a_supplied_road()
    {
        var incident = new TrafficIncident("i1", "Breakdown", 2, "Queen Street", "breakdown");
        var result = RoadNameGroundingDetector.Detect(
            "A breakdown is reported on Queen Street.",
            Context(incidents: [incident]));

        result.HasUngroundedRoad.ShouldBeFalse();
    }
    [Fact]
    public void Flags_a_route_code_when_no_roads_were_supplied()
    {
        RoadNameGroundingDetector.Detect("The M1 is slow.", Context())
            .HasUngroundedRoad.ShouldBeTrue();
    }


    [Fact]
    public void Blank_script_is_clean()
    {
        RoadNameGroundingDetector.Detect(string.Empty, Context([Corridor("M4")]))
            .HasUngroundedRoad.ShouldBeFalse();
    }
}
