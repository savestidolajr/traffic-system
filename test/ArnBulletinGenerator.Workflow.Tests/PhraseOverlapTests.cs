using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     Contracts for same-day continuity context and its deterministic repetition floor. The model
///     remains responsible for choosing fresh wording; this suite only proves that the previous read
///     reaches the composer and that copied idioms cannot silently pass the workflow checks.
/// </summary>
public sealed class PhraseOverlapTests
{
    private static WorkflowRequest Request(string? previousScript = null) =>
        new(
            "job-phrase-overlap",
            "sydney",
            "KIIS 1065",
            "Sydney",
            30,
            [],
            [],
            PreviousScript: previousScript);

    private static TraceRecorder Trace() =>
        new("run-phrase-overlap", "conversational-v1", "test", "job-phrase-overlap", "sydney", "KIIS 1065", 30);

    private static async Task<string> ComposePromptAsync(string? previousScript)
    {
        var llm = new ScriptedWorkflowLlm().Reply("compose2", "A clean short read.");
        var composer = new BulletinComposer(llm);
        await composer.ComposeAsync(
            Request(previousScript),
            new NormalizedInput([], []),
            BulletinBriefs.Empty,
            20,
            Trace());
        return llm.Calls.Single(call => call.Role == "compose2").UserPrompt;
    }

    [Fact]
    public async Task Previous_read_renders_once_before_the_word_count_tail()
    {
        const string previous = "Traffic was chewing up the freeway earlier today.";
        string prompt = await ComposePromptAsync(previous);
        const string heading = "PREVIOUS READ (aired earlier today)";
        int headingAt = prompt.IndexOf(heading, StringComparison.Ordinal);
        int tailAt = prompt.IndexOf("BEFORE YOU REPLY: count the words in your read.", StringComparison.Ordinal);

        prompt.Split(heading, StringSplitOptions.None).Length.ShouldBe(2);
        prompt.Split(previous, StringSplitOptions.None).Length.ShouldBe(2);
        headingAt.ShouldBeGreaterThanOrEqualTo(0);
        tailAt.ShouldBeGreaterThan(headingAt);
        prompt.TrimEnd().ShouldEndWith("rewrite it to fit before answering.", Case.Sensitive);
    }

    [Fact]
    public async Task Empty_previous_read_renders_no_previous_read_block()
    {
        string prompt = await ComposePromptAsync(null);
        prompt.ShouldNotContain("PREVIOUS READ (aired earlier today)");
    }

    [Fact]
    public void Phrase_overlap_normalises_identical_disjoint_and_road_only_reads()
    {
        const string identical = "Traffic is moving well across the city today.";
        PhraseOverlap.Score(identical, identical).ShouldBe(1.0);
        PhraseOverlap.Score("A crash blocks the eastern route.", "Weather is clear across town.").ShouldBe(0.0);

        var input = new NormalizedInput(
            [new BulletinEvent(
                "corridor:m4",
                BulletinEventKind.Corridor,
                "M4",
                [new EventFact("corridor:m4:road", "roadName", "M4")])],
            []);
        double roadOnly = PhraseOverlap.Score(
            "M4 is slow near the bridge.",
            "M4 is busy near the bridge.",
            input);
        roadOnly.ShouldBeLessThan(0.01);
    }

    [Fact]
    public void Copied_idiom_sentences_fail_the_repetition_check()
    {
        const string previous =
            "Traffic is chewing up the freeway this morning. It is a crawl through the city centre.";
        const string read =
            previous + " New details are ahead.";
        double score = PhraseOverlap.Score(read, previous);
        score.ShouldBeGreaterThanOrEqualTo(PhraseOverlap.FailureThreshold);

        IReadOnlyList<CheckResult> checks = ConversationalChecks.Run(
            new NormalizedInput([], []),
            read,
            BulletinBriefs.Empty,
            Trace(),
            previous);
        checks.ShouldContain(result =>
            result.Name == "repeats the previous read" && !result.Passed);
    }

    [Fact]
    public void Without_previous_read_the_repetition_check_is_not_applied()
    {
        IReadOnlyList<CheckResult> checks = ConversationalChecks.Run(
            new NormalizedInput([], []),
            "Traffic is chewing up the freeway this morning.",
            BulletinBriefs.Empty,
            Trace());
        checks.ShouldNotContain(result => result.Name == "repeats the previous read");
    }
}
