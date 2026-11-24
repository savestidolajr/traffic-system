using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     The Desk's enforcement WIRING, end to end through the orchestrator: an operator's Omitted
///     verdict must reach the fetch, so the draft agent never sees the event.
///     <para>
///         The records carry HERE-shaped identities and the verdict is keyed the way the Event
///         Ledger writes it (<c>here:original:{originalId}</c>) against a record whose CURRENT id has
///         since been re-stamped. That is one of the two defects this covers: an earlier version
///         matched bare ids on both sides, which no HERE record ever has, so this seam passed while
///         the deployed filter logged "0 matching" on every fetch in every market (test, 2026-08-24).
///     </para>
///     <para>
///         The stub provider applies the <see cref="IncidentExclusions" /> it is handed, because that
///         is now the contract (<see cref="ITrafficProvider.GetCorridorsAsync" />): exclusion happens
///         before aggregation so no corridor field can describe an excluded record. A stub that
///         ignored the argument would assert nothing about what the builder passes down.
///     </para>
/// </summary>
public class BroadcastOrchestratorDeskOmissionTests
{
    // HERE re-stamps Id on every revision; OriginalId is the event's stable identity.
    private const string OmittedRecordId = "2374869809755945913";
    private const string OmittedOriginalId = "1337132862205035110";
    private const string OmittedVerdictKey = $"here:original:{OmittedOriginalId}";
    private const string KeptRecordId = "1348655797593623895";
    private const string KeptOriginalId = "1603459432390544747";
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
    private readonly IFillerStatementsRepository _fillerStatementsRepository = Substitute.For<IFillerStatementsRepository>();
    private readonly IFutureLessonRepository _futureLessonRepository = Substitute.For<IFutureLessonRepository>();
    private readonly IGuidanceRepository _guidanceRepository = Substitute.For<IGuidanceRepository>();
    private readonly IEditorialSettingsRepository _editorialSettingsRepository =
        Substitute.For<IEditorialSettingsRepository>();
    private readonly IWeatherProvider _weatherProvider = Substitute.For<IWeatherProvider>();
    private readonly IFuelPriceProvider _fuelPriceProvider = Substitute.For<IFuelPriceProvider>();
    private readonly IRoutingProvider _routingProvider = Substitute.For<IRoutingProvider>();
    private readonly IBulletinFallbackService _bulletinFallbackService = Substitute.For<IBulletinFallbackService>();
    private readonly IIncidentStateRepository _incidentStateRepository = Substitute.For<IIncidentStateRepository>();
    private readonly IEventVerdictRepository _eventVerdictRepository = Substitute.For<IEventVerdictRepository>();

    [Fact]
    public async Task OmittedEvent_NeverReachesTheDraftAgent()
    {
        GivenVerdicts(new EventVerdict(
            "sydney", OmittedVerdictKey, VerdictKind.Omitted, null, "op", DateTimeOffset.UtcNow));
        var sut = Build();

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        var seen = DraftedContext();
        var keys = seen.Corridors.SelectMany(c => c.Incidents).Select(i => i.Id).ToList();
        keys.ShouldContain(KeptRecordId);
        keys.ShouldNotContain(OmittedRecordId);
    }

    [Fact]
    public async Task WithNoVerdicts_EveryEventReachesTheDraftAgent()
    {
        GivenVerdicts();
        var sut = Build();

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        var keys = DraftedContext().Corridors.SelectMany(c => c.Incidents).Select(i => i.Id).ToList();
        keys.ShouldContain(KeptRecordId);
        keys.ShouldContain(OmittedRecordId);
    }

    [Fact]
    public async Task VerdictStoreOutage_FailsSoft_AndStillAirsABulletin()
    {
        _eventVerdictRepository.GetVerdictsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EventVerdict>>.Failure("table storage unavailable"));
        var sut = Build();

        var result = await sut.OrchestrateAsync(Ctx());

        // Fail-soft is deliberate: a verdict-store outage must never block a bulletin.
        result.IsSuccess.ShouldBeTrue();
        DraftedContext().Corridors.SelectMany(c => c.Incidents).Select(i => i.Id)
            .ShouldContain(OmittedRecordId);
    }

    private void GivenVerdicts(params EventVerdict[] verdicts) =>
        _eventVerdictRepository.GetVerdictsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EventVerdict>>.Success(verdicts));

    /// <summary>The context the script generator was actually handed.</summary>
    private BroadcastWithTraffic DraftedContext()
    {
        var call = _scriptGenerator.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(IScriptGenerator.GenerateScriptAsync));
        call.ShouldNotBeNull("the draft agent was never called");
        return (BroadcastWithTraffic)call.GetArguments()[0]!;
    }

    private static TrafficCorridor Corridor(string road, string recordId, string originalId) => new(
        road, 3, 3, 500, 100, "construction", $"construction on {road}",
        [
            new TrafficIncident(recordId, $"works on {road}", 3, road, "construction", 500, 3,
                OriginalId: originalId,
                SourceRecordIdentities: [new SourceRecordIdentity("here", recordId, originalId)])
        ]);

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

    private BroadcastOrchestrator Build()
    {
        var incidentStateService = new IncidentStateService(
            _incidentStateRepository,
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(call =>
            {
                // A provider that honours its contract: excluded records never reach aggregation, so
                // a corridor built from one takes the record's direction/extent/impact with it.
                var exclusions = call.ArgAt<IncidentExclusions?>(3);
                IEnumerable<TrafficCorridor> corridors =
                [
                    Corridor("Kept Road", KeptRecordId, KeptOriginalId),
                    Corridor("Omitted Road", OmittedRecordId, OmittedOriginalId)
                ];
                return Result<IEnumerable<TrafficCorridor>>.Success(
                    corridors.Where(c =>
                        exclusions is null || !c.Incidents.Any(exclusions.Excludes)).ToList());
            });
        _trafficProvider.GetIncidentsAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficIncident>>.Success([]));

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(160, 5.8, 1.45));
        _contentSafetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([]));
        _fillerStatementsRepository.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _futureLessonRepository.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
            // Real coverage: without it the builder cannot resolve an area and returns the
            // context untouched, so the fetch (and therefore the omission filter) never runs.
            .Returns(Result<Job>.Success(Job.Create(
                "test-job", "Test Station", "sydney", 30, DateTime.UtcNow,
                latitude: Option<double>.Some(-33.8688),
                longitude: Option<double>.Some(151.2093),
                radiusKm: Option<double>.Some(35.0),
                coverage: Option<CoverageArea>.Some(CoverageArea.Create(-33.8688, 151.2093, 35.0).Value)).Value));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
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
            Substitute.For<ILogger<BroadcastOrchestrator>>(),
            eventVerdictRepository: _eventVerdictRepository);
    }
}
