using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageActivityLogRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageActivityLogRepositoryTests
{
    private readonly ILogger<TableStorageActivityLogRepository> _logger =
        Substitute.For<ILogger<TableStorageActivityLogRepository>>();
    private readonly TableStorageActivityLogRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageActivityLogRepositoryTests()
    {
        _sut = new TableStorageActivityLogRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ActivityLogEntity CreateEntity(DateTime atUtc, string actorName = "Jamie Operator",
        string resource = "schedules", string? entityId = "adelaide")
    {
        return new ActivityLogEntity
        {
            PartitionKey = atUtc.ToString("yyyy-MM-dd"),
            RowKey = Guid.NewGuid().ToString("N"),
            AtUtc = atUtc,
            ActorName = actorName,
            ActorObjectId = "op-123",
            Method = "PUT",
            Path = "/api/schedules/adelaide",
            Resource = resource,
            EntityId = entityId,
            StatusCode = 200,
            RequestBodyJson = """{"voiceProfileId":"vp-new"}"""
        };
    }

    private void SeedQuery(params ActivityLogEntity[] entities)
    {
        _tableClient.QueryAsync<ActivityLogEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    // ── QueryAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_PagingCursor_FilterUsesExactAtUtcExclusiveBound_NotJustDayBucket()
    {
        // Regression: paging backward ("load older") re-issues the query with ToUtc set to the
        // oldest previously-returned entry's exact AtUtc. Before this fix, the filter only
        // bounded the day-partition bucket (PartitionKey le toKey), so re-querying with that
        // cursor would re-return the rest of the SAME day's entries instead of advancing past
        // them. The fix adds a precise row-level "AtUtc lt {ToUtc}" bound alongside the
        // day-bucket prefilter — this is the exact cursor-exclusivity invariant paging depends on.
        string? capturedFilter = null;
        _tableClient.QueryAsync<ActivityLogEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<ActivityLogEntity>());

        var cursor = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero); // oldest entry from the prior page
        var query = new ActivityLogQuery(cursor.AddDays(-180), cursor, null, null, 200);

        var result = await _sut.QueryAsync(query);

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        // The coarse day-bucket prefilter is still present (cheap index-friendly narrowing)...
        capturedFilter.ShouldContain("PartitionKey le '2026-07-07'");
        // ...but so is the exact-timestamp exclusive bound the fix added. Without it, an entry
        // at exactly 10:00:00 and any later-same-day entry would wrongly resurface on the next page.
        capturedFilter.ShouldContain("AtUtc lt datetime'2026-07-07T10:00:00Z'");
    }

    [Fact]
    public async Task QueryAsync_CompositeCursor_AdmitsOnlyUntouchedEqualTimestampRows()
    {
        string? capturedFilter = null;
        _tableClient.QueryAsync<ActivityLogEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<ActivityLogEntity>());

        var cursor = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var result = await _sut.QueryAsync(
            new ActivityLogQuery(cursor.AddDays(-1), cursor, null, null, 2, "row-key-1"));

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldContain("AtUtc lt datetime'2026-07-07T10:00:00Z'");
        capturedFilter.ShouldContain("AtUtc eq datetime'2026-07-07T10:00:00Z'");
        capturedFilter.ShouldContain("RowKey gt 'row-key-1'");
    }

    [Fact]
    public async Task QueryAsync_WithResourceAndEntityIdFilters_AppendsBothClauses()
    {
        string? capturedFilter = null;
        _tableClient.QueryAsync<ActivityLogEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<ActivityLogEntity>());

        var query = new ActivityLogQuery(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, "schedules", "adelaide", 200);

        var result = await _sut.QueryAsync(query);

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldContain("Resource eq 'schedules'");
        capturedFilter.ShouldContain("EntityId eq 'adelaide'");
    }

    [Fact]
    public async Task QueryAsync_ReturnsMappedEntries_OrderedByAtUtcDescendingAndCappedAtLimit()
    {
        var now = DateTime.UtcNow;
        var oldest = CreateEntity(now.AddMinutes(-30));
        var mid = CreateEntity(now.AddMinutes(-20));
        var newest = CreateEntity(now.AddMinutes(-10));
        // Seeded out of order and split across what would be separate day-partitions in
        // production — QueryAsync must re-sort across them rather than trust row order.
        SeedQuery(oldest, newest, mid);

        var result = await _sut.QueryAsync(new ActivityLogQuery(now.AddDays(-1), now, null, null, 2));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Select(e => e.AtUtc).ShouldBe(new[] { newest.AtUtc, mid.AtUtc });
    }

    [Fact]
    public async Task QueryAsync_EmptyTable_ReturnsEmptyList()
    {
        SeedQuery();

        var result = await _sut.QueryAsync(
            new ActivityLogQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, null, null, 200));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task QueryAsync_StorageError_ReturnsFailure()
    {
        _tableClient.QueryAsync<ActivityLogEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(500, "Internal Server Error"));

        var result = await _sut.QueryAsync(
            new ActivityLogQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, null, null, 200));

        result.IsFailure.ShouldBeTrue();
    }
}
