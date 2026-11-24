using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>
///     Reconstructs the regression fixture for deployed job <c>62475adce79e4c77a03f138430a89d6b</c>
///     (Mildura) from its persisted <c>radioJobs</c> columns. That job predates <c>GeneratorInputJson</c>
///     snapshot persistence, so the writer input is rebuilt by hand from its corridors/incidents/weather/
///     fuel/good-run columns. Future jobs are captured losslessly via <c>scripts/capture-regression.sh</c>.
///     The test validates the snapshot round-trips; set <c>ARN_WRITE_FIXTURES=1</c> to (re)emit the
///     committed fixture at <c>test/corpus/regressions/mildura-flowlist.json</c>.
/// </summary>
public sealed class MilduraFixtureBuilder
{
    private const string OriginalScript =
        "Walker Road’s shut both ways, citybound and outbound, with a long queue. Burns Street " +
        "citybound’s also dealing with construction. It’s foggy and quite cool at 5 degrees. Running " +
        "well, Werrimull North Road, Tapaulin Road, Fletchers Lake Road, Benetook Avenue, Paschendael " +
        "Avenue, Brownport Road, Silver City Highway and Wentworth Street. Unleaded’s cheapest at $1.59 " +
        "a litre, with diesel from $1.78, average $1.68.";

    private static RegressionFixture Build()
    {
        var burns = new TrafficIncident(
            Id: "3164554483528938899", Description: "Road construction at Wentworth Street", Severity: 1,
            Location: "Burns St", Type: "construction", LengthMeters: 232, FunctionalClass: 2,
            Direction: Option<string>.Some("citybound"),
            Summary: Option<string>.Some("At Wentworth Street - Road construction"));
        var walkerCb = new TrafficIncident(
            Id: "4317557250287830989", Description: "Road closed", Severity: 3, Location: "Walker Rd",
            Type: "roadClosure", LengthMeters: 3505, FunctionalClass: 5,
            Direction: Option<string>.Some("citybound"), Summary: Option<string>.Some("Closed"));
        var walkerOb = new TrafficIncident(
            Id: "2061294591887963194", Description: "Road closed", Severity: 3, Location: "Walker Rd",
            Type: "roadClosure", LengthMeters: 3505, FunctionalClass: 5,
            Direction: Option<string>.Some("outbound"), Summary: Option<string>.Some("Closed"));

        List<TrafficCorridor> corridors =
        [
            new("Burns Street", 1, 2, 200, 169.46, "construction", "roadworks citybound",
                [burns], Direction: Option<string>.Some("citybound")),
            new("Walker Road", 3, 5, 3500, 85, "roadClosure", "road closure citybound",
                [walkerCb], Direction: Option<string>.Some("citybound")),
            new("Walker Road", 3, 5, 3500, 85, "roadClosure", "road closure outbound",
                [walkerOb], Direction: Option<string>.Some("outbound"))
        ];

        string[] goodRuns =
        [
            "Werrimull North Road", "Tapaulin Road", "Fletchers Lake Road", "Benetook Avenue",
            "Paschendael Avenue", "Brownport Road", "Silver City Highway", "Wentworth Street"
        ];
        var flowPool = new (string Road, double Jam)[]
        {
            ("Werrimull North Road", 0.3), ("Tapaulin Road", 0.3), ("Fletchers Lake Road", 0.7),
            ("Benetook Avenue", 1.7), ("Paschendael Avenue", 1.1), ("Brownport Road", 1.5),
            ("Silver City Highway", 0.4), ("Wentworth Street", 0.5)
        }.Select(r => new FlowRoad(r.Road, r.Jam, 50000)).ToList();

        var context = new BroadcastRequest
        {
            TargetDurationSeconds = 30,
            JobId = "62475adce79e4c77a03f138430a89d6b",
            Location = "Mildura",
            Coverage = CoverageArea.Create(-34.1855, 142.1625, 25).Value,
            Content = new BulletinContentSelection(true, true, true)
        }.WithCorridors(corridors) with
        {
            City = Option<string>.Some("Mildura"),
            MarketId = Option<string>.Some("mildura"),
            GoodRunRoads = goodRuns,
            FlowPool = flowPool,
            Weather = Option<WeatherReport>.Some(new WeatherReport(
                "Fog. Quite cool.", 5, Option<string>.None, 18, [])),
            Fuel = Option<FuelPriceReport>.Some(new FuelPriceReport(
            [
                new FuelPriceSummary("Unleaded 91", 1.595, 1.68, 24, DateTimeOffset.Parse("2026-06-24T23:31:05Z")),
                new FuelPriceSummary("Diesel", 1.785, 1.88, 18, DateTimeOffset.Parse("2026-06-24T23:31:05Z"))
            ]))
        };

        return new RegressionFixture(
            Label: "mildura-flowlist",
            Reason: "flowing-well roads read as a long flat enumerated list; fuel recited robotically; weather not elaborated (no later/outlook)",
            SourceJobId: "62475adce79e4c77a03f138430a89d6b",
            OriginalScript: OriginalScript,
            CapturedAtUtc: DateTimeOffset.Parse("2026-06-25T00:20:22Z"),
            Snapshot: GeneratorInputSnapshot.FromContext(context));
    }

    [Fact]
    public void MilduraFixture_BuildsAndRoundTrips()
    {
        var fixture = Build();
        var restored = JsonSerializer.Deserialize<RegressionFixture>(fixture.ToJson(), JsonDefaults.Storage)!;

        restored.Snapshot.Corridors.Count.ShouldBe(3);
        restored.Snapshot.GoodRunRoads.Count.ShouldBe(8);
        restored.Snapshot.Weather.HasValue.ShouldBeTrue();
        restored.Snapshot.Fuel.Value.Summaries.Count.ShouldBe(2);
        restored.Snapshot.ToContext("x").Corridors[0].Incidents.ShouldHaveSingleItem();

        if (Environment.GetEnvironmentVariable("ARN_WRITE_FIXTURES") == "1")
        {
            string dir = CorpusPaths.RegressionCorpusRoot();
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "mildura-flowlist.json"), fixture.ToJson());
        }
    }
}
