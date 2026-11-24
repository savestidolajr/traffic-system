using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Tests.Fixtures;
using ArnBulletinGenerator.Workflow.Tests.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     End-to-end behaviour of the staged workflow, driven by a scripted model so the STRUCTURE is
///     under test rather than any model's writing. Every test emits its full trace as HTML.
/// </summary>
public sealed class StagedWorkflowTests(ITestOutputHelper output)
{
    private const string AdelaidePlan = """
    {"stories":[
      {"storyId":"s1","eventIds":["corridor:South Road#1"],"requiredFacts":["r9:suburb","r9:direction","r9:detail","r9:location:0"],"role":"lead","priority":95,
       "optionalFacts":[{"evidenceIds":["r9:detail:1"],"estimatedWords":6,"priority":70},{"evidenceIds":["r9:delayMinutes"],"estimatedWords":4,"priority":50}],"relationship":null},
      {"storyId":"s2","eventIds":["corridor:Port Wakefield Road#1"],"requiredFacts":["r4:suburb","r4:incident:0"],"role":"traffic","priority":60,
       "optionalFacts":[{"evidenceIds":["r4:delayMinutes"],"estimatedWords":4,"priority":40}],"relationship":null},
      {"storyId":"s3","eventIds":["corridor:Northern Expressway#1"],"requiredFacts":["r7:suburb","r7:impact"],"role":"traffic","priority":40,
       "optionalFacts":[],"relationship":null},
      {"storyId":"s4","eventIds":["weather"],"requiredFacts":["weather:condition","weather:temperature"],"role":"weather","priority":10,
       "optionalFacts":[],"relationship":null}
    ]}
    """;

    private const string AdelaideRead = """
    {"sentences":[
      {"order":1,"storyId":"s1","evidenceIds":["r9:suburb","r9:direction","r9:detail","r9:location:0","r9:detail:1"],
       "text":"Southbound South Road is closed at Sir Donald Bradman Drive in Torrensville, and the closure spans this section."},
      {"order":2,"storyId":"s2","evidenceIds":["r4:suburb","r4:incident:0"],
       "text":"Further north, there are roadworks slowing the run on Port Wakefield Road at Virginia."},
      {"order":3,"storyId":"s3","evidenceIds":["r7:suburb","r7:impact"],
       "text":"The Northern Expressway is only lightly affected as you come through Buchfelde this morning."},
      {"order":4,"storyId":"s4","evidenceIds":["weather:condition","weather:temperature"],
       "text":"Passing clouds today, and it is seven degrees."}
    ]}
    """;

    private static string CleanGrounding(params (int order, string claim, string[] support)[] claims) =>
        "{\"claims\":[" + string.Join(",", claims.Select(c =>
            $"{{\"sentenceOrder\":{c.order},\"claim\":\"{c.claim}\",\"support\":[{string.Join(",", c.support.Select(s => $"\"{s}\""))}],\"status\":\"SUPPORTED\",\"note\":null}}"))
        + "]}";

    private static ScriptedWorkflowLlm CleanAdelaideRun() => new ScriptedWorkflowLlm()
        .Reply("plan", AdelaidePlan)
        .Reply("compose", AdelaideRead)
        .Reply("ground", CleanGrounding(
            (1, "South Road is closed southbound at Sir Donald Bradman Drive", ["r9:detail", "r9:location:0", "r9:direction"]),
            (2, "There are roadworks on Port Wakefield Road at Virginia", ["r4:incident:0", "r4:suburb"]),
            (3, "The Northern Expressway has minor impact at Buchfelde", ["r7:impact", "r7:suburb"]),
            (4, "It is passing clouds and seven degrees", ["weather:condition", "weather:temperature"])));

    private void EmitTrace(WorkflowOutcomeLike outcome)
    {
        string path = TraceOutput.Emit(outcome.Trace);
        output.WriteLine($"trace: {path}");
    }

    private readonly record struct WorkflowOutcomeLike(Workflow.Trace.WorkflowTrace Trace);

    [Fact]
    public async Task A_clean_run_produces_a_read_and_a_complete_trace()
    {
        var llm = CleanAdelaideRun();
        var workflow = new StagedBulletinWorkflow(llm, new OneWordPerSecondOracle(), new StagedWorkflowOptions());

        var result = await workflow.RunAsync(EvidenceFixtures.Adelaide30S() with { TargetSeconds = 55.0 });
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Script.ShouldContain("South Road");
        result.Sentences.Count.ShouldBeGreaterThan(0);

        // The trace is the deliverable: a run that cannot be inspected teaches nothing.
        result.Trace.Events.ShouldNotBeEmpty();

        // At minimum: plan, compose, ground. Deliberately NOT an exact count — pinning that would
        // couple this test to the word estimator's arithmetic, and a re-fit is correct behaviour
        // rather than a regression.
        result.Trace.LlmCallCount.ShouldBeGreaterThanOrEqualTo(3);
        llm.CallsTo("plan").ShouldBe(1);
        var stages = result.Trace.Events.Select(e => e.Stage).Distinct().ToList();
        stages.ShouldContain("Normalize");
        stages.ShouldContain("Plan");
        stages.ShouldContain("Fit");
        stages.ShouldContain("Compose");
    }

    [Fact]
    public async Task Every_llm_call_records_its_full_prompt_and_response()
    {
        var llm = CleanAdelaideRun();
        var workflow = new StagedBulletinWorkflow(llm, new OneWordPerSecondOracle());

        var result = await workflow.RunAsync(EvidenceFixtures.Adelaide30S() with { TargetSeconds = 55.0 });
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        // The defect this whole design answers: the old exporter stored
        // "(Foundry agent arn-draft - instructions server-side)" instead of the prompt, so a bad
        // read could not be explained after the fact. Every call must carry the real text.
        foreach (var call in result.Trace.LlmCalls)
        {
            call.SystemPrompt.ShouldNotBeNullOrWhiteSpace();
            call.UserPrompt.ShouldNotBeNullOrWhiteSpace();
            call.SystemPrompt.ShouldNotContain("server-side");

            // A call that failed has no response text, and pretending otherwise would hide the
            // failure. A call that SUCCEEDED must have its output captured verbatim.
            if (call.Error is null)
                call.RawResponse.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task The_planner_is_asked_once_however_many_times_the_read_is_refitted()
    {
        // A duration miss changes WHICH facts are voiced, never the editorial judgement about what
        // matters. Re-planning per attempt would let a slot overshoot silently reorder the read.
        var llm = new ScriptedWorkflowLlm()
            .Reply("plan", AdelaidePlan)
            .Reply("compose", AdelaideRead)
            .Reply("ground", CleanGrounding((1, "ok", ["r9:detail"])))
            .Reply("compose", AdelaideRead)
            .Reply("ground", CleanGrounding((1, "ok", ["r9:detail"])))
            .Reply("compose", AdelaideRead)
            .Reply("ground", CleanGrounding((1, "ok", ["r9:detail"])));

        // A slot far longer than the read can fill forces the duration loop to run out.
        var request = EvidenceFixtures.Adelaide30S() with { TargetSeconds = 120.0 };
        var workflow = new StagedBulletinWorkflow(llm, new WordRateDurationOracle());

        var result = await workflow.RunAsync(request);
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        llm.CallsTo("plan").ShouldBe(1);
        llm.CallsTo("compose").ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task A_contradicted_claim_triggers_a_whole_script_revision_not_a_sentence_patch()
    {
        const string flagged = """
        {"claims":[
          {"sentenceOrder":1,"claim":"South Road is closed","support":[],"status":"CONTRADICTED","note":"data marks roadworks"},
          {"sentenceOrder":2,"claim":"roadworks at Virginia","support":["r4:incident:0"],"status":"SUPPORTED","note":null}
        ]}
        """;

        var llm = new ScriptedWorkflowLlm()
            .Reply("plan", AdelaidePlan)
            .Reply("compose", AdelaideRead)
            .Reply("ground", flagged)
            .Reply("compose", AdelaideRead)
            .Reply("ground", CleanGrounding((1, "ok", ["r9:detail"])));

        var workflow = new StagedBulletinWorkflow(llm, new OneWordPerSecondOracle());
        var result = await workflow.RunAsync(EvidenceFixtures.Adelaide30S() with { TargetSeconds = 55.0 });
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        // Repair is whole-script. A listy or wrong read cannot be fixed one sentence at a time,
        // because the defect lives between the sentences — the predecessor's sentence refiner was
        // contractually forbidden from writing a second sentence and its provider rejected the
        // output when it tried anyway.
        llm.CallsTo("compose").ShouldBe(2);
        llm.Calls.Last(c => c.Role == "compose").UserPrompt.ShouldContain("South Road is closed");
    }

    [Fact]
    public async Task Voicing_a_story_the_fitter_dropped_rejects_the_whole_attempt_rather_than_stripping_its_citations()
    {
        // At a 30-word budget the fitter affords the mandatory weather fixture plus the lead, and
        // drops s2 and s3. The scripted read voices them anyway, so it cites ids no story was
        // granted.
        //
        // The old behaviour deleted the offending citations and kept the sentence. Live on
        // 2026-08-26 that fired for real: the composer copied five ids exactly as the prompt printed
        // them, the parser logged "Dropped hallucinated evidence id 'r5:direction:'" for every one,
        // and the claim went forward with NO provenance at all. Deleting the evidence while keeping
        // the assertion is precisely backwards - an unverifiable claim is the dangerous half.
        var llm = new ScriptedWorkflowLlm()
            .Reply("plan", AdelaidePlan)
            .Reply("compose", AdelaideRead)
            .Reply("ground", CleanGrounding((1, "ok", ["r9:detail"])));

        var workflow = new StagedBulletinWorkflow(llm, new OneWordPerSecondOracle());
        var result = await workflow.RunAsync(EvidenceFixtures.Adelaide30S() with { TargetSeconds = 30.0 });
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        // The attempt is refused outright, naming the ids, and nothing reaches the read.
        var failures = result.Trace.Events.Where(e => e.Error != null).Select(e => e.Summary + " " + e.Error).ToList();
        failures.ShouldContain(f => f.Contains("authorized", StringComparison.OrdinalIgnoreCase));
        failures.ShouldContain(f => f.Contains("r7:"));
        result.Script.ShouldBeEmpty();

        // Grounding is never paid for on a read the code already refused.
        llm.CallsTo("ground").ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_planner_call_still_returns_an_inspectable_trace()
    {
        var llm = new ScriptedWorkflowLlm().Fail("plan", "upstream 429");
        var workflow = new StagedBulletinWorkflow(llm, new WordRateDurationOracle());

        var result = await workflow.RunAsync(EvidenceFixtures.Sydney22S());
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        result.IsSuccess.ShouldBeFalse();
        result.Trace.Events.ShouldNotBeEmpty();
        result.Trace.Events.Any(e => e.Error != null).ShouldBeTrue();
    }

    [Fact]
    public async Task Evidence_with_no_events_fails_before_spending_a_single_call()
    {
        var llm = new ScriptedWorkflowLlm();
        var workflow = new StagedBulletinWorkflow(llm, new WordRateDurationOracle());

        var result = await workflow.RunAsync(EvidenceFixtures.Sydney22S() with { Evidence = [] });
        EmitTrace(new WorkflowOutcomeLike(result.Trace));

        result.IsSuccess.ShouldBeFalse();
        llm.Calls.ShouldBeEmpty();
    }
}
