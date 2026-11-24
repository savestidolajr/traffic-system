using System.Globalization;
using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Hosting;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;
using Azure.Identity;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Workflow.LiveTests;

/// <summary>
///     Proves the station's configured running order reaches the AIR path, not just the plan.
///     Measured live 2026-08-27 (job e858b062): a weather-first <c>SegmentOrder</c> reached the
///     stitch and the read still closed with weather, because the composer reads briefs through
///     a priority re-sort that pins fixtures (priority 0) to the tail and no conversational
///     check ordered segments. Same captured bulletin, two configs; the weather line must move.
/// </summary>
public sealed class SegmentOrderLiveTests(ITestOutputHelper output)
{
    private const string DefaultEndpoint = "https://ibp-aue-dev-fdy-app01.cognitiveservices.azure.com";
    private const string DefaultModel = "gpt-5.6-luna";

    [LiveFact]
    public async Task A_weather_first_running_order_moves_weather_to_the_top_of_the_read()
    {
        string endpoint = Environment.GetEnvironmentVariable("ARN_WORKFLOW_ENDPOINT") is { Length: > 0 } e
            ? e
            : DefaultEndpoint;
        string model = Environment.GetEnvironmentVariable("ARN_WORKFLOW_MODEL") is { Length: > 0 } m
            ? m
            : DefaultModel;
        string market = Environment.GetEnvironmentVariable("ARN_WORKFLOW_MARKETS") is { Length: > 0 } mk
            ? mk.Split(',')[0].Trim()
            : "sydney";

        DevBulletin? fixture = DevBulletinFixture.LoadRichest(perMarket: 1, minCorridors: 4)
            .FirstOrDefault(f => string.Equals(f.Market, market, StringComparison.OrdinalIgnoreCase));
        fixture.ShouldNotBeNull($"No captured bulletin with >=4 corridors for market '{market}'.");
        fixture.Request.Evidence.Any(unit => unit.SourceKey == "weather")
            .ShouldBeTrue("the fixture must carry weather evidence for this experiment");

        if (!Directory.Exists(Path.Combine(PromptLibrary.Root, "Prompts")))
        {
            PromptLibrary.Root = Path.GetDirectoryName(typeof(ConversationalBulletinWorkflow).Assembly.Location)
                                 ?? AppContext.BaseDirectory;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var llm = new AzureOpenAiWorkflowLlm(
            http,
            new DefaultAzureCredential(),
            new AzureOpenAiWorkflowLlmOptions { Endpoint = endpoint, Model = model });
        var duration = new WordRateDurationOracle();

        string weatherValue = fixture.Request.Evidence
            .First(unit => unit.SourceKey == "weather" && unit.Field == "temperature").Value;

        var reads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, IReadOnlyList<string>? order) in
                 new (string, IReadOnlyList<string>?)[] { ("default", null), ("weather-first", ["Weather", "Traffic", "Fuel"]) })
        {
            WorkflowRequest request = fixture.Request with
            {
                JobId = $"{fixture.JobId}-segorder-{name}",
                SegmentOrder = order,
                RequiredSections = ["weather"]
            };

            var workflow = new ConversationalBulletinWorkflow(llm, duration);
            WorkflowOutcome outcome = await workflow.RunAsync(request);
            outcome.Script.ShouldNotBeNullOrWhiteSpace($"variant '{name}' produced no read");
            reads[name] = outcome.Script!;

            double position = (double)outcome.Script!.IndexOf(weatherValue, StringComparison.OrdinalIgnoreCase)
                              / outcome.Script.Length;
            output.WriteLine($"=== {name}: weather at {position.ToString("P0", CultureInfo.InvariantCulture)} of the read");
            output.WriteLine(outcome.Script);
            output.WriteLine(string.Empty);
        }

        int defaultIndex = reads["default"].IndexOf(weatherValue, StringComparison.OrdinalIgnoreCase);
        int firstIndex = reads["weather-first"].IndexOf(weatherValue, StringComparison.OrdinalIgnoreCase);
        defaultIndex.ShouldBeGreaterThanOrEqualTo(0, "the default read must voice weather");
        firstIndex.ShouldBeGreaterThanOrEqualTo(0, "the weather-first read must voice weather");

        // Config moved the segment: weather opens the read instead of closing it.
        (firstIndex < reads["weather-first"].Length / 3).ShouldBeTrue(
            $"weather-first should voice weather in the opening third, found at index {firstIndex}");
        (defaultIndex > reads["default"].Length / 2).ShouldBeTrue(
            $"the default order should keep weather in the back half, found at index {defaultIndex}");
    }
}
