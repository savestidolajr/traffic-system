using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageFillerAudioCacheRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageFillerAudioCacheRepositoryTests
{
    private readonly ILogger<TableStorageFillerAudioCacheRepository> _logger =
        Substitute.For<ILogger<TableStorageFillerAudioCacheRepository>>();

    private readonly TableStorageFillerAudioCacheRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageFillerAudioCacheRepositoryTests()
    {
        _sut = new TableStorageFillerAudioCacheRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static FillerAudioCacheEntity CreateEntity(
        string fillerStatementId = "stmt-1",
        string cacheKey = "voice1_settings1_model1",
        double durationSeconds = 2.5,
        WordTiming[]? wordTimings = null)
    {
        return new FillerAudioCacheEntity
        {
            PartitionKey = fillerStatementId,
            RowKey = cacheKey,
            DurationSeconds = durationSeconds,
            WordTimingsJson = JsonSerializer.Serialize(wordTimings ??
                [new WordTiming("hello", 0.0, 0.5), new WordTiming("world", 0.5, 1.0)]),
            CreatedAtUtc = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero)
        };
    }

    private void SeedGet(FillerAudioCacheEntity entity)
    {
        _tableClient.GetEntityAsync<FillerAudioCacheEntity>(
                Arg.Is(entity.PartitionKey), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedGetNotFound(string partitionKey, string rowKey)
    {
        _tableClient.GetEntityAsync<FillerAudioCacheEntity>(
                Arg.Is(partitionKey), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    private void SeedQuery(params FillerAudioCacheEntity[] entities)
    {
        _tableClient.QueryAsync<FillerAudioCacheEntity>(
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var filter = callInfo.ArgAt<string?>(0);
                var matched = entities.Where(e =>
                    filter != null && filter.Contains($"'{e.PartitionKey}'")).ToList();
                return TableClientTestHelper.ToAsyncPageable(matched);
            });
    }

    // ── GetAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ExistingClip_ReturnsSuccessWithClip()
    {
        var timings = new[] { new WordTiming("hello", 0.0, 0.5) };
        var entity = CreateEntity("stmt-1", "voice1_settings1_v3", 3.2, timings);
        SeedGet(entity);

        var result = await _sut.GetAsync("stmt-1", "voice1_settings1_v3");

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        var clip = result.Value.Value;
        clip.FillerStatementId.ShouldBe("stmt-1");
        clip.CacheKey.ShouldBe("voice1_settings1_v3");
        clip.DurationSeconds.ShouldBe(3.2);
        clip.WordTimings.Count.ShouldBe(1);
        clip.WordTimings[0].Word.ShouldBe("hello");
        clip.WordTimings[0].StartSeconds.ShouldBe(0.0);
        clip.WordTimings[0].EndSeconds.ShouldBe(0.5);
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNone()
    {
        SeedGetNotFound("missing-stmt", "voice1_settings1_v3");

        var result = await _sut.GetAsync("missing-stmt", "voice1_settings1_v3");

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
    }

    // ── SaveAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_CallsUpsertWithCorrectEntity()
    {
        var clip = new FillerAudioClip(
            "stmt-1", "voice1_settings1_v3", 2.5,
            [new WordTiming("hi", 0.0, 0.3)],
            new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc));
        _tableClient.UpsertEntityAsync(
                Arg.Any<FillerAudioCacheEntity>(),
                Arg.Any<TableUpdateMode>(),
                Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.StubResponse());

        var result = await _sut.SaveAsync(clip);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<FillerAudioCacheEntity>(e =>
                e.PartitionKey == "stmt-1" &&
                e.RowKey == "voice1_settings1_v3" &&
                e.DurationSeconds == 2.5 &&
                e.WordTimingsJson.Contains("hi")),
            Arg.Is(TableUpdateMode.Replace),
            Arg.Any<CancellationToken>());
    }

    // ── DeleteAllForStatementAsync ────────────────────────────────────

    [Fact]
    public async Task DeleteAllForStatementAsync_DeletesMatchingPartitionRowsOnly()
    {
        var e1 = CreateEntity("stmt-1", "key-a", 1.0);
        var e2 = CreateEntity("stmt-1", "key-b", 2.0);
        var e3 = CreateEntity("stmt-2", "key-c", 3.0); // different partition
        SeedQuery(e1, e2, e3);

        var result = await _sut.DeleteAllForStatementAsync("stmt-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain("key-a");
        result.Value.ShouldContain("key-b");

        await _tableClient.Received(2).DeleteEntityAsync(
            Arg.Is("stmt-1"), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
        await _tableClient.DidNotReceive().DeleteEntityAsync(
            Arg.Is("stmt-2"), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAllForStatementAsync_NoRows_ReturnsEmptyList()
    {
        SeedQuery(); // empty — no entities match any partition

        var result = await _sut.DeleteAllForStatementAsync("stmt-empty");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await _tableClient.DidNotReceive().DeleteEntityAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }
}
