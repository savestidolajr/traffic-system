using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageFutureLessonRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageFutureLessonRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageFutureLessonRepository _sut;

    public TableStorageFutureLessonRepositoryTests()
    {
        _sut = new TableStorageFutureLessonRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            new FixedTimeProvider(Now),
            Substitute.For<ILogger<TableStorageFutureLessonRepository>>());
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Fact]
    public async Task AddAsync_NewLesson_CreatesStableStationRow()
    {
        var absent = Substitute.For<NullableResponse<FutureLessonEntity>>();
        absent.HasValue.Returns(false);
        _tableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(absent);
        var entry = Lesson();

        var result = await _sut.AddAsync(entry);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).AddEntityAsync(
            Arg.Is<FutureLessonEntity>(entity =>
                entity.PartitionKey == "kiis 101.1" &&
                entity.RowKey == entry.Id &&
                entity.Category == "PriorityOrdering" &&
                entity.OccurrenceCount == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_RepeatedLesson_MergesOccurrenceAndRefreshesExpiry()
    {
        var existing = Entity(
            id: "stable-id",
            lastSeen: Now.AddDays(-1),
            expires: Now.AddDays(10),
            occurrenceCount: 2);
        // A DIFFERENT job re-teaching the same lesson is genuine reinforcement.
        existing.SourceJobId = "job-earlier";
        var response = Substitute.For<NullableResponse<FutureLessonEntity>>();
        response.HasValue.Returns(true);
        response.Value.Returns(existing);
        _tableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        var incoming = Lesson(
            id: "stable-id",
            lastSeen: Now.UtcDateTime,
            expires: Now.AddDays(90).UtcDateTime);

        var result = await _sut.AddAsync(incoming);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<FutureLessonEntity>(entity =>
                entity.OccurrenceCount == 3 &&
                entity.SourceJobId == "job-1" &&
                entity.ExpiresAtUtc == Now.AddDays(90)),
            Arg.Any<ETag>(),
            TableUpdateMode.Replace,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecentAsync_DropsExpiredAndOrdersNewestFirst()
    {
        var expired = Entity("expired", Now.AddDays(-3), Now.AddMinutes(-1));
        var older = Entity("older", Now.AddDays(-2), Now.AddDays(10));
        var newest = Entity("newest", Now.AddDays(-1), Now.AddDays(20));
        _tableClient.QueryAsync<FutureLessonEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable([expired, older, newest]));

        var result = await _sut.GetRecentAsync("KIIS 101.1", 8);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(item => item.Id).ShouldBe(["newest", "older"]);
    }

    [Fact]
    public async Task DeleteAsync_MissingLesson_IsIdempotent()
    {
        _tableClient.DeleteEntityAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>())
            .Returns<Task<Response>>(_ => throw new RequestFailedException(404, "missing"));

        var result = await _sut.DeleteAsync("KIIS 101.1", "missing-id");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AddAsync_SameSourceJob_RefreshesWithoutFakingReinforcement()
    {
        // Queue redelivery of the same published job must not inflate the evidence count.
        var existing = Entity(
            id: "stable-id",
            lastSeen: Now.AddDays(-1),
            expires: Now.AddDays(10),
            occurrenceCount: 2);
        var response = Substitute.For<NullableResponse<FutureLessonEntity>>();
        response.HasValue.Returns(true);
        response.Value.Returns(existing);
        _tableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        var incoming = Lesson(id: "stable-id", expires: Now.AddDays(90).UtcDateTime);

        var result = await _sut.AddAsync(incoming);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<FutureLessonEntity>(entity =>
                entity.OccurrenceCount == 2 &&
                entity.ExpiresAtUtc == Now.AddDays(90)),
            Arg.Any<ETag>(),
            TableUpdateMode.Replace,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_Merge_NeverDowngradesActiveStatus()
    {
        var existing = Entity(
            id: "stable-id",
            lastSeen: Now.AddDays(-1),
            expires: Now.AddDays(10));
        existing.SourceJobId = "job-earlier";
        existing.Status = "Active";
        var response = Substitute.For<NullableResponse<FutureLessonEntity>>();
        response.HasValue.Returns(true);
        response.Value.Returns(existing);
        _tableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        var incoming = Lesson(id: "stable-id") with { Status = FutureLessonStatus.Candidate };

        var result = await _sut.AddAsync(incoming);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<FutureLessonEntity>(entity => entity.Status == "Active"),
            Arg.Any<ETag>(),
            TableUpdateMode.Replace,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecentAsync_LegacyRowsWithoutStatusColumns_MapToActiveOperator()
    {
        var legacy = Entity("legacy", Now.AddDays(-1), Now.AddDays(20));
        legacy.Status = null;
        legacy.Origin = null;
        _tableClient.QueryAsync<FutureLessonEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable([legacy]));

        var result = await _sut.GetRecentAsync("KIIS 101.1", 8);

        result.IsSuccess.ShouldBeTrue();
        var lesson = result.Value.Single();
        lesson.Status.ShouldBe(FutureLessonStatus.Active);
        lesson.Origin.ShouldBe(FutureLessonOrigin.Operator);
        lesson.ConflictWithLessonId.ShouldBeNull();
    }

    [Fact]
    public async Task SetStatusAsync_ExistingLesson_WritesNewStatus()
    {
        var existing = Entity("stable-id", Now.AddDays(-1), Now.AddDays(20));
        existing.Status = "Candidate";
        var response = Substitute.For<NullableResponse<FutureLessonEntity>>();
        response.HasValue.Returns(true);
        response.Value.Returns(existing);
        _tableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _sut.SetStatusAsync("KIIS 101.1", "stable-id", FutureLessonStatus.Active);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<FutureLessonEntity>(entity => entity.Status == "Active"),
            Arg.Any<ETag>(),
            TableUpdateMode.Replace,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetStatusAsync_MissingLesson_IsNoOpSuccess()
    {
        var absent = Substitute.For<NullableResponse<FutureLessonEntity>>();
        absent.HasValue.Returns(false);
        _tableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(absent);

        var result = await _sut.SetStatusAsync("KIIS 101.1", "missing", FutureLessonStatus.Active);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.DidNotReceive().UpdateEntityAsync(
            Arg.Any<FutureLessonEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(),
            Arg.Any<CancellationToken>());
    }

    private static FutureLessonEntry Lesson(
        string id = "stable-id",
        DateTime? lastSeen = null,
        DateTime? expires = null) =>
        new(
            id,
            "KIIS 101.1",
            "job-1",
            FutureLessonCategory.PriorityOrdering,
            "Lead with a full motorway closure before ordinary congestion.",
            Now.UtcDateTime,
            lastSeen ?? Now.UtcDateTime,
            1,
            expires ?? Now.AddDays(90).UtcDateTime);

    private static FutureLessonEntity Entity(
        string id,
        DateTimeOffset lastSeen,
        DateTimeOffset expires,
        int occurrenceCount = 1) =>
        new()
        {
            PartitionKey = "kiis 101.1",
            RowKey = id,
            SourceJobId = "job-1",
            Category = "PriorityOrdering",
            Text = "Lead with a full motorway closure before ordinary congestion.",
            CreatedAtUtc = Now,
            LastSeenAtUtc = lastSeen,
            OccurrenceCount = occurrenceCount,
            ExpiresAtUtc = expires,
            ETag = new ETag("etag")
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
