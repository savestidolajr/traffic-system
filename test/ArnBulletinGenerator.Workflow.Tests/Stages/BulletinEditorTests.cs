using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Tests.Fixtures;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>Regression coverage for fail-closed conversational editor verdict handling.</summary>
public sealed class BulletinEditorTests
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

    private const string Approved = """{"feedback":[],"approved":true}""";

    private static string Rejected(string reason) =>
        $$"""{"feedback":["{{reason}}"],"approved":false}""";

    private static string ReadOf(int words)
    {
        const string fixture = "Passing clouds and 7 degrees.";
        int filler = Math.Max(1, words - 10);
        return $"South Road is slow with {string.Join(" ", Enumerable.Repeat("word", filler))} end. {fixture}";
    }

    private static ConversationalBulletinWorkflow Workflow(ScriptedWorkflowLlm llm) =>
        new(llm, new OneWordPerSecondOracle(), new ConversationalWorkflowOptions
        {
            MaxRevisions = 2,
            ComposeCandidateCount = 1
        });

    [Fact]
    public async Task Malformed_editor_reply_is_retried_and_second_valid_verdict_is_used()
    {
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs).Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", "not json").Reply("edit", Approved);

        var result = await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        result.Trace.Outcome.ShouldBe("succeeded");
        llm.CallsTo("edit").ShouldBe(2);
        result.Trace.Events.ShouldNotContain(e => e.Kind == TraceEventKind.Failure
            && e.Summary == "Editor unavailable - read shipped unreviewed");
    }

    [Fact]
    public async Task Two_malformed_editor_replies_ship_the_read_unreviewed_without_revision()
    {
        string read = ReadOf(30);
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs).Reply("writeFlow", CleanBriefs)
            .Reply("compose2", read)
            .Reply("edit", "not json").Reply("edit", "still not json");

        var result = await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        result.Script.ShouldBe(read);
        result.Trace.Outcome.ShouldBe("succeeded-unreviewed");
        llm.CallsTo("edit").ShouldBe(2);
        llm.CallsTo("compose2").ShouldBe(1);
        result.Trace.Events.Any(e => e.Kind == TraceEventKind.Failure
            && e.Summary == "Editor unavailable - read shipped unreviewed"
            && e.Error != null).ShouldBeTrue();
    }

    [Fact]
    public async Task Malformed_then_rejection_uses_the_rejection_and_performs_revision()
    {
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs).Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", "not json").Reply("edit", Rejected("the read is list-like"))
            .Reply("compose2", ReadOf(30)).Reply("edit", Approved);

        var result = await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        result.Trace.Outcome.ShouldBe("succeeded");
        llm.CallsTo("edit").ShouldBe(3);
        llm.CallsTo("compose2").ShouldBe(2);
    }
}
