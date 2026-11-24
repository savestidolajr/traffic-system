using System.Globalization;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Hosting;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Trace;
using Azure.Identity;
using Xunit;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Workflow.LiveTests;

/// <summary>
///     xUnit opt-in gate for experiments that spend real Azure OpenAI calls. The environment check
///     lives in the attribute so the test runner reports the default state as skipped rather than
///     silently executing a live integration test during ordinary CI.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ARN_LIVE_WORKFLOW"), "1",
                StringComparison.Ordinal))
        {
            Skip = "Live workflow run is opt-in; set ARN_LIVE_WORKFLOW=1 to call Azure OpenAI.";
        }
    }
}

/// <summary>
///     Runs the clean-room workflow over one rich captured bulletin per configured market. This is
///     an observation harness: every attempt gets a trace, including workflow failures, and the only
///     assertion is that at least one market produced a script worth reviewing.
/// </summary>
public sealed class LiveBulletinRunTests(ITestOutputHelper output)
{
    private const string DefaultEndpoint = "https://ibp-aue-dev-fdy-app01.cognitiveservices.azure.com";
    private const string DefaultModel = "gpt-5.6-luna";
    private const string DefaultMarkets = "sydney,brisbane,adelaide,goldcoast,melbourne";

    [LiveFact]
    public async Task Produces_a_bulletin_for_each_market_and_writes_a_trace()
    {
        string endpoint = Environment.GetEnvironmentVariable("ARN_WORKFLOW_ENDPOINT") is { Length: > 0 } configuredEndpoint
            ? configuredEndpoint
            : DefaultEndpoint;
        string model = Environment.GetEnvironmentVariable("ARN_WORKFLOW_MODEL") is { Length: > 0 } configuredModel
            ? configuredModel
            : DefaultModel;
        string marketsValue = Environment.GetEnvironmentVariable("ARN_WORKFLOW_MARKETS") is { Length: > 0 } configuredMarkets
            ? configuredMarkets
            : DefaultMarkets;
        var markets = marketsValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<DevBulletin> fixtures = DevBulletinFixture.LoadRichest(perMarket: 1, minCorridors: 4)
            .Where(fixture => markets.Contains(fixture.Market))
            .ToList();
        string outputDirectory = Path.Combine(FindRepositoryRoot(), "workflow-traces");
        var traces = new List<WorkflowTrace>(fixtures.Count);
        bool producedScript = false;

        // Project references copy these assets into the test output. The fallback keeps the run
        // usable from a custom runner that loads the Workflow assembly from its own output folder.
        if (!Directory.Exists(Path.Combine(PromptLibrary.Root, "Prompts")))
        {
            string workflowDirectory = Path.GetDirectoryName(typeof(StagedBulletinWorkflow).Assembly.Location)
                                       ?? AppContext.BaseDirectory;
            PromptLibrary.Root = workflowDirectory;
        }

        foreach (DevBulletin fixture in fixtures)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var llm = new AzureOpenAiWorkflowLlm(
                    http,
                    new DefaultAzureCredential(),
                    new AzureOpenAiWorkflowLlmOptions { Endpoint = endpoint, Model = model });
                var duration = new WordRateDurationOracle();
                // Which architecture to exercise. The seam exists precisely so two approaches can
                // be run against identical captured data and compared on the same traces.
                IBulletinWorkflow workflow =
                    (Environment.GetEnvironmentVariable("ARN_WORKFLOW") ?? "staged-v1").Trim() switch
                    {
                        "conversational-v1" => new ConversationalBulletinWorkflow(llm, duration),
                        _ => new StagedBulletinWorkflow(llm, duration)
                    };
                WorkflowOutcome outcome = await workflow.RunAsync(fixture.Request);

                WorkflowTrace trace = outcome.Trace;
                var baselineData = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["baselineScript"] = fixture.AiredScript,
                    ["baselineWords"] = WordCounter.Count(fixture.AiredScript)
                        .ToString(CultureInfo.InvariantCulture)
                };
                if (fixture.AiredMeasuredSeconds is double measuredSeconds)
                {
                    baselineData["baselineMeasuredSeconds"] = measuredSeconds.ToString(
                        "0.###", CultureInfo.InvariantCulture);
                }

                var baselineEvent = new TraceEvent(
                    trace.Events.Count + 1,
                    trace.Iterations,
                    "Baseline",
                    TraceEventKind.Artifact,
                    DateTimeOffset.UtcNow,
                    "Script the deployed pipeline aired for this same data",
                    Data: baselineData);
                trace = trace with { Events = [.. trace.Events, baselineEvent] };
                string tracePath = TraceHtmlRenderer.Write(trace, outputDirectory);
                traces.Add(trace);

                int newWords = WordCounter.Count(outcome.Script);
                int airedWords = WordCounter.Count(fixture.AiredScript);
                double predictedSeconds = duration.PredictSeconds(outcome.Script);
                producedScript |= !string.IsNullOrWhiteSpace(outcome.Script);

                output.WriteLine($"MARKET {fixture.Market} | station={fixture.Station} | target={fixture.TargetSeconds:0.##}s");
                output.WriteLine($"LLM calls={trace.LlmCallCount} | iterations={trace.Iterations} | outcome={trace.Outcome ?? outcome.Error ?? "unknown"}");
                output.WriteLine($"predicted={predictedSeconds:0.###}s | new words={newWords} | aired words={airedWords}");
                output.WriteLine("NEW SCRIPT:");
                output.WriteLine(outcome.Script);
                output.WriteLine("AIRED SCRIPT:");
                output.WriteLine(fixture.AiredScript);
                output.WriteLine($"TRACE: {tracePath}");
            }
            catch (Exception exception)
            {
                output.WriteLine($"MARKET {fixture.Market} | station={fixture.Station} | EXCEPTION");
                output.WriteLine(exception.ToString());
            }
        }

        string indexPath = TraceHtmlRenderer.WriteIndex(traces, outputDirectory);
        output.WriteLine($"TRACE INDEX: {indexPath}");
        producedScript.ShouldBeTrue("At least one configured market must produce a non-empty script.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArnBulletinGenerator.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find ArnBulletinGenerator.slnx above {AppContext.BaseDirectory}.");
    }
}
