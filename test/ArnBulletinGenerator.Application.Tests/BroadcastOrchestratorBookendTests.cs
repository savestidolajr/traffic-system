using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Station bookends in the generation pipeline: the fixed opener/closer wrap the finished read
///     AFTER every LLM stage, their spoken cost is taken out of the writer's word target BEFORE
///     drafting, and — the ordering rule the whole feature turns on — filler tops up the slot
///     BEFORE the outro rather than being stitched on after it.
/// </summary>
public class BroadcastOrchestratorBookendTests
{
    private const string TestScript = "Traffic is flowing smoothly across all major roads in Sydney today";
    private const string IntroText = "Looking over Sydney today";
    private const string OutroText = "That's it for Sydney traffic";

    private readonly IAudioPipelineService _audioPipeline = Substitute.For<IAudioPipelineService>();
    private readonly IBroadcastOutput _broadcastOutput = Substitute.For<IBroadcastOutput>();
    private readonly IDurationEstimator _durationEstimator = Substitute.For<IDurationEstimator>();
    private readonly IFillerAudioCacheService _fillerAudioCacheService = Substitute.For<IFillerAudioCacheService>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IScriptGenerator _scriptGenerator = Substitute.For<IScriptGenerator>();
    private readonly IScriptRefiner _scriptRefiner = Substitute.For<IScriptRefiner>();
    private readonly IScriptDirector _scriptDirector = Substitute.For<IScriptDirector>();
    private readonly IScriptReviewer _scriptReviewer = Substitute.For<IScriptReviewer>();
    private readonly BroadcastOrchestrator _sut;
    private readonly ITrafficProvider _trafficProvider = Substitute.For<ITrafficProvider>();
    private readonly IVoiceCalibrationService _voiceCalibrationService = Substitute.For<IVoiceCalibrationService>();
    private readonly IContentSafetyFilter _contentSafetyFilter = Substitute.For<IContentSafetyFilter>();
    private readonly IScriptSafetyReviewer _scriptSafetyReviewer = Substitute.For<IScriptSafetyReviewer>();
    private readonly IPriorityRoadsRepository _priorityRoadsRepository = Substitute.For<IPriorityRoadsRepository>();
    private readonly IFillerStatementsRepository _fillerStatementsRepository = Substitute.For<IFillerStatementsRepository>();
    private readonly IFutureLessonRepository _marketFeedbackRepository = Substitute.For<IFutureLessonRepository>();
    private readonly IGuidanceRepository _guidanceRepository = Substitute.For<IGuidanceRepository>();
    private readonly IEditorialSettingsRepository _editorialSettingsRepository =
        Substitute.For<IEditorialSettingsRepository>();
    private readonly IWeatherProvider _weatherProvider = Substitute.For<IWeatherProvider>();
    private readonly IFuelPriceProvider _fuelPriceProvider = Substitute.For<IFuelPriceProvider>();
    private readonly IRoutingProvider _routingProvider = Substitute.For<IRoutingProvider>();
    private readonly IBulletinFallbackService _bulletinFallbackService = Substitute.For<IBulletinFallbackService>();
    // Covering a slot must finish at the playout drop; the orchestrator delegates
    // that sequence to IJobService.DeliverCoveredSlotAsync.
    private readonly IJobService _jobService = Substitute.For<IJobService>();
    private readonly IIncidentStateRepository _incidentStateRepository = Substitute.For<IIncidentStateRepository>();

    /// <summary>Every script handed to the synthesiser, in call order.</summary>
    private readonly List<string> _synthesizedScripts = [];

    public BroadcastOrchestratorBookendTests()
    {
        var incidentStateService = new IncidentStateService(
            _incidentStateRepository,
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));
        // A realistic ARN pace (160 wpm => 0.375 s/word). The bookend arithmetic these tests
        // assert only means anything at a real speaking rate: at the 20 wpm some sibling harnesses
        // use, nine words of bookend would claim 27 of the 30-second slot.
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(160, 3.87, 1.45));
        _contentSafetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([]));
        _fillerStatementsRepository.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _marketFeedbackRepository.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([]));
        _guidanceRepository.GetForScopesAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _editorialSettingsRepository.GetApplicableAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EditorialSettings>>.Success([]));
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Failure("not configured"));
        _fuelPriceProvider.GetFuelPricesAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<FuelPriceReport>.Failure("not configured"));

        // Covering an unairable slot finishes at the playout drop via
        // IJobService.DeliverCoveredSlotAsync; the sequence itself is pinned in JobServiceTests.
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService,
                _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            incidentStateService, _jobRepository,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0 }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());

        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));

        SetupSuccessfulTake();
    }

    /// <summary>The station's pair. Either line may be blank — a pair is one assignment.</summary>
    private static Option<ResolvedBookends> Pair(string intro, string outro) =>
        Option<ResolvedBookends>.Some(new ResolvedBookends(intro, outro, "bk-1", 3));

    private static Option<ResolvedBookends> Both => Pair(IntroText, OutroText);

    private static BroadcastWithTraffic Ctx() => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 30.0,
        JobId = "test-job-id",
        Location = "sydney",
        VoiceId = Option<string>.Some("test-voice-id"),
        Incidents = []
    };

    /// <summary>
    ///     A clean take that fills the slot. <paramref name="estimatedBodySeconds" /> is what the
    ///     duration estimator reports for the drafted body, which is what drives filler selection.
    /// </summary>
    private void SetupSuccessfulTake(double estimatedBodySeconds = 30.0)
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(estimatedBodySeconds);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(estimatedBodySeconds);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(callInfo =>
            {
                string script = callInfo.ArgAt<string>(0);
                _synthesizedScripts.Add(script);
                return Result<AudioSynthesisResult>.Success(
                    new AudioSynthesisResult([1, 2, 3], script, 30.0)
                    { SpokenDurationSeconds = 30.0, SlotFilled = true });
            });
        _audioPipeline.StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>()).Returns([1, 2, 3, 4]);
        _audioPipeline.FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>(), Arg.Any<double?>())
            .Returns([1, 2, 3, 4, 5]);
        _audioPipeline.IsSlotFilled(Arg.Any<byte[]>(), Arg.Any<double>()).Returns(true);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    /// <summary>
    ///     Captures the last job saved. Returns a one-slot box, not the job itself: the
    ///     substitute writes the reference during the call, long after this method returns.
    /// </summary>
    private Job?[] CaptureSavedJob()
    {
        var slot = new Job?[1];
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => slot[0] = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return slot;
    }

    // ── Wrapping ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BookendPair_WrapsTheSynthesisedRead()
    {
        var result = await _sut.OrchestrateAsync(Ctx() with { Bookends = Both });

        result.IsSuccess.ShouldBeTrue();
        _synthesizedScripts.ShouldNotBeEmpty();
        string spoken = _synthesizedScripts[^1];
        spoken.ShouldStartWith(IntroText);
        spoken.ShouldEndWith(OutroText);
        spoken.ShouldContain(TestScript);
    }

    [Fact]
    public async Task NoBookend_LeavesTheReadExactlyAsBefore()
    {
        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        _synthesizedScripts[^1].ShouldBe(TestScript);
    }

    [Fact]
    public async Task IntroOnlyPair_DoesNotAddACloser()
    {
        await _sut.OrchestrateAsync(Ctx() with { Bookends = Pair(IntroText, "") });

        string spoken = _synthesizedScripts[^1];
        spoken.ShouldStartWith(IntroText);
        spoken.ShouldNotContain(OutroText);
    }

    [Fact]
    public async Task OutroOnlyPair_DoesNotAddAnOpener()
    {
        await _sut.OrchestrateAsync(Ctx() with { Bookends = Pair("", OutroText) });

        string spoken = _synthesizedScripts[^1];
        spoken.ShouldNotContain(IntroText);
        spoken.ShouldEndWith(OutroText);
    }

    // ── The ordering rule: filler before the outro, never stitched after it ──

    [Fact]
    public async Task WithAnOutro_FillerIsSpokenInline_BeforeTheCloser_AndNeverStitched()
    {
        // A 20s body against a 30s slot leaves ~10s; the bookends claim their share first and a
        // statement is selected into what remains. With an outro present that statement must ride
        // the SAME take, before the closer — a stitched clip would play after it.
        const string fillerText = "Keep left unless overtaking";
        SetupSuccessfulTake(estimatedBodySeconds: 20.0);
        var ctx = Ctx() with
        {
            Bookends = Both,
            FillerStatements = [new FillerStatement("f1", fillerText)]
        };

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        string spoken = _synthesizedScripts[^1];
        spoken.ShouldContain(fillerText);
        spoken.IndexOf(fillerText, StringComparison.Ordinal)
            .ShouldBeLessThan(spoken.IndexOf(OutroText, StringComparison.Ordinal));
        // Inline means no cached-audio resolution and no stitching for that statement.
        await _fillerAudioCacheService.DidNotReceive().ResolveAsync(
            Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAnOutro_FillerStillResolvesToStitchedAudio()
    {
        // The legacy path is untouched for the stations that have no closer: filler keeps its
        // exact measured duration by being stitched rather than spoken in the take.
        const string fillerText = "Keep left unless overtaking";
        SetupSuccessfulTake(estimatedBodySeconds: 20.0);
        _fillerAudioCacheService.ResolveAsync(Arg.Any<FillerStatement>(), Arg.Any<string>(),
                Arg.Any<VoiceSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([9], 4.0, [])));
        var ctx = Ctx() with
        {
            Bookends = Pair(IntroText, ""),
            FillerStatements = [new FillerStatement("f1", fillerText)]
        };

        await _sut.OrchestrateAsync(ctx);

        await _fillerAudioCacheService.Received(1).ResolveAsync(
            Arg.Is<FillerStatement>(s => s.Id == "f1"), Arg.Any<string>(), Arg.Any<VoiceSettings>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        // …and the filler text is NOT inside the synthesised take, because the audio joins after it.
        _synthesizedScripts[^1].ShouldNotContain(fillerText);
    }

    // ── Budget ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BookendsReduceTheWritersWordTarget()
    {
        // The writer must be sized for the slot MINUS the bookends, or every bookended bulletin
        // drafts to the full slot and overshoots.
        var targets = new List<int>();
        _scriptGenerator.GenerateScriptAsync(
                Arg.Do<BroadcastWithTraffic>(c =>
                {
                    if (c.TargetWordCountOverride.HasValue) targets.Add(c.TargetWordCountOverride.Value);
                }),
                Arg.Any<double>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        await _sut.OrchestrateAsync(Ctx());
        int withoutBookends = targets[0];

        targets.Clear();
        await _sut.OrchestrateAsync(Ctx() with { Bookends = Both });
        int withBookends = targets[0];

        withBookends.ShouldBeLessThan(withoutBookends);
    }

    // ── Provenance ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolvedBookendIdAndVersion_ArePersistedOnTheJob()
    {
        var slot = CaptureSavedJob();

        await _sut.OrchestrateAsync(Ctx() with { Bookends = Both });

        var saved = slot[0];
        saved.ShouldNotBeNull();
        saved!.BookendId.Value.ShouldBe("bk-1");
        saved.BookendVersion.Value.ShouldBe(3);
    }
}
