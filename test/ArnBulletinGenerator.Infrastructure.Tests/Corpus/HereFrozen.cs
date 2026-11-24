using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Builders for the frozen-fetch eval: one live HERE pass is recorded across ALL five typed
///     clients (traffic, geocoding, routing, weather, fuel — <see cref="HereLive" />/<see cref="HereReplay" />
///     cover only the first three), then every subsequent prompt-iteration run replays that identical
///     snapshot. This isolates prompt changes from live-data drift: the LLM stays live (it is the
///     system under test); the DATA is frozen.
/// </summary>
internal static class HereFrozen
{
    /// <summary>Live HERE with a recording handler beneath every typed client.</summary>
    public static ServiceProvider BuildRecordingProvider(HereCorpusWriter writer)
    {
        var services = BaseServices();
        services.AddSingleton(writer);

        services.AddHttpClient<HereTrafficProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));
        services.AddHttpClient<HereGeocodingProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));
        services.AddHttpClient<HereRoutingProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));
        services.AddHttpClient<IWeatherProvider, HereWeatherProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));
        services.AddHttpClient<IFuelPriceProvider, HereFuelPriceProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));

        return services.BuildServiceProvider();
    }

    /// <summary>Recorded corpus behind every typed client; no network. Misses surface as 404s.</summary>
    public static ServiceProvider BuildReplayProvider(HereCorpusReplay replay)
    {
        var services = BaseServices();
        services.AddSingleton(replay);

        services.AddHttpClient<HereTrafficProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));
        services.AddHttpClient<HereGeocodingProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));
        services.AddHttpClient<HereRoutingProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));
        services.AddHttpClient<IWeatherProvider, HereWeatherProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));
        services.AddHttpClient<IFuelPriceProvider, HereFuelPriceProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));

        return services.BuildServiceProvider();
    }

    private static ServiceCollection BaseServices()
    {
        var configuration = TestConfigurationHelper.BuildConfiguration();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System); // Normally provided by the Functions host.
        return services;
    }

    /// <summary>
    ///     Frozen-eval corpus root — separate from the canonical replay corpus.
    ///     <c>ARN_FROZEN_EVAL_CORPUS</c> overrides it so a fresh-live validation pass can record a
    ///     NEW snapshot without touching (or being shadowed by) the iteration baseline.
    /// </summary>
    public static string CorpusRoot() =>
        Environment.GetEnvironmentVariable("ARN_FROZEN_EVAL_CORPUS") is { Length: > 0 } overrideRoot
            ? overrideRoot
            : Path.Combine(CorpusPaths.RepoRoot(), "test", "corpus", "frozen-eval");
}
