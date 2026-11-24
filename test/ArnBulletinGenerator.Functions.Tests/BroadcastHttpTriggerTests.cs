using System.Text.Json;
using System.Net;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class BroadcastHttpTriggerTests
{

    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IQueuePublisher _queuePublisher = Substitute.For<IQueuePublisher>();
    private readonly IVoiceProfileService _voiceProfiles = Substitute.For<IVoiceProfileService>();
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();

    // What the station config RESOLVES to is covered by AdHocStationConfigResolverTests, and that
    // it reaches the queue message by QueueMessageMappingExtensionsTests. Here it is stubbed to
    // "no station config" so these tests keep asserting only market resolution and the
    // unconfigured-market warning.
    private readonly IAdHocStationConfigResolver _adHocStationConfig =
        Substitute.For<IAdHocStationConfigResolver>();
    private BroadcastQueueMessage? _capturedMessage;
    private readonly BroadcastHttpTrigger _sut;
    private readonly InteractiveWorkGateSpy _gate = new();

    public BroadcastHttpTriggerTests()
    {
        var modelSelector = Substitute.For<IModelSelector>();
        modelSelector.SelectModelAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<ModelSelectionUseCase>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult("eleven_turbo_v2_5"));
        _adHocStationConfig
            .ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AdHocStationConfig.None));
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success(
                [new Voice("voice-1", "Test Voice", Option<string>.None, Option<string>.None, new Dictionary<string, string>())]));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));
        // Unrestricted scope: these tests exercise orchestration, not the market guard. Option.None
        // is what MarketAccessService returns for an admin or an unscoped caller, so the guard
        // waves every request through and the assertions below stay about the bulletin.
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Option<IReadOnlyList<string>>.None));

        _sut = new BroadcastHttpTrigger(
            new JobService(_jobRepository, _scheduleRepository, _queuePublisher, modelSelector, _voiceProfiles,
                _voiceProvider,
                Substitute.For<INotificationService>(),
                Substitute.For<IFileDeliveryService>(), Substitute.For<IBroadcastOutput>(),
                Substitute.For<IPronunciationDictionaryService>(),
                Substitute.For<IDeliveryStyleService>(),
                Substitute.For<IBookendService>(),
                Substitute.For<IBroadcastMetrics>(), Options.Create(new BroadcastOptions()),
                new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()),
                    Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
                Substitute.For<IBulletinFallbackService>(),
                TimeProvider.System,
                Substitute.For<ILogger<JobService>>()),
            _jobRepository,
            new CoverageResolver(_geocodingProvider, Substitute.For<ILogger<CoverageResolver>>()),
            _scheduleRepository,
            _voiceProfiles,
            _voiceProvider,
            _adHocStationConfig,
            _marketAccess,
            Substitute.For<ILogger<BroadcastHttpTrigger>>(), _gate);
    }

    private static OrchestrationRequestDto ValidRequest(double? duration = 30,
        string? location = "Melbourne, VIC, Australia",
        CoverageDto? coverage = null,
        string? voiceId = "voice-1",
        string? voiceProfileId = null)
    {
        return new OrchestrationRequestDto(duration, location,
            coverage ?? new CoverageDto(-37.8136, 144.9631, 5.0),
            VoiceId: voiceId,
            VoiceProfileId: voiceProfileId);
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private async Task<(HttpStatusCode Status, string Body)> Post(object? dto = null, string? raw = null)
    {
        var request = dto is not null ? MockHttpRequestData.CreatePost(dto)
            : raw is not null ? MockHttpRequestData.CreatePostRaw(raw)
            : MockHttpRequestData.CreatePostEmpty();
        var response = await _sut.Run(request, Ctx());
        return await MockHttpRequestData.ReadResponseAsync(response);
    }

    private static string ErrorMessage(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("Error").GetString()!;

    private static VoiceProfile MakeProfile(string id = "vp-valid") =>
        VoiceProfile.Create("Profile Voice", null, "profile-voice", "Profile Base Voice", "eleven_v3",
            VoiceSettings.Default, null, "test", DateTime.UtcNow, id: id).Value;

    [Fact]
    public async Task MissingVoice_Returns400()
    {
        (var status, string body) = await Post(ValidRequest(voiceId: null));

        status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorMessage(body).ShouldBe(
            "A voice is required: supply VoiceProfileId (preferred, resolves the profile's full tuning) or VoiceId.");
    }

    [Fact]
    public async Task UnknownVoiceProfile_Returns400()
    {
        _voiceProfiles.GetAsync("vp-missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("profile not found"));

        (var status, string body) =
            await Post(ValidRequest(voiceId: null, voiceProfileId: "vp-missing"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorMessage(body).ShouldBe("Voice profile 'vp-missing' does not exist.");
    }

    [Fact]
    public async Task UnknownVoiceId_Returns400()
    {
        (var status, string body) = await Post(ValidRequest(voiceId: "voice-missing"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorMessage(body).ShouldBe("Voice 'voice-missing' does not exist at the TTS provider.");
    }

    [Fact]
    public async Task VoiceProviderFailure_DoesNotBlockRequest()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Failure("provider unavailable"));

        (var status, _) = await Post(ValidRequest(voiceId: "voice-during-outage"));

        status.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ValidVoiceProfile_Returns202()
    {
        _voiceProfiles.GetAsync("vp-valid", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(MakeProfile()));

        (var status, string body) =
            await Post(ValidRequest(voiceId: null, voiceProfileId: "vp-valid"));

        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("Queued");
    }

    [Fact]
    public async Task ValidRequest_Returns202()
    {
        (var status, string body) = await Post(ValidRequest());
        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("Queued");
        body.ShouldContain("JobId");
    }

    [Fact]
    public async Task EmptyBody_Returns400()
    {
        (await Post()).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvalidJson_Returns400()
    {
        (await Post(raw: "{this is not valid json}")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingLocation_Returns400AndNeverGeocodes()
    {
        // Until 2026-07-28 this asserted a silent fallback to "Melbourne, VIC, Australia": a caller
        // who omitted Location got a 202 and a fully generated Melbourne bulletin, on any station.
        // The substitution also ran BEFORE OrchestrationValidation, making its own "Location is
        // required" check unreachable. Reproduced on dev three times before the fix.
        _geocodingProvider.GeocodeAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Result<CoverageArea>.Success(CoverageArea.Create(-37.8136, 144.9631, 5.0).Value));

        (var status, string body) = await Post(new OrchestrationRequestDto(30, null, null));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Location is required");
        await _geocodingProvider.DidNotReceive()
            .GeocodeAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GeocodingFails_Returns400()
    {
        _geocodingProvider.GeocodeAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Result<CoverageArea>.Failure("Geocoding failed"));

        (var status, string body) = await Post(new OrchestrationRequestDto(30, "NonexistentPlace", null));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Geocoding failed");
    }

    [Fact]
    public async Task MissingDuration_Returns400()
    {
        (var status, string body) = await Post(ValidRequest(null));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("TargetDurationSeconds");
    }

    [Fact]
    public async Task NegativeDuration_Returns400()
    {
        (await Post(ValidRequest(-5))).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task JobSaveFails_Returns500()
    {
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Storage unavailable"));
        (await Post(ValidRequest())).Status.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ScriptOnlyFlag_Returns202()
    {
        (await Post(new OrchestrationRequestDto(30, "Melbourne, VIC, Australia",
                new CoverageDto(-37.8136, 144.9631, 5.0), VoiceId: "voice-1", ScriptOnly: true)))
            .Status.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Preflight_UsesPrincipalScopedInteractiveGate()
    {
        (var status, _) = await Post(ValidRequest());

        status.ShouldBe(HttpStatusCode.Accepted);
        var key = _gate.LlmKeys.Last();
        key.Operation.ShouldBe("preflight");
        key.PrincipalId.ShouldBe("anonymous");
        key.Input.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PreflightFailure_Returns400AndCreatesNoJob()
    {
        _gate.FailLlm = true;
        _gate.FailureMessage = "preflight timed out";

        (var status, string body) = await Post(ValidRequest());

        status.ShouldBe(HttpStatusCode.BadRequest);
        ErrorMessage(body).ShouldBe("preflight timed out");
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _queuePublisher.DidNotReceive().PublishBroadcastMessageAsync(
            Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    private static MarketSchedule MakeSchedule(string marketId, string name) =>
        MarketSchedule.Create(marketId, name, "3AW", "Australia/Melbourne", "0 8 * * *",
            true, CoverageArea.Create(-37.8136, 144.9631, 5.0).Value,
            "voice-1", "/out", 30, 5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;

    private void SetupCapture()
    {
        _queuePublisher.When(pub => pub.PublishBroadcastMessageAsync(
                Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>()))
            .Do(ci => _capturedMessage = ci.Arg<BroadcastQueueMessage>());
    }

    [Fact]
    public async Task ResolveMarketId_MatchByExactId_ResolvesToCanonicalMarketId()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                new[] { MakeSchedule("mildura", "Mildura Drive Traffic") }));
        SetupCapture();
        var (status, _) = await Post(ValidRequest(location: "MILDURA",
            coverage: new CoverageDto(-34.1854, 142.1625, 5.0)));
        status.ShouldBe(HttpStatusCode.Accepted);
        _capturedMessage.ShouldNotBeNull();
        _capturedMessage!.MarketId.ShouldBe("mildura");
    }

    [Fact]
    public async Task ResolveMarketId_MatchByName_ResolvesToCanonicalMarketId()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                new[] { MakeSchedule("mildura", "Mildura Drive Traffic") }));
        SetupCapture();
        var (status, _) = await Post(ValidRequest(location: "Mildura Drive Traffic",
            coverage: new CoverageDto(-34.1854, 142.1625, 5.0)));
        status.ShouldBe(HttpStatusCode.Accepted);
        _capturedMessage.ShouldNotBeNull();
        _capturedMessage!.MarketId.ShouldBe("mildura");
    }

    [Fact]
    public async Task ResolveMarketId_MatchByStationDisplayName_ResolvesToCanonicalMarketId()
    {
        // The schedule's Station display name ("DCOAST 101.7") is a legitimate value copied
        // straight from the console. Before the fix it matched nothing, minting an orphan
        // market (Market=dcoast1017) and generating the bulletin with zero market config
        // (bug-bash 2026-08-05 #7).
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                new[]
                {
                    MarketSchedule.Create("demo-coast", "Demo Coast FM", "DCOAST 101.7",
                        "Australia/Melbourne", "0 8 * * *", true,
                        CoverageArea.Create(-37.8136, 144.9631, 5.0).Value,
                        "voice-1", "/out", 30, 5, 2, DateTime.UtcNow, DateTime.UtcNow).Value
                }));
        SetupCapture();
        var (status, _) = await Post(ValidRequest(location: "DCOAST 101.7",
            coverage: new CoverageDto(-37.8136, 144.9631, 5.0)));
        status.ShouldBe(HttpStatusCode.Accepted);
        _capturedMessage.ShouldNotBeNull();
        _capturedMessage!.MarketId.ShouldBe("demo-coast");
    }

    [Fact]
    public async Task ResolveMarketId_NoMatchingSchedule_FallsBackToRawLocation()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(new MarketSchedule[0]));
        SetupCapture();
        var (status, _) = await Post(ValidRequest(location: "Some Synthetic Eval Market",
            coverage: new CoverageDto(-37.8136, 144.9631, 5.0)));
        status.ShouldBe(HttpStatusCode.Accepted);
        _capturedMessage.ShouldNotBeNull();
        _capturedMessage!.MarketId.ShouldBe("Some Synthetic Eval Market");
    }

    [Fact]
    public async Task ResolveMarketId_ScheduleRepositoryFailure_FallsBackToRawLocation()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Failure("Table storage unavailable"));
        SetupCapture();
        var (status, _) = await Post(ValidRequest(location: "mildura",
            coverage: new CoverageDto(-34.1854, 142.1625, 5.0)));
        status.ShouldBe(HttpStatusCode.Accepted);
        _capturedMessage.ShouldNotBeNull();
        _capturedMessage!.MarketId.ShouldBe("mildura");
    }

    [Fact]
    public async Task UnconfiguredMarket_IsAcceptedButFlagged()
    {
        // 'Sydnye' (a typo) minted an orphan market and generated a real bulletin with none of
        // that market's priority roads, filler, guidance or pronunciation applied, and nothing on
        // screen said so (dev 2026-07-28). The endpoint is deliberately free-text for ad-hoc and
        // eval use, so this warns rather than rejects.
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));

        (var status, string body) = await Post(ValidRequest(location: "Sydnye"));

        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("does not match any configured station");
        await _jobRepository.Received().AddLogAsync(
            Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("does not match any configured station")),
            JobLogLevel.Warn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfiguredMarket_IsNotFlagged()
    {
        var schedule = MarketSchedule.Create("melbourne", "Melbourne Traffic", "KIIS 101.1",
            "Australia/Melbourne", "0 7 * * 1-5", true,
            CoverageArea.Create(-37.8136, 144.9631, 5.0).Value, "voice-1", "melbourne/traffic/",
            30, 15, 5, DateTime.UtcNow, DateTime.UtcNow).Value;
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([schedule]));

        (var status, string body) = await Post(ValidRequest(location: "melbourne"));

        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldNotContain("does not match any configured station");
        await _jobRepository.DidNotReceive().AddLogAsync(
            Arg.Any<string>(), Arg.Any<string>(), JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }

    // ─── The market guard that makes POST /api/broadcast safe at Operator ───────────────
    // Triggering became an Operator route on 2026-08-13, which is only defensible because the
    // handler scopes it. These two tests are that guarantee: an operator may generate for a
    // station they hold and nothing else, and a refusal must leave no job behind.

    private async Task<(HttpStatusCode Status, string Body)> PostAsOperatorAsync(
        string location, params string[] assignedMarkets)
    {
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Option<IReadOnlyList<string>>.Some(assignedMarkets)));
        var request = MockHttpRequestData.CreatePost(ValidRequest(location: location));
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                """{"userId":"op-1","userRoles":["Operator"],"identityProvider":"aad","claims":[{"typ":"http://schemas.microsoft.com/identity/claims/objectidentifier","val":"op-1"}]}""")));
        var response = await _sut.Run(request, Ctx());
        return await MockHttpRequestData.ReadResponseAsync(response);
    }

    [Fact]
    public async Task OperatorNotAssignedToTheResolvedMarket_Is403AndCreatesNoJob()
    {
        (var status, string body) = await PostAsOperatorAsync("melbourne", "sydney");

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("not assigned");
        // The guard runs BEFORE CreateJobAsync, so a refusal must not leave an orphan behind.
        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _queuePublisher.DidNotReceive()
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OperatorAssignedToTheResolvedMarket_IsAccepted()
    {
        (var status, _) = await PostAsOperatorAsync("melbourne", "melbourne");

        status.ShouldBe(HttpStatusCode.Accepted);
        await _jobRepository.Received().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }
}
