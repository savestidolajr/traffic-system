using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class CurationStateMachineTests
{
    private readonly IAudioPipelineService _audio = Substitute.For<IAudioPipelineService>();
    private readonly IBroadcastOutput _output = Substitute.For<IBroadcastOutput>();
    private readonly IDurationEstimator _duration = Substitute.For<IDurationEstimator>();
    private readonly IFillerAudioCacheService _fillerCache = Substitute.For<IFillerAudioCacheService>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IScriptGenerator _generator = Substitute.For<IScriptGenerator>();
    private readonly IScriptRefiner _refiner = Substitute.For<IScriptRefiner>();
    private readonly IScriptDirector _director = Substitute.For<IScriptDirector>();
    private readonly IScriptReviewer _reviewer = Substitute.For<IScriptReviewer>();
    private readonly ITrafficProvider _traffic = Substitute.For<ITrafficProvider>();
    private readonly IVoiceCalibrationService _calibration = Substitute.For<IVoiceCalibrationService>();
    private readonly IContentSafetyFilter _safety = Substitute.For<IContentSafetyFilter>();
    private readonly IScriptSafetyReviewer _safetyReviewer = Substitute.For<IScriptSafetyReviewer>();
    private readonly IPriorityRoadsRepository _priorityRoads = Substitute.For<IPriorityRoadsRepository>();
    private readonly IFillerStatementsRepository _fillers = Substitute.For<IFillerStatementsRepository>();
    private readonly IFutureLessonRepository _lessons = Substitute.For<IFutureLessonRepository>();
    private readonly IGuidanceRepository _guidance = Substitute.For<IGuidanceRepository>();
    private readonly IEditorialSettingsRepository _settings = Substitute.For<IEditorialSettingsRepository>();
    private readonly IWeatherProvider _weather = Substitute.For<IWeatherProvider>();
    private readonly IFuelPriceProvider _fuel = Substitute.For<IFuelPriceProvider>();
    private readonly IRoutingProvider _routing = Substitute.For<IRoutingProvider>();
    private readonly IIncidentStateRepository _incidentStates = Substitute.For<IIncidentStateRepository>();
    private readonly BroadcastOrchestrator _sut;
    private readonly Job _job;

    public CurationStateMachineTests()
    {
        var incidentState = new IncidentStateService(_incidentStates, Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()), TimeProvider.System);
        _incidentStates.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));
        _calibration.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(),
            Arg.Any<CancellationToken>(), Arg.Any<Option<string>>()).Returns(new CalibrationMetrics(20, 5.8, 1.45));
        _safety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
        _priorityRoads.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([]));
        _fillers.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _lessons.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([]));
        _guidance.GetForScopesAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _settings.GetApplicableAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EditorialSettings>>.Success([]));
        _weather.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Failure("not configured"));
        _fuel.GetFuelPricesAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<FuelPriceReport>.Failure("not configured"));
        _fillerCache.ResolveAsync(Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<ResolvedFillerAudio>.Success(new([], 0, [])));
        _jobs.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobs.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Result<Option<Job>>.Success(Option<Job>.None));

        _job = Job.Create("curation-job", "Station", "sydney", 30, DateTime.UtcNow,
            Option<double>.Some(-33.8), Option<double>.Some(151.2), Option<double>.Some(25)).Value;
        _jobs.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(_job));

        _sut = new BroadcastOrchestrator(
            new ScriptPipelineRoles(_generator, _reviewer, _refiner,
                _director, _safetyReviewer, _safety),
            new AudioPipelineServices(_audio, _duration, _fillerCache, _calibration, _output),
            new ContentProviders(_traffic, _weather, _fuel, _routing), incidentState, _jobs,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0 }), Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()), Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(), new NoOpBroadcastMetrics(), _priorityRoads, _fillers, _lessons, _guidance,
            _settings, Options.Create(new MonitoredRoutesOptions()),
            Substitute.For<IBulletinFallbackService>(), Substitute.For<IJobService>(),
            Substitute.For<ILogger<BroadcastOrchestrator>>());

        _traffic.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([
                new TrafficCorridor("M1", 2, 1, 1000, 50, "congestion", "Slow on M1", [
                    new TrafficIncident("incident-1", "Slow on M1", 2, "M1", "congestion")])
            ]));
        _generator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
            Arg.Any<CancellationToken>()).Returns(Result<DraftedScript>.Success(new DraftedScript("Traffic is slow on the M1.", [])));
        _duration.EstimateDuration(Arg.Any<string>()).Returns(20);
        _duration.GetDuration(Arg.Any<byte[]>()).Returns(30);
        _audio.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(),
            lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(new([1, 2, 3], "Traffic is slow on the M1.", 30)
            { SpokenDurationSeconds = 30, SlotFilled = true }));
        _output.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private static BroadcastRequest Request() => new()
    {
        JobId = "curation-job",
        Location = "sydney",
        TargetDurationSeconds = 30,
        Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value,
        VoiceId = Option<string>.Some("voice-1"),
        Content = BulletinContentSelection.Default
    };

    [Fact]
    public async Task FetchAndGenerate_WhenCurationIsDisabled_GeneratesInSameInvocation()
    {
        var result = await _sut.OrchestrateAsync(Request(), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error);
        _job.Status.ShouldNotBe(JobStatus.AwaitingCuration);
        await _generator.Received(1).GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fetch_WhenCurationIsEnabled_ParksSnapshotWithoutGenerating()
    {
        var result = await _sut.OrchestrateAsync(Request(), Phase.Fetch, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error);
        _job.Status.ShouldBe(JobStatus.AwaitingCuration);
        _job.GeneratorInput.ShouldNotBeNull();
        await _generator.DidNotReceive().GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Generate_OnAwaitingCurationJob_ResumesParkedSnapshot()
    {
        var context = new BroadcastWithTraffic
        {
            JobId = _job.JobId,
            Location = "sydney",
            TargetDurationSeconds = 30,
            Traffic = "Slow on M1",
            Incidents = [],
            Corridors = []
        };
        _job.GeneratorInput = GeneratorInputSnapshot.FromContext(context);
        _job.TransitionTo(JobStatus.HereFetched, utcNow: DateTime.UtcNow);
        _job.TransitionTo(JobStatus.IncidentsSelected, utcNow: DateTime.UtcNow);
        _job.TransitionTo(JobStatus.AwaitingCuration, utcNow: DateTime.UtcNow);

        var result = await _sut.OrchestrateAsync(Request(), Phase.Generate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error);
        await _generator.Received(1).GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }
}
