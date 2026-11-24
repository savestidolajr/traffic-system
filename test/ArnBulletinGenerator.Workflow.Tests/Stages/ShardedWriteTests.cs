using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>
///     The fan-out itself: who gets called, what each desk is shown, and what survives a desk
///     failing. The routing and ordering rules are pinned separately in
///     <see cref="BriefDivisionTests" />.
/// </summary>
public sealed class ShardedWriteTests
{
    private const string Incident = """
        {"stories":[{"storyId":"m5-crash","subject":"M5 crash","eventIds":["corridor:M5"],
        "facts":["detail: two lanes closed"],"whyItMatters":"a driver must route around it"}]}
        """;
    private const string Flow = """
        {"stories":[{"storyId":"princes-queue","subject":"Princes Highway","eventIds":["corridor:Princes"],
        "facts":["delayMinutes: four"],"whyItMatters":"a driver should expect it"}]}
        """;

    private const string Empty = """{"stories":[]}""";
    private static EvidenceUnit Unit(string source, string field, string value) =>
        new($"{source}:{field}", source, field, value);

    /// <summary>
    ///     Sydney-shaped evidence: a closure, a plain queue, a clear run, and the weather fixture.
    ///     Rank is derived from these facts, so the fixture carries no ranking field of its own.
    /// </summary>
    private static WorkflowRequest Request() => new(
        JobId: "job", Market: "sydney", Station: "KIIS", Location: "Sydney", TargetSeconds: 22,
        Evidence:
        [
            Unit("corridor:M5", "detail", "road closure"),
            Unit("corridor:Princes", "delayMinutes", "four"),
            Unit("goodrun:M7", "goodrun", "running well"),
            Unit("weather", "condition", "Passing clouds"),
            Unit("weather", "temperature", "15 degrees")
        ],
        RequiredSections: ["weather"]);

    private static (BulletinBriefs Briefs, ScriptedWorkflowLlm Llm) Write(
        ScriptedWorkflowLlm llm, WorkflowRequest? request = null)
    {
        request ??= Request();
        var trace = new TraceRecorder("run", "wf", "sharded write", "job", "sydney", "KIIS", 22.0);
        NormalizedInput input = Normalizer.Normalize(request, trace);
        BulletinBriefs briefs = new BulletinWriter(llm)
            .WriteBriefsAsync(request, input, targetWords: 44, trace)
            .GetAwaiter().GetResult();
        return (briefs, llm);
    }

    [Fact]
    public void Each_desk_is_called_once_and_weather_is_never_written_by_a_model()
    {
        // The fixture saving is the point: weather is every-word-supplied, which is what a model is
        // worst at and a projection is perfect at.
        var (briefs, llm) = Write(new ScriptedWorkflowLlm()
            .Reply("writeIncidents", Incident)
            .Reply("writeFlow", Flow));

        llm.Calls.Select(c => c.Role).ShouldBe(["writeIncidents", "writeFlow"], ignoreOrder: true);
        llm.Calls.Count.ShouldBe(2);
        briefs.Stories.Select(s => s.StoryId).ShouldContain("weather");
    }

    /// <summary>
    ///     A condition is ONE phrase, because the composer supplies the comma in front of it. Dev
    ///     goldcoast <c>f98d7521</c> (2026-09-03) aired "…from Smith Street Motorway to Arundel,
    ///     moving through, though not at its usual pace" from a hedged condition: two afterthoughts
    ///     on one sentence. The prompts ask for one phrase; the parser keeps the promise.
    /// </summary>
    [Theory]
    [InlineData("moving through, though not at its usual pace", "moving through")]
    [InlineData("traffic is still moving, with brief hold-ups", "traffic is still moving")]
    [InlineData("a little slower —, but freely", "a little slower")]
    [InlineData("no reported hold-up", "no reported hold-up")]
    [InlineData("", "")]
    public void A_hedged_condition_keeps_only_its_leading_phrase(string authored, string expected)
    {
        string reply = $$"""
            {"stories":[{"storyId":"m5-crash","subject":"M5 crash","eventIds":["corridor:M5"],
            "whyItMatters":"a driver must route around it","condition":"{{authored}}"}]}
            """;

        var (briefs, _) = Write(new ScriptedWorkflowLlm()
            .Reply("writeIncidents", reply)
            .Reply("writeFlow", Flow));

        briefs.Stories.Single(story => story.StoryId == "m5-crash").Condition.ShouldBe(expected);
    }

    [Fact]
    public void Neither_desk_sees_the_others_material()
    {
        // The whole reason for splitting: a prompt hedging closures and delay banding dilutes both.
        // If the evidence still arrives whole, nothing has been gained.
        var (_, llm) = Write(new ScriptedWorkflowLlm()
            .Reply("writeIncidents", Incident)
            .Reply("writeFlow", Flow));

        string incidents = llm.Calls.Single(c => c.Role == "writeIncidents").UserPrompt;
        string flow = llm.Calls.Single(c => c.Role == "writeFlow").UserPrompt;

        incidents.ShouldContain("corridor:M5");
        incidents.ShouldNotContain("goodrun:M7");
        flow.ShouldContain("goodrun:M7");
        flow.ShouldNotContain("corridor:M5");
    }

    [Fact]
    public void A_desk_that_loses_its_events_synthesizes_fallback_briefs_after_one_retry()
    {
        // When a desk fails with an empty or failed reply, mandatory events are recovered
        // via deterministic fallback briefs so the bulletin does not drop critical incidents.
        var llm = new ScriptedWorkflowLlm()
            .Fail("writeIncidents", "upstream 500")
            .Reply("writeFlow", Flow);

        var (briefs, _) = Write(llm);

        briefs.Stories.ShouldNotBeEmpty();
        briefs.Stories.ShouldContain(s => s.StoryId.Contains("corridor:M5") || s.Subject.Contains("M5"));
        llm.Calls.Count(call => call.Role == "writeIncidents").ShouldBe(2);
        llm.Calls.Count(call => call.Role == "writeFlow").ShouldBe(1);
    }

    [Fact]
    public void An_unremarkable_works_event_may_be_dropped_after_one_retry()
    {
        WorkflowRequest worksOnly = Request() with
        {
            Evidence =
            [
                Unit("corridor:Joynton", "incident", "construction"),
                Unit("corridor:Joynton", "detail", "Closed"),
                Unit("corridor:Joynton", "location", "Zetland"),
                Unit("corridor:Joynton", "road", "Joynton Ave")
            ],
            RequiredSections = []
        };
        var (briefs, llm) = Write(
            new ScriptedWorkflowLlm()
                .Reply("writeIncidents", Empty)
                .Reply("writeIncidents", Empty),
            worksOnly);

        briefs.Stories.ShouldBeEmpty();
        llm.CallsTo("writeIncidents").ShouldBe(2);
    }

    [Fact]
    public void Works_with_lanes_closed_synthesizes_fallback_brief_when_the_incidents_desk_is_empty()
    {
        WorkflowRequest worksWithClosedLane = Request() with
        {
            Evidence =
            [
                Unit("corridor:Joynton", "incident", "construction"),
                Unit("corridor:Joynton", "detail", "Closed"),
                Unit("corridor:Joynton", "location", "Zetland"),
                Unit("corridor:Joynton", "road", "Joynton Ave"),
                Unit("corridor:Joynton", "lanesClosed", "one")
            ],
            RequiredSections = []
        };
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", Empty)
            .Reply("writeIncidents", Empty);

        var (briefs, _) = Write(llm, worksWithClosedLane);
        briefs.Stories.ShouldNotBeEmpty();
        briefs.Stories.ShouldContain(s => s.Subject.Contains("Joynton Ave") || s.StoryId.Contains("corridor:Joynton"));
        llm.CallsTo("writeIncidents").ShouldBe(2);
    }

    [Fact]
    public void A_crash_synthesizes_fallback_brief_when_the_incidents_desk_is_empty()
    {
        WorkflowRequest crash = Request() with
        {
            Evidence =
            [
                Unit("corridor:Joynton", "incident", "crash"),
                Unit("corridor:Joynton", "location", "Zetland"),
                Unit("corridor:Joynton", "road", "Joynton Ave")
            ],
            RequiredSections = []
        };
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", Empty)
            .Reply("writeIncidents", Empty);

        var (briefs, _) = Write(llm, crash);
        briefs.Stories.ShouldNotBeEmpty();
        briefs.Stories.ShouldContain(s => s.Subject.Contains("Joynton Ave") || s.StoryId.Contains("corridor:Joynton"));
        llm.CallsTo("writeIncidents").ShouldBe(2);
    }
    [Fact]
    public void A_one_kind_board_calls_only_its_expert_desk()
    {
        // The former event-count threshold was superseded by the always-specialist split: the
        // desks run concurrently, and an empty desk is never called. This pins the cost boundary
        // that replaced the threshold rather than restoring a generalist prompt for small boards.
        WorkflowRequest incidentsOnly = Request() with
        {
            Evidence = [Unit("corridor:M5", "detail", "road closure")],
            RequiredSections = []
        };
        var (briefs, llm) = Write(
            new ScriptedWorkflowLlm().Reply("writeIncidents", Incident),
            incidentsOnly);

        llm.Calls.Select(c => c.Role).ShouldBe(["writeIncidents"]);
        briefs.Stories.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_stitched_order_is_the_consoles_rank_across_both_desks()
    {
        // A closure leads a four-minute queue across the desk boundary, and weather closes despite
        // being Required. This is the assertion that says sharding did not quietly reorder the
        // bulletin - the two desks never saw each other's stories, yet the running order is right.
        var (briefs, _) = Write(new ScriptedWorkflowLlm()
            .Reply("writeIncidents", Incident)
            .Reply("writeFlow", Flow));

        briefs.ByPriority.Select(s => s.StoryId).ShouldBe(["m5-crash", "princes-queue", "weather"]);
    }
}
