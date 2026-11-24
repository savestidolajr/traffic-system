using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Builds a service provider whose HERE typed clients are backed by the recorded corpus instead
///     of the live API. Base URLs keep their production defaults so outgoing request URIs match the
///     recorded ones; the replay handler short-circuits before any network call.
/// </summary>
internal static class HereReplay
{
    public static ServiceProvider BuildProvider(HereCorpusReplay replay)
    {
        var configuration = TestConfigurationHelper.BuildConfiguration();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System); // Normally provided by the Functions host.
        services.AddSingleton(replay);

        services.AddHttpClient<HereTrafficProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));
        services.AddHttpClient<HereGeocodingProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));
        services.AddHttpClient<HereRoutingProvider>()
            .AddHttpMessageHandler(sp => new HereReplayHandler(sp.GetRequiredService<HereCorpusReplay>()));

        return services.BuildServiceProvider();
    }
}
