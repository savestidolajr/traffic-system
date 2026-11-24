using System.Net;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class ActivityHttpTriggerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);

    private readonly IActivityLogRepository _activityLog = Substitute.For<IActivityLogRepository>();
    private readonly ActivityHttpTrigger _sut;

    public ActivityHttpTriggerTests()
    {
        _sut = new ActivityHttpTrigger(_activityLog, new FakeTimeProvider(FixedNow));
        _activityLog.QueryAsync(Arg.Any<ActivityLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ActivityLogEntry>>.Success(Array.Empty<ActivityLogEntry>()));
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static ActivityLogEntry Entry(string resource = "schedules", string? entityId = "adelaide") => new(
        DateTime.UtcNow, "Jamie Operator", "op-123", "PUT", "/api/schedules/adelaide",
        resource, entityId, 200, """{"voiceProfileId":"vp-new"}""");

    [Fact]
    public async Task List_NoRangeGiven_DefaultsToLast180DaysEndingNow()
    {
        await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/activity"), Ctx());

        await _activityLog.Received(1).QueryAsync(
            Arg.Is<ActivityLogQuery>(q =>
                q.ToUtc == FixedNow &&
                q.FromUtc == FixedNow - TimeSpan.FromDays(180)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_ContinuationTimestamp_IsPassedAsExclusiveUpperBound()
    {
        var cursor = new DateTimeOffset(2026, 7, 6, 12, 30, 0, TimeSpan.Zero);
        await _sut.List(
            MockHttpRequestData.CreateGet(
                $"http://localhost/api/activity?to={Uri.EscapeDataString($"{cursor:O}")}&limit=2"), Ctx());

        await _activityLog.Received(1).QueryAsync(
            Arg.Is<ActivityLogQuery>(q => q.ToUtc == cursor && q.Limit == 2),
            Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task List_CompositeContinuation_PassesRowKeyTieBreaker()
    {
        await _sut.List(
            MockHttpRequestData.CreateGet(
                "http://localhost/api/activity?to=2026-07-06T12:30:00%2B00:00%7Crow-key-2&limit=2"), Ctx());

        await _activityLog.Received(1).QueryAsync(
            Arg.Is<ActivityLogQuery>(q =>
                q.ToUtc == new DateTimeOffset(2026, 7, 6, 12, 30, 0, TimeSpan.Zero)
                && q.CursorRowKey == "row-key-2"
                && q.Limit == 2),
            Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task List_ToBeforeFrom_Returns400()
    {
        var resp = await _sut.List(
            MockHttpRequestData.CreateGet(
                "http://localhost/api/activity?from=2026-06-10&to=2026-06-01"), Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_RangeOverMaxDays_Returns400()
    {
        var resp = await _sut.List(
            MockHttpRequestData.CreateGet("http://localhost/api/activity?from=2020-01-01&to=2026-07-01"), Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_ExactlyMaxRangeDays_IsAccepted()
    {
        var from = FixedNow - TimeSpan.FromDays(366);
        var response = await _sut.List(
            MockHttpRequestData.CreateGet(
                "http://localhost/api/activity?from=" +
                Uri.EscapeDataString($"{from:O}") +
                "&to=" + Uri.EscapeDataString($"{FixedNow:O}")), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _activityLog.Received(1).QueryAsync(
            Arg.Is<ActivityLogQuery>(q => q.FromUtc == from && q.ToUtc == FixedNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_LimitOverMax_ClampsTo1000()
    {
        await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/activity?limit=5000"), Ctx());

        await _activityLog.Received(1).QueryAsync(
            Arg.Is<ActivityLogQuery>(q => q.Limit == 1000), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_HappyPath_ReturnsMappedDtoList()
    {
        _activityLog.QueryAsync(Arg.Any<ActivityLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ActivityLogEntry>>.Success(new[] { Entry() }));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.List(MockHttpRequestData.CreateGet(
                "http://localhost/api/activity?resource=schedules&entityId=adelaide"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Jamie Operator");
        body.ShouldContain("schedules");
        body.ShouldContain("adelaide");
        body.ShouldContain("\"Total\":1");
    }

    [Fact]
    public async Task List_FullPage_ReturnsHasMoreTrue()
    {
        // A page exactly the size of the requested limit implies more entries may lie further
        // back; the client re-queries with to = the oldest item's AtUtc to page backward.
        _activityLog.QueryAsync(Arg.Any<ActivityLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ActivityLogEntry>>.Success(new[] { Entry(), Entry() }));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/activity?limit=2"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":2");
        body.ShouldContain("\"HasMore\":true");
    }

    [Fact]
    public async Task List_ShortPage_ReturnsHasMoreFalse()
    {
        // Fewer entries than the requested limit means nothing older exists in range: exhausted.
        _activityLog.QueryAsync(Arg.Any<ActivityLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ActivityLogEntry>>.Success(new[] { Entry() }));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.List(MockHttpRequestData.CreateGet("http://localhost/api/activity?limit=5"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":1");
        body.ShouldContain("\"HasMore\":false");
    }

}
