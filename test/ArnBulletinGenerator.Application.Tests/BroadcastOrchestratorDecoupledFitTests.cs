using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Duration fitting always uses the same bounded loop; a pure duration underrun extends the
///     validated take and re-fits instead of redrafting the full review pipeline.
/// </summary>
public class BroadcastOrchestratorDecoupledFitTests
{
    private const string TestScript = "Traffic is flowing smoothly across all major roads in Sydney today";
    private const string CeilingScript =
        "Traffic is flowing smoothly across the major roads in Sydney today. " +
        "Traffic is looking good along the M1 and M2 across the network. " +
        "Roads are generally clear across the city at present.";

    private readonly IAudioPipelineService _audioPipeline = Substitute.For<IAudioPipelineService>();
    private readonly IBroadcastOutput _broadcastOutput = Substitute.For<IBroadcastOutput>();
    private readonly IDurationEstimator _durationEstimator = Substitute.For<IDurationEstimator>();
    private readonly IFillerAudioCacheService _fillerAudioCacheService = Substitute.For<IFillerAudioCacheService>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IScriptGenerator _scriptGenerator = Substitute.For<IScriptGenerator>();
    private readonly IScriptRefiner _scriptRefiner = Substitute.For<IScriptRefiner>();
    private readonly IScriptDirector _scriptDirector = Substitute.For<IScriptDirector>();
    private readonly IScriptReviewer _scriptReviewer = Substitute.For<IScriptReviewer>();
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
    private readonly IIncidentStateRepository _incidentStateRepository = Substitute.For<IIncidentStateRepository>();

    private BroadcastOrchestrator Build(
        BroadcastOptions options,
        IDurationPredictor? predictor = null,
        ISpokenDurationModel? spokenDurationModel = null,
        SpeechDurationOptions? durationOptions = null)
    {
        var incidentStateService = new IncidentStateService(
            _incidentStateRepository,
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(160, 5.8, 1.45));
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

        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FallbackOutcome(FallbackKind.None));

        return new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter, predictor, spokenDurationModel),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            incidentStateService, _jobRepository,
            Options.Create(options),
            Options.Create(new DeliveryOptions()),
            Options.Create(durationOptions ?? new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository, _fillerStatementsRepository,
            _marketFeedbackRepository, _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService, Substitute.For<IJobService>(),
            Substitute.For<ILogger<BroadcastOrchestrator>>());
    }

    private static BroadcastWithTraffic Ctx(double targetDurationSeconds = 30.0) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = targetDurationSeconds,
        JobId = "test-job-id",
        Location = "sydney",
        VoiceId = Option<string>.Some("test-voice-id"),
        Incidents = []
    };

    private void SetupDraftAndRefine()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        // Refiner echoes its input — the extend path exercises the wiring without growing content.
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(30.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private void SetupSynth(bool slotFilled, double spokenSeconds)
    {
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                // maxSlowDownFactorOverride is matched EXPLICITLY: omitting an optional parameter here
                // makes NSubstitute pin it to its default of null, so the moment an editorial override
                // supplies a real factor the stub stops matching and returns a null Task.
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), Arg.Any<double?>(),
                lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, spokenSeconds)
                { SpokenDurationSeconds = spokenSeconds, SlotFilled = slotFilled }));
    }
    // The audio pipeline's SlotFilled verdict is the ONLY fill ruler. The engine used to re-derive
    // a slow-down tolerance band of its own and accept a SlotFilled=false take inside it; on dev
    // 34402fbb (2026-09-02) the engine-side rulers disagreed with the pipeline about a 22.0s
    // file and failed an airable take. A near-miss the fitter CAN stretch is reported by the
    // pipeline as SlotFilled=true on its own output, so the engine has nothing to add.
    [Fact]
    public async Task NearMissUnderrun_ReportedUnfilledByThePipeline_IsContentLimited()
    {
        var sut = Build(new BroadcastOptions
        {
            MaxGenerationRetries = 0,
            MaxReviewRefineRetries = 0
        });
        SetupDraftAndRefine();
        SetupSynth(slotFilled: false, spokenSeconds: 20.1);

        var result = await sut.OrchestrateAsync(Ctx(22.0));

        result.IsFailure.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeepUnderrun_OutsideSlowDownHeadroom_RemainsContentLimited()
    {
        var sut = Build(new BroadcastOptions
        {
            MaxGenerationRetries = 0,
            MaxReviewRefineRetries = 0
        });
        SetupDraftAndRefine();
        SetupSynth(slotFilled: false, spokenSeconds: 14.9);

        var result = await sut.OrchestrateAsync(Ctx(22.0));

        result.IsFailure.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ARN, 2026-08-24 (non-negotiable): "the bulletin must NOT leave empty space at the end of the
    // audio, it must always try to 22s spoken audio". This REPLACES the former
    // CeilingTrimFollowedByUnderrun_IsNotContentLimited, which asserted the opposite — that a
    // pre-synthesis trim excuses whatever underrun follows it. That policy shipped 4.1s of silence
    // on dev d8bd52b5 and 1.2s on test a9109443, both logged as "Audio filled to slot", because a
    // trim driven by a wrong prediction certified its own hole. The excuse is now bounded by the
    // MEASURED read: a residual the silence pad can close still passes, a hole does not.
    [Fact]
    public async Task CeilingTrimFollowedByUnderrun_IsContentLimited()
    {
        var model = Substitute.For<ISpokenDurationModel>();
        model.Predict(Arg.Any<string>(), Arg.Any<DurationVoiceKey>())
            .Returns(Option<PredictedDuration>.Some(new PredictedDuration(60.0, 0.0)));
        var sut = Build(
            new BroadcastOptions
            {
                MaxGenerationRetries = 0,
                MaxReviewRefineRetries = 0
            },
            spokenDurationModel: model);
        SetupDraftAndRefine();
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(CeilingScript, [])));
        // 20.0s of speech in a 22s slot: two seconds of dead air, well past MaxSilencePadSeconds.
        SetupSynth(slotFilled: false, spokenSeconds: 20.0);

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task FirstAttemptFills_GeneratesContentOnce_SingleSynth()
    {
        var sut = Build(new BroadcastOptions { MaxReviewRefineRetries = 0 });
        SetupDraftAndRefine();
        SetupSynth(slotFilled: true, spokenSeconds: 30.0);

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        // Content drafted exactly once; the slot filled on the first read, so one TTS run only.
        await _scriptGenerator.Received(1).GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
        await _audioPipeline.Received(1).SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanInBandDraft_SkipsLlmReview()
    {
        var sut = Build(new BroadcastOptions
        {
            MaxReviewRefineRetries = 3
        });
        // Low WPM → tiny word band, so the short clean draft is in-band (not under-filling).
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(20, 5.8, 1.45));
        SetupDraftAndRefine();
        SetupSynth(slotFilled: true, spokenSeconds: 30.0);
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        await sut.OrchestrateAsync(Ctx());

        // Deterministic detectors were clean → the LLM reviewer was never invoked.
        await _scriptReviewer.DidNotReceive().ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    [Fact]
    public async Task ReviewLoopSkipped_CeilingStillTrimsBeforeSynthesis()
    {
        var model = Substitute.For<ISpokenDurationModel>();
        model.Predict(Arg.Any<string>(), Arg.Any<DurationVoiceKey>())
            .Returns(Option<PredictedDuration>.Some(new PredictedDuration(60.0, 0.0)));
        var sut = Build(
            new BroadcastOptions { MaxReviewRefineRetries = 0 },
            spokenDurationModel: model);
        SetupDraftAndRefine();
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(CeilingScript, [])));
        SetupSynth(slotFilled: true, spokenSeconds: 30.0);

        var outcome = await sut.OrchestrateAsync(Ctx());
        outcome.IsSuccess.ShouldBeTrue(outcome.IsFailure ? outcome.Error : "generation failed");

        var ceilingChoice = ScriptReviewLoop.ResolveCeilingTarget(
            Option<int>.None, 30.0, new SpeechDurationOptions().MaxSpeedUpFactor,
            new SpeechDurationOptions().MaxSilencePadSeconds,
            new SpeechDurationOptions().MaxSlowDownFactor,
            0.4,
            WordCounter.Count(CeilingScript),
            Option<PredictedDuration>.Some(new PredictedDuration(60.0, 0.0)));
        string expected = ScriptWordCeiling.TrimToCeiling(
            CeilingScript, ceilingChoice.TargetWords, minimumWords: ceilingChoice.FloorWords);
        var synthesisCall = _audioPipeline.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IAudioPipelineService.SynthesizeWithDurationTargetAsync));
        string actualSynth = synthesisCall.GetArguments()[0] as string ?? string.Empty;
        actualSynth.ShouldBe(expected);
        await _scriptReviewer.DidNotReceive().ReviewScriptAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(),
            Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }



}
