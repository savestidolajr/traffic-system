using ArnBulletinGenerator.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.ExternalSmoke.Tests;

public sealed class ExternalSmokeFixture : IDisposable
{
    public bool Enabled => Environment.GetEnvironmentVariable("ARN_EXTERNAL_SMOKE") == "1";

    public ServiceProvider Services { get; }

    public ExternalSmokeFixture()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(DefaultConfiguration())
            .AddEnvironmentVariables()
            .AddInMemoryCollection(EnvironmentMappings())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddInfrastructure(config);
        Services = services.BuildServiceProvider();
    }

    public void Dispose() => Services.Dispose();

    private static Dictionary<string, string?> DefaultConfiguration() => new()
    {
        ["Providers:OutputProvider"] = "Console",
        ["Providers:ExternalProviderMode"] = "Real",
        ["Storage:AccountName"] = "devstoreaccount1",
        ["Storage:ContainerName"] = "broadcasts",
        ["AiSettings:AzureAI:Endpoint"] = "https://example.openai.azure.com",
        ["AiSettings:AzureAI:Model"] = "fake-model",
        ["AiSettings:AzureAI:ApiKey"] = "fake",
        ["AiSettings:AzureAI:UseManagedIdentity"] = "false",
        ["AiSettings:ElevenLabs:ApiKey"] = "fake",
        ["AiSettings:ElevenLabs:BaseUrl"] = "https://api.elevenlabs.io/v1/text-to-speech",
        ["AiSettings:ElevenLabs:ModelId"] = "eleven_v3",
        ["AiSettings:ElevenLabs:OutputFormat"] = "pcm_44100",
        ["AiSettings:ElevenLabs:UseTimestampsEndpoint"] = "false",
        ["Here:ApiKey"] = "fake",
        ["Here:TrafficBaseUrl"] = "https://data.traffic.hereapi.com/v7",
        ["Here:GeocodingBaseUrl"] = "https://geocode.search.hereapi.com/v1",
        ["Here:RoutingBaseUrl"] = "https://router.hereapi.com/v8",
        ["Webhook:Enabled"] = "false",
        ["AzureSearch:Enabled"] = "false",
        ["Weather:Enabled"] = "true",
        ["Fuel:Enabled"] = "true",
        ["DurationPrediction:Mode"] = "StaticRate"
    };

    private static Dictionary<string, string?> EnvironmentMappings()
    {
        var values = new Dictionary<string, string?>();
        Map(values, "AZURE_OPENAI_ENDPOINT", "AiSettings:AzureAI:Endpoint");
        Map(values, "AZURE_OPENAI_API_KEY", "AiSettings:AzureAI:ApiKey");
        Map(values, "AZURE_OPENAI_MODEL", "AiSettings:AzureAI:Model");
        Map(values, "HERE_API_KEY", "Here:ApiKey");
        Map(values, "ELEVENLABS_API_KEY", "AiSettings:ElevenLabs:ApiKey");
        return values;
    }

    private static void Map(Dictionary<string, string?> values, string env, string key)
    {
        var value = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
    }
}

[CollectionDefinition(Name)]
public sealed class ExternalSmokeCollection : ICollectionFixture<ExternalSmokeFixture>
{
    public const string Name = "External smoke";
}
