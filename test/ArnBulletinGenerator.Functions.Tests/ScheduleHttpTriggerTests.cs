using ArnBulletinGenerator.Infrastructure;
using System.Net;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class ScheduleHttpTriggerTests
{
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly IVoiceProfileService _voiceProfiles = Substitute.For<IVoiceProfileService>();
    private readonly IDeliveryStyleService _deliveryStyles = Substitute.For<IDeliveryStyleService>();
    private readonly IPronunciationDictionaryService _pronunciationDictionaries = Substitute.For<IPronunciationDictionaryService>();
    private readonly IBookendService _bookends = Substitute.For<IBookendService>();
    private readonly IBulletinPlaybookService _playbooks = Substitute.For<IBulletinPlaybookService>();
    private readonly ScheduleHttpTrigger _sut;

    public ScheduleHttpTriggerTests()
    {
        // The REAL Cronos-backed parser, not a stub. A stub that answers TryParseCron=true and
        // leaves CalculateNextRun unset makes every cron rule at this layer untestable — the
        // minimum-interval check silently passes because the stub reports no occurrences.
        var service = new ScheduleService(_scheduleRepository, new CronParser(TimeProvider.System),
            TimeProvider.System, Substitute.For<ILogger<ScheduleService>>());
        var now = DateTime.UtcNow;
        // Referenced voice profile / delivery style resolve by default; a test overrides these
        // to simulate a dangling reference.
        _voiceProfiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(
                VoiceProfile.Create("V", null, "voice-1", "V", "model-1", VoiceSettings.Default, null, null, now).Value));
        _deliveryStyles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Success(
                DeliveryStyle.Create("D", null, DeliveryStylePolicy.Neutral, null, null, now).Value));
        _pronunciationDictionaries.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Success(
                PronunciationDictionary.Create("P", null, "pd-eleven", "v1", [], null, null, now).Value));
        _voiceProfiles.GetManyAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(BuildBatch(
                callInfo.ArgAt<IReadOnlyCollection<string>>(0),
                id => _voiceProfiles.GetAsync(id, CancellationToken.None).GetAwaiter().GetResult())));
        _deliveryStyles.GetManyAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(BuildBatch(
                callInfo.ArgAt<IReadOnlyCollection<string>>(0),
                id => _deliveryStyles.GetAsync(id, CancellationToken.None).GetAwaiter().GetResult())));
        _pronunciationDictionaries.GetManyAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(BuildBatch(
                callInfo.ArgAt<IReadOnlyCollection<string>>(0),
                id => _pronunciationDictionaries.GetAsync(id, CancellationToken.None).GetAwaiter().GetResult())));

        // No bookends exist by default — a test that assigns one overrides the library.
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success([]));
        _bookends.GetManyAsync(
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var listed = _bookends.ListAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (listed.IsFailure)
                    return Task.FromResult(
                        Result<IReadOnlyDictionary<string, Option<Bookend>>>.Failure(listed.Error));

                var ids = callInfo.ArgAt<IReadOnlyCollection<string>>(0);
                var values = ids.ToDictionary(
                    id => id,
                    id => listed.Value.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal))
                        is { } bookend
                        ? Option<Bookend>.Some(bookend)
                        : Option<Bookend>.None,
                    StringComparer.Ordinal);
                return Task.FromResult(
                    Result<IReadOnlyDictionary<string, Option<Bookend>>>.Success(values));
            });

        _sut = new ScheduleHttpTrigger(_scheduleRepository, service, _marketAccess, _voiceProfiles, _deliveryStyles,
            _playbooks, _pronunciationDictionaries, _bookends, Options.Create(new BroadcastOptions()),
            Substitute.For<ILogger<ScheduleHttpTrigger>>());
    }

    private static Result<IReadOnlyDictionary<string, Option<T>>> BuildBatch<T>(
        IReadOnlyCollection<string> ids,
        Func<string, Result<T>> resolve)
        where T : class
    {
        var values = new Dictionary<string, Option<T>>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var result = resolve(id);
            if (result.IsFailure)
                return Result<IReadOnlyDictionary<string, Option<T>>>.Failure(result.Error);
            values[id] = Option<T>.Some(result.Value);
        }

        return Result<IReadOnlyDictionary<string, Option<T>>>.Success(values);
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private static MarketSchedule Schedule(string marketId = "melbourne")
    {
        return MarketSchedule.Create(marketId, $"{marketId} Traffic", "3AW", "Australia/Melbourne", "*/15 * * * *", true,
            CoverageArea.Create(-37.8136, 144.9631, 5.0).Value, "voice-123", "melbourne/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;
    }

    private void SetupSchedule(string id)
    {
        _scheduleRepository.GetScheduleAsync(id, Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(Schedule(id)));
    }

    private void SetupScheduleNotFound(string id)
    {
        _scheduleRepository.GetScheduleAsync(id, Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));
    }

    // === ListSchedules ===

    [Fact]
    public async Task ListSchedules_ReturnsOkWithItems()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(new[] { Schedule(), Schedule("sydney") }));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListSchedules(MockHttpRequestData.CreateGet("http://localhost/api/schedules"), Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("melbourne");
        body.ShouldContain("sydney");
    }

    [Fact]
    public async Task ListSchedules_Empty_ReturnsZeroTotal()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(Enumerable.Empty<MarketSchedule>()));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListSchedules(MockHttpRequestData.CreateGet("http://localhost/api/schedules"), Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":0");
    }

    [Fact]
    public async Task ListSchedules_RepositoryFailure_Returns500()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Failure("Storage unavailable"));

        var resp = await _sut.ListSchedules(MockHttpRequestData.CreateGet("http://localhost/api/schedules"), Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // === GetSchedule ===

    [Fact]
    public async Task GetSchedule_Existing_ReturnsOk()
    {
        SetupSchedule("melbourne");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetSchedule(MockHttpRequestData.CreateGet("http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("melbourne");
        body.ShouldContain("3AW");
    }

    [Fact]
    public async Task GetSchedule_NonExistent_Returns404()
    {
        SetupScheduleNotFound("nonexistent");
        var resp = await _sut.GetSchedule(MockHttpRequestData.CreateGet("http://localhost/api/schedules/nonexistent"),
            "nonexistent", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // === CreateOrUpdateSchedule ===

    [Fact]
    public async Task CreateOrUpdate_NewSchedule_ReturnsOk()
    {
        SetupScheduleNotFound("new-market");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var upsert = new UpsertScheduleRequest("New Market", "ABC", "Australia/Sydney", "*/30 * * * *",
            true, 45, VoiceProfileId: "vp-new", Coverage: new CoverageDto(-33.8688, 151.2093, 10.0));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/new-market"), "new-market",
                Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("new-market");
    }

    [Fact]
    public async Task CreateOrUpdate_UpdateExisting_ReturnsOk()
    {
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("Updated", Enabled: false),
                "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrUpdate_EmptyBody_Returns400()
    {
        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/schedules/test-market"), "test-market", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateOrUpdate_ClearingName_Returns400(string blank)
    {
        // Returned 200 with the edit silently discarded (dev 2026-07-28): the operator was told
        // the save succeeded while the stored name was left untouched.
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest(blank),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Schedule name is required and cannot be cleared");
        await _scheduleRepository.DidNotReceive()
            .SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrUpdate_OmittedName_LeavesItUnchanged()
    {
        // The other half of the same rule: absent must stay valid, or every partial update breaks.
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest(Enabled: false),
                "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrUpdate_BodyMarketIdContradictsUrl_Returns400()
    {
        // Stored under the URL id and discarded the body value under a 200, so an operator who
        // edited the wrong row got no signal at all.
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("Updated", MarketId: "sydney"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("does not match the URL market");
        await _scheduleRepository.DidNotReceive()
            .SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrUpdate_BodyMarketIdMatchesUrl_IsAccepted()
    {
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("Updated", MarketId: "MELBOURNE"),
                "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrUpdate_OnceAMinuteCron_Returns400()
    {
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest(CronExpression: "* * * * *"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("more often than every 2 minutes");
    }

    [Fact]
    public async Task CreateOrUpdate_UpdateWithInvalidCoverage_Returns400()
    {
        // A present-but-unparseable coverage must fail loudly, not silently
        // keep the previous shape (the save would lie to the operator).
        SetupSchedule("melbourne");

        var upsert = new UpsertScheduleRequest("Updated",
            Coverage: new CoverageDto(-37.8, 145.0, 15.0, "polygon",
                PolygonPoints: [new CoveragePointDto(-37.7, 144.9), new CoveragePointDto(-37.9, 144.85)]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("polygon", Case.Insensitive);
    }

    [Fact]
    public async Task CreateOrUpdate_UpdateWithPolygonCoverage_SavesPolygon()
    {
        SetupSchedule("melbourne");
        MarketSchedule? saved = null;
        _scheduleRepository.SaveScheduleAsync(Arg.Do<MarketSchedule>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var upsert = new UpsertScheduleRequest("Updated",
            Coverage: new CoverageDto(-37.8, 145.0, 15.0, "polygon",
                PolygonPoints:
                [
                    new CoveragePointDto(-37.7, 144.9),
                    new CoveragePointDto(-37.9, 144.85),
                    new CoveragePointDto(-37.8, 145.1)
                ]));

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.ShouldNotBeNull();
        saved.Coverage.ShapeType.ShouldBe(CoverageShapeType.Polygon);
        saved.Coverage.PolygonPoints!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CreateOrUpdate_InvalidJson_Returns400()
    {
        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePostRaw("not valid json", "http://localhost/api/schedules/test-market"),
            "test-market", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrUpdate_MissingRequiredFields_Returns400()
    {
        SetupScheduleNotFound("incomplete");
        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest(Enabled: true),
                "http://localhost/api/schedules/incomplete"),
            "incomplete", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrUpdate_RequiredWithoutSelectedSource_Returns400()
    {
        // "Always include" implies selected: a fixture flag on a source that is not enabled
        // could never air, which is exactly the contract violation it exists to prevent.
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U",
                    Content: new ContentSelectionDto(Traffic: true, Weather: false, WeatherRequired: true)),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("WeatherRequired");

        (status, body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U",
                    Content: new ContentSelectionDto(Traffic: true, Fuel: false, FuelRequired: true)),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("FuelRequired");
    }

    [Fact]
    public async Task CreateOrUpdate_RequiredWithSelectedSource_IsAccepted()
    {
        // The valid half of the rule: weather required AND selected persists the fixture flag.
        SetupSchedule("melbourne");
        MarketSchedule? saved = null;
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success())
            .AndDoes(c => saved = c.Arg<MarketSchedule>());

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U",
                Content: new ContentSelectionDto(Traffic: true, Weather: true, Fuel: false,
                    WeatherRequired: true)),
                "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.ShouldNotBeNull();
        saved!.ContentSelection.HasValue.ShouldBeTrue();
        saved.ContentSelection.Value.Weather.ShouldBeTrue();
        saved.ContentSelection.Value.WeatherRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateOrUpdate_ReferenceValidationBatchesEachLibraryOnce()
    {
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over the city")]));

        var upsert = new UpsertScheduleRequest("Updated",
            VoiceProfileId: "vp-primary",
            VoiceRotation: new VoiceRotationDto("roundRobin", VoiceProfileIds: ["vp-a", "vp-b"]),
            DeliveryStyleId: "ds-1",
            PronunciationDictionaryId: "pd-1",
            BookendId: "bk-1");

        var response = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _voiceProfiles.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids =>
                ids.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "vp-primary", "vp-a", "vp-b" })),
            false, Arg.Any<CancellationToken>());
        await _deliveryStyles.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids =>
                ids.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "ds-1" })),
            false, Arg.Any<CancellationToken>());
        await _pronunciationDictionaries.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids =>
                ids.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "pd-1" })),
            false, Arg.Any<CancellationToken>());
        await _bookends.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids =>
                ids.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "bk-1" })),
            false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrUpdate_DanglingVoiceProfile_Returns400()
    {
        SetupSchedule("melbourne");
        _voiceProfiles.GetAsync("vp-missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("not found"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", VoiceProfileId: "vp-missing"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Voice profile", Case.Insensitive);
    }

    [Fact]
    public async Task CreateOrUpdate_RotationWithDanglingMember_Returns400()
    {
        SetupSchedule("melbourne");
        _voiceProfiles.GetAsync("vp-missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("not found"));

        var upsert = new UpsertScheduleRequest("U",
            VoiceRotation: new VoiceRotationDto("roundRobin", VoiceProfileIds: ["vp-a", "vp-missing"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Voice profile", Case.Insensitive);
    }

    [Fact]
    public async Task CreateOrUpdate_InvalidRotationMode_Returns400()
    {
        SetupSchedule("melbourne");

        var upsert = new UpsertScheduleRequest("U", VoiceRotation: new VoiceRotationDto("bogus"));

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrUpdate_ValidRoundRobinRotation_SavesPolicy()
    {
        SetupSchedule("melbourne");
        MarketSchedule? saved = null;
        _scheduleRepository.SaveScheduleAsync(Arg.Do<MarketSchedule>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var upsert = new UpsertScheduleRequest("U",
            VoiceRotation: new VoiceRotationDto("roundRobin", VoiceProfileIds: ["vp-a", "vp-b"]));

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.ShouldNotBeNull();
        saved.VoiceRotation.HasValue.ShouldBeTrue();
        saved.VoiceRotation.Value.Mode.ShouldBe(VoiceSelectionMode.RoundRobin);
        saved.VoiceRotation.Value.VoiceProfileIds.ShouldBe(["vp-a", "vp-b"]);
    }

    [Fact]
    public async Task CreateOrUpdate_DanglingDeliveryStyle_Returns400()
    {
        SetupSchedule("melbourne");
        _deliveryStyles.GetAsync("ds-missing", Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Failure("not found"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", DeliveryStyleId: "ds-missing"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Delivery style", Case.Insensitive);
    }

    [Fact]
    public async Task CreateOrUpdate_NegativeNotifyMinutes_Returns400_NotServerError()
    {
        // Regression: this validation message ("must be non-negative") didn't match the old
        // keyword heuristic and was wrongly returned as HTTP 500.
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", NotifyBeforePlayoutMinutes: -5),
                "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === Bookend assignment integrity ===

    private static Bookend MakeBookend(string id, string text, bool archived = false)
    {
        var bookend = Bookend.Create(id, null, new BookendTemplate(text, ""), null, null,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), id).Value;
        if (archived)
            bookend.Archive(DateTime.UtcNow);
        return bookend;
    }

    [Fact]
    public async Task CreateOrUpdate_ArchivedBookend_Returns400()
    {
        SetupSchedule("melbourne");
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over the city", archived: true)]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", BookendId: "bk-1"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("archived");
    }

    [Fact]
    public async Task CreateOrUpdate_ArchivedDeliveryStyle_Returns400()
    {
        // Finding 5: archiving a delivery style must retire it from NEW assignment — the same
        // guard bookends have had all along (saved 200 with the archived id persisted on dev
        // 2026-08-05). The web picker's client-side filter was the only thing stopping it.
        SetupSchedule("melbourne");
        var style = DeliveryStyle.Create("D", null, DeliveryStylePolicy.Neutral, null, null, DateTime.UtcNow).Value;
        style.Archive(DateTime.UtcNow);
        _deliveryStyles.GetAsync("ds-archived", Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Success(style));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", DeliveryStyleId: "ds-archived"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("is archived and cannot be assigned");
    }

    [Fact]
    public async Task CreateOrUpdate_ArchivedVoiceProfile_Returns400()
    {
        // Finding 5 (voice library): same rule once voice profiles can be archived — an archived
        // profile must not be newly assignable (retire-in-place only).
        SetupSchedule("melbourne");
        var profile = VoiceProfile.Create("V", null, "voice-1", "V", "model-1", VoiceSettings.Default, null, null,
            DateTime.UtcNow).Value;
        profile.Archive(DateTime.UtcNow);
        _voiceProfiles.GetAsync("vp-archived", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(profile));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", VoiceProfileId: "vp-archived"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("is archived and cannot be assigned");
    }

    [Fact]
    public async Task CreateOrUpdate_ArchivedVoiceRotationMember_Returns400()
    {
        // A rotation can name several profiles; each member needs the same archived guard, or a
        // rotation slot would silently degrade to the primary/default voice at airtime.
        SetupSchedule("melbourne");
        var archived = VoiceProfile.Create("V", null, "voice-1", "V", "model-1", VoiceSettings.Default, null, null,
            DateTime.UtcNow).Value;
        archived.Archive(DateTime.UtcNow);
        _voiceProfiles.GetAsync("vp-archived", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(archived));

        var upsert = new UpsertScheduleRequest("U",
            VoiceRotation: new VoiceRotationDto("roundRobin", VoiceProfileIds: ["vp-a", "vp-archived"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("is archived and cannot be assigned");
    }

    [Fact]
    public async Task CreateOrUpdate_ArchivedPronunciationDictionary_Returns400()
    {
        SetupSchedule("melbourne");
        var dictionary = PronunciationDictionary.Create("P", null, "pd-eleven", "v1", [], null, null,
            DateTime.UtcNow).Value;
        dictionary.Archive(DateTime.UtcNow);
        _pronunciationDictionaries.GetAsync("pd-archived", Arg.Any<CancellationToken>())
            .Returns(Result<PronunciationDictionary>.Success(dictionary));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U",
                    PronunciationDictionaryId: "pd-archived"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("is archived and cannot be assigned");
    }

    [Fact]
    public async Task CreateOrUpdate_ClearingVoiceProfile_Returns400_NotSilentKeep()
    {
        // Finding 17: PUT {"VoiceProfileId":""} returned 200 with the old voice silently kept
        // (dev 2026-08-05). A schedule cannot be created without a voice (MarketSchedule.Create
        // requires it), so the honest outcome for an explicit clear is a 400 — never a silent
        // keep, and never a save that contradicts the domain invariant.
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", VoiceProfileId: ""),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("VoiceProfileId");
        body.ShouldContain("required");
        await _scheduleRepository.DidNotReceive()
            .SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrUpdate_ClearingCron_Returns400_NotSilentKeep()
    {
        // Finding 45: PUT {"CronExpression":""} returned 200 with the old cron silently kept,
        // while create rejects the same empty value. Update must behave exactly like create.
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", CronExpression: ""),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Invalid cron expression");
        await _scheduleRepository.DidNotReceive()
            .SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrUpdate_ClearingTimezone_Returns400_NotSilentKeep()
    {
        // Finding 45, timezone half: same silent-keep class, same create/update asymmetry.
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", Timezone: ""),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Invalid timezone");
        await _scheduleRepository.DidNotReceive()
            .SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrUpdate_RotationOnlyEdit_UsesStoredTokensAndStation_Succeeds()
    {
        // Finding 18: a rotation-only PUT (tokens NOT resent) was rejected demanding values the
        // schedule already stores, because validation read only the request's delta. The check
        // must see the effective post-save values: stored tokens and station stand in when the
        // request omits them. Also proves the STATION built-in ({station}) resolves from the
        // stored schedule, not just the overrides.
        var stored = MarketSchedule.Create("melbourne", "melbourne Traffic", "3AW", "Australia/Melbourne",
            "*/15 * * * *", true, CoverageArea.Create(-37.8136, 144.9631, 5.0).Value, "voice-123",
            "melbourne/traffic/", 30, 5, 2, DateTime.UtcNow, DateTime.UtcNow,
            bookendTokens: new Dictionary<string, string> { ["presenter"] = "Sam" }).Value;
        _scheduleRepository.GetScheduleAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(stored));
        MarketSchedule? saved = null;
        _scheduleRepository.SaveScheduleAsync(Arg.Do<MarketSchedule>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over {presenter} for {station}")]));

        var upsert = new UpsertScheduleRequest("U",
            BookendRotation: new BookendRotationDto("timeOfDay",
                Windows: [new BookendWindowDto("06:00", "09:00", ["bk-1"])]));

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.ShouldNotBeNull();
        saved.BookendRotation.HasValue.ShouldBeTrue();
        saved.BookendRotation.Value.Mode.ShouldBe(BookendSelectionMode.TimeOfDay);
        // The stored tokens were NOT cleared by the rotation-only edit.
        saved.BookendTokens["presenter"].ShouldBe("Sam");
    }

    [Fact]
    public async Task CreateOrUpdate_NonFittingBookend_SucceedsWithFitWarning()
    {
        // Finding-4 advisory (NOT a rejection): assigning a pair that cannot share the 15s slot
        // with a minimum viable traffic read is allowed — the engine drops the pair and airs the
        // traffic — but the PUT response must carry BookendFitWarning so the admin learns at save
        // time that this station will not read the pair on air.
        var stored = MarketSchedule.Create("melbourne", "melbourne Traffic", "3AW", "Australia/Melbourne",
            "*/15 * * * *", true, CoverageArea.Create(-37.8136, 144.9631, 5.0).Value, "voice-123",
            "melbourne/traffic/", 15, 5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;
        _scheduleRepository.GetScheduleAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(stored));
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1",
                    "This is a very long opening line that goes on and on and on and takes far too " +
                    "many words to say anything at all and just keeps going past the point where any " +
                    "reasonable person would stop talking and wrap it up")]));

        var upsert = new UpsertScheduleRequest("U", BookendId: "bk-1");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"BookendFitWarning\":\"the");
        body.ShouldContain("will not fit");
    }

    [Fact]
    public async Task CreateOrUpdate_FittingBookend_HasNoFitWarning()
    {
        // The other half of the advisory: a pair that fits the slot gets a clean response — the
        // warning key serialises as null (responses never omit fields), so a null-vs-string
        // value is the console's notice discriminator.
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over the city")]));

        var upsert = new UpsertScheduleRequest("U", BookendId: "bk-1");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"BookendFitWarning\":null");
    }

    [Fact]
    public async Task CreateOrUpdate_CoverageOutsideOperatingRegion_Returns400()
    {
        // Finding 47: an operator-supplied coverage must fall inside the platform's operating
        // region — a Paris circle would silently fetch and air the wrong region's incidents.
        // Enforced at this input boundary, not inside CoverageArea.Create (which must stay
        // permissive enough to reconstruct persisted rows).
        SetupSchedule("melbourne");

        var upsert = new UpsertScheduleRequest("U", Coverage: new CoverageDto(48.8566, 2.3522, 5.0));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("operating region");
    }

    [Fact]
    public async Task CreateOrUpdate_MissingBookend_Returns400()
    {
        SetupSchedule("melbourne");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", BookendId: "bk-nope"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("does not exist");
    }

    [Fact]
    public async Task CreateOrUpdate_RotationWithMissingMember_Returns400()
    {
        SetupSchedule("melbourne");
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over the city")]));

        var upsert = new UpsertScheduleRequest("U",
            BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bk-1", "bk-nope"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("does not exist");
    }

    [Fact]
    public async Task CreateOrUpdate_BookendRotationMemberWithWrongCase_Returns400()
    {
        // DKI-03: the resolver reads bookends by exact Table RowKey, so a rotation member whose
        // id differs only by case from the stored pair is a DIFFERENT id that would fail at
        // airtime (falling back to the primary pair). Construction must reject it exactly, same
        // as the voice-rotation members — the previous OrdinalIgnoreCase library dictionary let
        // 'BK-1' pass when only 'bk-1' existed.
        SetupSchedule("melbourne");
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over the city")]));

        var upsert = new UpsertScheduleRequest("U",
            BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bk-1", "BK-1"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("BK-1");
        body.ShouldContain("does not exist");
    }

    [Fact]
    public async Task CreateOrUpdate_VoiceRotationMemberWithWrongCase_Returns400()
    {
        // The voice-rotation members are resolved by exact GetAsync at construction; a member
        // differing only by case from the stored profile must be rejected, not saved to degrade
        // to the primary voice at airtime.
        SetupSchedule("melbourne");
        _voiceProfiles.GetAsync("vp-a", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(
                VoiceProfile.Create("V", null, "voice-1", "V", "model-1", VoiceSettings.Default,
                    null, null, DateTime.UtcNow).Value));
        _voiceProfiles.GetAsync("VP-A", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("Voice profile 'VP-A' not found."));

        var upsert = new UpsertScheduleRequest("U",
            VoiceRotation: new VoiceRotationDto("roundRobin", VoiceProfileIds: ["vp-a", "VP-A"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("VP-A");
        body.ShouldContain("does not exist");
    }

    [Fact]
    public async Task CreateOrUpdate_UnresolvableToken_Returns400()
    {
        // A bookend whose text needs a token this station cannot fill would air with the word
        // silently dropped — reject the save so it can never get into that state.
        SetupSchedule("melbourne");
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Welcome to {brand} traffic")]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.CreateOrUpdateSchedule(
                MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U", BookendId: "bk-1"),
                    "http://localhost/api/schedules/melbourne"),
                "melbourne", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("brand");
        body.ShouldContain("no value");
    }

    [Fact]
    public async Task CreateOrUpdate_ResolvableTokens_AreAccepted()
    {
        SetupSchedule("melbourne");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Welcome to {brand} in {city}")]));

        var upsert = new UpsertScheduleRequest("U",
            BookendId: "bk-1",
            BookendTokens: new Dictionary<string, string> { ["brand"] = "3AW" });

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateOrUpdate_ValidBookends_SavesAssignment()
    {
        SetupSchedule("melbourne");
        MarketSchedule? saved = null;
        _scheduleRepository.SaveScheduleAsync(Arg.Do<MarketSchedule>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _bookends.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Bookend>>.Success(
                [MakeBookend("bk-1", "Looking over {city} today"),
                 MakeBookend("bk-2", "That's it for {city} traffic")]));

        var upsert = new UpsertScheduleRequest("U",
            BookendId: "bk-1",
            BookendRotation: new BookendRotationDto("roundRobin", BookendIds: ["bk-1", "bk-2"]),
            BookendTokens: new Dictionary<string, string> { ["city"] = "Melbourne" });

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(upsert, "http://localhost/api/schedules/melbourne"),
            "melbourne", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.ShouldNotBeNull();
        saved.BookendId.ShouldBe("bk-1");
        saved.BookendRotation.HasValue.ShouldBeTrue();
        saved.BookendRotation.Value.Mode.ShouldBe(BookendSelectionMode.RoundRobin);
        saved.BookendTokens["city"].ShouldBe("Melbourne");
    }

    [Fact]
    public async Task CreateOrUpdate_StorageIllegalMarketId_Returns400_NotServerError()
    {
        // A marketId with an Azure Table-illegal char ('#') must be rejected up front (400),
        // not passed to storage where it throws -> HTTP 500 + leaked storage error.
        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("U"),
                "http://localhost/api/schedules/qa%23hash"),
            "qa#hash", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === DeleteSchedule ===

    [Fact]
    public async Task DeleteSchedule_Existing_Returns204()
    {
        _scheduleRepository.DeleteScheduleAsync("melbourne", Arg.Any<CancellationToken>()).Returns(Result.Success());
        var resp = await _sut.DeleteSchedule(
            MockHttpRequestData.CreateDelete("http://localhost/api/schedules/melbourne"), "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteSchedule_NonExistent_Returns404()
    {
        _scheduleRepository.DeleteScheduleAsync("nonexistent", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Schedule not found"));
        var resp = await _sut.DeleteSchedule(
            MockHttpRequestData.CreateDelete("http://localhost/api/schedules/nonexistent"), "nonexistent", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // === Empty/whitespace marketId edge cases ===

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSchedule_EmptyMarketId_Returns400(string marketId)
    {
        var resp = await _sut.GetSchedule(MockHttpRequestData.CreateGet("http://localhost/api/schedules/x"), marketId,
            Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateOrUpdate_EmptyMarketId_Returns400(string marketId)
    {
        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("Test"), "http://localhost/api/schedules/x"),
            marketId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteSchedule_EmptyMarketId_Returns400(string marketId)
    {
        var resp = await _sut.DeleteSchedule(MockHttpRequestData.CreateDelete("http://localhost/api/schedules/x"),
            marketId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }



    // === CreateOrUpdate internal error (non-validation) ===

    [Fact]
    public async Task CreateOrUpdate_SaveFailure_Returns500()
    {
        SetupSchedule("fail-market");
        _scheduleRepository.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Storage connection failed"));

        var resp = await _sut.CreateOrUpdateSchedule(
            MockHttpRequestData.CreatePut(new UpsertScheduleRequest("Updated"),
                "http://localhost/api/schedules/fail-market"),
            "fail-market", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // === DeleteSchedule internal error ===

    [Fact]
    public async Task DeleteSchedule_InternalError_Returns500()
    {
        _scheduleRepository.DeleteScheduleAsync("fail-del", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Storage connection failed"));
        var resp = await _sut.DeleteSchedule(
            MockHttpRequestData.CreateDelete("http://localhost/api/schedules/fail-del"), "fail-del", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // === Market scoping ===

    [Fact]
    public async Task ListSchedules_ScopedOperator_FiltersByAllowedMarkets()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(new[] { Schedule("melbourne"), Schedule("sydney") }));

        // Scoped operator can only see "sydney"
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "sydney" }));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/schedules");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"scoped-op"},{"typ":"roles","val":"Operator"}]}""")));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListSchedules(request, Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        // Assert on the MarketId field specifically — the shared Schedule() helper hardcodes
        // Timezone "Australia/Melbourne", so a bare ShouldNotContain("melbourne") would false-fail.
        body.ShouldContain("\"MarketId\":\"sydney\"");
        body.ShouldNotContain("\"MarketId\":\"melbourne\"");
    }

    [Fact]
    public async Task ListSchedules_UnscopedOperator_SeesAll()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(new[] { Schedule("melbourne"), Schedule("sydney") }));

        // Unrestricted (no principal header set, or allowed markets is None) — sees all
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListSchedules(MockHttpRequestData.CreateGet("http://localhost/api/schedules"), Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("melbourne");
        body.ShouldContain("sydney");
    }

    [Fact]
    public async Task GetSchedule_ScopedOperatorNonAllowedMarket_Returns403()
    {
        SetupSchedule("melbourne");

        // Scoped operator can only see "sydney", not "melbourne"
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "sydney" }));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/schedules/melbourne");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"scoped-op"},{"typ":"roles","val":"Operator"}]}""")));

        var resp = await _sut.GetSchedule(request, "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSchedule_AdminWithAnyMarket_Passes()
    {
        SetupSchedule("melbourne");

        // Admin passes regardless of market
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), true, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);

        var request = MockHttpRequestData.CreateGet("http://localhost/api/schedules/melbourne");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"admin-op"},{"typ":"roles","val":"Admin"}]}""")));

        var resp = await _sut.GetSchedule(request, "melbourne", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
