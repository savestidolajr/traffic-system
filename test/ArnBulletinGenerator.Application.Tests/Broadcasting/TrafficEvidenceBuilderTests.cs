using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Tests;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public class TrafficEvidenceBuilderTests
{
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ITrafficProvider _trafficProvider = Substitute.For<ITrafficProvider>();
    private readonly IPriorityRoadsRepository _priorityRoads = Substitute.For<IPriorityRoadsRepository>();
    private readonly IFillerStatementsRepository _filler = Substitute.For<IFillerStatementsRepository>();
    private readonly IRoutingProvider _routing = Substitute.For<IRoutingProvider>();
    private readonly IWeatherProvider _weather = Substitute.For<IWeatherProvider>();
    private readonly IFuelPriceProvider _fuel = Substitute.For<IFuelPriceProvider>();
    private readonly IFutureLessonRepository _futureLessons = Substitute.For<IFutureLessonRepository>();
    private readonly IGuidanceRepository _guidance = Substitute.For<IGuidanceRepository>();
    private readonly IEditorialSettingsRepository _editorial = Substitute.For<IEditorialSettingsRepository>();
    private readonly IIncidentStateService _incidentState = Substitute.For<IIncidentStateService>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly ILogger<BroadcastOrchestrator> _logger = Substitute.For<ILogger<BroadcastOrchestrator>>();
    private readonly JobStatusUpdater _jobStatusUpdater;
    private readonly BroadcastOptions _options = new()
    {
        TrafficCacheTtlSeconds = 180,
        OfficialFeedsEnabled = true,
        OfficialFeedFreshMinutes = 15,
        OfficialFeedMaxStaleMinutes = 60
    };

    public TrafficEvidenceBuilderTests()
    {
        _timeProvider.SetUtcNow(new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc));
        _jobStatusUpdater = new JobStatusUpdater(
            _jobRepository, Substitute.For<INotificationService>(), _timeProvider, _logger);
    }

    private TrafficEvidenceBuilder CreateSut(
        IEnumerable<IOfficialTrafficFeed>? feeds = null,
        IEmergencyIncidentFeed? emergencyFeed = null)
    {
        return new TrafficEvidenceBuilder(
            _jobRepository,
            _timeProvider,
            _cache,
            _trafficProvider,
            _priorityRoads,
            _filler,
            _routing,
            Options.Create(new MonitoredRoutesOptions()),
            _weather,
            _fuel,
            _futureLessons,
            _guidance,
            _editorial,
            _incidentState,
            eventVerdictRepository: null,
            _metrics,
            Options.Create(_options),
            _logger,
            _jobStatusUpdater,
            officialFeeds: feeds,
            emergencyIncidentFeed: emergencyFeed);
    }

    [Fact]
    public async Task GetCachedCorridors_WithinTtl_ReturnsCachedCorridors_AndRecordsMetric()
    {
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var corridor = new TrafficCorridor("M4 Motorway", 3, 3, 500, 100, "congestion", "Delays on M4", []);
        _trafficProvider.GetCorridorsAsync(area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([corridor]));

        var sut = CreateSut();

        // 1. Initial fetch (cache miss)
        var result1 = await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-1");
        result1.IsSuccess.ShouldBeTrue();
        result1.Value.Corridors.Count.ShouldBe(1);
        result1.Value.Corridors[0].RoadName.ShouldBe("M4 Motorway");

        _metrics.Received(1).TrackPipelineStage("traffic_cache_miss", Arg.Any<double>(), "sydney", true);

        // Advance clock by 60 seconds (well within 180s TTL)
        _timeProvider.SetUtcNow(new DateTime(2026, 8, 27, 10, 1, 0, DateTimeKind.Utc));

        // 2. Second fetch within TTL (cache hit)
        var result2 = await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-2");
        result2.IsSuccess.ShouldBeTrue();
        result2.Value.Corridors.Count.ShouldBe(1);
        result2.Value.Corridors[0].RoadName.ShouldBe("M4 Motorway");

        _metrics.Received(1).TrackPipelineStage("traffic_cache_hit", 0, "sydney", true);

        // Traffic provider should only have been called ONCE
        await _trafficProvider.Received(1).GetCorridorsAsync(area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>());
    }

    [Fact]
    public async Task GetCachedCorridors_ConcurrentRequests_SingleFlightDeduplicates()
    {
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var corridor = new TrafficCorridor("M4 Motorway", 3, 3, 500, 100, "congestion", "Delays on M4", []);

        var tcs = new TaskCompletionSource<Result<IEnumerable<TrafficCorridor>>>();
        _trafficProvider.GetCorridorsAsync(area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(async _ => await tcs.Task);

        var sut = CreateSut();

        // Fire 3 concurrent requests simultaneously
        var task1 = sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-1");
        var task2 = sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-2");
        var task3 = sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-3");

        // Complete the single background fetch
        tcs.SetResult(Result<IEnumerable<TrafficCorridor>>.Success([corridor]));

        var results = await Task.WhenAll(task1, task2, task3);

        results.Length.ShouldBe(3);
        foreach (var res in results)
        {
            res.IsSuccess.ShouldBeTrue();
            res.Value.Corridors.Count.ShouldBe(1);
            res.Value.Corridors[0].RoadName.ShouldBe("M4 Motorway");
        }

        // Only ONE upstream fetch executed across all 3 concurrent callers
        await _trafficProvider.Received(1).GetCorridorsAsync(area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>());
    }

    [Fact]
    public async Task GetCachedCorridors_DifferentMaxCorridors_UsesDifferentCacheEntries()
    {
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        _trafficProvider.GetCorridorsAsync(
                area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([]));

        var sut = CreateSut();

        (await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-1"))
            .IsSuccess.ShouldBeTrue();
        (await sut.GetCachedCorridorsAsync(area, 6, CancellationToken.None, market: "sydney", jobId: "job-2"))
            .IsSuccess.ShouldBeTrue();

        await _trafficProvider.Received(2).GetCorridorsAsync(
            area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>());
    }

    [Fact]
    public async Task GetCachedCorridors_AfterTtl_RefetchesTraffic()
    {
        _options.TrafficCacheTtlSeconds = 1;
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        _trafficProvider.GetCorridorsAsync(
                area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([]));
        var sut = CreateSut();

        (await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-1"))
            .IsSuccess.ShouldBeTrue();
        await Task.Delay(1_250);
        (await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-2"))
            .IsSuccess.ShouldBeTrue();

        await _trafficProvider.Received(2).GetCorridorsAsync(
            area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>());
    }

    [Fact]
    public async Task OfficialFeed_AreaAwareCache_IsolatesDifferentAreas()
    {
        var sydneyArea = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var melbourneArea = CoverageArea.Create(-37.8136, 144.9631, 35.0).Value;
        var feed = Substitute.For<IOfficialTrafficFeed>();
        feed.SourceId.Returns("livetraffic-nsw");
        feed.Capabilities.Returns(new SourceCapabilities("livetraffic-nsw", "1.0", new HashSet<string>(), new HashSet<string>()));
        feed.GetEventsAsync(sydneyArea, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OfficialTrafficEvent>>.Success([
                new OfficialTrafficEvent("livetraffic-nsw", "M4 crash", true, [new CoveragePoint(-33.8688, 151.2093)], Id: "nsw-1")
            ]));
        feed.GetEventsAsync(melbourneArea, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OfficialTrafficEvent>>.Success([
                new OfficialTrafficEvent("livetraffic-nsw", "Monash crash", true, [new CoveragePoint(-37.8136, 144.9631)], Id: "vic-1")
            ]));

        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([]));

        var sut = CreateSut([feed]);

        // Fetch Sydney
        var sydRes = await sut.GetCachedCorridorsAsync(sydneyArea, 5, CancellationToken.None, market: "sydney", jobId: "job-syd");
        sydRes.IsSuccess.ShouldBeTrue();

        // Fetch Melbourne
        var melbRes = await sut.GetCachedCorridorsAsync(melbourneArea, 5, CancellationToken.None, market: "melbourne", jobId: "job-melb");
        melbRes.IsSuccess.ShouldBeTrue();

        // Feed was called for each distinct area
        await feed.Received(1).GetEventsAsync(sydneyArea, Arg.Any<CancellationToken>());
        await feed.Received(1).GetEventsAsync(melbourneArea, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OfficialFeed_FreshCache_ReusesDataWithoutUpstreamCall()
    {
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var feed = Substitute.For<IOfficialTrafficFeed>();
        feed.SourceId.Returns("livetraffic-nsw");
        feed.Capabilities.Returns(new SourceCapabilities("livetraffic-nsw", "1.0", new HashSet<string>(), new HashSet<string>()));
        feed.GetEventsAsync(area, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OfficialTrafficEvent>>.Success([
                new OfficialTrafficEvent("livetraffic-nsw", "M4 crash", true, [new CoveragePoint(-33.8688, 151.2093)], Id: "nsw-1")
            ]));

        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([]));

        var sut = CreateSut([feed]);

        // First call populates official feed cache
        await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-1");

        // Advance 5 minutes (within 15 minute freshness window)
        _timeProvider.SetUtcNow(new DateTime(2026, 8, 27, 10, 5, 0, DateTimeKind.Utc));

        // Clear corridor-level cache so we force a fresh corridor build
        _cache.Remove($"TrafficCorridors:{area.Latitude:F4}:{area.Longitude:F4}:{area.RadiusKm:F1}:2026:08:27:10:03:0::");
        _cache.Remove($"TrafficCorridors:{area.Latitude:F4}:{area.Longitude:F4}:{area.RadiusKm:F1}:2026:08:27:10:00:0::");

        // Second call with expired corridor cache but FRESH official feed cache
        await sut.GetCachedCorridorsAsync(area, 5, CancellationToken.None, market: "sydney", jobId: "job-2");

        // Official feed HTTP API was ONLY called ONCE
        await feed.Received(1).GetEventsAsync(area, Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The authority feeds must not be gated behind the HERE fetch. They were, and it put their
    ///     whole budget on the critical path of every cache miss: measured on dev 2026-08-29 they
    ///     were the LAST three calls of the fetch, and OfficialFeedTimeoutSeconds is 5, so a slow
    ///     jurisdiction added five seconds to a fetch that had already collected everything else.
    ///     <para>
    ///         Written as a barrier rather than a stopwatch so it is deterministic: the traffic
    ///         provider refuses to return until the feed has been entered. Serialise the feeds after
    ///         the corridor fetch again and this wait can never be satisfied, so the test fails
    ///         instead of merely getting slower.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task Official_feeds_are_fetched_alongside_the_here_request_not_after_it()
    {
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var feedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var feed = Substitute.For<IOfficialTrafficFeed>();
        feed.SourceId.Returns("barrier-feed");
        feed.Capabilities.Returns(new SourceCapabilities(
            "barrier-feed", "1", new HashSet<string>(), new HashSet<string>()));
        feed.GetEventsAsync(area, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            feedEntered.TrySetResult();
            return Task.FromResult(Result<IReadOnlyList<OfficialTrafficEvent>>.Success([]));
        });

        var corridor = new TrafficCorridor("M4 Motorway", 3, 3, 500, 100, "congestion", "Delays on M4", []);
        _trafficProvider
            .GetCorridorsAsync(area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(async _ =>
            {
                await feedEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return Result<IEnumerable<TrafficCorridor>>.Success([corridor]);
            });

        var sut = CreateSut([feed]);

        var result = await sut.GetCachedCorridorsAsync(
            area, 5, CancellationToken.None, market: "sydney", jobId: "job-overlap");

        result.IsSuccess.ShouldBeTrue();
        feedEntered.Task.IsCompletedSuccessfully.ShouldBeTrue();
        await feed.Received(1).GetEventsAsync(area, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OfficialFeeds_EnabledConcurrencyIsCappedAtThree()
    {
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var thirdEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        int maximum = 0;
        var feeds = Enumerable.Range(0, 5).Select(index =>
        {
            var feed = Substitute.For<IOfficialTrafficFeed>();
            feed.SourceId.Returns($"official-{index}");
            feed.Capabilities.Returns(new SourceCapabilities(
                $"official-{index}", "1", new HashSet<string>(), new HashSet<string>()));
            feed.GetEventsAsync(area, Arg.Any<CancellationToken>()).Returns(async _ =>
            {
                int current = Interlocked.Increment(ref active);
                int observed;
                do
                {
                    observed = maximum;
                    if (observed >= current)
                        break;
                } while (Interlocked.CompareExchange(ref maximum, current, observed) != observed);
                if (current == 3)
                    thirdEntered.TrySetResult();

                await release.Task;
                Interlocked.Decrement(ref active);
                return Result<IReadOnlyList<OfficialTrafficEvent>>.Success([]);
            });
            return feed;
        }).ToArray();
        _trafficProvider.GetCorridorsAsync(
                area, Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<IncidentExclusions?>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success([]));

        var sut = CreateSut(feeds);
        var fetch = sut.GetCachedCorridorsAsync(
            area, 5, CancellationToken.None, market: "sydney", jobId: "job-feed-limit");

        await thirdEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        maximum.ShouldBe(3);
        release.TrySetResult();
        var result = await fetch;

        result.IsSuccess.ShouldBeTrue();
        maximum.ShouldBe(3);
    }

    // ── Emergency-services rail (phase 1: evidence only) ──────────────

    private static BroadcastWithTraffic EmergencyTestContext(CoverageArea area) =>
        new BroadcastRequest
        {
            JobId = "job-emergency",
            Location = "Sydney, NSW",
            TargetDurationSeconds = 30.0,
            Coverage = area,
            Content = new BulletinContentSelection { Traffic = true, Weather = false, Fuel = false }
        }.WithCorridors([]);

    /// <summary>
    ///     Stubs the rest of the market-content fan-out so these tests exercise the real
    ///     <see cref="TrafficEvidenceBuilder.FetchMarketContentAsync" /> path and prove the emergency
    ///     rail rides the same parallel window, rather than testing the fetcher in isolation.
    /// </summary>
    private void StubMarketContentFanOut()
    {
        _guidance.GetForScopesAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _filler.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _futureLessons.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([]));
    }

    [Fact]
    public async Task EmergencyEvents_AreFetchedAndCarried_ButNeverReachTheWriterContext()
    {
        StubMarketContentFanOut();
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var warning = new EmergencyEvent(
            "673492", "nsw-rfs-cap", "RFS", "nsw", "DUFF RD, CECIL PARK",
            EmergencyWarningLevel.EmergencyWarning, "bushfire", "fire",
            Suburb: "Cecil Park", Licence: "CC BY 4.0", AirEligible: true);

        var emergencyFeed = Substitute.For<IEmergencyIncidentFeed>();
        emergencyFeed.SourceId.Returns("dataquoll");
        emergencyFeed.GetEventsAsync(area, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EmergencyEvent>>.Success([warning]));

        var sut = CreateSut(emergencyFeed: emergencyFeed);
        var context = EmergencyTestContext(area);

        var content = await sut.FetchMarketContentAsync(
            context, area, EditorialSettings.Empty, CancellationToken.None);

        content.EmergencyEvents.ShouldHaveSingleItem().Id.ShouldBe("673492");
        content.SourceDiagnostics.ShouldContain(d =>
            d.SourceId == "dataquoll" && d.Outcome == SourceFetchOutcome.Ok);

        // THE phase-1 invariant: the writer must be structurally unable to see this data.
        // BroadcastWithTraffic is the writer's whole world, so the guarantee is that its type
        // carries no emergency-shaped member at all — stronger than checking one instance, and it
        // fails the moment someone adds the field. When phase 2 deliberately wires emergency data
        // into the script, THIS is the test to change, consciously.
        var applied = TrafficEvidenceBuilder.ApplyMarketContent(context, content);
        applied.ShouldNotBeNull();
        typeof(BroadcastWithTraffic)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.PropertyType.FullName ?? p.PropertyType.Name)
            .ShouldNotContain(name => name.Contains(nameof(EmergencyEvent), StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoEmergencyFeedRegistered_ReportsSkippedNotFailed()
    {
        // "Off" and "broken" must stay distinguishable: an environment that has not opted in is
        // not a degraded bulletin, and the console hides Skipped while surfacing Failed. Reporting
        // a failure here is what let the QLDTraffic demo-key degradation go unnoticed (RISK-36).
        StubMarketContentFanOut();
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var sut = CreateSut();

        var content = await sut.FetchMarketContentAsync(
            EmergencyTestContext(area), area, EditorialSettings.Empty, CancellationToken.None);

        content.EmergencyEvents.ShouldBeEmpty();
        content.SourceDiagnostics.ShouldContain(d =>
            d.SourceId == "dataquoll" && d.Outcome == SourceFetchOutcome.Skipped);
    }

    [Fact]
    public async Task EmergencyFeedFailure_IsFailSoft_AndLeavesTheBulletinIntact()
    {
        StubMarketContentFanOut();
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var emergencyFeed = Substitute.For<IEmergencyIncidentFeed>();
        emergencyFeed.SourceId.Returns("dataquoll");
        emergencyFeed.GetEventsAsync(area, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EmergencyEvent>>.Failure("HTTP 503 (server error)"));

        var sut = CreateSut(emergencyFeed: emergencyFeed);

        var content = await sut.FetchMarketContentAsync(
            EmergencyTestContext(area), area, EditorialSettings.Empty, CancellationToken.None);

        content.EmergencyEvents.ShouldBeEmpty();
        content.SourceDiagnostics.First(d => d.SourceId == "dataquoll").Outcome
            .ShouldBe(SourceFetchOutcome.Failed);
    }

    [Fact]
    public async Task EmergencyFeedThrowing_IsContained_SoAThirdPartyBugCannotFailABulletin()
    {
        StubMarketContentFanOut();
        var area = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;
        var emergencyFeed = Substitute.For<IEmergencyIncidentFeed>();
        emergencyFeed.SourceId.Returns("dataquoll");
        emergencyFeed.GetEventsAsync(area, Arg.Any<CancellationToken>())
            .Returns<Task<Result<IReadOnlyList<EmergencyEvent>>>>(
                _ => throw new InvalidOperationException("parser blew up"));

        var sut = CreateSut(emergencyFeed: emergencyFeed);

        var content = await sut.FetchMarketContentAsync(
            EmergencyTestContext(area), area, EditorialSettings.Empty, CancellationToken.None);

        content.EmergencyEvents.ShouldBeEmpty();
        content.SourceDiagnostics.First(d => d.SourceId == "dataquoll").Outcome
            .ShouldBe(SourceFetchOutcome.Failed);
    }
}
