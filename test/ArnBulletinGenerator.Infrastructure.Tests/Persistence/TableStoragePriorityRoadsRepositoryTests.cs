using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStoragePriorityRoadsRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStoragePriorityRoadsRepositoryTests
{
    private readonly ILogger<TableStoragePriorityRoadsRepository> _logger =
        Substitute.For<ILogger<TableStoragePriorityRoadsRepository>>();

    private readonly TableStoragePriorityRoadsRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStoragePriorityRoadsRepositoryTests()
    {
        _sut = new TableStoragePriorityRoadsRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static PriorityRoadsEntity CreateEntity(string market = "sydney", string[]? roads = null)
    {
        return new PriorityRoadsEntity
        {
            PartitionKey = "PriorityRoads",
            RowKey = market,
            RoadsJson = System.Text.Json.JsonSerializer.Serialize(roads ?? new[] { "Pacific Highway", "M1" }),
            UpdatedBy = "admin@test.com",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void SeedGet(PriorityRoadsEntity entity)
    {
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Is("PriorityRoads"), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedGetNotFound(string rowKey)
    {
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Is("PriorityRoads"), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    // ── GetRoadsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetRoadsAsync_ExistingMarket_ReturnsRoads()
    {
        var entity = CreateEntity("sydney", ["Pacific Highway", "M1", "M2"]);
        SeedGet(entity);

        var result = await _sut.GetRoadsAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        // Legacy string[] entries migrate to boosting both axes.
        result.Value.Select(r => r.Name).ShouldBe(["Pacific Highway", "M1", "M2"]);
        result.Value.ShouldAllBe(r => r.BoostIncidents && r.FlowTier == FlowPriorityTier.High);
    }

    [Fact]
    public async Task GetRoadsAsync_LegacyTypedBoostFlowing_MigratesToTier()
    {
        var entity = CreateEntity("sydney");
        entity.RoadsJson =
            """[{"Name":"M1","BoostIncidents":true,"BoostFlowing":false},{"Name":"M2","BoostIncidents":true,"BoostFlowing":true}]""";
        SeedGet(entity);

        var result = await _sut.GetRoadsAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single(r => r.Name == "M1").FlowTier.ShouldBe(FlowPriorityTier.None);
        result.Value.Single(r => r.Name == "M2").FlowTier.ShouldBe(FlowPriorityTier.High);
    }

    [Fact]
    public async Task GetRoadsAsync_TypedFlowTier_RoundTrips()
    {
        var entity = CreateEntity("sydney");
        entity.RoadsJson = """[{"Name":"M1","BoostIncidents":false,"FlowTier":"Critical"}]""";
        SeedGet(entity);

        var result = await _sut.GetRoadsAsync("sydney");

        var road = result.Value.ShouldHaveSingleItem();
        road.BoostIncidents.ShouldBeFalse();
        road.FlowTier.ShouldBe(FlowPriorityTier.Critical);
    }

    [Fact]
    public async Task GetRoadsAsync_MissingMarket_ReturnsEmpty()
    {
        SeedGetNotFound("melbourne");

        var result = await _sut.GetRoadsAsync("melbourne");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // ── SetRoadsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SetRoadsAsync_ExistingEntity_UpdatesWithETag()
    {
        var entity = CreateEntity("sydney", ["Pacific Highway", "M1"]);
        entity.ETag = new ETag("v1");
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Any<string>(), Arg.Is("sydney"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        _tableClient.UpdateEntityAsync(
                Arg.Any<PriorityRoadsEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SetRoadsAsync("sydney", [new PriorityRoad("M1"), new PriorityRoad("M2")], "admin@test.com");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<PriorityRoadsEntity>(e => e.RoadsJson.Contains("M2")),
            Arg.Is<ETag>(e => e.ToString() == "v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetRoadsAsync_NewEntity_Creates()
    {
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Any<string>(), Arg.Is("melbourne"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.SetRoadsAsync("melbourne", [new PriorityRoad("M1")], "admin@test.com");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<PriorityRoadsEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetRoadsAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        _tableClient.UpsertEntityAsync(
                Arg.Any<PriorityRoadsEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var result = await _sut.SetRoadsAsync("sydney", [new PriorityRoad("M1")], "admin@test.com");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task SetRoadsAsync_ETagConflict_RetriesAndSucceeds()
    {
        var entity = CreateEntity("sydney", ["Pacific Highway"]);
        entity.ETag = new ETag("v1");
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Any<string>(), Arg.Is("sydney"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        // First update throws 412, second succeeds
        _tableClient.UpdateEntityAsync(
                Arg.Any<PriorityRoadsEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new RequestFailedException(412, "Precondition Failed"),
                _ => Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SetRoadsAsync("sydney", [new PriorityRoad("M1"), new PriorityRoad("M2")], "admin@test.com");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(2).UpdateEntityAsync(
            Arg.Any<PriorityRoadsEntity>(), Arg.Any<ETag>(),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    // ── Mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_RoundTrips_AllFields()
    {
        var roads = new[] { new PriorityRoad("Pacific Highway"), new PriorityRoad("M1", BoostIncidents: false), new PriorityRoad("M2") };
        const string market = "sydney";

        // Seed GetEntityAsync to return 404 (creation path)
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Any<string>(), Arg.Is(market),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        PriorityRoadsEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<PriorityRoadsEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SetRoadsAsync(market, roads, "admin@test.com");

        captured.ShouldNotBeNull();
        captured!.RowKey.ShouldBe(market);
        captured.UpdatedBy.ShouldBe("admin@test.com");
        captured.UpdatedAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-10));
        captured.RoadsJson.ShouldNotBeNullOrEmpty();

        // Feed entity back through GetEntityOrDefaultAsync path
        _tableClient.GetEntityAsync<PriorityRoadsEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(captured));

        var loaded = (await _sut.GetRoadsAsync(market)).Value;
        loaded.ShouldBe(roads);
    }
}
