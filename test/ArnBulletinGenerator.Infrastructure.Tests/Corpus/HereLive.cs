using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Builds a service provider whose HERE typed clients use the live API while the eval harness still
///     drives the normal production script-generation path.
/// </summary>
internal static class HereLive
{
    public static ServiceProvider BuildProvider()
    {
        var configuration = TestConfigurationHelper.BuildConfiguration();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System); // Normally provided by the Functions host.

        services.AddHttpClient<HereTrafficProvider>();
        services.AddHttpClient<HereGeocodingProvider>();
        services.AddHttpClient<HereRoutingProvider>();

        return services.BuildServiceProvider();
    }
}
