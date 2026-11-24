using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Centralised test configuration helper.
///     Loads appsettings.json -> appsettings.Development.json -> environment variables -> optional overrides.
///     All IOptions are wired via <see cref="DependencyInjection.AddInfrastructure" />.
/// </summary>
public static class TestConfigurationHelper
{
    /// <summary>
    ///     The config a harness needs to exercise THE PIPELINE THAT ACTUALLY AIRS.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The deployed harness configuration is loaded from the committed snapshot and wires the same
    ///         structured-matrix generator plus Foundry role agents used by the live environments.
    ///         The snapshot is deliberately kept separate from ordinary test defaults so live replay cannot
    ///         silently drift onto a different pipeline.
    ///     </para>
    ///     <para>
    ///         Hand-listing the settings fixed those four harnesses and would have rotted the next time
    ///         somebody changed an app setting, so the values are not written here at all: they are
    ///         snapshotted from the live Function App into <c>test/corpus/deployed-pipeline.json</c> by
    ///         <c>scripts/refresh-deployed-pipeline-snapshot.py</c>, and that script's <c>--check</c> mode
    ///         reports drift. Secrets, <c>Zetta__*</c> playout and host wiring are excluded by the script —
    ///         a local harness must never be one config slip from writing to a live station share.
    ///     </para>
    /// </remarks>
    public static Dictionary<string, string?> DeployedPipeline(
        Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>();

        // Azure app settings use "__" as the section separator; .NET configuration uses ":".
        foreach ((string key, string value) in DeployedSnapshot())
            settings[key.Replace("__", ":", StringComparison.Ordinal)] = value;

        if (overrides is { Count: > 0 })
            foreach ((string key, string? value) in overrides)
                settings[key] = value;

        return settings;
    }

    /// <summary>
    ///     The committed snapshot of the deployed generation configuration, keyed as Azure names it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the snapshot is absent or empty. Failing loudly is deliberate: silently falling
    ///     back to test defaults is precisely the failure this whole mechanism exists to prevent, and it
    ///     would look like a passing harness reporting the wrong pipeline's numbers.
    /// </exception>
    public static IReadOnlyDictionary<string, string> DeployedSnapshot()
    {
        string path = Path.Combine(ResolveRepositoryRoot(), "test", "corpus", "deployed-pipeline.json");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Deployed pipeline snapshot not found at '{path}'. Run "
                + "scripts/refresh-deployed-pipeline-snapshot.py to regenerate it.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("settings", out var section))
            throw new InvalidOperationException($"Snapshot '{path}' has no 'settings' object.");

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in section.EnumerateObject())
            snapshot[setting.Name] = setting.Value.GetString() ?? string.Empty;

        if (snapshot.Count == 0)
            throw new InvalidOperationException($"Snapshot '{path}' is empty.");

        return snapshot;
    }

    /// <summary>
    ///     Builds an <see cref="IConfiguration" /> from appsettings files,
    ///     environment variable substitution, and optional per-test overrides.
    /// </summary>
    public static IConfiguration BuildConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var builder = new ConfigurationBuilder();

        // Non-sensitive defaults only — secrets come from appsettings.Development.json or env vars.
        string configRoot = ResolveRepositoryRoot();
        if (!string.IsNullOrEmpty(configRoot))
        {
            builder.SetBasePath(configRoot);
            builder.AddJsonFile("appsettings.json", true, false);
            builder.AddJsonFile("appsettings.Development.json", true, false);

            builder.AddJsonFile(Path.Combine("test", "ArnBulletinGenerator.Infrastructure.Tests", "appsettings.json"),
                true, false);
            builder.AddJsonFile(
                Path.Combine("test", "ArnBulletinGenerator.Infrastructure.Tests", "appsettings.Development.json"), true,
                false);

            builder.AddJsonFile(Path.Combine("src", "ArnBulletinGenerator.Functions", "appsettings.json"), true, false);
            builder.AddJsonFile(Path.Combine("src", "ArnBulletinGenerator.Functions", "appsettings.Development.json"),
                true, false);
        }

        // Non-sensitive defaults (added after JSON files so they take precedence, but before env vars)
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiSettings:AzureAI:Model"] = "gpt-5-mini",
            ["AiSettings:AzureAI:Endpoint"] = "https://test.openai.azure.com",
            ["AiSettings:AzureAI:ApiKey"] = "test-key",
            ["AiSettings:ElevenLabs:ApiKey"] = "test-key",
            ["Here:ApiKey"] = "test-key",
            ["Storage:AccountName"] = "test",
            ["Storage:ContainerName"] = "broadcasts",
            // Fail fast against the dummy "test" account — no 4-retry stall when a repo is hit.
            ["Storage:MaxRetries"] = "0",
            ["Providers:OutputProvider"] = "Console",
            // Enable the supplementary providers so the eval can exercise weather/fuel fill (live HERE).
            ["Weather:Enabled"] = "true",
            ["Fuel:Enabled"] = "true",
            ["Prompts:SystemPrompt"] =
                "You are a test traffic presenter. {{traffic}} Target: {{targetSeconds}}s, {{targetWords}} words. City: {{city}}. Output script text only.",
            ["Prompts:ReviewPrompt"] =
                "Review script for {{targetSeconds}}s target. Current: {{estimatedSeconds}}s. Script: {{script}}. Output corrected script only.",
            ["Prompts:RefinePrompt"] = "DEPRECATED",
            ["MonitoredRoutes:Markets:sydney:0:Name"] = "M4 Parramatta to Sydney CBD",
            ["MonitoredRoutes:Markets:sydney:0:Origin:Lat"] = "-33.8179",
            ["MonitoredRoutes:Markets:sydney:0:Origin:Lng"] = "150.9999",
            ["MonitoredRoutes:Markets:sydney:0:Destination:Lat"] = "-33.8688",
            ["MonitoredRoutes:Markets:sydney:0:Destination:Lng"] = "151.2093",
            ["MonitoredRoutes:Markets:melbourne:0:Name"] = "Monash Freeway to Melbourne CBD",
            ["MonitoredRoutes:Markets:melbourne:0:Origin:Lat"] = "-37.9105",
            ["MonitoredRoutes:Markets:melbourne:0:Origin:Lng"] = "145.1648",
            ["MonitoredRoutes:Markets:melbourne:0:Destination:Lat"] = "-37.8136",
            ["MonitoredRoutes:Markets:melbourne:0:Destination:Lng"] = "144.9631",
            ["MonitoredRoutes:Markets:goldcoast:0:Name"] = "Pacific Motorway to Surfers Paradise",
            ["MonitoredRoutes:Markets:goldcoast:0:Origin:Lat"] = "-28.0836",
            ["MonitoredRoutes:Markets:goldcoast:0:Origin:Lng"] = "153.4072",
            ["MonitoredRoutes:Markets:goldcoast:0:Destination:Lat"] = "-27.9659",
            ["MonitoredRoutes:Markets:goldcoast:0:Destination:Lng"] = "153.4122"
        });

        // Environment variables override file-based values.
        builder.AddEnvironmentVariables();

        // Map well-known environment variables to hierarchical configuration paths.
        ApplyEnvironmentVariableMappings(builder);

        if (overrides is { Count: > 0 })
            builder.AddInMemoryCollection(overrides);

        return builder.Build();
    }

    /// <summary>
    ///     Builds a fully-wired <see cref="ServiceProvider" /> configured for integration tests
    ///     with mock providers and in-memory repositories by default.
    /// </summary>
    public static ServiceProvider BuildTestServiceProvider(
        Dictionary<string, string?>? configOverrides = null,
        LogLevel minimumLogLevel = LogLevel.Debug)
    {
        var configuration = BuildConfiguration(configOverrides);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(minimumLogLevel));
        services.AddInfrastructure(configuration);

        // In-memory repository overrides (no external storage dependency)
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();

        // Mock overrides for real AI/API services — these replace the real implementations
        // registered by AddInfrastructure, so no credentials are needed.
        var scriptGen = Substitute.For<IScriptGenerator>();
        scriptGen.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DraftedScript>.Success(new DraftedScript(
                "Traffic update. M1 inbound is heavy from Huntingdale Road to Warrigal Road. Allow extra time. West Gate Freeway outbound slow from Montague Street to Bolte Bridge. CityLink outbound heavy at Brunswick. Tullamarine Freeway inbound backed up from Bell Street to the airport. Mornington Peninsula Freeway northbound slow at Dromana. EastLink inbound heavy at Ringwood. Overall traffic is building across the network with multiple delays reported. Allow extra time for your commute today.",
                []))));
        scriptGen.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<DraftedScript>.Success(new DraftedScript(
                "Traffic update. M1 inbound is heavy. Allow extra time.", []))));
        services.AddSingleton<IScriptGenerator>(scriptGen);

        var reviewer = Substitute.For<IScriptReviewer>();
        reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));
        services.AddSingleton<IScriptReviewer>(reviewer);

        var refiner = Substitute.For<IScriptRefiner>();
        refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(call.Arg<DraftedSentence>()));
        services.AddSingleton<IScriptRefiner>(refiner);

        var audioPipeline = Substitute.For<IAudioPipelineService>();
        audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>())
            .Returns(Result<AudioSynthesisResult>.Success(new AudioSynthesisResult([], "Script text", 30.0)));
        services.AddSingleton<IAudioPipelineService>(audioPipeline);

        var durationEstimator = Substitute.For<IDurationEstimator>();
        durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        services.AddSingleton<IDurationEstimator>(durationEstimator);

        var calibration = Substitute.For<IVoiceCalibrationService>();
        calibration.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new CalibrationMetrics(150, 5.8, 2.3));
        services.AddSingleton<IVoiceCalibrationService>(calibration);

        var notifier = Substitute.For<INotificationService>();
        notifier.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton<INotificationService>(notifier);

        var trafficProvider = Substitute.For<ITrafficProvider>();
        trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(new List<TrafficCorridor>
            {
                new("M1", 1, 3, 1000, 50, "crash",
                    "Multi-vehicle crash at Springvale Road",
                    [new TrafficIncident("i1", "Multi-vehicle crash blocking two lanes", 1, "M1 at Springvale", "crash")])
            }));
        services.AddSingleton<ITrafficProvider>(trafficProvider);

        services.AddApplication();
        services.Configure<IncidentStateOptions>(
            configuration.GetSection(IncidentStateOptions.SectionName).Bind);

        return services.BuildServiceProvider();
    }

    public static ServiceProvider BuildRealServiceProvider(
        Dictionary<string, string?>? configOverrides = null,
        LogLevel minimumLogLevel = LogLevel.Debug)
    {
        var realProviderDefaults = new Dictionary<string, string?>
        {
            ["Providers:OutputProvider"] = "Console"
        };

        if (configOverrides is { Count: > 0 })
            foreach (var kvp in configOverrides)
                realProviderDefaults[kvp.Key] = kvp.Value;

        var configuration = BuildConfiguration(realProviderDefaults);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(minimumLogLevel));
        services.AddInfrastructure(configuration);

        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();

        services.AddApplication();

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Returns true when all required credentials for real-service integration tests are available.
    /// </summary>
    public static bool HasRealServiceCredentials()
    {
        var config = BuildConfiguration();
        string[] requiredPaths = new[]
        {
            "AiSettings:AzureAI:Endpoint",
            "AiSettings:AzureAI:Model",
            "Here:ApiKey",

            "AiSettings:ElevenLabs:ApiKey"
        };
        return requiredPaths.All(p => !string.IsNullOrWhiteSpace(config[p]));
    }

    /// <summary>
    ///     Maps conventional environment variable names to their configuration paths.
    ///     This allows CI/CD pipelines and local .env files to inject real credentials
    ///     without requiring appsettings.Development.json.
    /// </summary>
    private static void ApplyEnvironmentVariableMappings(IConfigurationBuilder builder)
    {
        var mappings = new Dictionary<string, string>
        {
            ["HERE_API_KEY"] = "Here:ApiKey",

            ["AZURE_OPENAI_ENDPOINT"] = "AiSettings:AzureAI:Endpoint",
            ["AZURE_OPENAI_API_KEY"] = "AiSettings:AzureAI:ApiKey",
            ["AZURE_OPENAI_MODEL"] = "AiSettings:AzureAI:Model",
            ["ELEVENLABS_API_KEY"] = "AiSettings:ElevenLabs:ApiKey",
            ["ELEVENLABS_VOICE_ID"] = "AiSettings:ElevenLabs:VoiceId"
        };

        var envOverrides = new Dictionary<string, string?>();

        foreach ((string envVar, string configPath) in mappings)
        {
            string? value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
                envOverrides[configPath] = value;
        }

        if (envOverrides.Count > 0)
            builder.AddInMemoryCollection(envOverrides);
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArnBulletinGenerator.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? string.Empty;
    }
}
