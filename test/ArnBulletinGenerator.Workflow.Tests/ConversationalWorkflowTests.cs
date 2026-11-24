using ArnBulletinGenerator.Workflow;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Tests.Fixtures;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     Structural behaviour of the conversational workflow, driven by a scripted model.
///     <para>
///         These pin one contract above all: THE SCRIPT THAT SHIPS IS THE SCRIPT THAT WAS REVIEWED.
///         The predecessor ran review and length as two loops in sequence, so a length rewrite
///         happened after the editor had finished and aired unexamined - and an approval given
///         before that rewrite was an approval of text nobody sent to air.
///     </para>
/// </summary>
public sealed class ConversationalWorkflowTests
{
    // Two stories, no overlap: the shape the writer is now asked for.
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

    /// <summary>
    ///     A read of exactly <paramref name="words" /> words; the oracle charges one second each.
    ///     <para>
    ///         It carries Adelaide's weather values because that fixture's station always does, and
    ///         a synthetic read that skips it is not a read the workflow should accept. Leaving it
    ///         out made six tests fail the moment fixtures became enforceable, which is the check
    ///         doing its job on the fixtures rather than a problem with the check.
    ///     </para>
    /// </summary>
    private static string ReadOf(int words)
    {
        const string fixture = "Passing clouds and 7 degrees.";
        int filler = Math.Max(1, words - 10);
        return $"South Road is slow with {string.Join(" ", Enumerable.Repeat("word", filler))} end. {fixture}";
    }

    private static string FiveSentenceReadOf80() =>
        "South Road is slow near Torrensville with delays in both directions this morning for commuters now. "
        + "Port Wakefield Road has roadworks near Virginia causing delays for drivers throughout morning in both directions. "
        + "Northern Expressway is moving slowly near Buchfelde after a minor incident affecting traffic this morning now. "
        + "South Road remains closed near Sir Donald Bradman Drive after crash affecting southbound traffic this morning. "
        + "Passing clouds continue over Adelaide with temperatures at seven degrees for the morning commute today now.";

    [Fact]
    public async Task Measured_duration_underrun_below_one_sentence_lengthens_existing_sentences()
    {
        var llm = new ScriptedWorkflowLlm()
            .ReplyWithResponseId("compose2", ReadOf(35), "composer-thread-short")
            .Reply("edit", Approved);

        var result = await Workflow(llm).ReviseForMeasuredDurationAsync(
            EvidenceFixtures.Adelaide30S(), "composer-thread-1", FiveSentenceReadOf80(), measuredSeconds: 27.8);

        result.IsSuccess.ShouldBeTrue(result.Error);
        string prompt = ComposerPrompts(llm).Single();
        prompt.ShouldContain("Lengthen the existing read");
        prompt.ShouldContain("7 words");
        prompt.ShouldNotContain("additional item");
        result.Trace.Events.ShouldContain(eventItem =>
            eventItem.Kind == TraceEventKind.Decision
            && eventItem.Summary.Contains("lengthens existing sentences by about 7 words", StringComparison.Ordinal)
            && eventItem.Summary.Contains("meanWordsPerSentence=16.00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Measured_duration_underrun_of_two_sentences_adds_one_brief_item()
    {
        var llm = new ScriptedWorkflowLlm()
            .ReplyWithResponseId("compose2", ReadOf(35), "composer-thread-long")
            .Reply("edit", Approved);

        var result = await Workflow(llm).ReviseForMeasuredDurationAsync(
            EvidenceFixtures.Adelaide30S(), "composer-thread-1", FiveSentenceReadOf80(), measuredSeconds: 21.5);

        result.IsSuccess.ShouldBeTrue(result.Error);
        string prompt = ComposerPrompts(llm).Single();
        prompt.ShouldContain("Add exactly one additional item");
        prompt.ShouldContain("about 16 words");
        result.Trace.Events.ShouldContain(eventItem =>
            eventItem.Kind == TraceEventKind.Decision
            && eventItem.Summary.Contains("adds one brief item of about 16 words", StringComparison.Ordinal)
            && eventItem.Summary.Contains("meanWordsPerSentence=16.00", StringComparison.Ordinal));
    }

    // One candidate by default. These tests pin the review/length contract, which is about what
    // happens to a read AFTER it exists; scripting three composes for every one of them would say
    // nothing about that and hide which reply drove the assertion. The candidate race has its own
    // tests below, which set the count explicitly.
    private static ConversationalBulletinWorkflow Workflow(
        ScriptedWorkflowLlm llm,
        int maxRevisions = 2,
        int candidates = 1,
        int maxWordLoopRounds = 2) =>
        new(llm, new OneWordPerSecondOracle(), new ConversationalWorkflowOptions
        {
            MaxRevisions = maxRevisions,
            ComposeCandidateCount = candidates,
            MaxWordLoopRounds = maxWordLoopRounds
        });

    private static IReadOnlyList<string> ComposerPrompts(ScriptedWorkflowLlm llm) =>
        llm.Calls.Where(c => c.Role == "compose2").Select(c => c.UserPrompt).ToList();

    private static IReadOnlyList<string> ReviewedReads(ScriptedWorkflowLlm llm) =>
        llm.Calls.Where(c => c.Role == "edit").Select(c => c.UserPrompt).ToList();

    /// <summary>
    ///     Whether the editor was shown this exact read. The editor prompt numbers the read one
    ///     sentence per line, so the script never appears in it as one contiguous string.
    /// </summary>
    private static bool ReviewSaw(string reviewPrompt, string read) =>
        read.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .All(sentence => reviewPrompt.Contains(sentence, StringComparison.Ordinal));

    [Fact]
    public async Task Measured_duration_revision_continues_the_existing_composer_thread()
    {
        string currentRead = ReadOf(20);
        string revisedRead = ReadOf(35);
        var llm = new ScriptedWorkflowLlm()
            .ReplyWithResponseId("compose2", revisedRead, "composer-thread-2")
            .Reply("edit", Approved);

        var result = await Workflow(llm).ReviseForMeasuredDurationAsync(
            EvidenceFixtures.Adelaide30S(), "composer-thread-1", currentRead, measuredSeconds: 20.0);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Script.ShouldBe(revisedRead);
        result.ComposerThreadId.ShouldBe("composer-thread-2");
        llm.Calls.ShouldContain(call =>
            call.Role == "compose2"
            && call.SystemPrompt == "composer-thread-1"
            && call.UserPrompt.Contains("Add exactly one additional item", StringComparison.Ordinal));
        llm.Calls.Count(call => call.Role == "compose2").ShouldBe(1);
        llm.Calls.Count(call => call.Role == "edit").ShouldBe(1);
        llm.Calls.Any(call =>
            call.Role == "writeIncidents" || call.Role == "writeFlow").ShouldBeFalse();
    }

    [Fact]
    public async Task Plain_wording_of_a_field_name_is_not_treated_as_leaked_reasoning()
    {
        // "delayMinutes: 11" is supplied, and "an eleven-minute delay" is the plainest correct way
        // to say it - but "delay" appears in no fact VALUE. Measured live on 2026-08-26 the check
        // called exactly this correct copy a fabrication, twice in one sydney read, so the field
        // name counts as supplied vocabulary too.
        const string delayBrief = """
        {"stories":[
          {"storyId":"south-road","subject":"South Road southbound closure",
           "eventIds":["corridor:South Road#1"],
           "facts":["delayMinutes: 11","suburb: Torrensville"],
           "priority":95,"whyItMatters":"an eleven minute delay on the main southbound arterial"}
        ]}
        """;

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", delayBrief)
            .Reply("writeFlow", delayBrief)
            .Reply("compose2", "South Road through Torrensville is carrying an eleven minute delay this morning. Passing clouds and 7 degrees.")
            .Reply("edit", Approved);

        var result = await Workflow(llm, maxRevisions: 0).RunAsync(EvidenceFixtures.Adelaide30S());

        result.Trace.Events
            .Where(e => e.Kind == TraceEventKind.Check && e.Error is not null)
            .Select(e => e.Summary)
            .ShouldNotContain(s => s.Contains("ReasoningNotVoiced", StringComparison.Ordinal));
    }



    [Fact]
    public async Task A_length_rewrite_that_breaks_the_copy_is_caught_rather_than_shipped()
    {
        // The point of re-reviewing: a rewrite asked for on length can introduce a correctness
        // problem, and there was previously no stage left to notice.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(40))
            .Reply("edit", Approved)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Rejected("sentence 2 now claims a closure the source does not support"))
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);

        var result = await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        llm.CallsTo("edit").ShouldBe(3);
        result.Trace.Outcome.ShouldBe("succeeded", "the last read was both approved and in band");
    }
    [Fact]
    public async Task Editorial_and_length_problems_reach_the_composer_as_one_note()
    {
        // Sending them separately is what let the two loops drift apart. One note, one rewrite, one
        // review of the result. Disable the pre-editor word loop so this exercises the combined
        // editor-plus-length revision path itself.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(50))
            .Reply("edit", Rejected("sentences 2 and 3 use the same construction"))
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);

        var result = await Workflow(llm, maxWordLoopRounds: 0).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue(result.Error);
        string revision = ComposerPrompts(llm)[1];
        revision.ShouldContain("same construction", Case.Sensitive);
        revision.ShouldContain("South Road", Case.Sensitive);
        llm.CallsTo("compose2").ShouldBe(2, "one rewrite should address both problems, not one each");
        llm.CallsTo("edit").ShouldBe(2, "the rewritten read must be reviewed before it ships");
    }
    [Fact]
    public async Task The_shipped_script_is_always_the_last_read_the_editor_saw()
    {
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(45))
            .Reply("edit", Rejected("too list-like"))
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);

        // Keep the initial read intact so this test isolates the editor-driven revision. Whatever
        // the loop did, the final script must appear in the last thing sent for review.
        var result = await Workflow(llm, maxWordLoopRounds: 0).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue(result.Error);
        ReviewSaw(ReviewedReads(llm)[^1], result.Script).ShouldBeTrue(
            "the shipped script must be the last read reviewed");
        result.Script.ShouldBe(ReadOf(30));
    }
    [Fact]
    public async Task Candidates_are_composed_concurrently_at_spread_word_targets()
    {
        // 30s slot, one word per second, so a 30-word target composes at 28, 30 and 32.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(28))
            .Reply("compose2", ReadOf(30))
            .Reply("compose2", ReadOf(32))
            .Reply("edit", Approved);

        await Workflow(llm, maxRevisions: 0, candidates: 3).RunAsync(EvidenceFixtures.Adelaide30S());

        ComposerPrompts(llm).Count.ShouldBe(3);

        var aims = ComposerPrompts(llm)
            .Select(prompt => System.Text.RegularExpressions.Regex.Match(
                prompt, @"between (\d+) and (\d+) words"))
            .Where(match => match.Success)
            .Select(match =>
                (int.Parse(match.Groups[1].Value) + int.Parse(match.Groups[2].Value)) / 2)
            .Order()
            .ToList();
        aims.ShouldBe([28, 30, 32],
            "the ladder must be one below, one on, and one above the exact body budget");
    }

    [Fact]
    public async Task In_band_candidate_is_not_shortened_back_to_its_prompt_aim()
    {
        // Dev ba48fe7b selected a clean 56-word candidate that fitted the duration band, then cut it
        // to 46 merely because its ladder rung had aimed at 47. The first render landed 0.7s short.
        // Word aims guide sampling; once prose exists, the fitter's duration ceiling owns fit.
        string selectedRead = ReadOf(35);
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", selectedRead)
            .Reply("edit", Approved);
        var request = EvidenceFixtures.Adelaide30S() with
        {
            SlotWords = 30,
            MaxSeconds = 36
        };

        var result = await Workflow(llm, maxRevisions: 0).RunAsync(request);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Script.ShouldBe(selectedRead);
        llm.CallsTo("compose2").ShouldBe(1,
            "an in-band winner must not pay for a word-band correction");
        result.Trace.Events.ShouldContain(e =>
            e.Kind == TraceEventKind.Decision
            && e.Stage == "FitWords"
            && e.Summary.StartsWith("Skipped:", StringComparison.Ordinal));
    }


    [Fact]
    public async Task An_unresolved_editor_concern_never_ships_as_a_clean_success()
    {
        // Rounds run out with the editor still objecting. It may still air - a slot is a hard
        // deadline - but it must not be recorded as a read that passed review.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Rejected("sentence 1 overstates the delay"))
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Rejected("sentence 1 still overstates the delay"));

        var result = await Workflow(llm, maxRevisions: 1).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        result.Trace.Outcome.ShouldBe("succeeded-with-notes");
        result.Trace.Events
            .Where(e => e.Kind == TraceEventKind.Decision)
            .Select(e => e.Summary)
            .ShouldContain(s => s.Contains("unresolved editorial concern", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_editorial_concern_outranks_a_mistimed_read_in_the_outcome()
    {
        // Both wrong at once. "Out of band" is a scheduling problem; an unresolved concern means a
        // reviewer said the copy was wrong and it aired anyway, so that is what the outcome reports.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(50))
            .Reply("edit", Rejected("the read is list-like"));

        var result = await Workflow(llm, maxRevisions: 0).RunAsync(EvidenceFixtures.Adelaide30S());

        result.Trace.Outcome.ShouldBe("succeeded-with-notes");
    }



    [Fact]
    public async Task Overlapping_stories_are_reduced_to_one_story_per_event()
    {
        // The exact shape the old menu produced: two roads, then their combination, as three
        // choices. A composer taking the combined story and either part voiced the closure twice.
        const string overlapping = """
        {"stories":[
          {"storyId":"combined","subject":"South Road and Port Wakefield Road",
           "eventIds":["corridor:South Road#1","corridor:Port Wakefield Road#1"],
           "facts":["detail: road closure southbound","incident: roadworks"],
           "priority":90,"whyItMatters":"both northern approaches"},
          {"storyId":"south-road","subject":"South Road closure",
           "eventIds":["corridor:South Road#1"],
           "facts":["detail: road closure southbound"],
           "priority":80,"whyItMatters":"a full closure"},
          {"storyId":"port-wakefield","subject":"Port Wakefield Road roadworks",
           "eventIds":["corridor:Port Wakefield Road#1"],
           "facts":["incident: roadworks"],
           "priority":70,"whyItMatters":"slower run north"}
        ]}
        """;

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", overlapping)
            .Reply("writeFlow", overlapping)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);

        await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        string composerPrompt = ComposerPrompts(llm)[0];
        composerPrompt.ShouldContain("combined");
        composerPrompt.ShouldNotContain("south-road", Case.Sensitive);
        composerPrompt.ShouldNotContain("port-wakefield", Case.Sensitive);
    }

    [Fact]
    public async Task A_story_keeps_the_events_no_higher_priority_story_claimed()
    {
        // Partitioning must not throw away coverage: an overlapping story that also owns an event
        // of its own survives, holding only that event.
        const string partialOverlap = """
        {"stories":[
          {"storyId":"lead","subject":"South Road closure",
           "eventIds":["corridor:South Road#1"],
           "facts":["detail: road closure southbound"],
           "priority":95,"whyItMatters":"a full closure"},
          {"storyId":"northern","subject":"Northern approaches",
           "eventIds":["corridor:South Road#1","corridor:Northern Expressway#1"],
           "facts":["impact: minor impact"],
           "priority":50,"whyItMatters":"minor delays north"}
        ]}
        """;

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", partialOverlap)
            .Reply("writeFlow", partialOverlap)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);

        await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        string composerPrompt = ComposerPrompts(llm)[0];
        composerPrompt.ShouldContain("lead");
        composerPrompt.ShouldContain("northern", Case.Sensitive);
    }

    [Fact]
    public async Task The_composer_is_briefed_and_never_handed_a_finished_sentence()
    {
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);

        await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        string composerPrompt = ComposerPrompts(llm)[0];
        composerPrompt.ShouldContain("STORY BRIEFS");
        composerPrompt.ShouldContain("AVAILABLE FACTS (you may voice any of these and nothing else");
        composerPrompt.ShouldContain("WHY IT MATTERS (editorial reasoning, never a fact, never voice it)");
    }

    [Fact]
    public async Task Reasoning_copied_out_of_a_brief_is_rejected_as_unsourced()
    {
        // "a full closure on the main southbound arterial" is the writer's judgement. It is in no
        // supplied value, so a read that asserts it states something no source supports.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", "South Road has a full closure on the main southbound arterial this morning.")
            .Reply("compose2", ReadOf(30))
            .Reply("edit", Approved);
        var result = await Workflow(llm).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        result.Trace.Events
            .Where(e => e.Kind == TraceEventKind.Check && e.Error is not null)
            .Select(e => e.Summary)
            .ShouldContain(s => s.Contains("ReasoningNotVoiced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wording_the_data_also_supplies_is_not_treated_as_leaked_reasoning()
    {
        // The guard against the failure mode this check could itself become. "road closure
        // southbound" appears in both the reasoning and the facts, so voicing it is grounded.
        const string echoingBrief = """
        {"stories":[
          {"storyId":"south-road","subject":"South Road southbound closure",
           "eventIds":["corridor:South Road#1"],
           "facts":["detail: road closure southbound","suburb: Torrensville"],
           "priority":95,"whyItMatters":"a road closure southbound is the biggest story today"}
        ]}
        """;

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", echoingBrief)
            .Reply("writeFlow", echoingBrief)
            .Reply("compose2", "South Road has a road closure southbound at Torrensville and that is the story. Passing clouds and 7 degrees.")
            .Reply("edit", Approved);

        var result = await Workflow(llm, maxRevisions: 0).RunAsync(EvidenceFixtures.Adelaide30S());

        result.Trace.Events
            .Where(e => e.Kind == TraceEventKind.Check && e.Error is not null)
            .ShouldBeEmpty();
    }

    // ─── the candidate race ────────────────────────────────────────────────────


    [Fact]
    public async Task A_clean_candidate_beats_a_better_fitting_one_that_failed_a_check()
    {
        // The 30-word read lands exactly on the slot but invents a delay figure the evidence does
        // not supply. The 26-word read is 4s short and says only what it was given. Fit must not
        // buy a fabrication: a wrong read that lands on the slot is the more dangerous of the two,
        // because nothing downstream will flag it.
        string ungrounded = "South Road is closed with a forty-two minute delay. " + ReadOf(24);

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(26))
            .Reply("compose2", ungrounded)
            .Reply("compose2", ungrounded)
            .Reply("edit", Approved);

        await Workflow(llm, maxRevisions: 0, candidates: 3).RunAsync(EvidenceFixtures.Adelaide30S());

        // The editor sees the grounded read, not the well-fitted fabrication.
        ReviewedReads(llm).ShouldHaveSingleItem();
        ReviewSaw(ReviewedReads(llm)[0], ReadOf(26)).ShouldBeTrue();
    }

    [Fact]
    public async Task Among_clean_candidates_the_one_closest_to_the_slot_is_reviewed()
    {
        // All three ground out. 30s slot: 22 words is 8s short, 29 is 1s short, 40 is 10s over.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", ReadOf(22))
            .Reply("compose2", ReadOf(29))
            .Reply("compose2", ReadOf(40))
            .Reply("edit", Approved);

        await Workflow(llm, maxRevisions: 0, candidates: 3).RunAsync(EvidenceFixtures.Adelaide30S());

        ReviewedReads(llm).ShouldHaveSingleItem();
        ReviewSaw(ReviewedReads(llm)[0], ReadOf(29)).ShouldBeTrue();
    }

    private sealed class VoicePaceOracle(double secondsPerWord) : ArnBulletinGenerator.Workflow.Contracts.IDurationOracle
    {
        public double PredictSeconds(string text) =>
            string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length * secondsPerWord;
        public double SecondsForWords(int words) => words * secondsPerWord;
        public int WordsForSeconds(double seconds) => (int)Math.Round(seconds / secondsPerWord);
    }

    [Fact]
    public async Task Candidate_selection_favors_natural_target_fit_over_risky_compression_ceiling()
    {
        // Reproduction of Dev Job dabcc1e2:
        // Voice pace is ~0.40s/word. Slot target is 30.0s (34.5s max compression limit).
        // Candidate 1: 76 words -> predicts 30.4s (within slot, 0.4s delta, safe natural speech).
        // Candidate 2: 86 words -> predicts 34.4s (sitting right on the 34.5s max compression limit).
        // Candidate 3: 104 words -> predicts 41.6s (severe overrun).
        // Under the current implementation, SelectCandidate's ThenByDescending(PredictedSeconds)
        // picks Candidate 2 (86 words) over Candidate 1 (76 words), which failed synthesis on Dev!
        string candidate1 = "South Road is slow with one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine thirty thirty-one thirty-two thirty-three thirty-four thirty-five thirty-six thirty-seven thirty-eight thirty-nine forty forty-one forty-two forty-three forty-four forty-five forty-six forty-seven forty-eight forty-nine fifty fifty-one fifty-two fifty-three fifty-four fifty-five fifty-six fifty-seven fifty-eight fifty-nine sixty sixty-one sixty-two sixty-three sixty-four sixty-five sixty-six. Passing clouds and 7 degrees.";
        string candidate2 = "South Road is slow with one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine thirty thirty-one thirty-two thirty-three thirty-four thirty-five thirty-six thirty-seven thirty-eight thirty-nine forty forty-one forty-two forty-three forty-four forty-five forty-six forty-seven forty-eight forty-nine fifty fifty-one fifty-two fifty-three fifty-four fifty-five fifty-six fifty-seven fifty-eight fifty-nine sixty sixty-one sixty-two sixty-three sixty-four sixty-five sixty-six sixty-seven sixty-eight sixty-nine seventy seventy-one seventy-two seventy-three seventy-four. Passing clouds and 7 degrees.";
        string candidate3 = "South Road is slow with one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twenty-one twenty-two twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine thirty thirty-one thirty-two thirty-three thirty-four thirty-five thirty-six thirty-seven thirty-eight thirty-nine forty forty-one forty-two forty-three forty-four forty-five forty-six forty-seven forty-eight forty-nine fifty fifty-one fifty-two fifty-three fifty-four fifty-five fifty-six fifty-seven fifty-eight fifty-nine sixty sixty-one sixty-two sixty-three sixty-four sixty-five sixty-six sixty-seven sixty-eight sixty-nine seventy seventy-one seventy-two seventy-three seventy-four seventy-five seventy-six seventy-seven seventy-eight seventy-nine eighty eighty-one eighty-two eighty-three eighty-four eighty-five eighty-six eighty-seven eighty-eight eighty-nine ninety ninety-one ninety-two ninety-three ninety-four ninety-five ninety-six. Passing clouds and 7 degrees.";

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Reply("compose2", candidate1)
            .Reply("compose2", candidate2)
            .Reply("compose2", candidate3)
            .Reply("edit", Approved);

        var workflow = new ConversationalBulletinWorkflow(
            llm,
            new VoicePaceOracle(0.40),
            new ConversationalWorkflowOptions { MaxRevisions = 0, ComposeCandidateCount = 3 });

        var result = await workflow.RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        result.Script.ShouldBe(candidate1,
            "Candidate selection should choose the safe natural target fit (76 words), not the risky ceiling candidate (86 words)");
    }
    [Fact]
    public async Task One_failed_candidate_does_not_sink_the_round()
    {
        // Two of three transports fail. A bulletin still has to go to air on the survivor rather
        // than fall to the filler cart over a partial outage.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", CleanBriefs)
            .Reply("writeFlow", CleanBriefs)
            .Fail("compose2", "429 throttled")
            .Reply("compose2", ReadOf(30))
            .Fail("compose2", "503 unavailable")
            .Reply("edit", Approved);

        var result = await Workflow(llm, maxRevisions: 0, candidates: 3).RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue();
        ReviewSaw(ReviewedReads(llm)[0], ReadOf(30)).ShouldBeTrue();
    }
}
