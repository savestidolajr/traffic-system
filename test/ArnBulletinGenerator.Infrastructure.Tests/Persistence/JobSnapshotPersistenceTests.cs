using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class JobSnapshotPersistenceTests
{
    private readonly ILogger<TableStorageJobRepository> _logger = Substitute.For<ILogger<TableStorageJobRepository>>();
    private readonly TableStorageJobRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public JobSnapshotPersistenceTests()
    {
        _sut = new TableStorageJobRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            TimeProvider.System,
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

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
    public async Task SaveAndGet_RoundTrips_GeneratorInputSnapshot()
    {
        var job = Job.Create("snap-1", "2GB", "Mildura", 30, DateTime.UtcNow).Value;
        job.GeneratorInput = GeneratorInputSnapshot.FromContext(SampleContext());

        // Capture the entity that gets upserted
        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.GeneratorInputJson.ShouldNotBeNull();
        captured.GeneratorInputJson.ShouldContain("Walker Road");

        // Feed captured entity back through Get to verify domain mapping
        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("snap-1")).Value;
        loaded.GeneratorInput.ShouldNotBeNull();
        loaded.GeneratorInput!.Corridors.Count.ShouldBe(1);
        loaded.GeneratorInput.Weather.HasValue.ShouldBeTrue();
        loaded.GeneratorInput.Weather.Value.Conditions.ShouldBe("Fog. Quite cool.");
    }

    [Fact]
    public async Task SaveAndGet_CompressesOversizedGeneratorInput_AndRoundTrips()
    {
        // An incident-heavy market's snapshot exceeds the 30K-char Table Storage cap
        // (observed live: adelaide 31-33K on every job). It must persist gzip-compressed
        // ("gz:" sentinel, within the cap) instead of being dropped, and round-trip intact.
        var corridors = Enumerable.Range(0, 80).Select(i => new TrafficCorridor(
            RoadName: $"Road {i} {new string('x', 180)}", Severity: 2, FunctionalClass: 3,
            TotalLengthMeters: 1000 + i, ImpactScore: 50 + i, PrimaryType: "congestion",
            Description: $"heavy congestion segment {i} {new string('y', 180)}",
            Incidents: [], Direction: Option<string>.Some("citybound"),
            DriveTimeMinutes: Option<int>.None, NormalDriveTimeMinutes: Option<int>.None,
            StartPoint: Option<(double, double)>.None)).ToList();
        var job = Job.Create("snap-big", "2GB", "Adelaide", 30, DateTime.UtcNow).Value;
        job.GeneratorInput = GeneratorInputSnapshot.FromContext(SampleContext()) with { Corridors = corridors };

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.GeneratorInputJson.ShouldNotBeNull();
        captured.GeneratorInputJson!.ShouldStartWith("gz:");
        captured.GeneratorInputJson.Length.ShouldBeLessThanOrEqualTo(30_000);

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("snap-big")).Value;
        loaded.GeneratorInput.ShouldNotBeNull();
        loaded.GeneratorInput!.Corridors.Count.ShouldBe(80);
        loaded.GeneratorInput.Corridors[79].RoadName.ShouldStartWith("Road 79");
        loaded.GeneratorInput.Weather.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task GetJob_LegacyEntity_WithoutGeneratorInputJson_LoadsNull()
    {
        var job = Job.Create("snap-legacy", "2GB", "Mildura", 30, DateTime.UtcNow).Value;

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);
        captured.ShouldNotBeNull();
        captured!.GeneratorInputJson.ShouldBeNull();

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("snap-legacy")).Value;
        loaded.GeneratorInput.ShouldBeNull();
    }

    private void SeedGet(JobEntity entity)
    {
        var indexEntity = new JobIndexEntity
        {
            PartitionKey = "JobIndex",
            RowKey = entity.RowKey,
            Market = entity.Market
        };
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(indexEntity));
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }
}
