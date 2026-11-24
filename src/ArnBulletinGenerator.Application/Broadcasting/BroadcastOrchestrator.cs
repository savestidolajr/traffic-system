using ArnBulletinGenerator.Domain.Contracts;
using System.Diagnostics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Coordinates the broadcast pipeline, delegating job status, traffic evidence, script
///     generation, and audio finalization to four extracted collaborators.
/// </summary>
public sealed class BroadcastOrchestrator(
    ScriptPipelineRoles scriptRoles,
    AudioPipelineServices audioServices,
    ContentProviders contentProviders,
    IIncidentStateService incidentStateService,
    IJobRepository jobRepository,
    IOptions<BroadcastOptions> options,
    IOptions<DeliveryOptions> deliveryOptions,
    IOptions<SpeechDurationOptions> speechDurationOptions,
    INotificationService notificationService,
    TimeProvider timeProvider,
    IMemoryCache cache,
    IBroadcastMetrics metrics,
    IPriorityRoadsRepository priorityRoadsRepository,
    IFillerStatementsRepository fillerStatementsRepository,
    IFutureLessonRepository futureLessonRepository,
    IGuidanceRepository guidanceRepository,
    IEditorialSettingsRepository editorialSettingsRepository,
    IOptions<MonitoredRoutesOptions> monitoredRoutesOptions,
#pragma warning disable CS9113 // Parameter is unread (retained for backward compatibility in constructor signature)
    IBulletinFallbackService? bulletinFallbackService,
    IJobService? jobService,
#pragma warning restore CS9113
    ILogger<BroadcastOrchestrator> logger,
    // from the container; a null one just means every suspect word is reported.
    IVerifiedPronunciationRepository? verifiedPronunciations = null,
    // Optional-last for the same reason: the Desk's omission verdicts are enforcement when
    // present, and a null repository (direct-construction tests) means nothing is omitted.
    IEventVerdictRepository? eventVerdictRepository = null,
    IOptions<BroadcastWorkBudgetOptions>? workBudgetOptions = null,
    IGenerationProgressReporterFactory? progressReporterFactory = null) : IBroadcastOrchestrator
{
    private readonly BroadcastOptions _options = options.Value;
    private readonly BroadcastWorkBudgetOptions _workBudgetOptions =
        workBudgetOptions?.Value ?? new BroadcastWorkBudgetOptions();

    // Collaborators extracted from this class: each owns one slice of the pipeline and gets
    // exactly the dependencies its methods use. Built here (plain `new`, no DI changes) so the
    // public constructor remains straightforward for direct-construction tests.
    // Built via one static factory (not four separate field initializers, which cannot
    // reference each other) so every collaborator shares the SAME JobStatusUpdater instance —
    // including the review/word-target/delivery collaborators the orchestrator never touches
    // directly but that ScriptGenerationEngine and AudioFinalizer both depend on.
    private readonly (
        JobStatusUpdater Jobs,
        TrafficEvidenceBuilder Traffic,
        ScriptGenerationEngine Script,
        AudioFinalizer Audio) _collaborators = BuildCollaborators(
        scriptRoles, audioServices, contentProviders, jobRepository, notificationService, timeProvider,
        logger, cache, priorityRoadsRepository, fillerStatementsRepository,
        monitoredRoutesOptions, futureLessonRepository, guidanceRepository,
        editorialSettingsRepository, incidentStateService, eventVerdictRepository, metrics, options, deliveryOptions,
        speechDurationOptions, verifiedPronunciations, progressReporterFactory);
    private JobStatusUpdater _jobStatusUpdater => _collaborators.Jobs;
    private TrafficEvidenceBuilder _trafficEvidenceBuilder => _collaborators.Traffic;
    private ScriptGenerationEngine _scriptGenerationEngine => _collaborators.Script;
    private AudioFinalizer _audioFinalizer => _collaborators.Audio;

    private static (
        JobStatusUpdater Jobs,
        TrafficEvidenceBuilder Traffic,
        ScriptGenerationEngine Script,
        AudioFinalizer Audio) BuildCollaborators(
        ScriptPipelineRoles scriptRoles,
        AudioPipelineServices audioServices,
        ContentProviders contentProviders,
        IJobRepository jobRepository,
        INotificationService notificationService,
        TimeProvider timeProvider,
        ILogger<BroadcastOrchestrator> logger,
        IMemoryCache cache,
        IPriorityRoadsRepository priorityRoadsRepository,
        IFillerStatementsRepository fillerStatementsRepository,
        IOptions<MonitoredRoutesOptions> monitoredRoutesOptions,
        IFutureLessonRepository futureLessonRepository,
        IGuidanceRepository guidanceRepository,
        IEditorialSettingsRepository editorialSettingsRepository,
        IIncidentStateService incidentStateService,
        IEventVerdictRepository? eventVerdictRepository,
        IBroadcastMetrics metrics,
        IOptions<BroadcastOptions> options,
        IOptions<DeliveryOptions> deliveryOptions,
        IOptions<SpeechDurationOptions> speechDurationOptions,
        IVerifiedPronunciationRepository? verifiedPronunciations,
        IGenerationProgressReporterFactory? progressReporterFactory)
    {
        var jobs = new JobStatusUpdater(
            jobRepository, notificationService, timeProvider, logger, progressReporterFactory);
        var traffic = new TrafficEvidenceBuilder(
            jobRepository, timeProvider, cache, contentProviders.TrafficProvider, priorityRoadsRepository,
            fillerStatementsRepository, contentProviders.RoutingProvider, monitoredRoutesOptions,
            contentProviders.WeatherProvider, contentProviders.FuelPriceProvider, futureLessonRepository, guidanceRepository,
            editorialSettingsRepository, incidentStateService, eventVerdictRepository, metrics, options, logger, jobs,
            contentProviders.OfficialFeeds,
            contentProviders.SourceParseReportRepository, contentProviders.IncidentPriorityRepository,
            contentProviders.EmergencyIncidentFeed);
        var scriptReviewLoop = new ScriptReviewLoop(
            scriptRoles.ScriptReviewer, scriptRoles.ScriptRefiner,
            options, metrics, scriptRoles.ContentSafetyFilter, scriptRoles.ScriptSafetyReviewer, logger, jobs,
            timeProvider);
        var deliveryDirector = new DeliveryDirector(
            scriptRoles.ScriptDirector, deliveryOptions, logger, jobs);
        var script = new ScriptGenerationEngine(
            scriptRoles.ScriptGenerator, audioServices.AudioPipeline,
            audioServices.DurationEstimator, audioServices.FillerAudioCacheService, audioServices.VoiceCalibrationService,
            options, speechDurationOptions, metrics, scriptRoles.DurationPredictor, scriptRoles.SpokenDurationModel, logger,
            timeProvider, jobs, scriptReviewLoop, deliveryDirector,
            scriptRoles.DurationSamples, scriptRoles.DurationCalibration);
        var audio = new AudioFinalizer(
            audioServices.DurationEstimator, audioServices.BroadcastOutput, jobRepository, notificationService, timeProvider,
            audioServices.VoiceCalibrationService, audioServices.AudioPipeline, scriptRoles.ScriptGenerator, options,
            speechDurationOptions, audioServices.AudioTranscriber, verifiedPronunciations, logger, jobs,
            scriptReviewLoop, deliveryDirector);
        return (jobs, traffic, script, audio);
    }
    public async Task<Result<BroadcastResult>> OrchestrateAsync(BroadcastRequest request, Phase phase,
        CancellationToken cancellationToken = default)
    {
        request = request with { Phase = phase };
        using (logger.BeginScope(new Dictionary<string, object> { ["JobId"] = request.JobId }))
        {
            var orchestrationSw = Stopwatch.StartNew();
            IGenerationProgressReporter? progress = progressReporterFactory?.Create(
                request.JobId,
                request.MarketId.GetValueOr(request.Location),
                JobStatus.Requested);

            logger.LogInformation(
                "[Orchestrate] Job {JobId} initiated — Location={Location}, TargetDuration={TargetDurationSec}s, ScriptOnly={ScriptOnly}",
                request.JobId, request.Location, request.TargetDurationSeconds, request.ScriptOnly);
            if (progress is not null)
                await progress.StageStarted(ProgressStage.Evidence, "Fetching bulletin evidence.");
            await _jobStatusUpdater.LogJobAsync(request.JobId, "Starting orchestration", cancellationToken);
            // Defense-in-depth (the queue lease is the primary gate): never reprocess an
            // operator-finalized or fallback-used job.
            if (await EnsureNotAlreadyFinalizedAsync(request.JobId, cancellationToken) is { } finalizedGuard)
                return finalizedGuard;

            if (request.Phase == Phase.Generate)
            {
                var parked = await jobRepository.GetJobAsync(request.JobId, cancellationToken);
                if (parked.IsFailure || parked.Value?.GeneratorInput is null)
                    return Result<BroadcastResult>.Failure(
                        parked.IsFailure ? parked.Error : "Curation context is unavailable.");

                var resumedContext = parked.Value.GeneratorInput.ToContext(request.JobId) with
                {
                    Location = request.Location,
                    TargetDurationSeconds = request.TargetDurationSeconds,
                    ScriptOnly = request.ScriptOnly,
                    MarketId = request.MarketId,
                    TimezoneId = request.TimezoneId,
                    VoiceId = request.VoiceId,
                    Model = request.Model,
                    VoiceSettings = request.VoiceSettings,
                    Phase = Phase.Generate,
                    Content = request.Content,
                    FillOrder = request.FillOrder
                };
                await _jobStatusUpdater.UpdateJobStatus(
                    request.JobId, JobStatus.IncidentsSelected, "Curation complete.", cancellationToken);
                return await GenerateBroadcastAsync(resumedContext, cancellationToken, progress);
            }
            var enrichedRequest = await _trafficEvidenceBuilder.EnrichRequestWithJobContext(request, cancellationToken);

            bool hasRejectionFeedback = enrichedRequest.RejectionFeedback.HasValue;
            bool hasPreviousScript = enrichedRequest.PreviousScript.HasValue;
            if (hasRejectionFeedback || hasPreviousScript)
                logger.LogInformation(
                    "[Orchestrate] Job {JobId} enriched with context — HasRejectionFeedback={HasFeedback}, HasPreviousScript={HasPrevScript}",
                    request.JobId, hasRejectionFeedback, hasPreviousScript);

            // WS1: content sources toggle independently. Traffic disabled -> weather/fuel-only
            // bulletin; skip the traffic fetch and staleness machinery entirely.
            if (!enrichedRequest.Content.Traffic)
                return await OrchestrateTrafficlessAsync(enrichedRequest, orchestrationSw, cancellationToken, progress);

            await _jobStatusUpdater.UpdateJobStatus(request.JobId, JobStatus.HereFetched, "Fetching traffic...", cancellationToken);

            // ARN's numeric editorial settings are resolved ONCE, up front: corridor selection below
            // needs the corridor cap before market content exists, and resolving twice per bulletin
            // risks the two reads disagreeing mid-generation.
            var editorialSettings = await _trafficEvidenceBuilder.ResolveEditorialSettingsAsync(
                enrichedRequest, cancellationToken);

            // Coverage/market-only content (weather, fuel, monitored-route drive times, plus
            // guidance/filler/style/feedback) depends on NONE of the traffic result — fetch it
            // concurrently with the traffic fetch below instead of in a second wave after it.
            var marketContentTask = _trafficEvidenceBuilder.FetchMarketContentAsync(
                enrichedRequest.WithCorridors([]), enrichedRequest.Coverage, editorialSettings, cancellationToken);
            // The free-flow pool likewise depends only on coverage. Started here it overlaps the
            // traffic fetch; awaited after ranking it added 6.9s to the critical path (dev 2e158de3).
            var freeFlowTask = _trafficEvidenceBuilder.FetchFreeFlowingRoadsAsync(
                enrichedRequest.Coverage, cancellationToken);

            var trafficSw = Stopwatch.StartNew();
            var corridorsResult =
                await _trafficEvidenceBuilder.GetCachedCorridorsAsync(enrichedRequest.Coverage, _options.MaxCorridorsForStorage,
                    cancellationToken, enrichedRequest.MarketId.GetValueOr(enrichedRequest.Location), request.JobId,
                    editorialSettings);
            metrics.TrackPipelineStage("traffic_fetch", trafficSw.Elapsed.TotalSeconds,
                request.Location, corridorsResult.IsSuccess);

            logger.LogInformation(
                "[TrafficFetch] Job {JobId} traffic fetch completed in {ElapsedMs}ms — Success={IsSuccess}",
                request.JobId, trafficSw.ElapsedMilliseconds, corridorsResult.IsSuccess);

            if (corridorsResult.IsSuccess)
            {
                var fetchedCorridors = corridorsResult.Value.Corridors;
                int totalIncidents = fetchedCorridors.SelectMany(c => c.Incidents).Count();
                await _jobStatusUpdater.LogJobAsync(request.JobId,
                    $"Traffic fetched: {fetchedCorridors.Count} corridor(s), {totalIncidents} incident(s) in {trafficSw.ElapsedMilliseconds}ms",
                    cancellationToken);
            }

            if (corridorsResult.IsFailure)
            {
                await Task.WhenAll(marketContentTask, freeFlowTask); // observe the concurrently-started fetches before bailing out
                await _jobStatusUpdater.MarkJobFailedAsync(request.JobId,
                    $"Failed to fetch traffic: {corridorsResult.Error}", cancellationToken);
                return Result<BroadcastResult>.Failure($"Failed to fetch traffic: {corridorsResult.Error}");
            }

            var trafficFetch = corridorsResult.Value;
            var allCorridors = trafficFetch.Corridors.ToList();
            if (trafficFetch.SourceDiagnostics.Any(d =>
                    d.SourceId.Equals("here", StringComparison.OrdinalIgnoreCase)
                    && d.Outcome == SourceFetchOutcome.Failed))
            {
                var failedMarketContent = await marketContentTask;
                await _trafficEvidenceBuilder.StoreIncidentsOnJob(
                    request.JobId, [], [], [], default, default,
                    failedMarketContent.MonitoredRoutes,
                    trafficFetch.SourceDiagnostics.Concat(failedMarketContent.SourceDiagnostics).ToList(),
                    cancellationToken);
                await _jobStatusUpdater.MarkJobFailedAsync(
                    request.JobId, "Failed to fetch traffic: HERE unavailable", cancellationToken);
                return Result<BroadcastResult>.Failure("Failed to fetch traffic: HERE unavailable");
            }


            if (progress is not null)
                await progress.StageStarted(ProgressStage.Select, "Ranking evidence for the bulletin slot.");
            // Priority arterials (WS2 / SDD §4.2) outrank equally-scored minor roads at the cap.
            var priorityRoadsSw = Stopwatch.StartNew();
            var priorityRoads = await _trafficEvidenceBuilder.GetPriorityRoadsSafeAsync(enrichedRequest, cancellationToken);
            metrics.TrackPipelineStage("priority_roads", priorityRoadsSw.Elapsed.TotalSeconds, request.Location, true);
            allCorridors = PriorityRoads.BoostAndSort(allCorridors, PriorityRoads.EnabledIncidentBoostNames(priorityRoads));

            int slotCapacity = _trafficEvidenceBuilder.CalculateMaxCorridors(
                enrichedRequest.TargetDurationSeconds, editorialSettings);
            // Unified rundown: incident corridors and flowing good-runs compete for the slot by score.
            var flowPoolSw = Stopwatch.StartNew();
            var flowPool = await _trafficEvidenceBuilder.BuildFlowPoolAsync(
                allCorridors, priorityRoads, enrichedRequest.Coverage, cancellationToken, freeFlowTask);
            metrics.TrackPipelineStage("flow_pool", flowPoolSw.Elapsed.TotalSeconds, request.Location, true);
            var content = BulletinContentSelector.Select(allCorridors, flowPool, slotCapacity, TrafficEvidenceBuilder.GoodRunScriptCap);
            var selectedCorridors = content.Corridors.ToList();
            var goodRunNames = content.GoodRuns.Select(f => f.RoadName).ToList();
            var incidents = selectedCorridors.SelectMany(c => c.Incidents).ToList();
            if (progress is not null)
                await progress.StageCompleted(
                    ProgressStage.Select,
                    $"Selected {selectedCorridors.Count} corridors and {goodRunNames.Count} flowing roads.",
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
                    request.JobId, string.Join(", ", suspiciousIncidentRoads));

            logger.LogInformation(
                "[TrafficAggregation] Job {JobId} — SelectedCorridors={SelectedCount}, TotalCorridors={TotalCount}, TotalIncidents={IncidentCount}",
                request.JobId, selectedCorridors.Count, allCorridors.Count, incidents.Count);

            // Per-fetch validity accounting, from the REAL data this bulletin is built from — the
            // trend series is driven by broadcasts, not a scheduled probe. data.validity metrics,
            // dimensions market + measure; entry_only_fresh_closures is the re-entry loophole's
            // tripwire on the dangerous claim class (build the demotion when it goes nonzero).
            var validity = FetchValidityAccounting.Count(incidents, timeProvider.GetUtcNow());
            string validityMarket = request.MarketId.GetValueOr(request.Location);
            metrics.TrackDataValidity(validityMarket, "here.records", validity.Records);
            metrics.TrackDataValidity(validityMarket, "here.fresh", validity.Fresh);
            metrics.TrackDataValidity(validityMarket, "here.ongoing", validity.Ongoing);
            metrics.TrackDataValidity(validityMarket, "here.scheduled", validity.Scheduled);
            metrics.TrackDataValidity(validityMarket, "here.closures", validity.Closures);
            metrics.TrackDataValidity(validityMarket, "here.closures.demoted", validity.DemotedClosures);
            metrics.TrackDataValidity(validityMarket, "here.flagged", validity.Flagged);
            metrics.TrackDataValidity(validityMarket, "here.closures.entry_only_fresh", validity.EntryOnlyFreshClosures);

            var incidentStateSw = Stopwatch.StartNew();
            var incidentChanges = await _trafficEvidenceBuilder.ComputeIncidentChangesAsync(enrichedRequest, incidents, cancellationToken);
            metrics.TrackPipelineStage("incident_state", incidentStateSw.Elapsed.TotalSeconds,
                request.Location, incidentChanges.HasValue);

            if (incidentChanges.HasValue)
            {
                var delta = incidentChanges.Value;
                logger.LogInformation(
                    "[IncidentDelta] Job {JobId} — New={NewCount}, Updated={UpdatedCount}, Cleared={ClearedCount}",
                    request.JobId, delta.NewIncidents.Count, delta.UpdatedIncidents.Count, delta.ClearedIncidents.Count);
                await _jobStatusUpdater.LogJobAsync(request.JobId,
                    $"Incident changes: {delta.NewIncidents.Count} new, {delta.UpdatedIncidents.Count} updated, {delta.ClearedIncidents.Count} cleared",
                    cancellationToken);
            }

            // Reconcile cleared-vs-active AFTER logging the raw delta above (audit trail keeps ground
            // truth) but BEFORE the narrative-facing context is built — a road cleared by one HERE
            // incident while a DIFFERENT incident is newly active on that same road must not be voiced
            // as both states at once (live feedback, Port Augusta 2026-07-06: "A1 is still under
            // construction ... construction on A1 has cleared").
            var narrativeIncidentChanges = incidentChanges.Map(
                delta => IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, selectedCorridors));

            // "Changes since your last bulletin"/"cleared" framing is a claim that the audience
            // already heard a prior bulletin today — only true when a same-day PUBLISHED predecessor
            // exists (enrichedRequest.PreviousJobId is only set from one, see
            // GetLatestBulletinForMarketTodayAsync). The audit trail (StoreIncidentsOnJob below) keeps
            // the full, ungated delta regardless — this only controls what reaches the writer's prompt.
            var promptIncidentChanges = enrichedRequest.PreviousJobId.HasValue
                ? narrativeIncidentChanges
                : Option<IncidentDeltaContext>.None;
            var marketContent = await marketContentTask;
            if (progress is not null)
                await progress.StageCompleted(
                    ProgressStage.Evidence,
                    $"Evidence fetched: {trafficFetch.Corridors.Count} corridors and {trafficFetch.Corridors.SelectMany(c => c.Incidents).Count()} incidents.",
                    new Dictionary<string, double>
                    {
                        ["corridors"] = trafficFetch.Corridors.Count,
                        ["incidents"] = trafficFetch.Corridors.SelectMany(c => c.Incidents).Count()
                    });
            var context = enrichedRequest.WithCorridors(selectedCorridors) with
            {
                IncidentChanges = promptIncidentChanges,
                FlowPool = flowPool,
                GoodRunRoads = goodRunNames
            };
            context = TrafficEvidenceBuilder.ApplyMarketContent(context, marketContent);

            await _trafficEvidenceBuilder.StoreIncidentsOnJob(request.JobId, incidents, selectedCorridors, allCorridors, incidentChanges,
                narrativeIncidentChanges, context.MonitoredRoutes,
                trafficFetch.SourceDiagnostics.Concat(marketContent.SourceDiagnostics).ToList(),
                cancellationToken);

            // Traffic data staleness check — drop incidents with old DataTimestampUtc
            var now = timeProvider.GetUtcNow();
            var stalenessThreshold = now.AddMinutes(-_options.TrafficDataMaxAgeMinutes);
            int preFilterCount = incidents.Count;
            incidents = incidents.Where(i => i.DataTimestampUtc is null || i.DataTimestampUtc >= stalenessThreshold).ToList();
            int droppedCount = preFilterCount - incidents.Count;

            if (droppedCount > 0)
            {
                metrics.TrackTrafficStalenessDropped(request.Location, droppedCount);
                logger.LogWarning(
                    "[TrafficStaleness] Job {JobId} dropped {DroppedCount} stale incidents (threshold={ThresholdMinutes}min)",
                    request.JobId, droppedCount, _options.TrafficDataMaxAgeMinutes);
            }

            if (incidents.Count == 0 && preFilterCount > 0)
            {
                logger.LogWarning(
                    "[TrafficStaleness] Job {JobId} ALL {PreFilterCount} incidents were stale — triggering fallback",
                    request.JobId, preFilterCount);
                await _jobStatusUpdater.MarkJobFailedAsync(request.JobId, "TrafficDataStale", cancellationToken);
                return Result<BroadcastResult>.Failure("All traffic data is stale");
            }

            _trafficEvidenceBuilder.LogTrafficStalenessWarning(request.JobId, trafficSw.Elapsed);
            if (enrichedRequest.Phase == Phase.Fetch)
            {
                var job = await jobRepository.GetJobAsync(request.JobId, cancellationToken);
                if (job.IsSuccess && job.Value is not null)
                {
                    job.Value.GeneratorInput = GeneratorInputSnapshot.FromContext(context);
                    await jobRepository.SaveJobAsync(job.Value, cancellationToken);
                }

                await _jobStatusUpdater.UpdateJobStatus(
                    request.JobId, JobStatus.AwaitingCuration,
                    "Traffic fetched; awaiting operator curation.", cancellationToken);
                return Result<BroadcastResult>.Success(new BroadcastResult(
                    string.Empty, "Awaiting operator curation",
                    new BroadcastDebugInfo(string.Empty, string.Empty, 0)));
            }

            await _jobStatusUpdater.UpdateJobStatus(request.JobId, JobStatus.IncidentsSelected, "Traffic fetched.", cancellationToken);

            var result = await GenerateBroadcastAsync(context, cancellationToken, progress);

            orchestrationSw.Stop();
            metrics.TrackPipelineStage("orchestration_total", orchestrationSw.Elapsed.TotalSeconds,
                request.Location, result.IsSuccess);
            metrics.TrackBulletinEmitted(request.Location, result.IsSuccess);
            logger.LogInformation(
                "[Orchestrate] Job {JobId} completed in {TotalElapsedMs}ms — Success={IsSuccess}",
                request.JobId, orchestrationSw.ElapsedMilliseconds, result.IsSuccess);

            return result;
        }
    }

    public async Task<Result<BroadcastResult>> OrchestrateAsync(BroadcastWithTraffic context,
        CancellationToken cancellationToken = default)
    {
        using (logger.BeginScope(new Dictionary<string, object> { ["JobId"] = context.JobId }))
        {
            IGenerationProgressReporter? progress = progressReporterFactory?.Create(
                context.JobId,
                context.MarketId.GetValueOr(context.Location),
                JobStatus.Requested);
            logger.LogInformation(
                "[Orchestrate] Job {JobId} started with pre-fetched traffic — CorridorCount={CorridorCount}, ScriptOnly={ScriptOnly}",
                context.JobId, context.Corridors.Count, context.ScriptOnly);
            if (progress is not null)
            {
                await progress.StageStarted(
                    ProgressStage.Evidence,
                    "Using pre-fetched bulletin evidence.",
                    new Dictionary<string, double>
                    {
                        ["corridors"] = context.Corridors.Count,
                        ["incidents"] = context.Corridors.SelectMany(c => c.Incidents).Count()
                    });
                await progress.StageCompleted(
                    ProgressStage.Evidence,
                    $"Evidence available: {context.Corridors.Count} corridors and {context.Corridors.SelectMany(c => c.Incidents).Count()} incidents.",
                    new Dictionary<string, double>
                    {
                        ["corridors"] = context.Corridors.Count,
                        ["incidents"] = context.Corridors.SelectMany(c => c.Incidents).Count()
                    });
            }
            await _jobStatusUpdater.LogJobAsync(context.JobId, "Starting orchestration with pre-fetched traffic", cancellationToken);

            if (await EnsureNotAlreadyFinalizedAsync(context.JobId, cancellationToken) is { } finalizedGuard)
                return finalizedGuard;

            // Re-voice only: the operator kept the words and just wants a fresh read (e.g. after a manual
            // script edit). Skip the traffic re-fetch and the whole LLM generation/review loop — synthesise
            // the existing/edited script verbatim. This is the path that puts an operator's exact wording
            // on air (the full Regenerate path is data-driven and re-writes from a fresh fetch).
            if (context.RevoiceOnly)
                return await _audioFinalizer.RevoiceExistingScriptAsync(context, cancellationToken, progress);

            if (context.Corridors.Count == 0)
                context = await _trafficEvidenceBuilder.TryRefreshTrafficAsync(context, cancellationToken, progress);

            return await GenerateBroadcastAsync(context, cancellationToken, progress);
        }
    }

    /// <summary>
    ///     Defense-in-depth guard (the queue lease is the primary gate): returns a Failure result
    ///     when the job is already operator-finalized (Approved/Published) or has used fallback.
    ///     Reprocessing would clobber that state at the first status transition. Failed, Requested,
    ///     and in-flight jobs stay reprocessable for transient retry, regenerate, and stale-lease recovery.
    /// </summary>
    private async Task<Result<BroadcastResult>?> EnsureNotAlreadyFinalizedAsync(
        string jobId, CancellationToken cancellationToken)
    {
        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsSuccess && jobResult.Value is { } job
            && job.Status is JobStatus.Approved or JobStatus.Published or JobStatus.FallbackUsed)
        {
            logger.LogWarning(
                "[Orchestrate] Job {JobId} already in terminal state {Status}; skipping reprocess.",
                jobId, job.Status);
            return Result<BroadcastResult>.Failure(OperationalFailure.Conflict(
                "broadcast.job_already_terminal", $"Job already in terminal state: {job.Status}"));
        }

        if (jobResult.IsSuccess && progressReporterFactory?.TryGet(jobId) is { } progress)
        {
            try
            {
                await progress.StatusChanged(jobResult.Value.Status);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "[Orchestrate] Job {JobId} initial progress status update failed", jobId);
            }
        }

        return null;
    }

    /// <summary>
    ///     WS1: weather/fuel-only bulletin when the traffic source is disabled. Requires at
    ///     least one fetched content source — an all-empty bulletin would invite fabrication.
    /// </summary>
    private async Task<Result<BroadcastResult>> OrchestrateTrafficlessAsync(
        BroadcastRequest enrichedRequest,
        Stopwatch orchestrationSw,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress)
    {
        await _jobStatusUpdater.LogJobAsync(enrichedRequest.JobId,
            "Traffic source disabled for this bulletin — generating from weather/fuel content",
            cancellationToken);

        var context = enrichedRequest.WithCorridors([]);
        var marketContent = await _trafficEvidenceBuilder.FetchMarketContentAsync(
            context, enrichedRequest.Coverage,
            await _trafficEvidenceBuilder.ResolveEditorialSettingsAsync(enrichedRequest, cancellationToken),
            cancellationToken);
        context = TrafficEvidenceBuilder.ApplyMarketContent(context, marketContent);
        if (progress is not null)
            await progress.StageCompleted(
                ProgressStage.Evidence,
                "Evidence fetched from the enabled non-traffic sources.",
                new Dictionary<string, double>
                {
                    ["corridors"] = 0,
                    ["incidents"] = 0
                });

        if (!context.Weather.HasValue && !context.Fuel.HasValue)
        {
            const string reason =
                "No content available: traffic is disabled and no weather/fuel data could be fetched.";
            await _jobStatusUpdater.MarkJobFailedAsync(enrichedRequest.JobId, reason, cancellationToken);
            return Result<BroadcastResult>.Failure(reason);
        }

        if (enrichedRequest.Phase == Phase.Fetch)
        {
            var job = await jobRepository.GetJobAsync(enrichedRequest.JobId, cancellationToken);
            if (job.IsSuccess && job.Value is not null)
            {
                job.Value.GeneratorInput = GeneratorInputSnapshot.FromContext(context);
                await jobRepository.SaveJobAsync(job.Value, cancellationToken);
            }

            await _jobStatusUpdater.UpdateJobStatus(
                enrichedRequest.JobId, JobStatus.AwaitingCuration,
                "Content fetched; awaiting operator curation.", cancellationToken);
            return Result<BroadcastResult>.Success(new BroadcastResult(
                string.Empty, "Awaiting operator curation",
                new BroadcastDebugInfo(string.Empty, string.Empty, 0)));
        }

        await _jobStatusUpdater.UpdateJobStatus(enrichedRequest.JobId, JobStatus.IncidentsSelected, "Content fetched.",
            cancellationToken);
        if (progress is not null)
        {
            await progress.StageStarted(ProgressStage.Select, "Applying the enabled content selection.");
            await progress.StageCompleted(
                ProgressStage.Select,
                "Selected the enabled non-traffic content.",
                new Dictionary<string, double>());
        }

        var result = await GenerateBroadcastAsync(context, cancellationToken, progress);
        orchestrationSw.Stop();
        metrics.TrackPipelineStage("orchestration_total", orchestrationSw.Elapsed.TotalSeconds,
            enrichedRequest.Location, result.IsSuccess);
        metrics.TrackBulletinEmitted(enrichedRequest.Location, result.IsSuccess);
        return result;
    }

    private async Task<Result<BroadcastResult>> GenerateBroadcastAsync(
        BroadcastWithTraffic context,
        CancellationToken cancellationToken,
        IGenerationProgressReporter? progress)
    {
        logger.LogInformation(
            "[GenerateBroadcast] Job {JobId} entering generation loop — TargetDuration={TargetSec}s",
            context.JobId, context.TargetDurationSeconds);
        var genSw = Stopwatch.StartNew();

        using var workBudget = BroadcastWorkBudget.Start(
            timeProvider.GetUtcNow(),
            slotCutoffUtc: null,
            invocationDeadlineUtc: null,
            cancellationToken,
            _workBudgetOptions);
        var generationOutcome = await _scriptGenerationEngine.TryGenerateValidBroadcastAsync(
            context, workBudget.Token, workBudget, progress);
        genSw.Stop();

        var generationResult = generationOutcome.Result;
        logger.LogInformation(
            "[GenerateBroadcast] Job {JobId} generation loop finished in {ElapsedMs}ms — Success={IsSuccess}",
            context.JobId, genSw.ElapsedMilliseconds, generationResult.IsSuccess);

        if (generationResult.IsFailure)
        {
            if (generationOutcome.Candidate is { } candidate)
            {
                try
                {
                    await _audioFinalizer.PersistFailedCandidateAsync(context, candidate, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Candidate retention is inspection-only evidence. A storage or repository
                    // outage must not replace the original generation failure or prevent the
                    // fallback/deadline policy from settling the slot.
                    logger.LogWarning(ex,
                        "[GenerateBroadcast] Job {JobId} failed to retain its best generation candidate",
                        context.JobId);
                }
            }
            var disposition = generationResult.FailureMetadata.HasValue
                ? generationResult.FailureMetadata.Value.Disposition
                : FailureDisposition.Retryable;

            // Cover the slot NOW for every failure the queue will not redeliver. QueueFailurePolicy
            // acknowledges (no retry) ContentLimited/SafetyRejected/Permanent/Conflict, so for those
            // there is no second chance coming — leaving the job Failed strands the slot until the
            // JobWatchdog deadline sweep, burning the operator's whole review window and raising an
            // intervention card for something the standby chain could have filled silently.
            // Conflict is excluded: it means the job is already terminal, so there is nothing to cover.
            // Retryable/InvalidInput still fall through — the queue redelivers and the watchdog remains
            // the net. (Asymmetry found by the 2026-07-26 TEST load burst: two jobs failed the same
            // grounding gate; the one that also tripped ContentLimited was covered, the other was not.)
            if (disposition is FailureDisposition.ContentLimited
                or FailureDisposition.SafetyRejected
                or FailureDisposition.Permanent)
            {
                var failure = generationResult.FailureMetadata.HasValue
                    ? generationResult.FailureMetadata.Value
                    : OperationalFailure.ContentLimited("broadcast.content_limited", generationResult.Error);
                return await HandleUnairableAsync(context, failure, cancellationToken);
            }

            await _jobStatusUpdater.MarkJobFailedAsync(context.JobId, generationResult.Error, cancellationToken);
            return generationResult.PropagateFailure<BroadcastResult>();
        }

        (string script, string taggedScript, byte[] audio, IReadOnlyList<WordTiming> wordTimings) = generationResult.Value;
        if (context.ScriptOnly)
        {
            logger.LogInformation("[GenerateBroadcast] Job {JobId} ScriptOnly mode — skipping audio pipeline",
                context.JobId);
            return await _audioFinalizer.HandleScriptOnlyResultAsync(context, script, cancellationToken, progress);
        }

        return await _audioFinalizer.FinalizeAudioAsync(
            context, script, taggedScript, audio, wordTimings, cancellationToken, progress);
    }

    /// <summary>
    ///     Handles a generation outcome that produced no airable script AND that the queue will not
    ///     redeliver, so this is the slot's last chance. Two causes reach here:
    ///     <list type="bullet">
    ///         <item>
    ///             <b>Content-limited</b> — not enough honest material to fill the slot within the
    ///             slow-down cap (a genuinely quiet market, not a pipeline fault).
    ///         </item>
    ///         <item>
    ///             <b>Quality gate</b> — the final grounding hard gate exhausted its refine budget, or
    ///             content safety rejected the script. Airing it is not an option and retrying the same
    ///             fetched content is not expected to clear the gate.
    ///         </item>
    ///     </list>
    ///     For an UNATTENDED generation (poller first-pass, ad-hoc trigger) the slot is covered from
    ///     the standby chain (last good same-day bulletin → filler cart) and the job becomes
    ///     <see cref="JobStatus.FallbackUsed" />. An OPERATOR-ATTENDED generation (regenerate) is
    ///     instead marked failed with an actionable message — covering early would steal the slot
    ///     mid-edit — and the <c>JobWatchdog</c> deadline sweep remains the net in both shapes.
    /// </summary>
    /// <summary>
    ///     Who signs off a slot the generator could not fill: no operator was asked, and the
    ///     source is content they already approved or the network's own cart.
    /// </summary>
    private const string SystemCoverageActor = "system (coverage)";

    private async Task<Result<BroadcastResult>> HandleUnairableAsync(BroadcastWithTraffic context,
        OperationalFailure failure, CancellationToken cancellationToken)
    {
        bool contentLimited = failure.Disposition == FailureDisposition.ContentLimited;
        string tag = contentLimited ? "ContentLimited" : "QualityGate";

        // Under all circumstances, an unairable generation (content-limited or quality-gate rejected)
        // must NOT auto-publish a fallback bulletin early. The best candidate script is retained
        // on the job entity so operators can inspect, edit, and manually publish if desired.
        // If no operator intervenes, the JobWatchdog deadline sweep will automatically cover the
        // slot at T-0 (the broadcast cutoff) to ensure no dead air.
        string note = contentLimited
            ? "Could not fill the slot: not enough eligible content. The best candidate script has been retained for editing. The slot will be covered automatically at the deadline if nothing is approved."
            : "Generated script did not clear the quality gates. The best candidate script has been retained for editing. The slot will be covered automatically at the deadline if nothing is approved.";

        logger.LogWarning(
            "[{Tag}] Job {JobId} — {Error} Marking failed/unairable without early playout delivery.",
            tag, context.JobId, failure.Message);
        await _jobStatusUpdater.LogJobAsync(context.JobId, note, cancellationToken);
        await _jobStatusUpdater.MarkJobFailedAsync(context.JobId, failure.Message, cancellationToken);
        metrics.TrackPipelineStage(contentLimited ? "content_limited" : "quality_gate", 0,
            context.MarketId.GetValueOr(context.Location), false);
        return Result<BroadcastResult>.Failure(failure);
    }
}
