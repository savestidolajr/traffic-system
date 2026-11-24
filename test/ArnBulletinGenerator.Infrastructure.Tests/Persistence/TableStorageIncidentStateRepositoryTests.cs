using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageIncidentStateRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageIncidentStateRepositoryTests
{
    private readonly ILogger<TableStorageIncidentStateRepository> _logger =
        Substitute.For<ILogger<TableStorageIncidentStateRepository>>();

    private readonly TableStorageIncidentStateRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageIncidentStateRepositoryTests()
    {
        _sut = new TableStorageIncidentStateRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static IncidentStateEntity CreateEntity(string marketId = "syd", string incidentId = "inc-1",
        string status = "Active")
    {
        return new IncidentStateEntity
        {
            PartitionKey = marketId,
            RowKey = incidentId,
            FirstSeenAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastSeenAt = DateTimeOffset.UtcNow,
            Status = status,
            Type = "CONSTRUCTION",
            Criticality = 3,
            LengthMeters = 500,
            DescriptionHash = "abc123",
            Source = "HERE",
            UpdatedAt = DateTimeOffset.UtcNow,
            RoadName = "Pacific Highway",
            Suburb = "Chatswood",
            Direction = "Northbound"
        };
    }

    private void SeedQuery(params IncidentStateEntity[] entities)
    {
        _tableClient.QueryAsync<IncidentStateEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    private void SeedGet(IncidentStateEntity entity)
    {
        _tableClient.GetEntityAsync<IncidentStateEntity>(
                Arg.Is(entity.PartitionKey), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedGetNotFound(string partitionKey, string rowKey)
    {
        _tableClient.GetEntityAsync<IncidentStateEntity>(
                Arg.Is(partitionKey), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    // ── GetCurrentByMarketAsync ──────────────────────────────────────

    [Fact]
    public async Task GetCurrentByMarketAsync_ReturnsIncidents()
    {
        var e1 = CreateEntity(incidentId: "inc-1");
        var e2 = CreateEntity(incidentId: "inc-2");
        SeedQuery(e1, e2);

        var result = await _sut.GetCurrentByMarketAsync("syd");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetCurrentByMarketAsync_EmptyMarket_ReturnsEmpty()
    {
        SeedQuery();

        var result = await _sut.GetCurrentByMarketAsync("empty");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // ── UpsertAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_Success_ReturnsSuccess()
    {
        var state = new IncidentState("inc-1", "syd",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow,
            IncidentStateStatus.Active, "ACCIDENT", 4, 1200, "hash1", "HERE", DateTime.UtcNow);

        var result = await _sut.UpsertAsync(state);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<IncidentStateEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_StorageError_ReturnsFailure()
    {
        _tableClient.UpsertEntityAsync(
                Arg.Any<IncidentStateEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var state = new IncidentState("inc-err", "syd",
            DateTime.UtcNow, DateTime.UtcNow,
            IncidentStateStatus.Active, "ACCIDENT", 2, 300, "h", "HERE", DateTime.UtcNow);

        var result = await _sut.UpsertAsync(state);

        result.IsFailure.ShouldBeTrue();
    }

    // ── MarkClearedAsync ─────────────────────────────────────────────

    [Fact]
    public async Task MarkClearedAsync_ExistingIncident_UpdatesStatus()
    {
        var entity = CreateEntity();
        entity.ETag = new ETag("v1");
        SeedGet(entity);

        var result = await _sut.MarkClearedAsync("syd", "inc-1", DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<IncidentStateEntity>(e => e.Status == "Cleared"),
            Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkClearedAsync_NonExistent_ReturnsSuccess()
    {
        SeedGetNotFound("syd", "ghost");

        var result = await _sut.MarkClearedAsync("syd", "ghost", DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task MarkClearedAsync_ETagConflict_ReturnsFailure()
    {
        var entity = CreateEntity();
        entity.ETag = new ETag("v1");
        SeedGet(entity);

        _tableClient.UpdateEntityAsync(
                Arg.Any<IncidentStateEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"));

        var result = await _sut.MarkClearedAsync("syd", "inc-1", DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Concurrent modification");
    }

    // ── PurgeExpiredAsync ────────────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredAsync_DeletesOldEntities()
    {
        var old = CreateEntity(incidentId: "old-1");
        old.LastSeenAt = DateTimeOffset.UtcNow.AddDays(-3);
        var recent = CreateEntity(incidentId: "new-1");
        recent.LastSeenAt = DateTimeOffset.UtcNow;
        SeedQuery(old, recent);

        var result = await _sut.PurgeExpiredAsync("syd", DateTime.UtcNow.AddDays(-1));

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).DeleteEntityAsync(
            Arg.Any<string>(), Arg.Is("old-1"), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    // ── Mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_PreservesOptionalFields()
    {
        var state = new IncidentState("map-1", "syd",
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow,
            IncidentStateStatus.Active, "LANE_RESTRICTION", 2, 800, "hash", "HERE",
            DateTime.UtcNow)
        {
            LastReportedAt = DateTime.UtcNow.AddMinutes(-10),
            RoadName = "M2 Motorway",
            Suburb = "North Ryde",
            Direction = "Westbound"
        };

        IncidentStateEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<IncidentStateEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.UpsertAsync(state);

        captured.ShouldNotBeNull();
        captured!.RoadName.ShouldBe("M2 Motorway");
        captured.Direction.ShouldBe("Westbound");
        captured.LastReportedAt.ShouldNotBeNull();
    }
}
