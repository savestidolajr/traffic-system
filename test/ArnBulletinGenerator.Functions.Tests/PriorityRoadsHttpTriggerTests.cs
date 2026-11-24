using System.Net;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class PriorityRoadsHttpTriggerTests
{
    private readonly IPriorityRoadsRepository _repository = Substitute.For<IPriorityRoadsRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();

    private readonly PriorityRoadsHttpTrigger _sut;

    public PriorityRoadsHttpTriggerTests()
    {
        // Default: no prior roads, so SetPriorityRoads tests that don't care about it never hit
        // an unconfigured (null) Task<Result<...>> from NSubstitute.
        _repository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([]));
        // Default: the market exists, so SetPriorityRoads tests that don't care about the
        // existence guard never hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Result<MarketSchedule>.Success(Schedule(callInfo.ArgAt<string>(0))));
        _sut = new PriorityRoadsHttpTrigger(
            _repository,
            _scheduleRepository,
            _marketAccess,
            Substitute.For<ILogger<PriorityRoadsHttpTrigger>>());
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private static MarketSchedule Schedule(string marketId = "sydney")
    {
        return MarketSchedule.Create(marketId, $"{marketId} Traffic", "2GB", "Australia/Sydney", "*/15 * * * *", true,
            CoverageArea.Create(-33.87, 151.21, 5.0).Value, "voice-123", $"{marketId}/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;
    }

    private static PriorityRoad TestRoad(string name = "M1 Pacific Motorway") => new(name);

    // === GetPriorityRoads ===

    [Fact]
    public async Task GetPriorityRoads_ReturnsOkWithRoads()
    {
        var roads = new[] { TestRoad(), TestRoad("Bruce Highway") };
        _repository.GetRoadsAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success(roads));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPriorityRoads(
                MockHttpRequestData.CreateGet("http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("M1 Pacific Motorway");
        body.ShouldContain("Bruce Highway");
    }

    [Fact]
    public async Task GetPriorityRoads_EmptyMarket_ReturnsBadRequest()
    {
        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPriorityRoads(
                MockHttpRequestData.CreateGet("http://localhost/api/markets//priority-roads"),
                "", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === SetPriorityRoads ===

    [Fact]
    public async Task SetPriorityRoads_ValidRoads_ReturnsOk()
    {
        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway")]);
        _repository.SetRoadsAsync(
                Arg.Is("sydney"),
                Arg.Any<IReadOnlyList<PriorityRoad>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("M1 Pacific Motorway");
    }

    [Fact]
    public async Task SetPriorityRoads_OmittedFlowTier_StoresAndReturnsNone()
    {
        IReadOnlyList<PriorityRoad>? captured = null;
        _repository.SetRoadsAsync(
                Arg.Is("sydney"),
                Arg.Do<IReadOnlyList<PriorityRoad>>(roads => captured = roads),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway")]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Single().FlowTier.ShouldBe(FlowPriorityTier.None);
        body.ShouldContain("\"FlowTier\":\"none\"");
    }

    [Fact]
    public async Task SetPriorityRoads_NullFlowTier_StoresNone()
    {
        IReadOnlyList<PriorityRoad>? captured = null;
        _repository.SetRoadsAsync(
                Arg.Is("sydney"),
                Arg.Do<IReadOnlyList<PriorityRoad>>(roads => captured = roads),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway", FlowTier: null!)]);

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Single().FlowTier.ShouldBe(FlowPriorityTier.None);
    }

    [Fact]
    public async Task SetPriorityRoads_UnknownFlowTier_ReturnsBadRequestWithValidValues()
    {
        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway", FlowTier: "BOGUS")]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("BOGUS");
        body.ShouldContain("none, normal, high, critical");
        await _repository.DidNotReceive().SetRoadsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<PriorityRoad>>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPriorityRoads_ControlCharacterInName_ReturnsBadRequest()
    {
        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway\u202E")]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("must not contain control or formatting characters (including line breaks)");
        await _repository.DidNotReceive().SetRoadsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<PriorityRoad>>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPriorityRoads_HighFlowTier_StoresHigh()
    {
        IReadOnlyList<PriorityRoad>? captured = null;
        _repository.SetRoadsAsync(
                Arg.Is("sydney"),
                Arg.Do<IReadOnlyList<PriorityRoad>>(roads => captured = roads),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway", FlowTier: "high")]);

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Single().FlowTier.ShouldBe(FlowPriorityTier.High);
    }

    [Fact]
    public async Task SetPriorityRoads_UnknownMarket_ReturnsNotFound()
    {
        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway")]);
        _scheduleRepository.GetScheduleAsync("qa-test-market", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Schedule not found"));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/qa-test-market/priority-roads"),
                "qa-test-market", Ctx()));
        status.ShouldBe(HttpStatusCode.NotFound);

        await _repository.DidNotReceive().SetRoadsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<PriorityRoad>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPriorityRoads_RepositoryFailure_ReturnsError()
    {
        var request = new UpsertPriorityRoadsRequest(
            [new PriorityRoadDto("M1 Pacific Motorway")]);
        _repository.SetRoadsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<PriorityRoad>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Table Storage error"));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/priority-roads"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SetPriorityRoads_OperatorNotAssignedToMarket_ReturnsForbidden()
    {
        var principal = CreatePrincipal("operator1", isAdmin: false);
        var req = MockHttpRequestData.CreatePut(
            new UpsertPriorityRoadsRequest([new PriorityRoadDto("M1 Pacific Motorway")]),
            "http://localhost/api/markets/sydney/priority-roads");
        req.Headers.Add(ClientPrincipal.HeaderName, principal);

        _marketAccess.GetAllowedMarketsAsync("operator1", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["melbourne"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetPriorityRoads(req, "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("Forbidden");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    ///     Creates a base64-encoded X-MS-CLIENT-PRINCIPAL header value for a test principal.
    /// </summary>
    private static string CreatePrincipal(string name, bool isAdmin = false)
    {
        var roles = isAdmin ? new[] { "admin" } : new[] { "operator" };
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            auth_typ = "aad",
            name_typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            claims = new[]
            {
                new { typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", val = name },
                new { typ = "http://schemas.microsoft.com/identity/claims/objectidentifier", val = name },
                new { typ = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", val = roles[0] }
            }
        });
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
    }
}
