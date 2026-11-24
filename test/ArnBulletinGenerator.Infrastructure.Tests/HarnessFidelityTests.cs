using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ArnBulletinGenerator.Infrastructure;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Guards the properties every live-agent harness must have: it runs the deployed pipeline and
///     binds every surviving Foundry role.
/// </summary>
public sealed class HarnessFidelityTests
{
    /// <summary>Harness directories whose tests drive the real orchestrator against live agents.</summary>
    private static readonly string[] LiveHarnessDirectories = ["Replay", "Eval"];

    /// <summary>
    ///     Foundry settings that identify the live pipeline's endpoint and role agents. Hardcoding one of
    ///     these in a harness creates a second source of truth.
    /// </summary>
    private static readonly string[] PipelineSelectingKeys =
    [
        "AiSettings:AzureAI:FoundryProjectEndpoint",
        "AiSettings:AzureAI:DraftAgentName",
        "AiSettings:AzureAI:ReviewAgentName",
        "AiSettings:AzureAI:RefineSentenceAgentName",
        "AiSettings:AzureAI:SafetyAgentName",
        "AiSettings:AzureAI:DirectorAgentName",
        "AiSettings:AzureAI:DeliveryWriterAgentName"
    ];

    private static string TestProjectRoot() =>
        Path.Combine(CorpusPaths.RepoRoot(), "test", "ArnBulletinGenerator.Infrastructure.Tests");

    [Fact]
    public void Live_agent_harnesses_run_the_deployed_pipeline()
    {
        string root = TestProjectRoot();
        var offenders = new List<string>();

        foreach (string directory in LiveHarnessDirectories)
        {
            string path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
                continue;

            foreach (string file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);

                // Only harnesses that stand up real infrastructure make a claim about production.
                if (!source.Contains("AddInfrastructure", StringComparison.Ordinal))
                    continue;

                if (!source.Contains("DeployedPipeline", StringComparison.Ordinal))
                    offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        offenders.ShouldBeEmpty(
            "A harness that builds real infrastructure claims to show what production does, so it must "
            + "build its config from TestConfigurationHelper.DeployedPipeline(). Without it the harness "
            + "silently runs a different configuration and reports results that do not describe production.");
    }

    [Fact]
    public void Harness_resolves_conversational_generator_and_surviving_foundry_roles()
    {
        var configuration = TestConfigurationHelper.BuildConfiguration(TestConfigurationHelper.DeployedPipeline());
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        provider.GetRequiredService<IScriptGenerator>()
            .ShouldBeOfType<ArnBulletinGenerator.Infrastructure.AiProviders.Conversational.ConversationalScriptGenerator>();

        var options = provider.GetRequiredService<IOptions<AzureAIOptions>>().Value;
        foreach (string? value in new string?[]
        {
            options.DraftAgentName,
            options.ReviewAgentName,
            options.RefineSentenceAgentName,
            options.SafetyAgentName,
            options.DirectorAgentName
        })
            value.ShouldNotBeNullOrWhiteSpace();
    }


    [Fact]
    public void Snapshot_never_carries_secrets_or_playout_delivery()
    {
        var snapshot = TestConfigurationHelper.DeployedSnapshot();

        // Zetta is the live station share. A local harness that could be one config slip away from
        // writing to it should not hold the transport, the account or the market codes at all.
        var playout = snapshot.Keys.Where(k => k.StartsWith("Zetta__", StringComparison.Ordinal)).ToList();
        playout.ShouldBeEmpty("Playout delivery config must never enter a local harness snapshot.");

        string[] markers = ["apikey", "secret", "connectionstring", "password", "token"];
        var secrets = snapshot.Keys
            .Where(k => markers.Any(m => k.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        secrets.ShouldBeEmpty("Secrets are not committed; credentials come from local settings or env vars.");
    }

    [Fact]
    public void Pipeline_selecting_config_has_exactly_one_definition()
    {
        string root = TestProjectRoot();
        string helper = Path.Combine(root, "TestConfigurationHelper.cs");
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // The single source of truth, and this guard's own list of keys, are the exceptions.
            if (string.Equals(file, helper, StringComparison.Ordinal))
                continue;
            if (Path.GetFileName(file) == "HarnessFidelityTests.cs")
                continue;

            string source = File.ReadAllText(file);
            foreach (string key in PipelineSelectingKeys)
            {
                // A hardcoded literal is a second ANSWER and must not exist. Assignment from a
                // variable is an opt-in experiment knob (ARN_REPLAY_DRAFT_AGENT and friends), which
                // is how a candidate agent gets A/B'd against the deployed default — keep those.
                if (Regex.IsMatch(source, Regex.Escape(key) + @"""\]\s*=\s*"""))
                    offenders.Add($"{Path.GetRelativePath(root, file)} hardcodes {key}");
            }
        }

        offenders.ShouldBeEmpty(
            "These keys decide which generation path and which prompts run, so a second copy is a second "
            + "answer to 'what does production do'. Add or change them in "
            + "TestConfigurationHelper.DeployedPipeline() and reference that instead.");
    }
}
