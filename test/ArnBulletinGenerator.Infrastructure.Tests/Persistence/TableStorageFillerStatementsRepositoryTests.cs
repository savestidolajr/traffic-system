using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageFillerStatementsRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageFillerStatementsRepositoryTests
{
    private readonly ILogger<TableStorageFillerStatementsRepository> _logger =
        Substitute.For<ILogger<TableStorageFillerStatementsRepository>>();

    private readonly TableStorageFillerStatementsRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageFillerStatementsRepositoryTests()
    {
        _sut = new TableStorageFillerStatementsRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static FillerStatementsEntity CreateEntity(string market = "sydney", FillerStatement[]? statements = null)
    {
        return new FillerStatementsEntity
        {
            PartitionKey = "FillerStatements",
            RowKey = market,
            StatementsJson = System.Text.Json.JsonSerializer.Serialize(statements ??
                [
                    new FillerStatement("s1", "Stay safe on the roads."),
                    new FillerStatement("s2", "Drive carefully today.")
                ]),
            UpdatedBy = "admin@test.com",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void SeedGet(FillerStatementsEntity entity)
    {
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Is("FillerStatements"), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedGetNotFound(string rowKey)
    {
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Is("FillerStatements"), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    // ── GetStatementsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetStatementsAsync_ExistingMarket_ReturnsStatements()
    {
        var entity = CreateEntity("sydney");
        SeedGet(entity);

        var result = await _sut.GetStatementsAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value[0].Text.ShouldBe("Stay safe on the roads.");
        result.Value[1].Text.ShouldBe("Drive carefully today.");
    }

    [Fact]
    public async Task GetStatementsAsync_MissingMarket_ReturnsEmpty()
    {
        SeedGetNotFound("melbourne");

        var result = await _sut.GetStatementsAsync("melbourne");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStatementsAsync_EmptyJson_ReturnsEmpty()
    {
        var entity = CreateEntity("perth");
        entity.StatementsJson = "[]";
        SeedGet(entity);

        var result = await _sut.GetStatementsAsync("perth");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // ── SetStatementsAsync ───────────────────────────────────────────

    [Fact]
    public async Task SetStatementsAsync_ExistingEntity_UpdatesWithETag()
    {
        var entity = CreateEntity("sydney");
        entity.ETag = new ETag("v1");
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Any<string>(), Arg.Is("sydney"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        _tableClient.UpdateEntityAsync(
                Arg.Any<FillerStatementsEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var statements = new[]
        {
            new FillerStatement("s1", "Updated statement."),
            new FillerStatement("s2", "New filler.")
        };

        var result = await _sut.SetStatementsAsync("sydney", statements, "admin@test.com");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<FillerStatementsEntity>(e => e.StatementsJson.Contains("Updated statement.")),
            Arg.Is<ETag>(e => e.ToString() == "v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetStatementsAsync_NewEntity_Creates()
    {
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Any<string>(), Arg.Is("melbourne"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.SetStatementsAsync("melbourne",
            [new FillerStatement("s1", "Hello.")], "admin@test.com");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<FillerStatementsEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetStatementsAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        _tableClient.UpsertEntityAsync(
                Arg.Any<FillerStatementsEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var result = await _sut.SetStatementsAsync("sydney",
            [new FillerStatement("s1", "Hello.")], "admin@test.com");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task SetStatementsAsync_ETagConflict_RetriesAndSucceeds()
    {
        var entity = CreateEntity("sydney");
        entity.ETag = new ETag("v1");
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Any<string>(), Arg.Is("sydney"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        // First update throws 412, second succeeds
        _tableClient.UpdateEntityAsync(
                Arg.Any<FillerStatementsEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new RequestFailedException(412, "Precondition Failed"),
                _ => Task.FromResult(TableClientTestHelper.StubResponse()));

        var statements = new[]
        {
            new FillerStatement("s1", "Updated."),
            new FillerStatement("s2", "New.")
        };

        var result = await _sut.SetStatementsAsync("sydney", statements, "admin@test.com");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(2).UpdateEntityAsync(
            Arg.Any<FillerStatementsEntity>(), Arg.Any<ETag>(),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    // ── Mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_RoundTrips_AllFields()
    {
        var statements = new[]
        {
            new FillerStatement("abc", "Stay safe."),
            new FillerStatement("def", "Drive carefully.", Enabled: false)
        };
        const string market = "sydney";

        // Seed GetEntityAsync to return 404 (creation path)
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Any<string>(), Arg.Is(market),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        FillerStatementsEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<FillerStatementsEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SetStatementsAsync(market, statements, "admin@test.com");

        captured.ShouldNotBeNull();
        captured!.RowKey.ShouldBe(market);
        captured.UpdatedBy.ShouldBe("admin@test.com");
        captured.UpdatedAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-10));
        captured.StatementsJson.ShouldNotBeNullOrEmpty();

        // Feed entity back through GetEntityOrDefaultAsync path
        _tableClient.GetEntityAsync<FillerStatementsEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(captured));

        var loaded = (await _sut.GetStatementsAsync(market)).Value;
        loaded.Count.ShouldBe(statements.Length);
        loaded[0].Id.ShouldBe("abc");
        loaded[0].Text.ShouldBe("Stay safe.");
        loaded[0].Enabled.ShouldBeTrue();
        loaded[1].Id.ShouldBe("def");
        loaded[1].Enabled.ShouldBeFalse();
    }
}
