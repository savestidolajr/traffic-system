using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class GeneratorInputSnapshotTests
{
    private static BroadcastWithTraffic SampleContext()
    {
        var incident = new TrafficIncident(
            Id: "i1", Description: "road closure", Severity: 3, Location: "Walker Rd",
            Type: "roadClosure", LengthMeters: 3505, FunctionalClass: 5,
            Direction: Option<string>.Some("citybound"));
        var corridor = new TrafficCorridor(
            RoadName: "Walker Road", Severity: 3, FunctionalClass: 5, TotalLengthMeters: 3505,
            ImpactScore: 85, PrimaryType: "roadClosure", Description: "road closure citybound",
            Incidents: [incident], Direction: Option<string>.Some("citybound"),
            DriveTimeMinutes: Option<int>.Some(12), NormalDriveTimeMinutes: Option<int>.Some(9),
            StartPoint: Option<(double, double)>.Some((-34.2, 141.9)));

        return new BroadcastRequest
        {
            TargetDurationSeconds = 30,
            JobId = "src-job",
            Location = "Mildura",
            Coverage = CoverageArea.Create(-34.1855, 142.1625, 25).Value,
            Content = new BulletinContentSelection(true, true, true)
        }.WithCorridors([corridor]) with
        {
            City = Option<string>.Some("Mildura"),
            MarketId = Option<string>.Some("mildura"),
            FillOrder = [FillSource.Fuel, FillSource.Weather],
            MonitoredRoutes =
            [
                new MonitoredRouteStatus("M1 to CBD", new CoveragePoint(-34.1, 141.9),
                    new CoveragePoint(-34.2, 142.0), 18, 14, 12.3)
            ],
            GoodRunRoads = ["Silver City Highway", "Benetook Avenue"],
            FlowPool = [new FlowRoad("Silver City Highway", 0.3, 5000, IsWeighted: true)],
            Weather = Option<WeatherReport>.Some(new WeatherReport(
                "Fog. Quite cool.", 5, Option<string>.None, 18, [])),
            Fuel = Option<FuelPriceReport>.Some(new FuelPriceReport(
                [new FuelPriceSummary("Unleaded 91", 1.595, 1.68, 24, DateTimeOffset.UtcNow)]))
        };
    }

    [Fact]
    public void Snapshot_RoundTripsThroughStorageJson_PreservingWriterInput()
    {
        var original = GeneratorInputSnapshot.FromContext(SampleContext());

        string json = JsonSerializer.Serialize(original, JsonDefaults.Storage);
        var restored = JsonSerializer.Deserialize<GeneratorInputSnapshot>(json, JsonDefaults.Storage)!;
        var ctx = restored.ToContext("replay-job");

        ctx.TargetDurationSeconds.ShouldBe(30);
        ctx.Content.Weather.ShouldBeTrue();
        ctx.FillOrder.ShouldBe(new[] { FillSource.Fuel, FillSource.Weather });
        // Persisted as enum names, not ordinals, so the stored snapshot stays human-auditable.
        json.ShouldContain("\"FillOrder\":[\"Fuel\",\"Weather\"]");
        ctx.Corridors.Count.ShouldBe(1);
        ctx.Corridors[0].RoadName.ShouldBe("Walker Road");
        ctx.Corridors[0].Incidents.Count.ShouldBe(1);
        ctx.Corridors[0].Incidents[0].Type.ShouldBe("roadClosure");
        ctx.Corridors[0].Direction.HasValue.ShouldBeTrue();
        ctx.MonitoredRoutes.ShouldHaveSingleItem().DriveTimeMinutes.ShouldBe(18);
        ctx.GoodRunRoads.ShouldContain("Silver City Highway");
        ctx.FlowPool.ShouldHaveSingleItem().IsWeighted.ShouldBeTrue();
        ctx.Weather.Value.Conditions.ShouldBe("Fog. Quite cool.");
        ctx.Weather.Value.WindDescription.HasValue.ShouldBeFalse();
        ctx.Fuel.Value.Summaries.ShouldHaveSingleItem().MinPrice.ShouldBe(1.595);
        // ToContext regenerates the traffic narrative + flat incident list from corridors.
        ctx.Traffic.ShouldNotBeNullOrWhiteSpace();
        ctx.Incidents.ShouldHaveSingleItem();
    }

    [Fact]
    public void FromContext_StripsRoutingCoordinates()
    {
        var snapshot = GeneratorInputSnapshot.FromContext(SampleContext());
        snapshot.Corridors[0].StartPoint.HasValue.ShouldBeFalse();
    }

    // Verbatim GeneratorInputJson from a historical dev job (test/corpus/dev-bulletins/jobs/
    // 0235ecadb7eb4489bb56444dee71ce53.json), captured before the snapshot persisted FillOrder.
    private const string OldFormatSnapshotJson =
        """{"TargetDurationSeconds":30,"Location":"demo-coast","City":null,"MarketId":null,"Content":{"Traffic":true,"Weather":true,"Fuel":true,"HasAnySource":true},"Corridors":[],"MonitoredRoutes":[],"GoodRunRoads":[],"FlowPool":[],"Weather":null,"Fuel":null,"IncidentChanges":null,"Guidance":{"Entries":[],"DroppedCount":0,"Count":0,"HasEntries":false},"MarketFeedback":[]}""";

    [Fact]
    public void Snapshot_WithoutFillOrderProperty_LoadsAndDefaultsToWeatherFuelMinorTrafficFiller()
    {
        // A stored snapshot that predates the FillOrder property must still deserialise (no throw)
        // and yield the BroadcastContextBase default order on rebuild — the corpus loader and the
        // replay harness load every pre-change job through exactly this path.
        var restored = JsonSerializer.Deserialize<GeneratorInputSnapshot>(OldFormatSnapshotJson, JsonDefaults.Storage)!;

        restored.FillOrder.ShouldBe(
            new[] { FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler });

        var ctx = restored.ToContext("replay-job");
        ctx.FillOrder.ShouldBe(
            new[] { FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler });
    }
}
