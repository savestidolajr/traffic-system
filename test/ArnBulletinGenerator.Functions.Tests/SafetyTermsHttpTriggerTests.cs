using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class SafetyTermsHttpTriggerTests
{
    private static readonly DateTime OriginalCreated = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

    private readonly ISafetyTermRepository _repository = Substitute.For<ISafetyTermRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private readonly SafetyTermsHttpTrigger _sut;

    public SafetyTermsHttpTriggerTests()
    {
        _sut = new SafetyTermsHttpTrigger(
            _repository, _scheduleRepository, _timeProvider, Substitute.For<ILogger<SafetyTermsHttpTrigger>>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    // See GuidanceHttpTriggerTests.CreatePutRequest: CreatePut<T> emits numeric enums, but
    // HttpRequestParser's JsonSerializationDefaults.Request rejects them (string-only enums),
    // so a plain CreatePut(request) 400s before any Upsert logic runs.
    private static HttpRequestData CreatePutRequest<T>(T body, string url)
        => MockHttpRequestData.CreatePutRaw(JsonSerializer.Serialize(body, JsonSerializationDefaults.Request), url);

    private static MarketSchedule Schedule(string marketId = "sydney")
        => MarketSchedule.Create(marketId, $"{marketId} Traffic", "3AW", "Australia/Sydney", "*/15 * * * *", true,
            CoverageArea.Create(-33.8688, 151.2093, 5.0).Value, "voice-123", "sydney/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;

    // === H4: CreatedAtUtc preservation on update / dedup-reuse ===

    [Fact]
    public async Task Upsert_UpdateExisting_ByExplicitId_PreservesCreatedAtUtc_AdvancesUpdatedAtUtc()
    {
        var existing = new SafetyTerm("s1", SafetyTermKind.Banned, SafetyScope.Market, "sydney", "bad word",
            "alice", OriginalCreated, OriginalCreated);
        _scheduleRepository.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule()));
        _repository.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SafetyTerm>>.Success([existing]));

        SafetyTerm? captured = null;
        _repository.UpsertAsync(Arg.Do<SafetyTerm>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertSafetyTermRequest(
            SafetyTermKind.Banned, SafetyScope.Market, "sydney", "worse word", Id: "s1");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Upsert(CreatePutRequest(request, "http://localhost/api/safety/terms"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.CreatedAtUtc.ShouldBe(OriginalCreated);
        captured.UpdatedAtUtc.ShouldBe(Now);
        body.ShouldContain("worse word");
    }

    [Fact]
    public async Task Upsert_DedupReuse_PreservesCreatedAtUtc_AdvancesUpdatedAtUtc()
    {
        var existing = new SafetyTerm("s2", SafetyTermKind.Allowed, SafetyScope.Global, string.Empty, "Bankstown",
            "alice", OriginalCreated, OriginalCreated);
        _repository.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SafetyTerm>>.Success([existing]));

        SafetyTerm? captured = null;
        _repository.UpsertAsync(Arg.Do<SafetyTerm>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Same kind/scope/scopeId/term (case-insensitive), no Id supplied — hits the dedup path.
        var request = new UpsertSafetyTermRequest(SafetyTermKind.Allowed, SafetyScope.Global, null, "bankstown");

        (var status, string _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Upsert(CreatePutRequest(request, "http://localhost/api/safety/terms"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Id.ShouldBe("s2");
        captured.CreatedAtUtc.ShouldBe(OriginalCreated);
        captured.UpdatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task Upsert_NewTerm_SetsCreatedAndUpdatedToNow()
    {
        _repository.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SafetyTerm>>.Success([]));

        SafetyTerm? captured = null;
        _repository.UpsertAsync(Arg.Do<SafetyTerm>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertSafetyTermRequest(SafetyTermKind.Banned, SafetyScope.Global, null, "brand new term");

        (var status, string _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Upsert(CreatePutRequest(request, "http://localhost/api/safety/terms"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.CreatedAtUtc.ShouldBe(Now);
        captured.UpdatedAtUtc.ShouldBe(Now);
    }

    // === L12: market-existence guard ===

    [Fact]
    public async Task Upsert_UnknownMarket_ReturnsNotFound()
    {
        _scheduleRepository.GetScheduleAsync("nowhere", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Schedule not found for market 'nowhere'."));

        var request = new UpsertSafetyTermRequest(SafetyTermKind.Banned, SafetyScope.Market, "nowhere", "some term");

        (var status, string _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Upsert(CreatePutRequest(request, "http://localhost/api/safety/terms"), Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<SafetyTerm>(), Arg.Any<CancellationToken>());
    }
}
