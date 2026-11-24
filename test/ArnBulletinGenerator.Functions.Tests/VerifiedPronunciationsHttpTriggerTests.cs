using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class VerifiedPronunciationsHttpTriggerTests
{
    private static readonly DateTime OriginalVerified = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc);

    private readonly IVerifiedPronunciationRepository _repository =
        Substitute.For<IVerifiedPronunciationRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private readonly VerifiedPronunciationsHttpTrigger _sut;

    public VerifiedPronunciationsHttpTriggerTests()
    {
        _repository.GetForMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VerifiedPronunciation>>.Success([]));
        _repository.SetForMarketAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedPronunciation>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Result<MarketSchedule>.Success(Schedule(callInfo.ArgAt<string>(0))));

        _sut = new VerifiedPronunciationsHttpTrigger(
            _repository, _scheduleRepository, _marketAccess, _timeProvider,
            Substitute.For<ILogger<VerifiedPronunciationsHttpTrigger>>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static MarketSchedule Schedule(string marketId = "melbourne")
        => MarketSchedule.Create(marketId, $"{marketId} Traffic", "3AW", "Australia/Melbourne", "*/15 * * * *", true,
            CoverageArea.Create(-37.8136, 144.9631, 5.0).Value, "voice-123", "melbourne/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;

    private async Task<(HttpStatusCode Status, string Body)> Put(
        UpsertVerifiedPronunciationsRequest request, string market = "melbourne")
    {
        return await MockHttpRequestData.ReadResponseAsync(
            await _sut.Set(
                MockHttpRequestData.CreatePut(
                    request, $"http://localhost/api/markets/{market}/verified-pronunciations"),
                market, Ctx()));
    }

    private IReadOnlyList<VerifiedPronunciation> CapturedWords()
    {
        var call = _repository.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IVerifiedPronunciationRepository.SetForMarketAsync));
        return (IReadOnlyList<VerifiedPronunciation>)call.GetArguments()[1]!;
    }

    // === Provenance ===

    [Fact]
    public async Task Set_TogglingOneWord_PreservesOriginalVerifierAndTimestampOfEveryWord()
    {
        // The whole-list PUT shape means a toggle resends every word. Rewriting provenance on
        // each PUT would silently reattribute other operators' verdicts to whoever last
        // clicked anything.
        _repository.GetForMarketAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VerifiedPronunciation>>.Success([
                new VerifiedPronunciation("Keilor", "sounds right", true, "alice", OriginalVerified),
                new VerifiedPronunciation("Prahran", null, true, "bob", OriginalVerified)
            ]));

        var (status, _) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor", "sounds right", Enabled: false),
            new VerifiedPronunciationDto("Prahran")
        ]));

        status.ShouldBe(HttpStatusCode.OK);
        var words = CapturedWords();
        words.Single(w => w.Word == "Keilor").VerifiedBy.ShouldBe("alice");
        words.Single(w => w.Word == "Keilor").VerifiedAtUtc.ShouldBe(OriginalVerified);
        words.Single(w => w.Word == "Keilor").Enabled.ShouldBeFalse();
        words.Single(w => w.Word == "Prahran").VerifiedBy.ShouldBe("bob");
        words.Single(w => w.Word == "Prahran").VerifiedAtUtc.ShouldBe(OriginalVerified);
    }

    [Fact]
    public async Task Set_NewWord_StampsCurrentTime()
    {
        var (status, _) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Cheltenham")
        ]));

        status.ShouldBe(HttpStatusCode.OK);
        CapturedWords().Single().VerifiedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task Set_RemovedWord_IsDroppedFromTheStoredList()
    {
        _repository.GetForMarketAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VerifiedPronunciation>>.Success([
                new VerifiedPronunciation("Keilor", null, true, "alice", OriginalVerified)
            ]));

        var (status, _) = await Put(new UpsertVerifiedPronunciationsRequest([]));

        status.ShouldBe(HttpStatusCode.OK);
        CapturedWords().ShouldBeEmpty();
    }

    // === Identity ===

    [Fact]
    public async Task Set_PunctuationAndCaseDuplicates_AreRejected()
    {
        // "Gate's" / "GATES" / "gates" are ONE verdict under the normalised key — and a payload
        // carrying more than one of them is a caller error, rejected exactly as the config-import
        // path rejects it (the previous shape silently kept the first under a 200).
        var (status, body) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Gate's"),
            new VerifiedPronunciationDto("GATES"),
            new VerifiedPronunciationDto("gates")
        ]));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Each word may appear once");
        body.ShouldContain("gates");
    }

    [Fact]
    public async Task Set_WordWithNoAlphanumerics_IsPreserved_LikeImport()
    {
        // "--" is neither blank nor a duplicate, and the config-import path stores it unchanged —
        // two write paths to the same data must enforce the same rule, so it is kept, not silently
        // dropped.
        var (status, _) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("--"),
            new VerifiedPronunciationDto("Keilor")
        ]));

        status.ShouldBe(HttpStatusCode.OK);
        CapturedWords().Select(w => w.Word).ShouldBe(["--", "Keilor"]);
    }

    [Fact]
    public async Task Set_BlankWord_IsRejected()
    {
        var (status, body) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor"),
            new VerifiedPronunciationDto("   ")
        ]));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Every verified pronunciation needs a word.");
    }

    [Fact]
    public async Task Set_OverCount_IsRejected_BeforeAnyBuild()
    {
        // The cap counts the payload the client actually sent — blanks and all — like the import
        // planner and the priority-roads route.
        var words = Enumerable.Range(1, ConfigValidationLimits.MaxVerifiedPronunciations + 1)
            .Select(i => new VerifiedPronunciationDto($"Word {i}"))
            .ToList();

        var (status, body) = await Put(new UpsertVerifiedPronunciationsRequest(words));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("At most 200 verified pronunciations per market");
        await _repository.DidNotReceive().SetForMarketAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedPronunciation>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // === Validation ===

    [Fact]
    public async Task Set_MissingWordsArray_IsBadRequest()
    {
        (await Put(new UpsertVerifiedPronunciationsRequest())).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Set_UnknownMarket_IsNotFound()
    {
        _scheduleRepository.GetScheduleAsync("nowhere", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Schedule not found"));

        (await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor")
        ]), "nowhere")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Set_OverWordLengthCap_IsBadRequest()
    {
        (await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto(new string('a', 81))
        ]))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Set_OverNoteLengthCap_IsBadRequest()
    {
        (await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor", new string('a', 201))
        ]))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Set_TypedOversizeFailure_ReturnsBadRequest_NotServerError()
    {
        // Fully-valid input can still serialize past the 32,768-char Azure Table property
        // ceiling (200 x 80-char word + 200-char note ≈ 74K). The repository's pre-flight
        // refuses with a typed InvalidInput, and the trigger must map that disposition to 400
        // (the previous message-only mapping sent it to an opaque 500).
        _repository.SetForMarketAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedPronunciation>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.InvalidInput(
                "verifiedpronunciations.list_too_large",
                "This verified pronunciation list is too large to save — it is 74,123 characters against a " +
                "32,768 limit. Remove or shorten some entries and save again.")));

        (var status, string body) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor")
        ]));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("too large to save");
        body.ShouldContain("32,768");
    }

    [Fact]
    public async Task Set_UntypedStorageFailure_StillReturnsServerError()
    {
        // Guard against over-mapping: an unclassified storage failure stays a 500, while the
        // "Conflict: ..." message path below still maps to 409.
        _repository.SetForMarketAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedPronunciation>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Table Storage error"));

        (var status, _) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor")
        ]));

        status.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Set_ConflictFailure_StillReturns409()
    {
        _repository.SetForMarketAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedPronunciation>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(
                "Conflict: the verified pronunciations were modified by another request. Reload and try again."));

        (var status, _) = await Put(new UpsertVerifiedPronunciationsRequest([
            new VerifiedPronunciationDto("Keilor")
        ]));

        status.ShouldBe(HttpStatusCode.Conflict);
    }

    // === Market scoping ===

    [Fact]
    public async Task Set_MarketOutsideOperatorScope_IsForbidden()
    {
        var req = MockHttpRequestData.CreatePut(
            new UpsertVerifiedPronunciationsRequest([new VerifiedPronunciationDto("Keilor")]),
            "http://localhost/api/markets/melbourne/verified-pronunciations");
        req.Headers.Add(ClientPrincipal.HeaderName, CreatePrincipal("operator1"));

        _marketAccess.GetAllowedMarketsAsync("operator1", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["sydney"]));

        var (status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Set(req, "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.Forbidden);
        await _repository.DidNotReceive().SetForMarketAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedPronunciation>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // === Read ===

    [Fact]
    public async Task Get_ReturnsStoredWordsWithProvenance()
    {
        _repository.GetForMarketAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<VerifiedPronunciation>>.Success([
                new VerifiedPronunciation("Keilor", "sounds right", true, "alice", OriginalVerified)
            ]));

        var (status, body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Get(
                MockHttpRequestData.CreateGet(
                    "http://localhost/api/markets/melbourne/verified-pronunciations"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var dto = JsonSerializer.Deserialize<VerifiedPronunciationsDto>(
            body, JsonSerializationDefaults.Request);
        dto.ShouldNotBeNull();
        dto!.Words.Single().Word.ShouldBe("Keilor");
        dto.Words.Single().VerifiedBy.ShouldBe("alice");
        dto.Words.Single().Note.ShouldBe("sounds right");
    }

    [Fact]
    public async Task Get_EmptyMarket_IsBadRequest()
    {
        var (status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Get(
                MockHttpRequestData.CreateGet("http://localhost/api/markets//verified-pronunciations"),
                "", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Base64 X-MS-CLIENT-PRINCIPAL header value for a test principal.</summary>
    private static string CreatePrincipal(string name, bool isAdmin = false)
    {
        string role = isAdmin ? "admin" : "operator";
        string payload = JsonSerializer.Serialize(new
        {
            auth_typ = "aad",
            name_typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            claims = new[]
            {
                new { typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", val = name },
                new { typ = "http://schemas.microsoft.com/identity/claims/objectidentifier", val = name },
                new { typ = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", val = role }
            }
        });
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
    }
}
