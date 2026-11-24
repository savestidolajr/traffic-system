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
///     The per-station data-source switch, WIRED end to end through the orchestrator. The domain
///     predicate is unit-tested beside <c>EditorialSettings</c>; what this file pins is the part a
///     unit test cannot see: that a disabled feed is never REQUESTED, and that it reports
///     <see cref="SourceFetchOutcome.Skipped" /> rather than a failure.
///     <para>
///         Both halves matter for the reason the feature was built. Traffic SA has returned a
///         deterministic 403 to our Azure egress since 2026-08-19 (AWS WAF at their CloudFront
///         distribution, verified from the Function App's own outbound IP), so every Sydney and
///         Cairns bulletin was making a blocked request and writing an operator-visible failure for
///         a jurisdiction 1,000 km outside its coverage. "Not requested" is the fix; "Skipped rather
///         than Failed" is what keeps it out of the evidence rail, since the console drops Skipped
///         as normal operation while still showing anything that genuinely broke.
///     </para>
/// </summary>
public class BroadcastOrchestratorSourceToggleTests
{
    private const string TestScript = "Traffic is flowing smoothly across all major roads in Sydney today";

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
    private readonly IFillerStatementsRepository _fillerStatementsRepository =
        Substitute.For<IFillerStatementsRepository>();
    private readonly IFutureLessonRepository _futureLessonRepository = Substitute.For<IFutureLessonRepository>();
    private readonly IGuidanceRepository _guidanceRepository = Substitute.For<IGuidanceRepository>();
    private readonly IEditorialSettingsRepository _editorialSettingsRepository =
        Substitute.For<IEditorialSettingsRepository>();
    private readonly IWeatherProvider _weatherProvider = Substitute.For<IWeatherProvider>();
    private readonly IFuelPriceProvider _fuelPriceProvider = Substitute.For<IFuelPriceProvider>();
    private readonly IRoutingProvider _routingProvider = Substitute.For<IRoutingProvider>();
    private readonly IBulletinFallbackService _bulletinFallbackService = Substitute.For<IBulletinFallbackService>();
    private readonly IIncidentStateRepository _incidentStateRepository = Substitute.For<IIncidentStateRepository>();

    private readonly RecordingFeed _sa = new(OfficialTrafficSources.TrafficSa);
    private readonly RecordingFeed _nsw = new(OfficialTrafficSources.LiveTrafficNsw);

    [Fact]
    public async Task ADisabledSourceIsNeverRequested()
    {
        GivenDisabled(OfficialTrafficSources.TrafficSa);

        (await Build().OrchestrateAsync(Ctx())).IsSuccess.ShouldBeTrue();

        _sa.Calls.ShouldBe(0);
        _nsw.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ADisabledSourceReportsSkippedRatherThanFailed()
    {
        GivenDisabled(OfficialTrafficSources.TrafficSa);

        (await Build().OrchestrateAsync(Ctx())).IsSuccess.ShouldBeTrue();

        var diagnostic = SavedDiagnostics()
            .SingleOrDefault(d => d.SourceId == OfficialTrafficSources.TrafficSa);
        diagnostic.ShouldNotBeNull("Traffic SA reported no diagnostic at all");
        diagnostic!.Outcome.ShouldBe(SourceFetchOutcome.Skipped);
    }

    [Fact]
    public async Task WithNothingDisabledEverySourceIsStillConsulted()
    {
        GivenSettings([]);

        (await Build().OrchestrateAsync(Ctx())).IsSuccess.ShouldBeTrue();

        _sa.Calls.ShouldBe(1);
        _nsw.Calls.ShouldBe(1);
    }

    /// <summary>
    ///     The guardrail that stops this becoming a mute button: a source left ON that fails must
    ///     still surface as Failed, because the console shows Failed and hides Skipped.
    /// </summary>
    [Fact]
    public async Task ASourceLeftOnThatFailsIsStillReportedAsFailed()
    {
        GivenSettings([]);
        _sa.FailWith("Response status code does not indicate success: 403 (Forbidden).");

        (await Build().OrchestrateAsync(Ctx())).IsSuccess.ShouldBeTrue();

        _sa.Calls.ShouldBe(1);
        var diagnostic = SavedDiagnostics()
            .SingleOrDefault(d => d.SourceId == OfficialTrafficSources.TrafficSa);
        diagnostic.ShouldNotBeNull("Traffic SA reported no diagnostic at all");
        diagnostic!.Outcome.ShouldBe(SourceFetchOutcome.Failed);
        diagnostic.Detail.ShouldBe("HTTP 403 (forbidden)");
    }

    private void GivenDisabled(params string[] disabled) =>
        GivenSettings([
            new EditorialSettings(EditorialScope.Market, "sydney", DisabledTrafficSources: disabled)
        ]);

    private void GivenSettings(EditorialSettings[] stored) =>
        _editorialSettingsRepository
            .GetApplicableAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EditorialSettings>>.Success(stored));

    /// <summary>The diagnostics the orchestrator actually persisted on the job.</summary>
    private IReadOnlyList<SourceFetchDiagnostic> SavedDiagnostics()
    {
        var saved = _jobRepository.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IJobRepository.SaveJobAsync))
            .Select(c => (Job)c.GetArguments()[0]!)
            .LastOrDefault(j => j.SourceDiagnostics.Count > 0);
        saved.ShouldNotBeNull("no job was saved carrying source diagnostics");
        return saved!.SourceDiagnostics;
    }

    private static BroadcastWithTraffic Ctx() => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 30.0,
        JobId = "test-job-id",
        Location = "sydney",
        MarketId = Option<string>.Some("sydney"),
        VoiceId = Option<string>.Some("test-voice-id"),
        Incidents = []
    };

    /// <summary>
    ///     A feed that records whether it was asked. NSubstitute would work, but a hand-rolled
    ///     double makes "was this endpoint contacted at all" the explicit subject of the test.
    /// </summary>
    private sealed class RecordingFeed(string sourceId) : IOfficialTrafficFeed
    {
        private string? _failure;

        public int Calls { get; private set; }
        public string SourceId { get; } = sourceId;

        public SourceCapabilities Capabilities { get; } = new(
            sourceId, "test-v1",
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        public void FailWith(string error) => _failure = error;

        public Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
            CoverageArea area, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_failure is null
                ? Result<IReadOnlyList<OfficialTrafficEvent>>.Success([])
                : Result<IReadOnlyList<OfficialTrafficEvent>>.Failure(_failure));
        }
    }

    private BroadcastOrchestrator Build()
    {
        var incidentStateService = new IncidentStateService(
            _incidentStateRepository,
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([
                new TrafficCorridor("Parramatta Road", 3, 3, 500, 100, "construction",
                    "construction on Parramatta Road",
                    [
                        new TrafficIncident("inc-1", "works on Parramatta Road", 3, "Parramatta Road",
                            "construction", 500, 3)
                    ])
            ]));
        _trafficProvider.GetIncidentsAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficIncident>>.Success([]));

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(160, 5.8, 1.45));
        _contentSafetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([]));
        _fillerStatementsRepository.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _futureLessonRepository.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([]));
        _guidanceRepository.GetForScopesAsync(Arg.Any<string>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Failure("not configured"));
        _fuelPriceProvider.GetFuelPricesAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<FuelPriceReport>.Failure("not configured"));

        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(Job.Create(
                "test-job", "Test Station", "sydney", 30, DateTime.UtcNow,
                latitude: Option<double>.Some(-33.8688),
                longitude: Option<double>.Some(151.2093),
                radiusKm: Option<double>.Some(35.0),
                coverage: Option<CoverageArea>.Some(
                    CoverageArea.Create(-33.8688, 151.2093, 35.0).Value)).Value));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new FallbackOutcome(FallbackKind.None));

        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(30.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(),
                lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, 30.0)
                { SpokenDurationSeconds = 30.0, SlotFilled = true }));

        return new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService,
                _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider,
                OfficialFeeds: [_nsw, _sa]),
            incidentStateService, _jobRepository,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0 }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            new MemoryCache(new MemoryCacheOptions()),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository, _fillerStatementsRepository,
            _futureLessonRepository, _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService, Substitute.For<IJobService>(),
            Substitute.For<ILogger<BroadcastOrchestrator>>());
    }
}
