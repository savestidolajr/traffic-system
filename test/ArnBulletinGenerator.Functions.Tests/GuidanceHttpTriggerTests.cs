using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class GuidanceHttpTriggerTests
{
    private static readonly DateTime OriginalCreated = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

    private readonly IGuidanceRepository _repository = Substitute.For<IGuidanceRepository>();
    private readonly IFutureLessonRepository _futureLessonRepository = Substitute.For<IFutureLessonRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private readonly GuidanceHttpTrigger _sut;

    public GuidanceHttpTriggerTests()
    {
        _sut = new GuidanceHttpTrigger(
            _repository, _futureLessonRepository, _scheduleRepository, _marketAccess, _timeProvider,
            Substitute.For<ILogger<GuidanceHttpTrigger>>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    // MockHttpRequestData.CreatePut<T> serializes with default JsonSerializerOptions, which
    // emits enums as numbers. HttpRequestParser deserializes with JsonSerializationDefaults
    // .Request, whose JsonStringEnumConverter(allowIntegerValues: false) rejects numeric enums
    // — so a plain CreatePut(request) 400s before Validate() ever runs. Serialize with the same
    // options the parser expects instead (mirrors the raw-JSON pattern used elsewhere for DTOs
    // with enum properties, e.g. ActivityLoggingMiddlewareTests' safety/terms bodies).
    private static HttpRequestData CreatePutRequest<T>(T body, string url)
        => MockHttpRequestData.CreatePutRaw(JsonSerializer.Serialize(body, JsonSerializationDefaults.Request), url);

    private static MarketSchedule Schedule(string marketId = "sydney")
        => MarketSchedule.Create(marketId, $"{marketId} Traffic", "3AW", "Australia/Sydney", "*/15 * * * *", true,
            CoverageArea.Create(-33.8688, 151.2093, 5.0).Value, "voice-123", "sydney/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;

    private static GuidanceEntry ExistingEntry(string id = "g1", string market = "sydney")
        => new(id, GuidanceScope.Market, market, GuidanceType.Style, "Old text", true, "alice",
            OriginalCreated, OriginalCreated, Option<string>.Some(GuidanceEntry.ManualSource));

    // === H4: CreatedAtUtc preservation on update ===

    [Fact]
    public async Task UpsertMarketGuidance_UpdateExisting_PreservesCreatedAtUtc_AdvancesUpdatedAtUtc()
    {
        _scheduleRepository.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule()));
        _repository.ListAsync(GuidanceScope.Market, "sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([ExistingEntry()]));

        GuidanceEntry? captured = null;
        _repository.UpsertAsync(Arg.Do<GuidanceEntry>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertGuidanceRequest(
            GuidanceScope.Market, "sydney", GuidanceType.Style, "New text", true, null, "g1");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertMarketGuidance(
                CreatePutRequest(request, "http://localhost/api/markets/sydney/guidance"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.CreatedAtUtc.ShouldBe(OriginalCreated);
        captured.UpdatedAtUtc.ShouldBe(Now);
        body.ShouldContain("New text");
    }

    [Fact]
    public async Task UpsertGuidance_UpdateExisting_PreservesCreatedAtUtc_AdvancesUpdatedAtUtc()
    {
        _repository.ListAsync(GuidanceScope.Global, string.Empty, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success(
                [new GuidanceEntry("g2", GuidanceScope.Global, string.Empty, GuidanceType.Style, "Old text", true,
                    "alice", OriginalCreated, OriginalCreated, Option<string>.Some(GuidanceEntry.ManualSource))]));

        GuidanceEntry? captured = null;
        _repository.UpsertAsync(Arg.Do<GuidanceEntry>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertGuidanceRequest(
            GuidanceScope.Global, null, GuidanceType.Style, "New text", true, null, "g2");

        // Global writes on the unscoped route are re-checked for Admin in-handler (the route
        // itself is Operator-reachable since the scope lives in the payload).
        var req = CreatePutRequest(request, "http://localhost/api/guidance");
        req.Headers.Add(ClientPrincipal.HeaderName, AdminPrincipal());

        (var status, string _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(req, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.CreatedAtUtc.ShouldBe(OriginalCreated);
        captured.UpdatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task UpsertMarketGuidance_NewEntry_SetsCreatedAndUpdatedToNow()
    {
        _scheduleRepository.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule()));

        GuidanceEntry? captured = null;
        _repository.UpsertAsync(Arg.Do<GuidanceEntry>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertGuidanceRequest(GuidanceScope.Market, "sydney", GuidanceType.Style, "Brand new");

        (var status, string _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertMarketGuidance(
                CreatePutRequest(request, "http://localhost/api/markets/sydney/guidance"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.CreatedAtUtc.ShouldBe(Now);
        captured.UpdatedAtUtc.ShouldBe(Now);
    }

    // === L12: market-existence guard ===

    [Fact]
    public async Task UpsertMarketGuidance_UnknownMarket_ReturnsNotFound()
    {
        _scheduleRepository.GetScheduleAsync("nowhere", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Schedule not found for market 'nowhere'."));

        var request = new UpsertGuidanceRequest(GuidanceScope.Market, "nowhere", GuidanceType.Style, "Some text");

        (var status, string _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertMarketGuidance(
                CreatePutRequest(request, "http://localhost/api/markets/nowhere/guidance"),
                "nowhere", Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    // === F2 (bug-bash 2026-08-05): cross-scope upsert must enforce the schedule catalogue ===

    [Fact]
    public async Task UpsertGuidance_MarketScopeUnknownMarket_ReturnsNotFound_IdenticallyToMarketRoute()
    {
        _scheduleRepository.GetScheduleAsync("zz-bb-nope", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure(ErrorMessages.Schedule.NotFound));

        var request = new UpsertGuidanceRequest(GuidanceScope.Market, "zz-bb-nope", GuidanceType.Style, "zz-bb orphan");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(
                CreatePutRequest(request, "http://localhost/api/guidance"), Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain(ErrorMessages.Schedule.NotFound);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertGuidance_MarketScopeKnownMarket_StillSucceeds()
    {
        _scheduleRepository.GetScheduleAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule()));
        _repository.UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertGuidanceRequest(GuidanceScope.Market, "sydney", GuidanceType.Style, "Some text");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(
                CreatePutRequest(request, "http://localhost/api/guidance"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpsertGuidance_StationScopeUnknownStation_ReturnsNotFound()
    {
        // The catalogue owns Station "3AW" only — "No Such FM" must be rejected like an
        // unknown market, or the directive would never compose (GuidanceComposer matches
        // ScopeId against schedule.Station).
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule()]));

        var request = new UpsertGuidanceRequest(GuidanceScope.Station, "No Such FM", GuidanceType.Style, "Some text");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(
                CreatePutRequest(request, "http://localhost/api/guidance"), Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertGuidance_StationScopeKnownStation_StillSucceeds()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule()]));
        _repository.UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertGuidanceRequest(GuidanceScope.Station, "3AW", GuidanceType.Style, "Some text");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(
                CreatePutRequest(request, "http://localhost/api/guidance"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }

    // === F1 (bug-bash 2026-08-05): delete must 404 when nothing matched ===

    [Fact]
    public async Task DeleteGuidance_MissingRow_ReturnsNotFound()
    {
        _repository.DeleteAsync(GuidanceScope.Global, string.Empty, "g-nope", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        // Global deletes on the unscoped route are re-checked for Admin in-handler.
        var req = MockHttpRequestData.CreateDelete("http://localhost/api/guidance/g-nope?scope=Global");
        req.Headers.Add(ClientPrincipal.HeaderName, AdminPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteGuidance(req, "g-nope", Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain(ErrorMessages.Guidance.NotFound);
    }

    [Fact]
    public async Task DeleteGuidance_WrongScopeRow_ReturnsNotFound()
    {
        // A row that lives in the Global partition deleted with scope=Market&scopeId=portlincoln
        // must not report success — the repository reports false, the route answers 404.
        _repository.DeleteAsync(GuidanceScope.Market, "portlincoln", "56d07a92", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteGuidance(
                MockHttpRequestData.CreateDelete(
                    "http://localhost/api/guidance/56d07a92?scope=Market&scopeId=portlincoln"),
                "56d07a92", Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteGuidance_ExistingRow_ReturnsSuccessShape()
    {
        _repository.DeleteAsync(GuidanceScope.Global, string.Empty, "g1", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var req = MockHttpRequestData.CreateDelete("http://localhost/api/guidance/g1?scope=Global");
        req.Headers.Add(ClientPrincipal.HeaderName, AdminPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteGuidance(req, "g1", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Deleted\":\"g1\"");
    }

    [Fact]
    public async Task DeleteMarketGuidance_MissingRow_ReturnsNotFound()
    {
        _repository.DeleteAsync(GuidanceScope.Market, "sydney", "g-nope", Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteMarketGuidance(
                MockHttpRequestData.CreateDelete("http://localhost/api/markets/sydney/guidance/g-nope"),
                "sydney", "g-nope", Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain(ErrorMessages.Guidance.NotFound);
    }

    // === RGP-1: the unscoped PUT/DELETE routes carry the scope-dependent role guard ===

    /// <summary>Base64 X-MS-CLIENT-PRINCIPAL for an Operator caller (SWA shape).</summary>
    private static string OperatorPrincipal()
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            """{"identityProvider":"aad","userId":"op-oid","userDetails":"op@arn.com.au","userRoles":["authenticated","Operator"]}"""));

    [Fact]
    public async Task UpsertGuidance_GlobalScopeByOperator_ReturnsForbidden()
    {
        // RouteRoleMatrix admits any Operator to PUT /guidance; the handler re-checks Admin
        // for Global payloads because the scope lives in the body, not the route.
        var request = new UpsertGuidanceRequest(GuidanceScope.Global, null, GuidanceType.Style, "Global rule");
        var req = CreatePutRequest(request, "http://localhost/api/guidance");
        req.Headers.Add(ClientPrincipal.HeaderName, OperatorPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(req, Ctx()));

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("global guidance requires Admin");
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteGuidance_GlobalScopeByOperator_ReturnsForbidden()
    {
        var req = MockHttpRequestData.CreateDelete("http://localhost/api/guidance/g1?scope=Global");
        req.Headers.Add(ClientPrincipal.HeaderName, OperatorPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteGuidance(req, "g1", Ctx()));

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("global guidance requires Admin");
        await _repository.DidNotReceive().DeleteAsync(
            Arg.Any<GuidanceScope>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertGuidance_MarketScopeUnassignedMarket_ReturnsForbidden()
    {
        // Operator scoped to "sydney" only; the directive targets "portlincoln".
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "sydney" }));

        var request = new UpsertGuidanceRequest(GuidanceScope.Market, "portlincoln", GuidanceType.Style, "Other market rule");
        var req = CreatePutRequest(request, "http://localhost/api/guidance");
        req.Headers.Add(ClientPrincipal.HeaderName, OperatorPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(req, Ctx()));

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("Forbidden: you are not assigned to market");
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertGuidance_StationScopeUnassignedMarket_ReturnsForbidden()
    {
        // Station "3AW" belongs to portlincoln (catalogue); the operator holds only sydney.
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("portlincoln")]));
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "sydney" }));

        var request = new UpsertGuidanceRequest(GuidanceScope.Station, "3AW", GuidanceType.Style, "Station rule");
        var req = CreatePutRequest(request, "http://localhost/api/guidance");
        req.Headers.Add(ClientPrincipal.HeaderName, OperatorPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(req, Ctx()));

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("Forbidden: you are not assigned to market");
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertGuidance_StationScopeAssignedMarket_Succeeds()
    {
        // Station "3AW" belongs to sydney (catalogue) and the operator holds sydney.
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("sydney")]));
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "sydney" }));
        _repository.UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertGuidanceRequest(GuidanceScope.Station, "3AW", GuidanceType.Style, "Station rule");
        var req = CreatePutRequest(request, "http://localhost/api/guidance");
        req.Headers.Add(ClientPrincipal.HeaderName, OperatorPrincipal());

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.UpsertGuidance(req, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteGuidance_StationScopeUnassignedMarket_ReturnsForbidden()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("portlincoln")]));
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "sydney" }));

        var req = MockHttpRequestData.CreateDelete(
            "http://localhost/api/guidance/st-1?scope=Station&scopeId=3AW");
        req.Headers.Add(ClientPrincipal.HeaderName, OperatorPrincipal());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteGuidance(req, "st-1", Ctx()));

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("Forbidden: you are not assigned to market");
        await _repository.DidNotReceive().DeleteAsync(
            Arg.Any<GuidanceScope>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // === F3 (bug-bash 2026-08-05): promote validates the candidate and is idempotent ===

    private static HttpRequestData CreatePostRequest<T>(T body, string url)
        => MockHttpRequestData.CreatePostRaw(JsonSerializer.Serialize(body, JsonSerializationDefaults.Request), url);

    private static FutureLessonEntry Candidate(string jobId = "job-42", string text = "Lead with the closure")
        => new(
            FutureLessonPolicy.CreateId("3AW", FutureLessonCategory.PriorityOrdering, text),
            "3AW",
            jobId,
            FutureLessonCategory.PriorityOrdering,
            text,
            Now,
            Now,
            1,
            Now.AddDays(90));

    [Fact]
    public async Task PromoteGuidance_MissingJobId_ReturnsBadRequest()
    {
        var request = new PromoteGuidanceRequest(GuidanceScope.Market, "portlincoln", GuidanceType.Editorial, "Some text");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PromoteGuidance(
                CreatePostRequest(request, "http://localhost/api/guidance/promote"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
        await _futureLessonRepository.DidNotReceive().GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteGuidance_GhostJobId_ReturnsNotFound()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("portlincoln")]));
        _futureLessonRepository.GetRecentAsync("3AW", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([]));

        var request = new PromoteGuidanceRequest(GuidanceScope.Market, "portlincoln", GuidanceType.Editorial,
            "Some text", "zz-bb-ghost-job");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PromoteGuidance(
                CreatePostRequest(request, "http://localhost/api/guidance/promote"), Ctx()));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain(ErrorMessages.Guidance.CandidateNotFound);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteGuidance_ValidCandidate_CreatesDirective_WithPromotedSource()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("portlincoln")]));
        _futureLessonRepository.GetRecentAsync("3AW", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([Candidate()]));
        _repository.ListAsync(GuidanceScope.Global, string.Empty, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _repository.ListAsync(GuidanceScope.Market, "portlincoln", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _repository.ListAsync(GuidanceScope.Station, "3AW", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));

        GuidanceEntry? captured = null;
        _repository.UpsertAsync(Arg.Do<GuidanceEntry>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new PromoteGuidanceRequest(GuidanceScope.Market, "portlincoln", GuidanceType.Editorial,
            "Lead with the closure", "job-42");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PromoteGuidance(
                CreatePostRequest(request, "http://localhost/api/guidance/promote"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Scope.ShouldBe(GuidanceScope.Market);
        captured.ScopeId.ShouldBe("portlincoln");
        captured.Source.ShouldBe(Option<string>.Some(GuidanceEntry.PromotedSource("job-42")));
    }

    [Fact]
    public async Task PromoteGuidance_SameCandidateTwice_ReturnsExistingDirective_NoSecondUpsert()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("portlincoln")]));
        _futureLessonRepository.GetRecentAsync("3AW", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([Candidate()]));
        _repository.ListAsync(GuidanceScope.Global, string.Empty, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _repository.ListAsync(GuidanceScope.Market, "portlincoln", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([
                new GuidanceEntry("promo-1", GuidanceScope.Market, "portlincoln", GuidanceType.Editorial,
                    "Lead with the closure", true, "alice", Now, Now,
                    Option<string>.Some(GuidanceEntry.PromotedSource("job-42")))]));
        _repository.ListAsync(GuidanceScope.Station, "3AW", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));

        var request = new PromoteGuidanceRequest(GuidanceScope.Market, "portlincoln", GuidanceType.Editorial,
            "Lead with the closure", "job-42");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PromoteGuidance(
                CreatePostRequest(request, "http://localhost/api/guidance/promote"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("promo-1");
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<GuidanceEntry>(), Arg.Any<CancellationToken>());
    }

    // === F4 (bug-bash 2026-08-05): inventory and candidates must surface Station scope ===

    /// <summary>Base64 X-MS-CLIENT-PRINCIPAL for an Admin caller (SWA shape).</summary>
    private static string AdminPrincipal()
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            """{"identityProvider":"aad","userId":"admin-oid","userDetails":"admin@arn.com.au","userRoles":["authenticated","Admin"]}"""));

    [Fact]
    public async Task GuidanceInventory_IncludesStationScopedRows()
    {
        var req = MockHttpRequestData.CreateGet("http://localhost/api/guidance/inventory?markets=sydney");
        req.Headers.Add(ClientPrincipal.HeaderName, AdminPrincipal());
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), true, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);

        // The catalogue maps sydney -> station "3AW"; the inventory must query that Station
        // partition so a Station directive that reaches bulletins is visible to the console.
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("sydney")]));
        _repository.ListAsync(GuidanceScope.Global, string.Empty, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _repository.ListAsync(GuidanceScope.Market, "sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _repository.ListAsync(GuidanceScope.Station, "3AW", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([
                new GuidanceEntry("st-1", GuidanceScope.Station, "3AW", GuidanceType.Style,
                    "Station rule", true, "alice", Now, Now,
                    Option<string>.Some(GuidanceEntry.ManualSource))]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GuidanceInventory(req, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("st-1");
        body.ShouldContain("Station rule");
        await _repository.Received(1).ListAsync(GuidanceScope.Station, "3AW", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListGuidanceCandidates_MarksAlreadyPromotedCandidates()
    {
        _futureLessonRepository.GetRecentAsync("3AW", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([
                Candidate("job-1", "Note one"), Candidate("job-2", "Note two")]));
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("portlincoln")]));
        _repository.ListAsync(GuidanceScope.Global, string.Empty, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _repository.ListAsync(GuidanceScope.Market, "portlincoln", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([
                new GuidanceEntry("p1", GuidanceScope.Market, "portlincoln", GuidanceType.Editorial,
                    "Note one", true, "alice", Now, Now,
                    Option<string>.Some(GuidanceEntry.PromotedSource("job-1")))]));
        _repository.ListAsync(GuidanceScope.Station, "3AW", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListGuidanceCandidates(
                MockHttpRequestData.CreateGet("http://localhost/api/markets/portlincoln/guidance/candidates"),
                "portlincoln", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(body);
        var candidates = doc.RootElement.EnumerateArray().ToList();
        candidates.ShouldContain(c =>
            c.GetProperty("JobId").GetString() == "job-1" && c.GetProperty("Promoted").GetBoolean());
        candidates.ShouldContain(c =>
            c.GetProperty("JobId").GetString() == "job-2" && !c.GetProperty("Promoted").GetBoolean());
    }
}
