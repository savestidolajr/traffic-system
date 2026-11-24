using System.Net;
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

public sealed class FillerStatementsHttpTriggerTests
{
    private readonly IFillerStatementsRepository _repository = Substitute.For<IFillerStatementsRepository>();
    private readonly IFillerAudioCacheService _fillerAudioCacheService = Substitute.For<IFillerAudioCacheService>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();

    private readonly FillerStatementsHttpTrigger _sut;

    public FillerStatementsHttpTriggerTests()
    {
        // Default: no prior statements, so SetFillerStatements tests that don't care about
        // invalidation never hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _repository.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        // Default: the market exists, so SetFillerStatements tests that don't care about the
        // existence guard never hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Result<MarketSchedule>.Success(Schedule(callInfo.ArgAt<string>(0))));
        _sut = new FillerStatementsHttpTrigger(
            _repository,
            _fillerAudioCacheService,
            _scheduleRepository,
            _marketAccess,
            Substitute.For<ILogger<FillerStatementsHttpTrigger>>());
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private static FillerStatement TestStatement(string id = "s1", string text = "Stay safe on the roads.")
        => new(id, text);

    private static MarketSchedule Schedule(string marketId = "sydney")
    {
        return MarketSchedule.Create(marketId, $"{marketId} Traffic", "2GB", "Australia/Sydney", "*/15 * * * *", true,
            CoverageArea.Create(-33.87, 151.21, 5.0).Value, "voice-123", $"{marketId}/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;
    }

    // === GetFillerStatements ===

    [Fact]
    public async Task GetFillerStatements_ReturnsOkWithStatements()
    {
        var statements = new[] { TestStatement(), TestStatement("s2", "Drive carefully.") };
        _repository.GetStatementsAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success(statements));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetFillerStatements(
                MockHttpRequestData.CreateGet("http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Stay safe on the roads.");
        body.ShouldContain("Drive carefully.");
    }

    [Fact]
    public async Task GetFillerStatements_EmptyMarket_ReturnsBadRequest()
    {
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetFillerStatements(
                MockHttpRequestData.CreateGet("http://localhost/api/markets//filler-statements"),
                "", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Market is required.");
    }

    // === SetFillerStatements ===

    [Fact]
    public async Task SetFillerStatements_ValidStatements_ReturnsOk()
    {
        var request = new UpsertFillerStatementsRequest(
            [new FillerStatementDto("s1", "Stay safe.")]);
        _repository.SetStatementsAsync(
                Arg.Is("sydney"),
                Arg.Any<IReadOnlyList<FillerStatement>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetFillerStatements_TextExceedsMaxLength_ReturnsBadRequest()
    {
        var longText = new string('A', 501);
        var request = new UpsertFillerStatementsRequest(
            [new FillerStatementDto("s1", longText)]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("500 characters");
    }

    [Fact]
    public async Task SetFillerStatements_UnknownMarket_ReturnsNotFound()
    {
        var request = new UpsertFillerStatementsRequest(
            [new FillerStatementDto("s1", "Stay safe.")]);
        _scheduleRepository.GetScheduleAsync("qa-test-market", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Schedule not found"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/qa-test-market/filler-statements"),
                "qa-test-market", Ctx()));
        status.ShouldBe(HttpStatusCode.NotFound);

        await _repository.DidNotReceive().SetStatementsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<FillerStatement>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFillerStatements_RepositoriesFailure_ReturnsError()
    {
        var request = new UpsertFillerStatementsRequest(
            [new FillerStatementDto("s1", "Stay safe.")]);
        _repository.SetStatementsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<FillerStatement>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Table Storage error"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SetFillerStatements_Conflict_Returns409()
    {
        var request = new UpsertFillerStatementsRequest(
            [new FillerStatementDto("s1", "Stay safe.")]);
        _repository.SetStatementsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<FillerStatement>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Conflict: the filler statements were modified by another request. Reload and try again."));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.Conflict);
    }

    // === Payload validation (findings 41 + 44) ===

    [Fact]
    public async Task SetFillerStatements_OverCapCountedBeforeBlanks_ReturnsBadRequest()
    {
        // The cap describes the payload the client actually sent, not the survivors after blank
        // filtering — 51 items with 2 blanks must 400, not store 49 under a 200.
        var statements = new List<FillerStatementDto>(51);
        statements.AddRange(Enumerable.Range(1, 49).Select(i => new FillerStatementDto($"s{i}", "Stay safe.")));
        statements.Add(new FillerStatementDto("blank1", ""));
        statements.Add(new FillerStatementDto("blank2", "   "));
        var request = new UpsertFillerStatementsRequest(statements);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("At most 50 filler statements per market");
        body.ShouldContain("received 51");
        await _repository.DidNotReceive().SetStatementsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<FillerStatement>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFillerStatements_BlankStatement_ReturnsBadRequest()
    {
        // Blanks are rejected, not silently dropped under a 200 (the filler analog of the roads
        // route's name check).
        var request = new UpsertFillerStatementsRequest([
            new FillerStatementDto("s1", "Stay safe."),
            new FillerStatementDto("blank", "   ")
        ]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Every filler statement needs text.");
        await _repository.DidNotReceive().SetStatementsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<FillerStatement>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFillerStatements_TokenSyntax_ReturnsBadRequest()
    {
        // Filler is appended verbatim with no token substitution — bookend-style {token} text
        // would air as literal braces, so it is rejected at the write boundary.
        var request = new UpsertFillerStatementsRequest([
            new FillerStatementDto("s1", "Traffic updates are brought to you by {STATION_NAME} news.")
        ]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("STATION_NAME");
        body.ShouldContain("Tokens are only substituted in bookends");
        await _repository.DidNotReceive().SetStatementsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<FillerStatement>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFillerStatements_DuplicateStatementIds_ReturnsBadRequest()
    {
        // Copying a statement to tweak it and keeping the source id used to silently DROP the
        // tweak under a 200 (first-wins dedupe). The config-import planner rejects the identical
        // payload ("Each filler statement id may appear only once") and ids match
        // case-insensitively there too — so "s1"/"S1" is a duplicate, exactly like the planner.
        var request = new UpsertFillerStatementsRequest([
            new FillerStatementDto("s1", "Stay safe."),
            new FillerStatementDto("S1", "Drive carefully.")
        ]);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Each filler statement id may appear only once");
        body.ShouldContain("s1");
        await _repository.DidNotReceive().SetStatementsAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<FillerStatement>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFillerStatements_DistinctCaseStatementIds_AreStoredIndividually()
    {
        // Guard against over-tightening: distinct ids that merely LOOK similar (different case)
        // are different statements to the planner's key too — but the leading/trailing whitespace
        // trim still applies on both sides.
        var request = new UpsertFillerStatementsRequest([
            new FillerStatementDto("s1", "Stay safe."),
            new FillerStatementDto("s2", "Drive carefully.")
        ]);
        _repository.SetStatementsAsync(
                Arg.Is("sydney"),
                Arg.Any<IReadOnlyList<FillerStatement>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(
                MockHttpRequestData.CreatePut(request, "http://localhost/api/markets/sydney/filler-statements"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("s1");
        body.ShouldContain("s2");
    }

    // === Market access guard ====

    [Fact]
    public async Task SetFillerStatements_OperatorNotAssignedToMarket_ReturnsForbidden()
    {
        var principal = CreatePrincipal("operator1", isAdmin: false);
        var req = MockHttpRequestData.CreatePut(
            new UpsertFillerStatementsRequest([new FillerStatementDto("s1", "Stay safe.")]),
            "http://localhost/api/markets/sydney/filler-statements");
        req.Headers.Add(ClientPrincipal.HeaderName, principal);

        _marketAccess.GetAllowedMarketsAsync("operator1", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["melbourne"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.SetFillerStatements(req, "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("Forbidden");
    }

    [Fact]
    public async Task GetFillerStatements_OperatorAssignedToMarket_ReturnsOk()
    {
        var principal = CreatePrincipal("operator1", isAdmin: false);
        var req = MockHttpRequestData.CreateGet("http://localhost/api/markets/sydney/filler-statements");
        req.Headers.Add(ClientPrincipal.HeaderName, principal);

        _marketAccess.GetAllowedMarketsAsync("operator1", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["sydney"]));

        _repository.GetStatementsAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([TestStatement()]));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetFillerStatements(req, "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
    }

    // === FillerStatementsInventory ===

    [Fact]
    public async Task FillerStatementsInventory_ReturnsOkWithMultipleMarkets()
    {
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["sydney", "melbourne"]));

        _repository.GetStatementsAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([TestStatement("s1", "Sydney filler.")]));
        _repository.GetStatementsAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/filler-statements/inventory?markets=sydney,melbourne");
        req.Headers.Add(ClientPrincipal.HeaderName, CreatePrincipal("operator1", isAdmin: false));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.FillerStatementsInventory(req, Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("sydney");
        body.ShouldContain("melbourne");
        body.ShouldContain("Sydney filler.");
    }

    [Fact]
    public async Task FillerStatementsInventory_StoreFailureYieldsEmptyList()
    {
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["sydney"]));

        _repository.GetStatementsAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Failure("Boom"));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/filler-statements/inventory?markets=sydney");
        req.Headers.Add(ClientPrincipal.HeaderName, CreatePrincipal("operator1", isAdmin: false));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.FillerStatementsInventory(req, Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        // Should still succeed with empty statements for the failed market
        body.ShouldContain("sydney");
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
