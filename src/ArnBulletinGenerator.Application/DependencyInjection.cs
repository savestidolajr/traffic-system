using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Learning;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application;

/// <summary>
///     Registers the Application layer's DI services: orchestrator, job/schedule services,
///     incident-state tracking, model selection, and config libraries.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        // ModelSelector (capability-aware model resolution) caches the ElevenLabs voice list here.
        // TryAdd-based, so this is a no-op if a composition root already called AddMemoryCache().
        services.AddMemoryCache();
        // No-op metrics by default; the Functions host overrides with AppInsights adapter.
        services.AddSingleton<IBroadcastMetrics, NoOpBroadcastMetrics>();
        services.AddSingleton<IInteractiveWorkMetrics, NoOpInteractiveWorkMetrics>();
        services.AddSingleton<IInteractiveWorkGate, InteractiveWorkGate>();

        services.AddSingleton<IScheduleOccurrenceResolver>(sp =>
            new ScheduleOccurrenceResolver(
                sp.GetRequiredService<ICronParser>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ScheduleOccurrenceResolver>>()));

        return services
            .AddScoped<ScriptPipelineRoles>()
            .AddScoped<AudioPipelineServices>()
            .AddScoped<ContentProviders>()
            .AddScoped<IGenerationProgressReporterFactory, GenerationProgressReporterFactory>()
            .AddScoped<IBroadcastOrchestrator, BroadcastOrchestrator>()
            .AddScoped<ICoverageResolver, CoverageResolver>()
            .AddScoped<JobService>()
            .AddScoped<IJobService>(provider => provider.GetRequiredService<JobService>())
            .AddScoped<IJobDeliveryProcessor>(provider => provider.GetRequiredService<JobService>())
            .AddScoped<IIncidentStateService, IncidentStateService>()
            .AddScoped<ILessonLearningService, LessonLearningService>()
            .AddScoped<IJobRetentionService, JobRetentionService>()
            .AddScoped<IJobPronunciationService, JobPronunciationService>()
            .AddScoped<ISchedulePollerService, SchedulePollerService>()
            .AddScoped<IAdHocStationConfigResolver, AdHocStationConfigResolver>()
            .AddScoped<IBulletinFallbackService, BulletinFallbackService>()
            .AddScoped<PerVoiceFillerCartResolver>()
            .AddScoped<FallbackAudioPadder>()
            .AddScoped<Metrics.ValidityMonitorService>()
            .AddScoped<IScheduleService, ScheduleService>()
            .AddScoped<IModelSelector, ModelSelector>()
            .AddScoped<IVoiceProfileService, VoiceProfileService>()
            .AddScoped<IDeliveryStyleService, DeliveryStyleService>()
            .AddScoped<IBulletinPlaybookService, BulletinPlaybookService>()
            .AddScoped<IBookendService, BookendService>()
            .AddScoped<IPronunciationDictionaryService, PronunciationDictionaryService>()
            .AddScoped<MarketAccessService>()
            .AddScoped<IMarketAccessService>(
                provider => provider.GetRequiredService<MarketAccessService>())
            .AddScoped<IMarketAccessCacheInvalidator>(
                provider => provider.GetRequiredService<MarketAccessService>())
            .AddScoped<Ledger.IEventLedgerService, Ledger.EventLedgerService>()
            .AddScoped<ConfigImportSnapshotReader>()
            .AddScoped<ConfigImportService>();
    }
}
