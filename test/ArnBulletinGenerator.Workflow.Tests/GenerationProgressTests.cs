using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     Verifies the workflow observer's operator-facing projection. These tests deliberately use the
///     scripted model so stage order and word-loop boundaries are deterministic and provider-free.
/// </summary>
public sealed class GenerationProgressTests
{
    private const string CleanBriefs = """
    {"stories":[
      {"storyId":"south-road-closure","subject":"South Road southbound closure",
       "eventIds":["corridor:South Road#1"],
       "facts":["detail: road closure southbound","location: At Sir Donald Bradman Drive","suburb: Torrensville","delayMinutes: 11"],
       "priority":95,"whyItMatters":"a full closure on the main southbound arterial"},
      {"storyId":"weather","subject":"Weather",
       "eventIds":["weather"],
       "facts":["condition: Passing clouds","temperature: 7 degrees"],
       "priority":10,"whyItMatters":"the station always carries weather"}
    ]}
    """;

    private static string ReadOf(int words)
    {
        const string fixture = "Passing clouds and 7 degrees.";
        const string prefix = "South Road is slow";
        int prefixWords = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int fixtureWords = fixture.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int filler = Math.Max(0, words - prefixWords - fixtureWords);
        return filler > 0
            ? $"{prefix} {string.Join(" ", Enumerable.Repeat("word", filler))}. {fixture}"
            : $"{prefix}. {fixture}";
    }

    private static string OverlongRead(int words)
    {
        const string body = "South Road is closed southbound at Torrensville with delays.";
        const string fixture = "Passing clouds and 7 degrees.";
        int fixedWords = body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
            + fixture.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int filler = Math.Max(0, words - fixedWords);
        return filler > 0
            ? $"{body} {string.Join(" ", Enumerable.Repeat("word", filler))}. {fixture}"
            : $"{body} {fixture}";
    }

    private static ConversationalBulletinWorkflow Workflow(
        ScriptedWorkflowLlm llm,
        int candidates = 1,
        int maxWordLoopRounds = 2) =>
        new(llm, new OneWordPerSecondOracle(), new ConversationalWorkflowOptions
        {
            MaxRevisions = 0,
            ComposeCandidateCount = candidates,
            MaxWordLoopRounds = maxWordLoopRounds
        });

    [Fact]
    public async Task Happy_path_emits_write_compose_edit_and_gates_in_order()
    {
        var progress = new RecordingProgressReporter();
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(30))
            .Reply("compose2", ReadOf(30))
            .Reply("compose2", ReadOf(30))
            .Reply("edit", "{\"feedback\":[],\"approved\":true}");

        var request = EvidenceFixtures.Adelaide30S() with { ProgressReporter = progress };
        var result = await Workflow(llm, candidates: 3).RunAsync(request);

        result.IsSuccess.ShouldBeTrue(result.Error);
        progress.Events
            .Where(e => e.Kind is ProgressEventKind.StageStarted or ProgressEventKind.StageCompleted)
            .Select(e => e.Stage)
            .Distinct()
            .ShouldBe([ProgressStage.Write, ProgressStage.Compose, ProgressStage.Edit, ProgressStage.Gates]);
        progress.Events.Count(e => e is { Kind: ProgressEventKind.SubStep, Stage: ProgressStage.Compose })
            .ShouldBe(3);
        progress.Events.Any(e =>
            e is { Kind: ProgressEventKind.SubStep, Stage: ProgressStage.Edit }
            && e.Detail == "approved").ShouldBeTrue();
        progress.Events
            .Where(e => e.Stage == ProgressStage.Gates)
            .Select(e => e.Kind)
            .ShouldContain(ProgressEventKind.SubStep);
        progress.Events.Any(e => e.Stage == ProgressStage.FitWords).ShouldBeFalse();
    }

    [Fact]
    public async Task Overlong_chosen_read_emits_fit_words_round_substeps()
    {
        var progress = new RecordingProgressReporter();
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", OverlongRead(45))
            .Reply("compose2", ReadOf(30))
            .Reply("edit", "{\"feedback\":[],\"approved\":true}");

        var request = EvidenceFixtures.Adelaide30S() with { ProgressReporter = progress };
        var result = await Workflow(llm).RunAsync(request);

        result.IsSuccess.ShouldBeTrue(result.Error);
        progress.Events
            .Where(e => e.Stage == ProgressStage.FitWords)
            .Any(e => e.Kind == ProgressEventKind.StageStarted).ShouldBeTrue();
        progress.Events
            .Where(e => e.Kind == ProgressEventKind.SubStep && e.Stage == ProgressStage.FitWords)
            .Any(e =>
                e.Counts is not null
                && e.Counts.TryGetValue("round", out double round) && round == 1
                && e.Counts.TryGetValue("words", out double words) && words == 30).ShouldBeTrue();
        progress.Events
            .Where(e => e.Stage == ProgressStage.FitWords)
            .Any(e => e.Kind == ProgressEventKind.StageCompleted).ShouldBeTrue();
    }

    private sealed class RecordingProgressReporter : IGenerationProgressReporter
    {
        private readonly Lock _gate = new();

        public List<ProgressRecord> Events { get; } = [];

        private void Record(ProgressRecord record)
        {
            lock (_gate)
                Events.Add(record);
        }

        public ValueTask StatusChanged(JobStatus status) => ValueTask.CompletedTask;

        public ValueTask StageStarted(
            ProgressStage stage,
            string detail,
            IReadOnlyDictionary<string, double>? counts = null)
        {
            Record(new ProgressRecord(ProgressEventKind.StageStarted, stage, detail, counts));
            return ValueTask.CompletedTask;
        }

        public ValueTask StageCompleted(
            ProgressStage stage,
            string detail,
            IReadOnlyDictionary<string, double>? counts = null,
            int? durationMs = null)
        {
            Record(new ProgressRecord(ProgressEventKind.StageCompleted, stage, detail, counts));
            return ValueTask.CompletedTask;
        }

        public ValueTask SubStep(
            ProgressStage stage,
            string detail,
            IReadOnlyDictionary<string, double>? counts = null)
        {
            Record(new ProgressRecord(ProgressEventKind.SubStep, stage, detail, counts));
            return ValueTask.CompletedTask;
        }

        public ValueTask AttemptStarted(int attempt, int maxAttempts, string detail) => ValueTask.CompletedTask;
        public ValueTask AttemptFailed(string detail) => ValueTask.CompletedTask;
        public ValueTask LlmCall(ProgressStage stage, string route, string model, int durationMs) => ValueTask.CompletedTask;
        public ValueTask Ready(string detail) => ValueTask.CompletedTask;
        public ValueTask Failed(string detail) => ValueTask.CompletedTask;
    }

    private sealed record ProgressRecord(
        ProgressEventKind Kind,
        ProgressStage Stage,
        string Detail,
        IReadOnlyDictionary<string, double>? Counts);
}
