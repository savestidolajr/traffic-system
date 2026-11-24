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
///     The pre-draft bookend drop (bug-bash 2026-08-05 finding 4): when a slot cannot fit the
///     pair plus a minimum viable traffic read, the engine drops the bookend and airs the
///     traffic — instead of spiralling through 4 generations and 4+ TTS renders into the generic
///     filler cart with no traffic at all (measured live on job f9907416a3: 15s slot + 17-word
///     pair). The voice is calibrated at 150 wpm here — the pace measured on that live failure —
///     so the 17-word pair claims 6.8s and needs 15.2s with the 8.4s minimum viable traffic read.
/// </summary>
public class BroadcastOrchestratorBookendFitTests
{
    private const string TestScript = "Traffic is flowing smoothly across all major roads in Sydney today";
    // The live failure's pair: 7 intro + 10 outro = 17 words ≈ 6.8s at 150 wpm.
    private const string IntroText = "Looking over Adelaide today, your traffic update.";
    private const string OutroText = "That is it for Adelaide traffic on ZZ Test FM.";
    // A synthetic 18-word pair (7 + 11) for the retain-path guards — 7.2s at 150 wpm.
    private const string OutroText18 = "That is it for Adelaide traffic on ZZ Test FM today.";

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

    /// <summary>Every job-log line written via JobStatusUpdater, for asserting the drop decision is recorded.</summary>
    private readonly List<string> _jobLogMessages = [];

    public BroadcastOrchestratorBookendFitTests()
    {
        var incidentStateService = new IncidentStateService(
            _incidentStateRepository,
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));
        // 150 wpm => 0.4 s/word — the calibrated pace measured on the live 15s-slot failure
        // (18 bookend words ≈ 7.2s ≈ 48% of the slot).
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 3.87, 1.45));
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
        _jobRepository.AddLogAsync(
                Arg.Any<string>(), Arg.Do<string>(m => _jobLogMessages.Add(m)), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));

        SetupSuccessfulTake();
    }

    private static Option<ResolvedBookends> Pair() =>
        Option<ResolvedBookends>.Some(new ResolvedBookends(IntroText, OutroText, "bk-1", 3));

    private static Option<ResolvedBookends> Pair18() =>
        Option<ResolvedBookends>.Some(new ResolvedBookends(IntroText, OutroText18, "bk-1", 3));

    private static BroadcastWithTraffic Ctx(double slotSeconds) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = slotSeconds,
        JobId = "test-job-id",
        Location = "sydney",
        VoiceId = Option<string>.Some("test-voice-id"),
        Incidents = []
    };

    private void SetupSuccessfulTake()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(15.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(15.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(15.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(callInfo =>
            {
                string script = callInfo.ArgAt<string>(0);
                _synthesizedScripts.Add(script);
                return Result<AudioSynthesisResult>.Success(
                    new AudioSynthesisResult([1, 2, 3], script, 15.0)
                    { SpokenDurationSeconds = 15.0, SlotFilled = true });
            });
        _audioPipeline.StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>()).Returns([1, 2, 3, 4]);
        _audioPipeline.FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>(), Arg.Any<double?>())
            .Returns([1, 2, 3, 4, 5]);
        _audioPipeline.IsSlotFilled(Arg.Any<byte[]>(), Arg.Any<double>()).Returns(true);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private Job?[] CaptureSavedJob()
    {
        var slot = new Job?[1];
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => slot[0] = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return slot;
    }

    // ── The 15s live-failure shape: pair dropped, traffic aired ─────────

    [Fact]
    public async Task FifteenSecondSlot_WithSeventeenWordPair_DropsBookendAndAirsTraffic()
    {
        var saved = CaptureSavedJob();

        var result = await _sut.OrchestrateAsync(Ctx(15.0) with { Bookends = Pair() });

        // The bulletin is generated successfully — it does NOT fall through to the filler cart.
        result.IsSuccess.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // The read that was synthesised is the real traffic content, without the pair.
        _synthesizedScripts.ShouldNotBeEmpty();
        string spoken = _synthesizedScripts[^1];
        spoken.ShouldContain(TestScript);
        spoken.ShouldNotContain(IntroText);
        spoken.ShouldNotContain(OutroText);

        // The drop decision is recorded for the operator.
        _jobLogMessages.ShouldContain(m => m.Contains("Bookend pair dropped", StringComparison.Ordinal));

        // The job record carries no bookend provenance — nothing bookended aired.
        var persisted = saved[0];
        persisted.ShouldNotBeNull();
        persisted!.BookendId.HasValue.ShouldBeFalse();
    }

    // ── The ordinary case is provably unchanged ──────────────────────────

    [Fact]
    public async Task ThirtySecondSlot_WithEighteenWordPair_RetainsBookends()
    {
        var saved = CaptureSavedJob();

        var result = await _sut.OrchestrateAsync(Ctx(30.0) with { Bookends = Pair18() });

        result.IsSuccess.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // The pair is RETAINED: the read is wrapped exactly as before this fix.
        string spoken = _synthesizedScripts[^1];
        spoken.ShouldStartWith(IntroText);
        spoken.ShouldEndWith(OutroText18);
        spoken.ShouldContain(TestScript);

        // No drop was logged, and the bookend provenance is persisted.
        _jobLogMessages.ShouldNotContain(m => m.Contains("Bookend pair dropped", StringComparison.Ordinal));
        var persisted = saved[0];
        persisted.ShouldNotBeNull();
        persisted!.BookendId.Value.ShouldBe("bk-1");
        persisted.BookendVersion.Value.ShouldBe(3);
    }

    [Fact]
    public async Task SixteenSecondSlot_WithEighteenWordPair_StillRetainsBookends()
    {
        // 7.2s pair + 8.4s minimum viable traffic = 15.6s ≤ 16s: the slot genuinely fits the
        // pair, so the ordinary path (budget subtraction + compose) must be untouched.
        var result = await _sut.OrchestrateAsync(Ctx(16.0) with { Bookends = Pair18() });

        result.IsSuccess.ShouldBeTrue();
        string spoken = _synthesizedScripts[^1];
        spoken.ShouldStartWith(IntroText);
        spoken.ShouldEndWith(OutroText18);
        _jobLogMessages.ShouldNotContain(m => m.Contains("Bookend pair dropped", StringComparison.Ordinal));
    }
}
