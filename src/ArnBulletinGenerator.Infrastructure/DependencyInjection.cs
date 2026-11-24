using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureSpeech;
using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Audio.DurationStrategies;
using ArnBulletinGenerator.Infrastructure.Calibration;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.ContentSafety;
using ArnBulletinGenerator.Infrastructure.Diagnostics;
using ArnBulletinGenerator.Infrastructure.Fakes;
using ArnBulletinGenerator.Infrastructure.Identity;
using ArnBulletinGenerator.Infrastructure.Notifications;
using ArnBulletinGenerator.Infrastructure.Output;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Workflow.Hosting;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using OfficialFeedsOptions = ArnBulletinGenerator.Infrastructure.Configuration.OfficialFeedsOptions;

namespace ArnBulletinGenerator.Infrastructure;

/// <summary>
///     Registers every Infrastructure-layer implementation (HERE/ElevenLabs/Azure OpenAI
///     providers, audio processing, Azure Table/Blob persistence, Teams notifications, output
///     sinks) with the DI container, binding and validating each configuration Options type,
///     and substitutes the deterministic Fakes/* providers for external systems when the
///     ARN_E2E fake-provider mode is enabled.
/// </summary>
public static class DependencyInjection
{
    private static readonly TokenCredential AzureCredential = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions
        {
            ExcludeAzurePowerShellCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeInteractiveBrowserCredential = true
        });

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<TokenCredential>(_ => AzureCredential);
        BindOptions(services, configuration);
        AddAiServices(services, configuration);
        AddPersistence(services);
        AddIdentity(services);
        AddStorage(services, configuration);
        AddFakeExternalProvidersForE2E(services, configuration);
        services.AddSingleton(EmbeddedSpokenDurationCalibration.Load());
        services.AddSingleton<ISpokenDurationModel>(sp =>
            new TokenAdditiveDurationModel(
                sp.GetRequiredService<SpokenDurationCalibration>(),
                sp.GetRequiredService<IOptions<SpokenDurationModelOptions>>().Value));
        return services;
    }

    private static void BindOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BroadcastOptions>()
            .Bind(configuration.GetSection(BroadcastOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AzureAIOptions>()
            .Bind(configuration.GetSection(AzureAIOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ProviderOptions>()
            .Bind(configuration.GetSection(ProviderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ElevenLabsOptions>()
            .Bind(configuration.GetSection(ElevenLabsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<HereOptions>()
            .Bind(configuration.GetSection(HereOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OfficialFeedsOptions>()
            .Bind(configuration.GetSection(OfficialFeedsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<EmergencyFeedOptions>()
            .Bind(configuration.GetSection(EmergencyFeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OfficialFeedsOptions>()
            .PostConfigure<ILoggerFactory>((options, loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("OfficialFeeds");
                if (string.IsNullOrWhiteSpace(options.QldApiKey))
                {
                    logger.LogWarning(
                        "[OfficialFeeds] QLD corroboration is unusable: no API key configured. " +
                        "Set the OfficialFeeds--QldApiKey Key Vault secret.");
                }
                else if (string.Equals(options.QldApiKey, OfficialFeedsOptions.PublishedDemoApiKey,
                             StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "[OfficialFeeds] RISK-36: QLD is using the published shared demo API key. " +
                        "It is globally rate-limited, so corroboration may drop out unpredictably.");
                }
            });
        services.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ModelSelectionOptions>()
            .Bind(configuration.GetSection(ModelSelectionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SpeechDurationOptions>()
            .Bind(configuration.GetSection(SpeechDurationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AudioLoudnessOptions>()
            .Bind(configuration.GetSection(AudioLoudnessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ZettaOptions>()
            .Bind(configuration.GetSection(ZettaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DurationPredictionOptions>()
            .Bind(configuration.GetSection(DurationPredictionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SpokenDurationModelOptions>()
            .Bind(configuration.GetSection(SpokenDurationModelOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<MonitoredRoutesOptions>()
            .Bind(configuration.GetSection(MonitoredRoutesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DeliveryOptions>()
            .Bind(configuration.GetSection(DeliveryOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<SynthesisHistoryOptions>()
            .Bind(configuration.GetSection(SynthesisHistoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ContentSafetyOptions>()
            .Bind(configuration.GetSection(ContentSafetyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<WeatherOptions>()
            .Bind(configuration.GetSection(WeatherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<FuelOptions>()
            .Bind(configuration.GetSection(FuelOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AuditExportOptions>()
            .Bind(configuration.GetSection(AuditExportOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ConfigImportOptions>()
            .Bind(configuration.GetSection(ConfigImportOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<JobPersistenceOptions>()
            .Bind(configuration.GetSection(JobPersistenceOptions.SectionName));
        services.AddOptions<JobRetentionOptions>()
            .Bind(configuration.GetSection(JobRetentionOptions.SectionName));
        services.AddOptions<JobWorkIndexOptions>()
            .Bind(configuration.GetSection(JobWorkIndexOptions.SectionName));
        services.AddOptions<ScheduleDueIndexOptions>()
            .Bind(configuration.GetSection(ScheduleDueIndexOptions.SectionName));
        services.AddOptions<LedgerReadModelOptions>()
            .Bind(configuration.GetSection(LedgerReadModelOptions.SectionName));
        services.AddOptions<DeliveryQueueOptions>()
            .Bind(configuration.GetSection(DeliveryQueueOptions.SectionName));
        services.AddOptions<InteractiveWorkOptions>()
            .Bind(configuration.GetSection(InteractiveWorkOptions.SectionName));
        services.AddOptions<LlmCaptureOptions>()
            .Bind(configuration.GetSection(LlmCaptureOptions.SectionName));
        services.AddOptions<VersionedLibraryOptions>()
            .Bind(configuration.GetSection(VersionedLibraryOptions.SectionName));
        services.AddOptions<BlobAudioCacheOptions>()
            .Bind(configuration.GetSection(BlobAudioCacheOptions.SectionName));
        services.AddOptions<GuidanceOptions>()
            .Bind(configuration.GetSection(GuidanceOptions.SectionName));
        services.AddOptions<SafetyTermIdentityOptions>()
            .Bind(configuration.GetSection("SafetyTermIdentity"));
        services.AddOptions<CoverageOptions>()
            .Bind(configuration.GetSection(CoverageOptions.SectionName));
        services.AddOptions<IncidentLandscapeOptions>()
            .Bind(configuration.GetSection(IncidentLandscapeOptions.SectionName));
        services.AddOptions<MapAttributesCacheOptions>()
            .Bind(configuration.GetSection(MapAttributesCacheOptions.SectionName));
        services.AddOptions<PreviewAudioCleanupOptions>()
            .Bind(configuration.GetSection(PreviewAudioCleanupOptions.SectionName));
        services.AddOptions<AuditQueueOptions>()
            .Bind(configuration.GetSection(AuditQueueOptions.SectionName));
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName));
        services.AddOptions<QueueClaimOptions>()
            .Bind(configuration.GetSection(QueueClaimOptions.SectionName));
        services.AddOptions<DebugOptions>()
            .Bind(configuration.GetSection(DebugOptions.SectionName))
            .ValidateOnStart();
    }

    private static void AddAiServices(IServiceCollection services, IConfiguration configuration)
    {
        // RateLimitRetryHandler owns BOTH the 429/503 retry loop AND the per-attempt timeout
        // (AzureAIOptions.TimeoutSeconds, default 120s, resolved via constructor-injected
        // IOptions<AzureAIOptions>) — a single hung attempt is cut and retried instead of
        // blocking a pipeline slot for the full 5-minute client timeout below, which remains
        // the overall backstop for the whole retry loop (not per attempt).
        services.AddTransient<RateLimitRetryHandler>();
        var openAiBuilder = services.AddHttpClient("AzureOpenAI",
            client => client.Timeout = TimeSpan.FromMinutes(5));
        openAiBuilder.AddHttpMessageHandler<RateLimitRetryHandler>();
        openAiBuilder.AddTransientHttpErrorPolicy(policy => policy
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        // Pre-approval audio review: Azure AI Speech fast transcription on the same AIServices
        // account. Inert (Enabled=false) until AiSettings:AzureAI:SpeechEndpoint is configured.
        services.AddHttpClient(nameof(AzureSpeechTranscriber),
            client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<IAudioTranscriber, AzureSpeechTranscriber>();

        services.AddHttpClient(); // Untyped default — used by HealthHttpTrigger downstream probes.

        var elevenBuilder = services.AddHttpClient<ElevenLabsClient>((sp, client) =>
            client.Timeout =
                TimeSpan.FromSeconds(
                    sp.GetRequiredService<IOptions<ElevenLabsOptions>>().Value.TimeoutSeconds));
        elevenBuilder.AddTransientHttpErrorPolicy(policy => policy
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
        services.AddSingleton<ElevenLabsTtsBudget>();
        services.AddSingleton<CachedVoiceProvider>(sp =>
            new CachedVoiceProvider(
                sp.GetRequiredService<ElevenLabsClient>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<CachedVoiceProvider>>(),
                sp.GetRequiredService<IMemoryCache>()));
        services.AddSingleton<IVoiceProvider>(sp => sp.GetRequiredService<CachedVoiceProvider>());
        services.AddSingleton<IVoiceCatalogCacheInvalidator>(
            sp => sp.GetRequiredService<CachedVoiceProvider>());
        services.AddScoped<IVoiceRemixer>(sp => sp.GetRequiredService<ElevenLabsClient>());
        services.AddScoped<IPronunciationDictionaryProvider>(sp => sp.GetRequiredService<ElevenLabsClient>());
        services.AddSingleton<ILlmCallCapture, BlobLlmCallCapture>();
        services.AddMemoryCache();
        services.AddScoped<LlmScriptGenerator>();
        services.AddScoped<ILessonSummarizer, LessonSummarizer>();
        services.AddSingleton<IShippedPlaybookProvider, ShippedPlaybookProvider>();
        // WHICH WRITER AIRS. Conversational v1 is the only script pipeline. Everything downstream
        // is shared: bookends, filler, pronunciation, ceiling trim, TTS, safety, fallback and
        // persistence all remain in the ScriptGenerationEngine.
        var aiSection = configuration.GetSection("AiSettings:AzureAI");
        var workflowAgents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string role in (string[])["writeIncidents", "writeFlow", "compose2", "edit"])
        {
            if (aiSection[$"WorkflowAgents:{role}"] is { Length: > 0 } agent)
                workflowAgents[role] = agent;
        }

        var azureAiOptions = configuration.GetSection(AzureAIOptions.SectionName).Get<AzureAIOptions>()
            ?? new AzureAIOptions();

        services.AddBulletinWorkflows(
            endpoint: aiSection["Endpoint"] ?? string.Empty,
            model: aiSection["Model"] ?? string.Empty,
            workflowId: "conversational-v1",
            projectEndpoint: aiSection["FoundryProjectEndpoint"],
            agentByRole: workflowAgents,
            callBudget: azureAiOptions.CallBudget);
        services.AddScoped<IScriptGenerator>(sp =>
            new AiProviders.Conversational.ConversationalScriptGenerator(
                sp.GetRequiredService<Workflow.Contracts.IWorkflowLlm>(),
                sp.GetRequiredService<IDurationPredictor>(),
                sp.GetRequiredService<Workflow.Contracts.IDurationOracle>(),
                sp.GetRequiredService<ILlmCallCapture>(),
                sp.GetRequiredService<IOptions<BroadcastOptions>>().Value,
                sp.GetRequiredService<ILogger<AiProviders.Conversational.ConversationalScriptGenerator>>()));
        services.AddScoped<AiProviders.Conversational.IPlaybookPreviewGenerator,
            AiProviders.Conversational.ConversationalPlaybookPreviewGenerator>();
        services.AddScoped<AiProviders.Conversational.IPlaybookPreviewService,
            AiProviders.Conversational.PlaybookPreviewService>();
        services.AddScoped<IScriptReviewer>(sp => sp.GetRequiredService<LlmScriptGenerator>());
        services.AddScoped<IScriptRefiner>(sp => sp.GetRequiredService<LlmScriptGenerator>());
        services.AddScoped<IScriptSafetyReviewer>(sp => sp.GetRequiredService<LlmScriptGenerator>());
        services.AddScoped<IScriptDirector>(sp => sp.GetRequiredService<LlmScriptGenerator>());
        services.AddScoped<IDeliveryWriter>(sp => sp.GetRequiredService<LlmScriptGenerator>());
        services.AddScoped<IScriptRewriter>(sp => sp.GetRequiredService<LlmScriptGenerator>());

        // All HERE clients share ONE resilience policy (transient-error + timeout retry with
        // jittered backoff, wrapping a PER-ATTEMPT timeout) via AddHereResilience — consistent
        // across traffic/routing/weather/fuel/geocoding. The per-attempt timeout means a transient
        // failure's backoff can never cancel a healthy retry (the bug that reported weather/fuel
        // "unavailable" while traffic on the same API key succeeded).
        services.AddHttpClient<HereTrafficProvider>()
            .AddHereResilience(sp => sp.GetRequiredService<IOptions<HereOptions>>().Value.RequestTimeoutSeconds);
        services.AddScoped<ITrafficProvider>(sp => sp.GetRequiredService<HereTrafficProvider>());

        // Official road-authority feeds (validity monitor now; corroboration wave next). Public
        // CC-BY endpoints — no secrets. Registered as IOfficialTrafficFeed so callers ask every
        // jurisdiction; a feed outside the coverage area naturally returns nothing.
        services.AddTransient<OfficialFeedRetryHandler>();
        AddOfficialFeedHttpClient<LiveTrafficNswFeed>(services);
        AddOfficialFeedHttpClient<QldTrafficFeed>(services);
        AddOfficialFeedHttpClient<VicTrafficFeed>(services);
        AddOfficialFeedHttpClient<MainRoadsWaFeed>(services);
        AddOfficialFeedHttpClient<TrafficSaFeed>(services);
        AddOfficialFeedHttpClient<TasAlertFeed>(services);
        services.AddScoped<IOfficialTrafficFeed>(sp => sp.GetRequiredService<LiveTrafficNswFeed>());
        services.AddScoped<IOfficialTrafficFeed>(sp => sp.GetRequiredService<QldTrafficFeed>());
        services.AddScoped<IOfficialTrafficFeed>(sp => sp.GetRequiredService<VicTrafficFeed>());
        services.AddScoped<IOfficialTrafficFeed>(sp => sp.GetRequiredService<MainRoadsWaFeed>());
        services.AddScoped<IOfficialTrafficFeed>(sp => sp.GetRequiredService<TrafficSaFeed>());
        services.AddScoped<IOfficialTrafficFeed>(sp => sp.GetRequiredService<TasAlertFeed>());
        services.AddHttpClient<DataQuollEmergencyFeed>((sp, client) =>
            client.Timeout = TimeSpan.FromSeconds(Math.Max(
                1,
                sp.GetRequiredService<IOptions<EmergencyFeedOptions>>().Value.TimeoutSeconds)));
        services.AddScoped<IEmergencyIncidentFeed>(
            sp => sp.GetRequiredService<DataQuollEmergencyFeed>());
        services.AddHttpClient<HereRoutingProvider>()
            .AddHereResilience(sp => sp.GetRequiredService<IOptions<HereOptions>>().Value.RequestTimeoutSeconds);
        services.AddScoped<IRoutingProvider>(sp => sp.GetRequiredService<HereRoutingProvider>());

        services.AddHttpClient<IWeatherProvider, HereWeatherProvider>()
            .AddHereResilience(sp => sp.GetRequiredService<IOptions<WeatherOptions>>().Value.TimeoutSeconds);

        services.AddHttpClient<IFuelPriceProvider, HereFuelPriceProvider>()
            .AddHereResilience(sp => sp.GetRequiredService<IOptions<FuelOptions>>().Value.TimeoutSeconds);

        services.AddHttpClient<HereGeocodingProvider>()
            .AddHereResilience(sp => sp.GetRequiredService<IOptions<HereOptions>>().Value.RequestTimeoutSeconds);
        services.AddScoped<IGeocodingProvider>(sp => sp.GetRequiredService<HereGeocodingProvider>());

        // The map's own record of a road segment — the only HERE source that can settle an incident's
        // road identity from the segments the incident itself references (see HereMapAttributesProvider).
        services.AddHttpClient<HereMapAttributesProvider>()
            .AddHereResilience(sp => sp.GetRequiredService<IOptions<HereOptions>>().Value.RequestTimeoutSeconds);
        services.AddScoped<IMapAttributesProvider>(sp => sp.GetRequiredService<HereMapAttributesProvider>());

        services.AddScoped<ElevenLabsAudioSynthesizer>();
        services.AddScoped<IAudioSynthesizer>(sp => sp.GetRequiredService<ElevenLabsAudioSynthesizer>());

        services.AddScoped<IAudioProcessor, WavDurationProcessor>();
        services.AddScoped<WavLoudnessNormalizer>();
        services.AddScoped<WavDurationProcessor>(); // expose the concrete type alongside the existing IAudioProcessor registration, same pattern as ElevenLabsAudioSynthesizer/IAudioSynthesizer
        services.AddScoped<IFillerAudioCacheService, FillerAudioCacheService>();
        services.AddScoped<SpeechDurationEstimator>();
        services.AddScoped<ISpeechDurationEstimator>(sp => sp.GetRequiredService<SpeechDurationEstimator>());

        // Duration prediction — pluggable strategy selected at runtime via DurationPredictionOptions.Mode.
        services.AddScoped<StaticRateDurationPredictor>();
        // Explicit fallback wiring: HistoryBasedDurationPredictor takes an
        // IDurationPredictor fallback. Resolving that via ctor injection would
        // re-enter the IDurationPredictor factory below (mode=HistoryBased →
        // HistoryBasedDurationPredictor → IDurationPredictor → …): a runtime
        // circular dependency that MS.DI cannot detect through the factory
        // lambda. In Azure this hung every queue/timer invocation for 10
        // minutes until functionTimeout (found in sandbox validation 11 Jun
        // 2026). The fallback is always the static-rate strategy.
        services.AddScoped(sp => new HistoryBasedDurationPredictor(
            sp.GetRequiredService<ISynthesisHistoryRepository>(),
            sp.GetRequiredService<IOptions<DurationPredictionOptions>>(),
            sp.GetRequiredService<StaticRateDurationPredictor>()));
        services.AddSingleton<ISynthesisHistoryRepository, TableStorageSynthesisHistoryRepository>();
        // Lossless duration-calibration capture: written on the broadcast path, read by the fit.
        services.AddSingleton<ISpokenDurationSampleRepository, TableStorageSpokenDurationSampleRepository>();
        services.AddScoped<IDurationPredictor>(sp =>
        {
            var mode = sp.GetRequiredService<IOptions<DurationPredictionOptions>>().Value.Mode;
            return mode switch
            {
                PredictionMode.HistoryBased => sp.GetRequiredService<HistoryBasedDurationPredictor>(),
                _ => sp.GetRequiredService<StaticRateDurationPredictor>()
            };
        });
        services
            .AddScoped<IAudioPostSynthesisDurationAdjustmentStrategy,
                SentencePauseInsertionDurationAdjustmentStrategy>();
        services.AddScoped<AudioPipelineService>();
        services.AddScoped<IAudioPipelineService>(sp => sp.GetRequiredService<AudioPipelineService>());
        services.AddScoped<IDurationEstimator>(sp => sp.GetRequiredService<AudioPipelineService>());
        services.AddScoped<IVoiceCalibrationService, VoiceCalibrationService>();
        services.AddSingleton<IContentSafetyFilter, ConfigurableContentSafetyFilter>();

        // Polly owns nothing here: WebhookRetryHandler owns BOTH the per-attempt timeout and the
        // retry, so the client's global timeout must be disabled or it would cut the retry budget.
        services.AddTransient<WebhookRetryHandler>();
        services
            .AddHttpClient<INotificationService, WebhookNotificationService>(
                client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<WebhookRetryHandler>();
    }

    /// <summary>
    ///     Shared HERE HTTP resilience: a fresh PER-ATTEMPT timeout wrapped by a transient-error +
    ///     timeout retry with jittered exponential backoff. The HttpClient's own global timeout is
    ///     disabled (Polly owns all timeouts) so the retry budget is never cut short mid-backoff —
    ///     the defect that turned a recoverable blip into a hard weather/fuel failure.
    /// </summary>
    private static IHttpClientBuilder AddHereResilience(
        this IHttpClientBuilder builder, Func<IServiceProvider, int> perAttemptTimeoutSeconds)
    {
        builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);

        // Outer: retry transient HTTP errors AND per-attempt timeouts.
        builder.AddPolicyHandler(HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(3, attempt =>
                TimeSpan.FromSeconds(Math.Pow(2, attempt))
                + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250))));

        // Inner: a fresh timeout for EACH attempt (optimistic — honours the request token).
        builder.AddPolicyHandler((sp, _) =>
            Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(perAttemptTimeoutSeconds(sp)),
                TimeoutStrategy.Optimistic));

        return builder;
    }

    private static void AddOfficialFeedHttpClient<TFeed>(IServiceCollection services)
        where TFeed : class
    {
        services.AddHttpClient<TFeed>()
            .AddHttpMessageHandler<OfficialFeedRetryHandler>();
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddSingleton<TableStorageJobRepository>();
        services.AddSingleton<IJobRepository>(sp => sp.GetRequiredService<TableStorageJobRepository>());
        services.AddSingleton<IJobStatusRepository>(sp => sp.GetRequiredService<TableStorageJobRepository>());
        services.AddSingleton<ILedgerBucketRepository, TableStorageLedgerBucketRepository>();
        services.AddSingleton<IJobTimelineRepository, TableStorageJobTimelineRepository>();
        services.AddSingleton<IJobProgressRepository, TableStorageJobProgressRepository>();
        services.AddSingleton<IJobPayloadRepository, TableStorageJobPayloadRepository>();
        services.AddSingleton<IJobReadIndexRepository, TableStorageJobReadIndexRepository>();
        services.AddSingleton<IJobWorkIndexRepository, TableStorageJobWorkIndexRepository>();
        services.AddSingleton<IJobClaimRepository, TableStorageJobClaimRepository>();
        services.AddSingleton<IJobRetentionStore, TableStorageJobRetentionStore>();
        services.AddSingleton<ISafetyTermIdentityRepository, TableStorageSafetyTermIdentityRepository>();
        services.AddSingleton<IMapAttributesCacheRepository, TableStorageMapAttributesCacheRepository>();
        services.AddSingleton<IScheduleRepository, TableStorageScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, TableStorageIncidentStateRepository>();
        services.AddSingleton<IVoiceCalibrationRepository, TableStorageVoiceCalibrationRepository>();
        services.AddSingleton<IPriorityRoadsRepository, TableStoragePriorityRoadsRepository>();
        services.AddSingleton<IIncidentPriorityRepository, TableStorageIncidentPriorityRepository>();
        services.AddSingleton<IEventVerdictRepository, TableStorageEventVerdictRepository>();
        services.AddSingleton<IActivityLogRepository, TableStorageActivityLogRepository>();
        services.AddSingleton<IFillerStatementsRepository, TableStorageFillerStatementsRepository>();
        services.AddSingleton<IFillerAudioCacheRepository, TableStorageFillerAudioCacheRepository>();
        services.AddSingleton<IOperatorAssignmentRepository, TableStorageOperatorAssignmentRepository>();
        services.AddSingleton<IFutureLessonRepository, TableStorageFutureLessonRepository>();
        services.AddSingleton<IGuidanceRepository, TableStorageGuidanceRepository>();
        services.AddSingleton<ISafetyTermRepository, TableStorageSafetyTermRepository>();
        services.AddSingleton<IVoiceProfileRepository, TableStorageVoiceProfileRepository>();
        services.AddSingleton<IDeliveryStyleRepository, TableStorageDeliveryStyleRepository>();
        services.AddSingleton<IBulletinPlaybookRepository, TableStorageBulletinPlaybookRepository>();
        services.AddSingleton<IBookendRepository, TableStorageBookendRepository>();
        services.AddSingleton<IPronunciationDictionaryRepository, TableStoragePronunciationDictionaryRepository>();
        services.AddSingleton<IVerifiedPronunciationRepository, TableStorageVerifiedPronunciationRepository>();
        services.AddSingleton<IEditorialSettingsRepository, TableStorageEditorialSettingsRepository>();
        services.AddSingleton<ISourceParseReportRepository, TableStorageSourceParseReportRepository>();
        // The shipped seeds and slot manifest are deploy-time assets read once; only ARN's
        // overrides vary at runtime, and those come from the repository above per bulletin.
        services.AddSingleton<ICronParser, CronParser>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddHttpClient(nameof(EntraOperatorDirectory), client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IOperatorDirectory, EntraOperatorDirectory>();
    }


    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        // Singletons (not scoped): Azure SDK clients are thread-safe and designed for reuse,
        // and the blob/queue consumers that ensure container/queue existence once per process
        // (AzureBlobBroadcastOutput, QueuePublisher) rely on a process-lifetime instance — the
        // same reason the table repositories are singletons. Scoped services may freely consume
        // a singleton client (scoped -> singleton is the legal direction).
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            if (opts.AccountName is StorageOptions.LocalEmulatorAccount or null or "")
                return new BlobServiceClient("UseDevelopmentStorage=true");
            return new BlobServiceClient(
                new Uri($"https://{opts.AccountName}.blob.core.windows.net"),
                sp.GetRequiredService<TokenCredential>());
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            var queueClientOptions = new QueueClientOptions
            {
                MessageEncoding = QueueMessageEncoding.None
            };
            if (opts.AccountName is StorageOptions.LocalEmulatorAccount or null or "")
                return new QueueServiceClient("UseDevelopmentStorage=true", queueClientOptions);
            return new QueueServiceClient(
                new Uri($"https://{opts.AccountName}.queue.core.windows.net"),
                sp.GetRequiredService<TokenCredential>(),
                queueClientOptions);
        });

        // Singleton so its per-queue ensure-once set spans the process (see QueuePublisher).
        services.AddSingleton<IQueuePublisher, QueuePublisher>();
        services.AddScoped<IAuditExporter, TableStorageAuditExporter>();
        services.AddScoped<IConfigImportStore, BlobConfigImportStore>();
        services.AddSingleton<IJobAudioArtifactRepository, AzureBlobJobAudioArtifactRepository>();
        services.AddSingleton<IPreviewAudioCleanup, PreviewAudioCleanupService>();
        // Intentionally eager: ZettaOptions.Transport is read at registration time because
        // it determines WHICH delivery writer to register (AD DS-authenticated Azure Files
        // drop vs SFTP-enabled blob drop, both on ARN's one drop account).
        var zettaOpts = configuration.GetSection(ZettaOptions.SectionName).Get<ZettaOptions>() ?? new ZettaOptions();
        if (zettaOpts.Transport == DeliveryTransport.Mock)
            services.AddSingleton<IDeliveryFileWriter, MockDropFileWriter>();
        else if (zettaOpts.Transport == DeliveryTransport.BlobDrop)
            services.AddSingleton<IDeliveryFileWriter, BlobDropFileWriter>();
        else
            services.AddSingleton<IDeliveryFileWriter, AzureFilesDropFileWriter>();
        services.AddScoped<IFileDeliveryService, ZettaFileDeliveryService>();

        // Intentionally eager: ProviderOptions is read at registration time because
        // IsAzureBlobOutput determines WHICH implementations to register (blob vs local).
        // This cannot be deferred to IOptions<T> since the type selection must happen
        // during DI container configuration, not at resolution time.
        var providerOpts = configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ??
                           new ProviderOptions();
        if (providerOpts.IsAzureBlobOutput)
        {
            services.AddScoped<IAudioUrlProvider, BlobAudioUrlProvider>();
            // Singleton so its ensure-once container flag spans the process (see
            // AzureBlobBroadcastOutput); a scoped instance would re-issue the create every
            // broadcast, which is the 409-per-minute warning this fix removes.
            services.AddSingleton<IBroadcastOutput, AzureBlobBroadcastOutput>();
        }
        else
        {
            services.AddScoped<IAudioUrlProvider, LocalAudioUrlProvider>();
            services.AddScoped<IBroadcastOutput, ConsoleBroadcastOutput>();
        }
    }

    private static void AddFakeExternalProvidersForE2E(IServiceCollection services, IConfiguration configuration)
    {
        var providerOpts = configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ??
                           new ProviderOptions();
        if (!providerOpts.UseFakeExternalProviders)
            return;

        services.AddSingleton<FakeExternalCallLog>();
        services.AddScoped<ITrafficProvider, FakeTrafficProvider>();
        services.AddScoped<IRoutingProvider, FakeRoutingProvider>();
        services.AddScoped<IDetourRoutingProvider, FakeDetourRoutingProvider>();
        services.AddScoped<IGeocodingProvider, FakeGeocodingProvider>();
        services.AddScoped<IWeatherProvider, FakeWeatherProvider>();
        services.AddScoped<IFuelPriceProvider, FakeFuelPriceProvider>();
        services.AddScoped<FakeScriptAiProvider>();
        services.AddScoped<IScriptGenerator>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IScriptReviewer>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IScriptRefiner>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IScriptSafetyReviewer>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IScriptDirector>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IDeliveryWriter>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IScriptRewriter>(sp => sp.GetRequiredService<FakeScriptAiProvider>());
        services.AddScoped<IAudioSynthesizer, FakeAudioSynthesizer>();
        services.AddScoped<IVoiceProvider, FakeVoiceProvider>();
        services.AddScoped<IVoiceRemixer, FakeVoiceRemixer>();
        services.AddScoped<IPronunciationDictionaryProvider, FakePronunciationDictionaryProvider>();
        services.AddScoped<INotificationService, FakeNotificationService>();
        services.AddSingleton<IDeliveryFileWriter, FakeDeliveryFileWriter>();
    }
}
