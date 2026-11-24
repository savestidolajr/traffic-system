using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Infrastructure;
using ArnBulletinGenerator.Workflow.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_RegistersWithoutError()
    {
        var services = new ServiceCollection();
        var configuration = BuildMinimalConfiguration();

        Should.NotThrow(() =>
        {
            services.AddInfrastructure(configuration);
            services.AddApplication();
        });
    }

    [Fact]
    public void AddInfrastructure_WithAddApplication_ServiceProviderBuildsWithoutError()
    {
        var services = new ServiceCollection();
        var configuration = BuildMinimalConfiguration();

        services.AddInfrastructure(configuration);
        services.AddApplication();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        provider.ShouldNotBeNull();
    }

    // Conversational v1 is the only script pipeline. Configuration cannot select a different writer.
    [Theory]
    [InlineData(null)]
    [InlineData("conversational-v1")]
    [InlineData("structured-matrix")]
    [InlineData("does-not-exist")]
    public void Script_generator_always_resolves_to_conversational_v1(string? pipeline)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildMinimalConfiguration(
            new Dictionary<string, string?> { ["AiSettings:AzureAI:Pipeline"] = pipeline }));
        services.AddApplication();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<global::ArnBulletinGenerator.Domain.Interfaces.IScriptGenerator>()
            .ShouldBeOfType<global::ArnBulletinGenerator.Infrastructure.AiProviders.Conversational.ConversationalScriptGenerator>();
    }

    // The capture sink and the shape coefficients are OPTIONAL parameters on ScriptPipelineRoles so a
    // hand-built orchestrator in a test needs no telemetry wiring. That convenience is also a trap: if
    // either is unregistered, DI silently supplies the default and capture is skipped forever with no
    // error anywhere to find. Assert the composition root actually fills them.
    [Fact]
    public void ScriptPipelineRoles_ResolvesTheDurationCaptureSinkAndShapeCoefficients()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildMinimalConfiguration());
        services.AddApplication();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var roles = provider.GetRequiredService<Application.Broadcasting.ScriptPipelineRoles>();

        roles.DurationSamples.ShouldNotBeNull(
            "without the capture sink every measured read is silently discarded");
        roles.DurationCalibration.ShouldNotBeNull(
            "without the shape coefficients a captured ShapeTotal cannot be computed");
    }

    [Fact]
    public async Task DurationPredictor_HistoryBasedMode_ResolvesWithoutCircularDependency()
    {
        // Regression: HistoryBasedDurationPredictor takes an IDurationPredictor
        // fallback. When IDurationPredictor's factory (mode=HistoryBased) resolved
        // that fallback via ctor injection, resolution re-entered the factory and
        // recursed forever — in Azure every queue/timer invocation hung until
        // functionTimeout (sandbox validation, 11 Jun 2026). The composition root
        // must wire the fallback to the concrete StaticRateDurationPredictor.
        var services = new ServiceCollection();
        var configuration = BuildMinimalConfiguration(new Dictionary<string, string?>
        {
            ["DurationPrediction:Mode"] = "HistoryBased",
        });

        services.AddInfrastructure(configuration);
        services.AddApplication();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolveTask = Task.Run(() =>
            scope.ServiceProvider.GetRequiredService<ArnBulletinGenerator.Domain.Interfaces.IDurationPredictor>());
        var winner = await Task.WhenAny(resolveTask, Task.Delay(TimeSpan.FromSeconds(10)));
        winner.ShouldBe(resolveTask, "IDurationPredictor resolution must not hang (circular fallback wiring)");
        (await resolveTask).ShouldBeOfType<ArnBulletinGenerator.Infrastructure.Calibration.HistoryBasedDurationPredictor>();
    }

    [Fact]
    public void AddInfrastructure_BindsConfiguredAzureOpenAiBulkheadToWorkflow()
    {
        var services = new ServiceCollection();
        var configuration = BuildMinimalConfiguration(new Dictionary<string, string?>
        {
            ["AiSettings:AzureAI:CallBudget:BulkheadPermits"] = "10",
        });

        services.AddInfrastructure(configuration);
        services.AddApplication();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<AzureOpenAiWorkflowLlmOptions>()
            .CallBudget.BulkheadPermits.ShouldBe(10);
    }

    private static IConfiguration BuildMinimalConfiguration(IDictionary<string, string?>? overrides = null)
    {
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Azure AI (all [Required] per AzureAIOptions)
                ["AiSettings:AzureAI:Endpoint"] = "https://test.openai.azure.com",
                ["AiSettings:AzureAI:Model"] = "gpt-5-mini",
                ["AiSettings:AzureAI:ApiKey"] = "test-key",

                // ElevenLabs (all [Required] per ElevenLabsOptions)
                ["AiSettings:ElevenLabs:ApiKey"] = "test-key",
                ["AiSettings:ElevenLabs:ModelId"] = "eleven_v3",
                ["AiSettings:ElevenLabs:BaseUrl"] = "https://api.elevenlabs.io/v1/text-to-speech",
                ["AiSettings:ElevenLabs:OutputFormat"] = "pcm_24000",

                // HERE Maps ([Required] per HereOptions)
                ["Here:ApiKey"] = "test-key",

                // Storage ([Required] per StorageOptions)
                ["Storage:AccountName"] = "test",
                ["Storage:ContainerName"] = "broadcasts",

                // Provider defaults (no [Required], but included for completeness)
                ["Providers:OutputProvider"] = "Console",

                // Broadcast defaults (no [Required])
                ["Broadcast:WordsPerMinute"] = "150",
                ["Broadcast:MaxGenerationRetries"] = "3",

                // Webhook (no [Required], Enabled defaults to false)
                ["Webhook:EndpointUrl"] = "https://test.webhook.com",

                // AzureSearch (no [Required])
                ["AzureSearch:Endpoint"] = "https://test.search.windows.net",
                ["AzureSearch:ApiKey"] = "test-key",

                // Zetta (no [Required])
                ["Zetta:Enabled"] = "false",
                ["Zetta:ServerAddress"] = "10.0.0.1",
                ["Zetta:ShareName"] = "share",
                ["Zetta:Domain"] = "test",
                ["Zetta:Username"] = "test",
                ["Zetta:Password"] = "test",
                ["Zetta:FileNamePattern"] = "{MarketId}_TRAFF",

                // ModelSelection (no [Required])
                ["ModelSelection:BroadcastModel"] = "eleven_v3",
                ["ModelSelection:QualityModel"] = "eleven_turbo_v2_5",
                ["ModelSelection:FallbackModel"] = "eleven_multilingual_v2",

                // SpeechDuration (no [Required])
                ["SpeechDuration:FineTuningThresholdSeconds"] = "0.01",
                ["SpeechDuration:MaxSlowDownFactor"] = "1.10",
                ["SpeechDuration:MaxSilencePadSeconds"] = "0.3",
                ["SpeechDuration:MaxSafeOverrunTrimSeconds"] = "3.0",
                ["SpeechDuration:EffectiveToleranceSeconds"] = "0.15",
                ["SpeechDuration:CandidateToleranceSeconds"] = "2.5",
                ["SpeechDuration:ExactHitThresholdSeconds"] = "0.05",

                // IncidentState (from Domain/Configuration — no [Required])
                ["IncidentState:GenericLocations:0"] = "Unidentified Road",
                ["IncidentState:LengthChangeToleranceMeters"] = "50",
                ["IncidentState:DefaultSource"] = "HERE",

                // CorridorSelection (from Domain/Configuration — no [Required])
                ["CorridorSelection:MaxCorridors"] = "5",
            }!);

        if (overrides is not null)
            builder.AddInMemoryCollection(overrides);

        return builder.Build();
    }
}
