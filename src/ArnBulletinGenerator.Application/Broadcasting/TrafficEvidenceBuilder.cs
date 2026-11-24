using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Fetches and assembles the traffic/market evidence a bulletin is written from: cached HERE
///     corridor fetches, the coverage/market-only content fan-out (weather, fuel, monitored
///     routes, guidance, filler, feedback, and future lessons), the flowing-road pool, incident-delta
///     computation, and the job's persisted incident/corridor audit trail. Extracted from
///     <see cref="BroadcastOrchestrator" /> — see that class for the fields it did not need.
/// </summary>
internal sealed class TrafficEvidenceBuilder(
    IJobRepository jobRepository,
    TimeProvider timeProvider,
    IMemoryCache cache,
    ITrafficProvider trafficProvider,
    IPriorityRoadsRepository priorityRoadsRepository,
    IFillerStatementsRepository fillerStatementsRepository,
    IRoutingProvider routingProvider,
    IOptions<MonitoredRoutesOptions> monitoredRoutesOptions,
    IWeatherProvider weatherProvider,
    IFuelPriceProvider fuelPriceProvider,
    IFutureLessonRepository futureLessonRepository,
    IGuidanceRepository guidanceRepository,
    IEditorialSettingsRepository editorialSettingsRepository,
    IIncidentStateService incidentStateService,
    IEventVerdictRepository? eventVerdictRepository,
    IBroadcastMetrics metrics,
    IOptions<BroadcastOptions> options,
    ILogger<BroadcastOrchestrator> logger,
    JobStatusUpdater jobStatusUpdater,
    IEnumerable<IOfficialTrafficFeed>? officialFeeds = null,
    ISourceParseReportRepository? sourceParseReportRepository = null,
    IIncidentPriorityRepository? incidentPriorityRepository = null,
    IEmergencyIncidentFeed? emergencyIncidentFeed = null)
{
    // Hard cap on good-run roads fed to the script. The prompt now names only 2–3 and sweeps the
    // rest into one phrase (ATN style), so a small pool is plenty and curbs the road-list dump.
    internal const int GoodRunScriptCap = 6;
    private static readonly ConditionalWeakTable<IMemoryCache, TrafficEvidenceCacheState> TrafficCaches = new();
    private readonly TrafficEvidenceCacheState _trafficCache = TrafficCaches.GetValue(
        cache, _ => new TrafficEvidenceCacheState(logger, metrics));


    private readonly BroadcastOptions _options = options.Value;
    private readonly IReadOnlyList<IOfficialTrafficFeed> _officialFeeds =
        officialFeeds?.ToArray() ?? [];
    private sealed record OfficialFeedCacheEntry(
        IReadOnlyList<OfficialTrafficEvent> Events,
        DateTimeOffset FetchedAtUtc);

    private sealed record OfficialFeedFetch(
        string SourceId,
        IReadOnlyList<OfficialTrafficEvent> Events,
        SourceFetchDiagnostic Diagnostic);

    internal sealed record TrafficFetchResult(
        IReadOnlyList<TrafficCorridor> Corridors,
        IReadOnlyList<SourceFetchDiagnostic> SourceDiagnostics);

    private static string DescribeFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "upstream request failed";

        var statusMatch = System.Text.RegularExpressions.Regex.Match(error, @"\b([45]\d{2})\b");
        if (statusMatch.Success && int.TryParse(statusMatch.Groups[1].Value, out int status))
        {
            string reason = status switch
            {
                401 => "unauthorised",
                403 => "forbidden",
                408 => "request timeout",
                429 => "rate limited",
                >= 500 => "server error",
                _ => "request failed"
            };
            return $"HTTP {status} ({reason})";
        }

        return "upstream request failed";
    }
    private readonly ISourceParseReportRepository? _sourceParseReportRepository = sourceParseReportRepository;
    private readonly IIncidentPriorityRepository? _incidentPriorityRepository = incidentPriorityRepository;
    private readonly MarketContentFetcher _marketContentFetcher = new(
        fillerStatementsRepository, futureLessonRepository, guidanceRepository,
        editorialSettingsRepository, routingProvider, monitoredRoutesOptions,
        weatherProvider, fuelPriceProvider, metrics, logger, jobStatusUpdater, timeProvider);

    private readonly FlowPoolBuilder _flowPoolBuilder = new(trafficProvider, logger);

    internal async Task<BroadcastRequest> EnrichRequestWithJobContext(BroadcastRequest request,
        CancellationToken cancellationToken)
    {
        var jobResult = await jobRepository.GetJobAsync(request.JobId, cancellationToken);
        if (jobResult.IsFailure)
            return request;

        var job = jobResult.Value;
        var rejectionFeedback = BuildRejectionFeedback(job);
        var previousScript = request.PreviousScript.Or(job.PreviousScriptContext);
        var previousJobId = request.PreviousJobId.Or(job.PreviousJobId);

        // Resolve the market's local timezone for same-day logic. Scheduled bulletins carry it on
        // the request/job; a fresh manual trigger carries neither, so fall back to the platform
        // default (an all-Australian product). Without a real local timezone BOTH the same-day
        // continuity window AND the incident delta default to UTC, which splits an Australian
        // market's early-morning bulletins across two UTC days and breaks continuity.
        var timezoneId = request.TimezoneId.Or(job.TimezoneId);
        if (timezoneId.IsNone && !string.IsNullOrWhiteSpace(_options.DefaultTimezone))
            timezoneId = Option<string>.Some(_options.DefaultTimezone);

        // Ad-hoc/manual path: scheduled jobs already carry PreviousScriptContext, and regen
        // carries request.PreviousScript / rejection feedback. A fresh manual trigger has
        // neither, so look up this station's most recent same-day bulletin for continuity.
        if (previousScript.IsNone && rejectionFeedback.IsNone)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var (dayStartUtc, dayEndUtc) =
                TimeZoneHelper.GetLocalDayWindowUtc(nowUtc, timezoneId.GetValueOr(string.Empty));

            var priorResult = await jobRepository.GetLatestBulletinForMarketTodayAsync(
                job.Market, job.Station, dayStartUtc, dayEndUtc, cancellationToken);

            if (priorResult.IsSuccess && priorResult.Value.HasValue)
            {
                var prior = priorResult.Value.Value;
                if (prior.JobId != job.JobId)
                {
                    previousScript = prior.ScriptText;
                    previousJobId = Option<string>.Some(prior.JobId);
                }
            }
        }

        return rejectionFeedback.HasValue || previousScript.HasValue || timezoneId.HasValue
            ? request with
            {
                RejectionFeedback = rejectionFeedback,
                PreviousScript = previousScript,
                PreviousJobId = previousJobId,
                TimezoneId = timezoneId
            }
            : request;
    }

    private static Option<string> BuildRejectionFeedback(Job job)
    {
        return job.RejectionHistory.Count == 0
            ? default
            : Option<string>.Some(JobMessageBuilder.BuildRejectionFeedback(job.RejectionHistory, default));
    }

    internal async Task<BroadcastWithTraffic> TryRefreshTrafficAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress = null)
    {
        if (progress is not null)
            await progress.StageStarted(ProgressStage.Evidence, "Refreshing bulletin evidence.");
        var jobResult = await jobRepository.GetJobAsync(context.JobId, cancellationToken);
        if (jobResult.IsFailure)
        {
            logger.LogWarning(
                "[TrafficRefresh] Job {JobId} — could not load job for geo data, proceeding with provided traffic",
                context.JobId);
            return context;
        }

        var job = jobResult.Value;
        if (!job.Latitude.HasValue || !job.Longitude.HasValue || !job.RadiusKm.HasValue)
        {
            logger.LogWarning("[TrafficRefresh] Job {JobId} — no geo data on job, proceeding with provided traffic",
                context.JobId);
            return context;
        }

        CoverageArea coverage;
        if (job.Coverage.HasValue)
        {
            // Use the point-in-time snapshot so a regeneration re-fetches the
            // SAME shape the bulletin was generated with, not a derived circle.
            coverage = job.Coverage.Value;
        }
        else
        {
            var coverageResult = CoverageArea.Create(job.Latitude.Value, job.Longitude.Value, job.RadiusKm.Value);
            if (coverageResult.IsFailure)
            {
                logger.LogWarning(
                    "[TrafficRefresh] Job {JobId} — invalid geo data on job ({Error}), proceeding with provided traffic",
                    context.JobId, coverageResult.Error);
                return context;
            }

            coverage = coverageResult.Value;
        }
        // Resolved from the repository, NOT read off the context: on the queue path
        // BroadcastOrchestrator.OrchestrateAsync calls this refresh BEFORE it resolves editorial
        // settings, so context.EditorialSettings is still Empty here and trusting it would leave
        // every disabled source silently consulted on the main pipeline. Costs one table read that
        // GenerateBroadcastAsync repeats a moment later, which is the price of the gate applying at
        // all. Caught by BroadcastOrchestratorSourceToggleTests.
        var settings = await ResolveEditorialSettingsAsync(context, cancellationToken);
        var corridorsResult = await GetCachedCorridorsAsync(
            coverage, _options.MaxCorridorsForStorage, cancellationToken,
            context.MarketId.GetValueOr(context.Location), context.JobId, settings,
            // The operator's rundown exclusions are an INPUT to the fetch, not a filter over its
            // result: dropping them afterwards left the corridor's direction, extent, length,
            // impact and provenance derived from the record they had unticked.
            context.ExcludedIncidentKeys.Match<IReadOnlyList<string>?>(k => k, () => null));
        if (corridorsResult.IsFailure)
        {
            logger.LogWarning(
                "[TrafficRefresh] Job {JobId} — re-fetch failed: {Error}, proceeding with provided traffic",
                context.JobId, corridorsResult.Error);
            return context;
        }
        if (progress is not null)
            await progress.StageStarted(ProgressStage.Select, "Selecting the strongest evidence for the slot.");

        var trafficFetch = corridorsResult.Value;
        var allCorridors = trafficFetch.Corridors.ToList();
        var priorityRoads = await GetPriorityRoadsSafeAsync(context, cancellationToken);
        List<TrafficCorridor> selectedCorridors;
        IReadOnlyList<FlowRoad> flowPool;
        IReadOnlyList<string> goodRunNames;
        int slotCapacity = CalculateMaxCorridors(context.TargetDurationSeconds, context.EditorialSettings);

        if (context.CuratedCorridorRoads.HasValue)
        {
            // Operator-curated regeneration: honour the producer's corridor + good-run picks
            // against the FRESH fetch (preserves the ≤30-min freshness rule). Selections whose
            // incidents have since cleared drop out; unselected fresh corridors are excluded.
            var curation = CorridorCurator.Apply(allCorridors, context.CuratedCorridorRoads.Value);
            selectedCorridors = curation.Corridors.ToList();

            logger.LogInformation(
                "[TrafficRefresh] Job {JobId} curated selection applied — kept {Kept}, cleared since selection: [{Missing}], excluded fresh: {Excluded}",
                context.JobId, selectedCorridors.Count, string.Join(", ", curation.MissingSelections),
                curation.ExcludedFreshCount);

            if (curation.MissingSelections.Count > 0)
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    $"Curated corridors no longer active (cleared): {string.Join(", ", curation.MissingSelections)}",
                    cancellationToken);

            if (selectedCorridors.Count == 0)
            {
                logger.LogWarning(
                    "[TrafficRefresh] Job {JobId} — every curated corridor has cleared; falling back to standard selection",
                    context.JobId);
                await jobStatusUpdater.LogJobAsync(context.JobId,
                    "All curated corridors have cleared since selection — using standard corridor ranking instead",
                    cancellationToken);
                selectedCorridors = allCorridors.Take(slotCapacity).ToList();
                context = context with { CuratedCorridorRoads = Option<IReadOnlyList<string>>.None };
            }

            // Good runs: honour the operator's explicit pick; otherwise fill leftover slots from the
            // data-backed pool (the operator chose the corridors, not necessarily the good runs).
            flowPool = await BuildFlowPoolAsync(allCorridors, priorityRoads, coverage, cancellationToken);
            goodRunNames = GoodRunPolicy.SelectForGeneration(
                flowPool, selectedCorridors.Count, slotCapacity, GoodRunScriptCap,
                context.CuratedGoodRunRoads).Roads;
        }
        else
        {
            // No curation: incidents and flowing good-runs compete for the slot by score.
            allCorridors = await ApplyIncidentPrioritiesSafeAsync(allCorridors, context, cancellationToken);
            allCorridors = PriorityRoads.BoostAndSort(allCorridors, PriorityRoads.EnabledIncidentBoostNames(priorityRoads));
            flowPool = await BuildFlowPoolAsync(allCorridors, priorityRoads, coverage, cancellationToken);
            var content = BulletinContentSelector.Select(
                allCorridors, flowPool, slotCapacity, GoodRunScriptCap);
            selectedCorridors = content.Corridors.ToList();
            goodRunNames = content.GoodRuns.Select(f => f.RoadName).ToList();
        }

        var incidents = selectedCorridors.SelectMany(c => c.Incidents).ToList();
        if (progress is not null)
            await progress.StageCompleted(
                ProgressStage.Select,
                $"Selected {selectedCorridors.Count} corridors and {incidents.Count} incidents.",
                new Dictionary<string, double>
                {
                    ["corridors"] = selectedCorridors.Count,
                    ["incidents"] = incidents.Count,
                    ["goodRuns"] = goodRunNames.Count
                });

        var suspiciousIncidentRoads = RoadNamePlausibility.SuspiciousIncidentRoads(selectedCorridors);
        if (suspiciousIncidentRoads.Count > 0)
            logger.LogWarning(
                "[RoadNamePlausibility] Job {JobId} retained suspicious incident road name(s): {RoadNames}",
                context.JobId, string.Join(", ", suspiciousIncidentRoads));

        logger.LogInformation(
            "[TrafficRefresh] Job {JobId} re-fetched traffic — {CorridorCount} corridor(s), {IncidentCount} incident(s)",
            context.JobId, selectedCorridors.Count, incidents.Count);

        var refreshed = context.WithCorridors(selectedCorridors) with
        {
            FlowPool = flowPool,
            GoodRunRoads = goodRunNames,
            // What the cut removed, kept for writers who do their own choosing. See
            // BroadcastWithTraffic.AllCorridors for why the selected set is not enough.
            AllCorridors = allCorridors
        };
        var marketContent = await FetchMarketContentAsync(
            refreshed, coverage, refreshed.EditorialSettings, cancellationToken);
        await StoreIncidentsOnJob(context.JobId, incidents, selectedCorridors, allCorridors, default,
            default, context.MonitoredRoutes,
            trafficFetch.SourceDiagnostics.Concat(marketContent.SourceDiagnostics).ToList(),
            cancellationToken);
        await jobStatusUpdater.LogJobAsync(context.JobId,
            context.CuratedCorridorRoads.HasValue
                ? $"Re-fetched traffic with curated selection: {selectedCorridors.Count} corridor(s), {incidents.Count} incident(s)"
                : $"Re-fetched traffic: {selectedCorridors.Count} corridor(s), {incidents.Count} incident(s)",
            cancellationToken);
        if (progress is not null)
            await progress.StageCompleted(
                ProgressStage.Evidence,
                $"Evidence refreshed: {selectedCorridors.Count} corridors and {incidents.Count} incidents.",
                new Dictionary<string, double>
                {
                    ["corridors"] = selectedCorridors.Count,
                    ["incidents"] = incidents.Count
                });

        return ApplyMarketContent(refreshed, marketContent);
    }

    /// <summary>
    ///     Fetches all per-market content that depends only on the coverage area and market/station
    ///     ids (never the traffic result): weather, fuel, monitored-route drive times, standing
    ///     guidance, filler statements, and explicit station future lessons. Fired
    ///     CONCURRENTLY with the traffic fetch by the main path. Everything here is fail-soft
    ///     (a failure degrades to that source being absent, never a failed bulletin).
    /// </summary>

    /// <summary>
    ///     Resolves ARN's numeric editorial settings. Exposed separately because corridor selection
    ///     needs the corridor cap BEFORE market content is fetched, and resolving twice per bulletin
    ///     would risk the two reads disagreeing mid-generation.
    /// </summary>
    internal Task<EditorialSettings> ResolveEditorialSettingsAsync(
        BroadcastContextBase context, CancellationToken cancellationToken) =>
        _marketContentFetcher.GetEditorialSettingsSafeAsync(context, cancellationToken);

    internal async Task<MarketContent> FetchMarketContentAsync(
        BroadcastWithTraffic context,
        CoverageArea coverage,
        EditorialSettings settings,
        CancellationToken cancellationToken)
    {
        var weatherTask = _marketContentFetcher.FetchWeatherSafeAsync(context, coverage, cancellationToken);
        var fuelTask = _marketContentFetcher.FetchFuelSafeAsync(context, coverage, cancellationToken);
        var emergencyTask = FetchEmergencyEventsSafeAsync(coverage, cancellationToken);
        var futureLessonsTask = _marketContentFetcher.GetFutureLessonsSafeAsync(context, cancellationToken);
        var monitoredRoutesTask = _marketContentFetcher.FetchMonitoredRoutesSafeAsync(context, cancellationToken);
        var settingsTask = Task.FromResult(settings);
        var guidanceTask = _marketContentFetcher.GetComposedGuidanceSafeAsync(context, settingsTask, cancellationToken);
        var fillerTask = _marketContentFetcher.GetFillerStatementsSafeAsync(context, cancellationToken);
        await Task.WhenAll(
            weatherTask, fuelTask, emergencyTask, futureLessonsTask, monitoredRoutesTask,
            settingsTask, guidanceTask, fillerTask);

        var weather = await weatherTask;
        var fuel = await fuelTask;
        var emergency = await emergencyTask;
        if (weather.Value.HasValue && weather.Value.Value.HasMajorAlert)
            await jobStatusUpdater.LogJobAsync(context.JobId,
                $"Weather alert detected: {string.Join("; ", weather.Value.Value.Alerts)}",
                cancellationToken);

        return new MarketContent(
            weather.Value,
            fuel.Value,
            await monitoredRoutesTask,
            await futureLessonsTask,
            await guidanceTask,
            await fillerTask,
            await settingsTask,
            emergency.Events,
            [weather.Diagnostic, fuel.Diagnostic, emergency.Diagnostic]);
    }

    private async Task<(IReadOnlyList<EmergencyEvent> Events, SourceFetchDiagnostic Diagnostic)>
        FetchEmergencyEventsSafeAsync(CoverageArea coverage, CancellationToken cancellationToken)
    {
        const string defaultSourceId = "dataquoll";
        if (emergencyIncidentFeed is null)
            return ([], new SourceFetchDiagnostic(
                defaultSourceId, SourceFetchOutcome.Skipped, "not configured", null, null));

        try
        {
            var result = await emergencyIncidentFeed.GetEventsAsync(coverage, cancellationToken);
            if (result.IsSuccess)
                return (result.Value, new SourceFetchDiagnostic(
                    emergencyIncidentFeed.SourceId, SourceFetchOutcome.Ok, null,
                    timeProvider.GetUtcNow(), null));

            // A feed that is registered but not configured for this environment is a skip, not a
            // failure: dev has no DataQuoll key, and reporting that as "upstream request failed"
            // on every job painted a permanently red source the operators learnt to ignore.
            if (result.Error?.Contains("not configured", StringComparison.OrdinalIgnoreCase) == true)
                return ([], new SourceFetchDiagnostic(
                    emergencyIncidentFeed.SourceId, SourceFetchOutcome.Skipped, "not configured", null, null));

            string detail = DescribeFailure(result.Error);
            logger.LogWarning(
                "[EmergencyFeed] {SourceId} failed; continuing without emergency events: {Detail}",
                emergencyIncidentFeed.SourceId, detail);
            return ([], new SourceFetchDiagnostic(
                emergencyIncidentFeed.SourceId, SourceFetchOutcome.Failed, detail, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[EmergencyFeed] {SourceId} threw; continuing without emergency events",
                emergencyIncidentFeed.SourceId);
            return ([], new SourceFetchDiagnostic(
                emergencyIncidentFeed.SourceId, SourceFetchOutcome.Failed,
                "upstream request failed", null, null));
        }
    }

    /// <summary>Applies fetched <see cref="MarketContent" /> onto the traffic context.</summary>
    internal static BroadcastWithTraffic ApplyMarketContent(BroadcastWithTraffic context, MarketContent content) =>
        context with
        {
            Weather = content.Weather,
            Fuel = content.Fuel,
            MonitoredRoutes = content.MonitoredRoutes,
            FutureLessons = content.FutureLessons,
            Guidance = content.Guidance,
            FillerStatements = content.FillerStatements,
            EditorialSettings = content.EditorialSettings
        };
    internal sealed record MarketContent(
        Option<WeatherReport> Weather,
        Option<FuelPriceReport> Fuel,
        IReadOnlyList<MonitoredRouteStatus> MonitoredRoutes,
        IReadOnlyList<FutureLessonEntry> FutureLessons,
        ComposedGuidance Guidance,
        IReadOnlyList<FillerStatement> FillerStatements,
        EditorialSettings EditorialSettings,
        IReadOnlyList<EmergencyEvent> EmergencyEvents,
        IReadOnlyList<SourceFetchDiagnostic> SourceDiagnostics);

    /// <summary>
    ///     Starts the live free-flow fetch (HERE flow tiles plus the map-attribute and reverse-geocode
    ///     naming behind them). It depends only on coverage, so the orchestrator starts it alongside
    ///     the traffic fetch instead of after ranking: measured 6.9s on the critical path of dev job
    ///     2e158de3 (2026-09-02) for work that needed nothing the traffic fetch produced.
    /// </summary>
    internal Task<IReadOnlyList<FlowRoad>> FetchFreeFlowingRoadsAsync(
        CoverageArea coverage, CancellationToken cancellationToken) =>
        _flowPoolBuilder.FetchFreeFlowingRoadsSafeAsync(coverage, cancellationToken);

    /// <summary>
    ///     Builds the data-backed flowing-road pool: the live free-flow fetch, with station/market
    ///     "weighted" roads flagged and watched roads with no active corridor recovered, scored on
    ///     the same scale as incidents (damped low). Built BEFORE selection so good-runs can be
    ///     ranked against incidents for the slot (<see cref="BulletinContentSelector" />).
    ///     <paramref name="freeFlow" /> is the fetch started by <see cref="FetchFreeFlowingRoadsAsync" />
    ///     when the caller began it early; otherwise it is fetched here.
    /// </summary>
    internal async Task<IReadOnlyList<FlowRoad>> BuildFlowPoolAsync(
        IReadOnlyList<TrafficCorridor> allCorridors,
        IReadOnlyList<PriorityRoad> priorityRoads,
        CoverageArea coverage,
        CancellationToken cancellationToken,
        Task<IReadOnlyList<FlowRoad>>? freeFlow = null)
    {
        var flowPool = await (freeFlow ?? _flowPoolBuilder.FetchFreeFlowingRoadsSafeAsync(coverage, cancellationToken));

        // `allCorridors` is already capped at MaxCorridorsForStorage, so a road whose incident lost
        // that cut was never excluded — HERE could report a crash on a road this same bulletin then
        // called "moving nicely". Exclude every road HERE named an incident on, uncapped. Fail-soft:
        // a lookup failure leaves the corridor-based exclusion in place, exactly as before.
        var incidentRoadNames = await GetIncidentRoadNamesSafeAsync(coverage, cancellationToken);
        var excludedRoadNames = allCorridors
            .Select(corridor => corridor.RoadName)
            .Concat(incidentRoadNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyList<FlowRoad> ExcludeUnsafeRoads(IReadOnlyList<FlowRoad> roads)
        {
            var withoutCorridorRoads = GoodRunPolicy.ExcludeCorridorRoads(roads, allCorridors);
            return incidentRoadNames.Count == 0
                ? withoutCorridorRoads
                : GoodRunPolicy.ExcludeIncidentRoads(withoutCorridorRoads, incidentRoadNames);
        }

        IReadOnlyList<FlowRoad> FilterPlausible(IReadOnlyList<FlowRoad> roads)
        {
            var plausible = RoadNamePlausibility.FilterDiscretionaryFlowPool(roads);
            if (plausible.Count == roads.Count)
                return plausible;

            var dropped = roads
                .Where(flow => !RoadNamePlausibility.IsPlausible(flow.RoadName))
                .Select(flow => flow.RoadName)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            logger.LogWarning(
                "[RoadNamePlausibility] Dropped {DroppedCount} suspicious discretionary flow road(s): {RoadNames}",
                roads.Count - plausible.Count, string.Join(", ", dropped));
            return plausible;
        }

        var withoutIncidentRoads = ExcludeUnsafeRoads(flowPool);
        var filtered = FilterPlausible(withoutIncidentRoads);
        if (filtered.Count < FlowPoolBuilder.FlowPoolCap
            && withoutIncidentRoads.Count < flowPool.Count
            && excludedRoadNames.Count > 0)
        {
            var refill = await _flowPoolBuilder.FetchFreeFlowingRoadsSafeAsync(
                coverage, cancellationToken, excludedRoadNames);
            var refilled = FilterPlausible(ExcludeUnsafeRoads(refill));
            filtered = filtered
                .Concat(refilled)
                .DistinctBy(road => CorridorAggregator.CanonicalRoadKey(road.RoadName))
                .ToList();
        }

        // This is the one common finalisation point for both first-run and refreshed generation.
        // GoodRunRoads and the persisted Flow tab are downstream views of this pool, so filtering
        // here avoids three drifting copies of the same discretionary editorial rule.

        return FlowPoolBuilder.MarkWeightedFlowRoads(filtered, priorityRoads);
    }

    private async Task<IReadOnlyList<string>> GetIncidentRoadNamesSafeAsync(
        CoverageArea coverage, CancellationToken cancellationToken)
    {
        string cacheKey = $"IncidentRoadNames:{coverage.Latitude:F4}:{coverage.Longitude:F4}:{coverage.RadiusKm:F1}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        try
        {
            var result = await trafficProvider.GetIncidentRoadNamesAsync(
                coverage, _options.MaxCorridorsForStorage, cancellationToken);
            var names = result.IsSuccess ? result.Value : [];
            cache.Set(cacheKey, names, TimeSpan.FromSeconds(60));
            return names;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[GoodRun] Incident road-name lookup failed; good-run exclusion falls back to the corridor set.");
            return [];
        }
    }

    internal async Task<IReadOnlyList<PriorityRoad>> GetPriorityRoadsSafeAsync(
        BroadcastContextBase context, CancellationToken cancellationToken)
    {
        string market = context.MarketId.GetValueOr(context.Location);
        var result = await priorityRoadsRepository.GetRoadsAsync(market, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("[MarketContent] Job {JobId} priority-roads lookup failed (ignored): {Error}",
                context.JobId, result.Error);
            return [];
        }

        return result.Value;
    }

    private async Task<List<TrafficCorridor>> ApplyIncidentPrioritiesSafeAsync(
        List<TrafficCorridor> corridors, BroadcastContextBase context, CancellationToken cancellationToken)
    {
        if (_incidentPriorityRepository is null) return corridors;
        string market = context.MarketId.GetValueOr(context.Location);
        if (string.IsNullOrWhiteSpace(market)) return corridors;

        try
        {
            var rulesResult = await _incidentPriorityRepository.GetRulesAsync(market, cancellationToken);
            if (rulesResult.IsFailure || rulesResult.Value.Count == 0) return corridors;

            var ruleMap = rulesResult.Value
                .Where(r => r.Enabled)
                .ToDictionary(r => r.IncidentClass, r => r.ScoreBonus, StringComparer.OrdinalIgnoreCase);

            if (ruleMap.Count == 0) return corridors;

            return corridors.Select(c =>
            {
                string canonicalClass = MapPrimaryTypeToCanonical(c.PrimaryType);
                if (ruleMap.TryGetValue(canonicalClass, out int bonus) && bonus != 0)
                {
                    double baseScore = c.BaseScore ?? c.ImpactScore;
                    double newScore = Math.Max(0, c.ImpactScore + bonus);
                    double totalBoost = (c.BoostAmount ?? 0) + bonus;
                    return c with
                    {
                        BaseScore = baseScore,
                        BoostAmount = totalBoost,
                        ImpactScore = newScore,
                        PriorityBoosted = bonus > 0 || c.PriorityBoosted
                    };
                }
                return c;
            }).OrderByDescending(c => c.ImpactScore).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[IncidentPriorities] Failed to load rules for {Market}, using base scores", market);
            return corridors;
        }
    }

    private static string MapPrimaryTypeToCanonical(string primaryType)
    {
        string norm = primaryType?.ToLowerInvariant() ?? "";
        if (norm.Contains("accident") || norm.Contains("crash") || norm.Contains("collision")) return "Crash";
        if (norm.Contains("close") || norm.Contains("closure")) return "Closure";
        if (norm.Contains("break") || norm.Contains("stall") || norm.Contains("disabled")) return "Breakdown";
        if (norm.Contains("flood") || norm.Contains("water")) return "Flooding";
        if (norm.Contains("fire")) return "Fire";
        if (norm.Contains("congestion") || norm.Contains("queue")) return "Congestion";
        if (norm.Contains("construct") || norm.Contains("work") || norm.Contains("maintenance")) return "Roadworks";
        if (norm.Contains("event")) return "SpecialEvent";
        if (norm.Contains("ferry")) return "Ferry";
        return "Hazard";
    }
    // ── Private helpers ──────────────────────────────────────────────────

    internal async Task<Result<TrafficFetchResult>> GetCachedCorridorsAsync(
        CoverageArea area, int maxCorridors, CancellationToken ct, string? market = null, string? jobId = null,
        EditorialSettings? editorialSettings = null, IReadOnlyList<string>? excludedRecordIds = null)
    {
        var exclusions = await BuildExclusionsAsync(market, excludedRecordIds, ct);
        var fetched = await GetCachedCorridorsCoreAsync(
            area, maxCorridors, ct, market, jobId, editorialSettings, exclusions);
        if (fetched.IsSuccess)
            await ReportExclusionsAsync(exclusions, market, jobId, ct);
        return fetched;
    }

    /// <summary>
    ///     Resolves what this fetch must leave out: the market's standing Desk verdicts, plus any
    ///     records the operator unticked on the rundown of the job being regenerated. Null when
    ///     there is nothing to exclude, so the ordinary fetch carries no exclusion state at all.
    ///     Fail-soft by design — a verdict-store outage must never block a bulletin, so on error the
    ///     fetch proceeds with the job's own exclusions and nothing else.
    /// </summary>
    private async Task<IncidentExclusions?> BuildExclusionsAsync(
        string? market, IReadOnlyList<string>? excludedRecordIds, CancellationToken ct)
    {
        IReadOnlyList<string> recordIds = excludedRecordIds ?? [];
        IReadOnlyList<string> omittedKeys = [];

        if (!string.IsNullOrWhiteSpace(market))
        {
            if (eventVerdictRepository is null)
            {
                logger.LogWarning(
                    "[DeskOmissions] No verdict repository wired for {Market} — evidence unfiltered", market);
            }
            else
            {
                var verdictsResult = await eventVerdictRepository.GetVerdictsAsync(market, ct);
                if (verdictsResult.IsFailure)
                    logger.LogWarning(
                        "[DeskOmissions] Verdict lookup failed for {Market}: {Error} — evidence unfiltered",
                        market, verdictsResult.Error);
                else
                    omittedKeys = verdictsResult.Value
                        .Where(v => v.Verdict == VerdictKind.Omitted)
                        .Select(v => v.EventKey)
                        .ToList();
            }
        }

        return omittedKeys.Count == 0 && recordIds.Count == 0
            ? null
            : new IncidentExclusions(omittedKeys, recordIds);
    }

    /// <summary>
    ///     Reports what the fetch actually held back. A cache hit filters nothing (the cached set was
    ///     already built under an identical <see cref="IncidentExclusions.FetchToken" />), so an empty
    ///     drop list on a live fetch is the honest "nothing matched" signal.
    /// </summary>
    private async Task ReportExclusionsAsync(
        IncidentExclusions? exclusions, string? market, string? jobId, CancellationToken ct)
    {
        if (exclusions is null or { IsEmpty: true }) return;

        var drops = exclusions.Drops;
        logger.LogInformation(
            "[DeskOmissions] {Market}: {DeskCount} standing verdict(s) + {OperatorCount} rundown "
            + "exclusion(s); {DroppedCount} record(s) held out of this fetch before aggregation",
            market, exclusions.DeskVerdictCount, exclusions.OperatorExcludedCount, drops.Count);
        foreach (var drop in drops)
            logger.LogInformation("[DeskOmissions] Held out of {Market} ({Reason}): {Event}",
                market, drop.Reason, drop.Description);

        if (string.IsNullOrWhiteSpace(jobId)) return;

        int operatorDrops = drops.Count(d => d.Reason == IncidentExclusions.Reason.OperatorExcluded);
        if (operatorDrops > 0)
            await jobStatusUpdater.LogJobAsync(jobId,
                $"Operator excluded {operatorDrops} incident(s) from the regenerated bulletin", ct);
    }

    private static string BuildTrafficCacheKey(
        CoverageArea area,
        int maxCorridors,
        DateTimeOffset now,
        EditorialSettings? editorialSettings,
        IncidentExclusions? exclusions)
    {
        int minuteBucket = (now.Minute / 3) * 3;
        string sourceKey = string.Join(
            ',', editorialSettings?.NormalizeDisabledTrafficSources() ?? []);
        string exclusionKey = (exclusions ?? IncidentExclusions.None).FetchToken;
        static string PointKey(IReadOnlyList<CoveragePoint>? points) =>
            points is null
                ? string.Empty
                : string.Join(
                    ';', points.Select(point => $"{point.Lat:F5},{point.Lng:F5}"));
        string shapeKey = area.ShapeType switch
        {
            CoverageShapeType.BoundingBox when area.West.HasValue =>
                $":{area.West:F4},{area.South:F4},{area.East:F4},{area.North:F4}",
            CoverageShapeType.Corridor =>
                $":{area.CorridorRadiusMeters ?? 0:F0}:points={PointKey(area.CorridorPoints)}",
            CoverageShapeType.Polygon =>
                $":points={PointKey(area.PolygonPoints)}",
            _ => string.Empty
        };
        return
            $"TrafficEvidence:provider=here:source={sourceKey}:max={maxCorridors}:{area.Latitude:F4}:{area.Longitude:F4}:{area.RadiusKm:F1}:{now.Year:D4}:{now.Month:D2}:{now.Day:D2}:{now.Hour:D2}:{minuteBucket:D2}:{(int)area.ShapeType}:exclusion={exclusionKey}{shapeKey}";
    }
    private static string BuildOfficialFeedCacheKey(
        string sourceId, CoverageArea area)
    {
        static string PointKey(IReadOnlyList<CoveragePoint>? points) =>
            points is null
                ? string.Empty
                : string.Join(
                    ';', points.Select(point => $"{point.Lat:F5},{point.Lng:F5}"));
        string shapeKey = area.ShapeType switch
        {
            CoverageShapeType.BoundingBox when area.West.HasValue =>
                $":{area.West:F4},{area.South:F4},{area.East:F4},{area.North:F4}",
            CoverageShapeType.Corridor =>
                $":{area.CorridorRadiusMeters ?? 0:F0}:points={PointKey(area.CorridorPoints)}",
            CoverageShapeType.Polygon =>
                $":points={PointKey(area.PolygonPoints)}",
            _ => string.Empty
        };
        return
            $"officialfeed:{sourceId}:{area.Latitude:F4}:{area.Longitude:F4}:{area.RadiusKm:F1}:{(int)area.ShapeType}{shapeKey}";
    }


    private async Task<Result<TrafficFetchResult>> GetCachedCorridorsCoreAsync(
        CoverageArea area, int maxCorridors, CancellationToken ct, string? market = null, string? jobId = null,
        EditorialSettings? editorialSettings = null, IncidentExclusions? exclusions = null)
    {
        var now = timeProvider.GetUtcNow();
        string cacheKey = BuildTrafficCacheKey(
            area, maxCorridors, now, editorialSettings, exclusions);
        if (_trafficCache.TryGet(cacheKey, out Result<TrafficFetchResult>? cached))
        {
            metrics.TrackPipelineStage(
                "traffic_cache_hit", 0, market ?? string.Empty, true);
            return cached!;
        }

        var pending = _trafficCache.GetOrAddFlight(
            cacheKey,
            () => GetCachedCorridorsUncoalescedAsync(
                area, maxCorridors, ct, market, jobId, editorialSettings, exclusions, cacheKey, now));
        try
        {
            return await pending.Value.WaitAsync(ct);
        }
        finally
        {
            _trafficCache.RemoveFlight(cacheKey, pending);
        }
    }

    private async Task<Result<TrafficFetchResult>> GetCachedCorridorsUncoalescedAsync(
        CoverageArea area, int maxCorridors, CancellationToken ct, string? market, string? jobId,
        EditorialSettings? editorialSettings, IncidentExclusions? exclusions,
        string cacheKey, DateTimeOffset now)
    {
        var diagnostics = new List<SourceFetchDiagnostic>();
        var enabledOfficialFeeds = new List<IOfficialTrafficFeed>();
        Task<OfficialFeedFetch[]> officialFetchTask =
            Task.FromResult(Array.Empty<OfficialFeedFetch>());

        if (_options.OfficialFeedsEnabled && _officialFeeds.Count > 0)
        {
            // One pass, so the enabled/skipped split cannot drift out of sync the way a predicate
            // written twice with a negation can.
            foreach (var feed in _officialFeeds)
            {
                if (editorialSettings?.IsTrafficSourceEnabled(feed.SourceId) ?? true)
                    enabledOfficialFeeds.Add(feed);
                else
                    diagnostics.Add(new SourceFetchDiagnostic(
                        feed.SourceId, SourceFetchOutcome.Skipped, "turned off for this station", null, null));
            }

            var deadline = TimeSpan.FromSeconds(
                Math.Min(Math.Max(1, _options.OfficialFeedTimeoutSeconds), 15));
            // Start the authority feeds before awaiting HERE. This is deliberately a task rather
            // than an await inline: the slowest authority must overlap the HERE request rather than
            // add its full budget to the traffic critical path.
            officialFetchTask = HarvestOfficialFeedsAsync(
                enabledOfficialFeeds, area, deadline, ct, jobId, now);
        }

        metrics.TrackPipelineStage(
            "traffic_cache_miss", 0, market ?? string.Empty, true);
        var result = await trafficProvider.GetCorridorsAsync(area, maxCorridors, ct, exclusions);
        if (result.IsFailure)
        {
            var diagnostic = new SourceFetchDiagnostic(
                "here", SourceFetchOutcome.Failed, DescribeFailure(result.Error), null, null);
            if (!string.IsNullOrWhiteSpace(jobId))
                await LogSourceDiagnosticAsync(jobId, $"HERE traffic unavailable: {diagnostic.Detail}", ct);
            return Result<TrafficFetchResult>.Success(
                new TrafficFetchResult([], [diagnostic]));
        }

        var corridors = result.Value.ToList();
        var fetchedFeeds = await officialFetchTask;
        diagnostics.AddRange(fetchedFeeds.Select(e => e.Diagnostic));
        var officialEvents = fetchedFeeds.SelectMany(e => e.Events).ToList();
        var freshSourceIds = fetchedFeeds
            .Where(e => e.Diagnostic.Outcome == SourceFetchOutcome.Ok)
            .Select(e => e.SourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_options.OfficialFeedsEnabled && _officialFeeds.Count > 0)
        {
            // `enabledOfficialFeeds`, not `_officialFeeds`: enrichment resolves per-source
            // capabilities, and a disabled feed contributed no events to enrich.
            officialEvents = (await EnrichOfficialEventsAsync(
                officialEvents, enabledOfficialFeeds, market, ct)).ToList();

            var corroborationOpts = new CorroborationOptions(
                Enabled: _options.OfficialFeedsEnabled,
                CorroborateIncidents: _options.CorroborateIncidents,
                InjectMissingOfficialIncidents: _options.InjectMissingOfficialIncidents,
                MaxCorroborationDistanceMeters: _options.MaxCorroborationDistanceMeters,
                CorroborationGraceMinutes: _options.CorroborationGraceMinutes,
                FreshSourceIds: freshSourceIds,
                Exclusions: exclusions);

            corridors = TrafficEvidenceFusion.Fuse(
                corridors, officialEvents, corroborationOpts, now).ToList();
        }

        diagnostics.Insert(0, new SourceFetchDiagnostic(
            "here", SourceFetchOutcome.Ok, null, timeProvider.GetUtcNow(), null));
        var finalResult = Result<TrafficFetchResult>.Success(
            new TrafficFetchResult(corridors, diagnostics));
        _trafficCache.Set(
            cacheKey,
            finalResult,
            TimeSpan.FromSeconds(Math.Max(1, _options.TrafficCacheTtlSeconds)));
        return finalResult;
    }

    private async Task<OfficialFeedFetch[]> HarvestOfficialFeedsAsync(
        IReadOnlyList<IOfficialTrafficFeed> enabledFeeds,
        CoverageArea area,
        TimeSpan deadline,
        CancellationToken ct,
        string? jobId,
        DateTimeOffset now)
    {
        // Every ENABLED jurisdiction is bounded and runs in parallel. A feed outside the coverage
        // area returns nothing quickly; a slow or dead endpoint costs at most its own budget.
        using var feedConcurrency = new SemaphoreSlim(3);
        var fetches = enabledFeeds.Select(async feed =>
        {
            await feedConcurrency.WaitAsync(ct);
            try
            {
                var fetched = await FetchOfficialFeedAsync(
                    feed, area, deadline, ct, jobId, now);
                return new OfficialFeedFetch(
                    fetched.SourceId, fetched.Events, fetched.Diagnostic);
            }
            finally
            {
                feedConcurrency.Release();
            }
        });
        return await Task.WhenAll(fetches);
    }

    private async Task<(string SourceId, IReadOnlyList<OfficialTrafficEvent> Events, SourceFetchDiagnostic Diagnostic)>
        FetchOfficialFeedAsync(
            IOfficialTrafficFeed feed, CoverageArea area, TimeSpan deadline, CancellationToken ct,
            string? jobId, DateTimeOffset now)
    {
        string cacheKey = BuildOfficialFeedCacheKey(feed.SourceId, area);
        var current = timeProvider.GetUtcNow();
        if (cache.TryGetValue(cacheKey, out OfficialFeedCacheEntry? cached)
            && cached is not null)
        {
            var age = current - cached.FetchedAtUtc;
            if (age >= TimeSpan.Zero
                && age <= TimeSpan.FromMinutes(Math.Max(0, _options.OfficialFeedFreshMinutes)))
            {
                logger.LogDebug(
                    "[Corroboration] Feed {SourceId} served fresh cached payload age {AgeSeconds}s",
                    feed.SourceId, Math.Max(0, (int)age.TotalSeconds));
                return (feed.SourceId, cached.Events,
                    new SourceFetchDiagnostic(
                        feed.SourceId, SourceFetchOutcome.Ok, null, cached.FetchedAtUtc, null));
            }
        }

        using var feedBudget = OfficialFeedCallBudget.Begin(deadline);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);
        try
        {
            var feedRes = await feed.GetEventsAsync(area, cts.Token);
            if (feedRes.IsSuccess)
            {
                var fetchedAt = timeProvider.GetUtcNow();
                var entry = new OfficialFeedCacheEntry(feedRes.Value, fetchedAt);
                cache.Set(
                    cacheKey,
                    entry,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow =
                            TimeSpan.FromMinutes(Math.Max(1, _options.OfficialFeedMaxStaleMinutes)),
                        Size = 1
                    });
                return (feed.SourceId, entry.Events,
                    new SourceFetchDiagnostic(feed.SourceId, SourceFetchOutcome.Ok, null, fetchedAt, null));
            }

            return await ServeOfficialFeedFailureAsync(
                feed, area, deadline, ct, jobId, now, DescribeFailure(feedRes.Error));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await ServeOfficialFeedFailureAsync(
                feed, area, deadline, ct, jobId, now,
                $"timed out after {deadline.TotalSeconds:0}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await ServeOfficialFeedFailureAsync(
                feed, area, deadline, ct, jobId, now, DescribeFailure(ex.Message));
        }
    }

    private async Task<(string SourceId, IReadOnlyList<OfficialTrafficEvent> Events, SourceFetchDiagnostic Diagnostic)>
        ServeOfficialFeedFailureAsync(
            IOfficialTrafficFeed feed, CoverageArea area, TimeSpan deadline, CancellationToken ct,
            string? jobId, DateTimeOffset now, string detail)
    {
        var current = timeProvider.GetUtcNow();
        string cacheKey = BuildOfficialFeedCacheKey(feed.SourceId, area);
        if (cache.TryGetValue(cacheKey, out OfficialFeedCacheEntry? cached)
            && cached is not null)
        {
            var age = current - cached.FetchedAtUtc;
            if (age >= TimeSpan.Zero
                && age <= TimeSpan.FromMinutes(Math.Max(1, _options.OfficialFeedMaxStaleMinutes)))
            {
                int staleSeconds = Math.Max(0, (int)age.TotalSeconds);
                var outcome = new SourceFetchDiagnostic(
                    feed.SourceId, SourceFetchOutcome.ServedStale, detail,
                    cached.FetchedAtUtc, staleSeconds);
                if (!string.IsNullOrWhiteSpace(jobId))
                    await LogSourceDiagnosticAsync(
                        jobId,
                        $"Official feed {feed.SourceId} failed ({detail}); served cached data from {cached.FetchedAtUtc:O} ({staleSeconds}s old)",
                        ct);
                if (age <= TimeSpan.FromMinutes(Math.Max(0, _options.OfficialFeedFreshMinutes)))
                    logger.LogInformation(
                        "[Corroboration] Feed {SourceId} failed ({Detail}); serving cached payload age {AgeSeconds}s",
                        feed.SourceId, detail, staleSeconds);
                else
                    logger.LogWarning(
                        "[Corroboration] Feed {SourceId} failed ({Detail}); serving cached payload age {AgeSeconds}s",
                        feed.SourceId, detail, staleSeconds);
                return (feed.SourceId, cached.Events, outcome);
            }
        }

        var diagnostic = new SourceFetchDiagnostic(
            feed.SourceId, SourceFetchOutcome.Failed, detail, null, null);
        logger.LogWarning("[Corroboration] Feed {SourceId} failed ({Detail}); no usable cache",
            feed.SourceId, detail);
        if (!string.IsNullOrWhiteSpace(jobId))
            await LogSourceDiagnosticAsync(jobId,
                $"Official feed {feed.SourceId} unavailable: {detail}", ct);
        return (feed.SourceId, [], diagnostic);
    }
    private async Task LogSourceDiagnosticAsync(
        string jobId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await jobStatusUpdater.LogJobAsync(jobId, message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[Corroboration] Could not persist source diagnostic for Job {JobId}", jobId);
        }
    }

    private async Task<IReadOnlyList<OfficialTrafficEvent>> EnrichOfficialEventsAsync(
        IReadOnlyList<OfficialTrafficEvent> events,
        IEnumerable<IOfficialTrafficFeed> feeds,
        string? market,
        CancellationToken cancellationToken)
    {
        var feedList = feeds.ToList();
        var capabilitiesBySource = feedList
            .GroupBy(feed => feed.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Capabilities,
                StringComparer.OrdinalIgnoreCase);
        var enriched = events.ToArray();
        var unknownBySource = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < enriched.Length; index++)
        {
            var current = enriched[index];
            var classification = IncidentClassCrosswalk.Classify(
                current.SourceId, current.Category, current.SubCategory);
            if (classification.HasValue)
            {
                enriched[index] = current with { CanonicalClass = classification.Value };
            }
            else if (capabilitiesBySource.ContainsKey(current.SourceId))
            {
                var unknown = unknownBySource.GetValueOrDefault(current.SourceId)
                    ?? (unknownBySource[current.SourceId] = []);
                AddUnknownVocabulary(unknown, current.Category);
                AddUnknownVocabulary(unknown, current.SubCategory);
            }
        }

        var reports = new List<SourceParseReport>();
        foreach (var source in feedList.GroupBy(feed => feed.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            var capabilities = source.First().Capabilities;
            var origins = Enumerable.Range(0, enriched.Length)
                .Where(index => string.Equals(enriched[index].SourceId, source.Key,
                    StringComparison.OrdinalIgnoreCase))
                .Select(index => BuildOrigins(enriched[index], capabilities))
                .ToArray();
            reports.Add(SourceParseReport.FromOrigins(
                capabilities,
                origins,
                unknownBySource.GetValueOrDefault(source.Key) ?? []));
        }

        if (_sourceParseReportRepository is not null && reports.Count > 0)
        {
            try
            {
                var saveResult = await _sourceParseReportRepository.SaveAsync(
                    market ?? string.Empty, timeProvider.GetUtcNow(), reports, cancellationToken);
                if (saveResult.IsFailure)
                    logger.LogWarning("[ParseCoverage] Could not persist source reports: {Error}",
                        saveResult.Error);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ParseCoverage] Source report persistence failed (ignored)");
            }
        }

        if (_incidentPriorityRepository is not null && !string.IsNullOrWhiteSpace(market))
        {
            var landscapeEntries = enriched
                .Select(e => new IncidentLandscapeEntry(
                    Provider: e.SourceId,
                    CanonicalClass: e.CanonicalClass?.ToString() ?? "Hazard",
                    EventType: e.Category ?? e.Headline ?? "Traffic Event",
                    Details: e.Description ?? e.Detail ?? e.Headline ?? "",
                    Impact: e.ImpactType ?? (e.IsIncidentClass ? "Incident" : "Roadworks"),
                    Codes: null,
                    Count: 1,
                    FirstSeenUtc: DateTime.UtcNow,
                    LastSeenUtc: DateTime.UtcNow))
                .ToList();

            if (landscapeEntries.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _incidentPriorityRepository.RecordOccurrencesAsync(
                            market, landscapeEntries, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[Landscape] Failed background landscape recording for {Market}", market);
                    }
                });
            }
        }
        return enriched;
    }

    private static void AddUnknownVocabulary(List<string> values, string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0
            || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || values.Contains(value!, StringComparer.Ordinal))
            return;

        values.Add(value!);
    }

    private static string? GetOfficialProse(OfficialTrafficEvent @event) =>
        !string.IsNullOrWhiteSpace(@event.Detail) ? @event.Detail : @event.Description;

    private static bool HasStructuredValue(OfficialTrafficEvent @event, string field) =>
        field switch
        {
            ParsedField.RoadName => !string.IsNullOrWhiteSpace(@event.RoadName),
            ParsedField.Category => !string.IsNullOrWhiteSpace(@event.Category),
            ParsedField.SubCategory => !string.IsNullOrWhiteSpace(@event.SubCategory),
            ParsedField.Direction => !string.IsNullOrWhiteSpace(@event.Direction),
            ParsedField.LanesClosed => @event.LanesClosed is > 0,
            ParsedField.LanesTotal => @event.LanesTotal is > 0,
            ParsedField.SpeedLimitKph => @event.SpeedLimitKph is > 0,
            ParsedField.StartTime => @event.StartTime.HasValue,
            ParsedField.EndTime => @event.EndTime.HasValue,
            ParsedField.AuthorityPriority => !string.IsNullOrWhiteSpace(@event.AuthorityPriority),
            ParsedField.OtherAssets => @event.OtherAssets is { Count: > 0 },
            _ => false
        };

    private static IReadOnlyDictionary<string, FieldOrigin> BuildOrigins(
        OfficialTrafficEvent @event,
        SourceCapabilities capabilities)
    {
        var origins = new Dictionary<string, FieldOrigin>(StringComparer.Ordinal);
        foreach (string field in capabilities.StructuredFields.Concat(capabilities.ProseFields).Distinct(
                     StringComparer.Ordinal))
        {
            origins[field] = HasStructuredValue(@event, field)
                ? FieldOrigin.Structured
                : FieldOrigin.Absent;
        }

        return origins;
    }


    internal async Task<Option<IncidentDeltaContext>> ComputeIncidentChangesAsync(
        BroadcastRequest request,
        List<TrafficIncident> incidents,
        CancellationToken cancellationToken)
    {
        string marketId = request.MarketId.GetValueOr(request.Location);
        if (string.IsNullOrWhiteSpace(marketId))
            return default;

        var deltaOption = await incidentStateService.ComputeAndPersistDeltaAsync(marketId, request.TimezoneId, incidents, cancellationToken);

        if (deltaOption.HasValue && _incidentPriorityRepository is not null)
        {
            var newIncidentKeys = deltaOption.Value.NewIncidents.Select(n => n.IncidentId).ToHashSet(StringComparer.Ordinal);
            if (newIncidentKeys.Count > 0)
            {
                var newEntries = incidents
                    .Where(i => newIncidentKeys.Contains(i.Id))
                    .Select(i =>
                    {
                        string? codesStr = i.Codes is { Count: > 0 } ? $"ALERT-C {string.Join(", ", i.Codes)}" : null;
                        return new IncidentLandscapeEntry(
                            Provider: i.Sources is { Count: > 0 } ? string.Join(", ", i.Sources) : "HERE",
                            CanonicalClass: MapPrimaryTypeToCanonical(i.Type),
                            EventType: i.Type,
                            Details: i.Comment.HasValue ? i.Comment.Value : (i.Summary.HasValue ? i.Summary.Value : i.Description),
                            Impact: i.RoadClosed ? "Full road closure" : "Traffic flow affected",
                            Codes: codesStr,
                            Count: 1,
                            FirstSeenUtc: DateTime.UtcNow,
                            LastSeenUtc: DateTime.UtcNow);
                    })
                    .ToList();

                if (newEntries.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _incidentPriorityRepository.RecordOccurrencesAsync(
                                marketId, newEntries, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "[Landscape] Failed background landscape recording for new incidents in {Market}", marketId);
                        }
                    });
                }
            }
        }

        return deltaOption;
    }

    internal async Task StoreIncidentsOnJob(
        string jobId,
        IEnumerable<TrafficIncident> incidents,
        IReadOnlyList<TrafficCorridor> selectedCorridors,
        IReadOnlyList<TrafficCorridor> allCorridors,
        Option<IncidentDeltaContext> incidentChanges,
        Option<IncidentDeltaContext> narrativeIncidentChanges,
        IReadOnlyList<MonitoredRouteStatus> monitoredRoutes,
        IReadOnlyList<SourceFetchDiagnostic> sourceDiagnostics,
        CancellationToken cancellationToken)
    {
        var selectedRoads = new HashSet<string>(
            selectedCorridors.Select(c => c.RoadName),
            StringComparer.OrdinalIgnoreCase);

        var storeResult = await jobStatusUpdater.UpdateJobAsync(jobId, job =>
        {
            job.IncidentsFetchedAt = Option<DateTime>.Some(timeProvider.GetUtcNow().UtcDateTime);
            job.SetIncidents(incidents.Select(i => i.ToJobIncident()));
            job.SetAllIncidents(allCorridors
                .SelectMany(c => c.Incidents)
                .DistinctBy(i => i.Id)
                .Select(i => i.ToJobIncident()));
            job.SetCorridors(allCorridors.Select((c, i) =>
                c.ToJobCorridor(i + 1, selectedRoads.Contains(c.RoadName))));
            incidentChanges.Tap(delta => job.IncidentChangeSummary = delta.ToChangeSummary(timeProvider));
            // Reconciled delta the script generator actually saw — deliberately a SEPARATE field
            // from the raw IncidentChangeSummary above, which stays pre-reconciliation ground
            // truth. Auditing a script against the raw summary alone can flag a correct "has
            // cleared" claim as fabricated when SuppressClearedOnActiveRoads legitimately dropped
            // a DIFFERENT road from the narrative-facing list (2026-07-07, job 4417add2).
            narrativeIncidentChanges.Tap(
                delta => job.NarrativeIncidentChangeSummary = delta.ToChangeSummary(timeProvider));
            job.MonitoredRoutes = monitoredRoutes;
            job.SourceDiagnostics = sourceDiagnostics.ToList();
        }, null, cancellationToken);

        if (storeResult.IsFailure)
            logger.LogWarning("[StoreIncidents] Job {JobId} failed to persist incident data: {Error}",
                jobId, storeResult.Error);
    }

    internal void LogTrafficStalenessWarning(string jobId, TimeSpan fetchElapsed)
    {
        // FallbackDeadlineMinutes is the threshold for how stale traffic data
        // can be before fallback is triggered.  Log a warning when the round-trip
        // fetch time itself eats significantly into that window.
        int deadlineMinutes = _options.FallbackDeadlineMinutes;
        if (deadlineMinutes > 0 && fetchElapsed.TotalMinutes > deadlineMinutes * 0.5)
            logger.LogWarning(
                "[TrafficStaleness] Job {JobId} traffic fetch took {ElapsedSec:F1}s ({Pct:F0}% of FallbackDeadlineMinutes={DeadlineMin}m). Data may arrive too late for fallback window.",
                jobId, fetchElapsed.TotalSeconds,
                fetchElapsed.TotalMinutes / deadlineMinutes * 100,
                deadlineMinutes);
    }

    internal sealed class TrafficEvidenceCacheState
    {
        private const int EntryLimit = 2_000;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = EntryLimit });
        private readonly ConcurrentDictionary<string, Lazy<Task<Result<TrafficFetchResult>>>> _flights =
            new(StringComparer.Ordinal);
        private readonly ILogger<BroadcastOrchestrator> _logger;
        private readonly IBroadcastMetrics _metrics;

        internal TrafficEvidenceCacheState(
            ILogger<BroadcastOrchestrator> logger, IBroadcastMetrics metrics)
        {
            _logger = logger;
            _metrics = metrics;
        }

        internal int Count => _cache.Count;

        internal bool TryGet(
            string key, out Result<TrafficFetchResult>? value) =>
            _cache.TryGetValue(key, out value);

        internal void Set(string key, Result<TrafficFetchResult> value, TimeSpan ttl)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
                Size = 1
            };
            options.RegisterPostEvictionCallback(
                (evictedKey, _, reason, state) =>
                {
                    var stateCache = (TrafficEvidenceCacheState)state!;
                    stateCache._logger.LogDebug(
                        "[TrafficCache] Evicted {Key} ({Reason})", evictedKey, reason);
                    stateCache._metrics.TrackDataValidity("traffic-cache", "evicted", 1);
                },
                this);
            _cache.Set(key, value, options);
        }

        internal Lazy<Task<Result<TrafficFetchResult>>> GetOrAddFlight(
            string key, Func<Task<Result<TrafficFetchResult>>> factory) =>
            _flights.GetOrAdd(
                key,
                _ => new Lazy<Task<Result<TrafficFetchResult>>>(
                    factory, LazyThreadSafetyMode.ExecutionAndPublication));

        internal void RemoveFlight(
            string key, Lazy<Task<Result<TrafficFetchResult>>> flight) =>
            ((ICollection<KeyValuePair<string, Lazy<Task<Result<TrafficFetchResult>>>>>)_flights)
                .Remove(new KeyValuePair<string, Lazy<Task<Result<TrafficFetchResult>>>>(key, flight));
    }

    /// <summary>
    ///     Rundown items the slot can hold — see <see cref="SlotCapacity" />. ARN's
    ///     <see cref="EditorialSettings.MaxCorridors" /> caps it further where set: a busy market that
    ///     wants more than the shipped ceiling raises it, which is what RISK-33 was waiting on. The
    ///     slot's own arithmetic still wins when it is the tighter of the two — no cap can conjure
    ///     airtime that does not exist.
    /// </summary>
    internal int CalculateMaxCorridors(double targetDurationSec, EditorialSettings? settings = null)
    {
        int slotCapacity = SlotCapacity.Compute(targetDurationSec, _options.SlotSecondsPerItem);
        return settings?.MaxCorridors is { } cap ? Math.Min(slotCapacity, cap) : slotCapacity;
    }
}
