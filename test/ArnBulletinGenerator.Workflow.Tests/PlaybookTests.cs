using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Workflow.Prompts;
using ArnBulletinGenerator.Workflow.Tests.Fakes;
using ArnBulletinGenerator.Workflow.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests;

/// <summary>
///     The composer's instructions are station-owned content, not a file we keep to ourselves.
///     <para>
///         These pin the contract that makes handing it over safe: a station may rewrite, disable,
///         reorder or extend any block, and grounding is unaffected because it is enforced in code.
///         The only refusal is the response contract, which is a machine requirement rather than an
///         editorial one.
///     </para>
/// </summary>
public sealed class PlaybookTests
{
    private static Playbook Shipped() => PlaybookParser.Parse(PromptLibrary.System("compose2"));

    [Fact]
    public void The_shipped_default_parses_into_addressable_blocks()
    {
        Playbook playbook = Shipped();

        playbook.Blocks.ShouldNotBeEmpty();
        playbook.Blocks.Select(b => b.Id).ShouldBeUnique();
        playbook.Blocks.ShouldContain(b => b.Id == "voice.name-roads");
        playbook.Blocks.ShouldContain(b => b.Id == "material.ownership");
    }

    [Fact]
    public void Only_the_rules_code_also_enforces_are_locked()
    {
        // The lock list is not "things we would rather stations did not touch" - it is exactly the
        // rules the runtime enforces anyway, so a prompt edit could only ever make the prompt and
        // the code disagree. Everything editorial must stay theirs.
        //
        // Widened 2026-08-26 from output.format alone, once operators could edit the prompts that
        // actually write bulletins. Measured then: locking these is necessary and NOT sufficient -
        // a rogue overlay still aired a fabricated crash by planting it in an EDITABLE block, and
        // what caught it was the edit agent, which never sees the composer's prompt. So if this
        // list grows, check the rule is genuinely code-enforced; if it is only editorial taste, it
        // does not belong here and the guard you actually want is a check, not a lock.
        Shipped().Blocks.Where(b => b.Locked).Select(b => b.Id).ShouldBe(
        [
            "material.facts-only",
            "material.values-are-exact",
            "material.ownership",
            "material.reasoning",
            "selection.required",
            "selection.overshoot",
            "output.format"
        ], ignoreOrder: true);
    }

    [Fact]
    public void Every_steering_block_stays_editable()
    {
        // The other half of the guarantee, and the one that rots quietly: a lock added by reflex
        // takes a knob away from ARN without anyone noticing.
        IEnumerable<string> editable = Shipped().Blocks.Where(b => !b.Locked).Select(b => b.Id);

        editable.ShouldContain("role.standard");
        editable.ShouldContain("voice.variety");
        editable.ShouldContain("length.ladder");
    }

    [Fact]
    public void Text_above_the_first_marker_never_reaches_the_model()
    {
        Shipped().Render().ShouldNotContain("Text above the first marker");
    }

    [Fact]
    public void Rendering_groups_blocks_under_their_section_in_a_fixed_order()
    {
        string rendered = Shipped().Render();

        int role = rendered.IndexOf("## Who you are", StringComparison.Ordinal);
        int material = rendered.IndexOf("## What you may say", StringComparison.Ordinal);
        int selection = rendered.IndexOf("## What goes in", StringComparison.Ordinal);
        int voice = rendered.IndexOf("## How it sounds", StringComparison.Ordinal);
        int output = rendered.IndexOf("## How to reply", StringComparison.Ordinal);

        new[] { role, material, selection, voice, output }.ShouldAllBe(i => i >= 0);
        role.ShouldBeLessThan(material);
        material.ShouldBeLessThan(selection);
        selection.ShouldBeLessThan(voice);
        voice.ShouldBeLessThan(output);
    }

    [Fact]
    public void A_station_can_rewrite_a_block()
    {
        var result = Shipped().Apply(new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string>
            {
                ["voice.name-roads"] = "Use the road's nickname where locals would."
            }
        });

        result.Refused.ShouldBeEmpty();
        result.Playbook.Render().ShouldContain("nickname where locals would");
        result.Playbook.Render().ShouldNotContain("wastes the sentence it appears in");
    }

    [Fact]
    public void A_station_can_switch_a_block_off_entirely()
    {
        // "never what a driver should do" is ARN's house position, not a law. A station that wants
        // its presenters giving advice must be able to have that, and NoAdvice becomes their choice.
        var result = Shipped().Apply(new PlaybookOverlay { Disable = ["voice.no-advice"] });

        result.Refused.ShouldBeEmpty();
        result.Playbook.Render().ShouldNotContain("never what a driver should do");
    }

    [Fact]
    public void A_station_can_add_an_instruction_we_never_shipped()
    {
        var result = Shipped().Apply(new PlaybookOverlay
        {
            Add = [new PlaybookBlock("voice.school-zones", PlaybookSection.Voice,
                "Call out school zones by name between 8 and 9.", Order: 99)]
        });

        result.Refused.ShouldBeEmpty();
        result.Playbook.Render().ShouldContain("Call out school zones by name");
    }

    [Fact]
    public void A_station_addition_can_never_arrive_locked()
    {
        var result = Shipped().Apply(new PlaybookOverlay
        {
            Add = [new PlaybookBlock("voice.mine", PlaybookSection.Voice, "Ours.", 1, Locked: true)]
        });

        result.Playbook.Blocks.Single(b => b.Id == "voice.mine").Locked.ShouldBeFalse(
            "locking protects the machine contract we ship, not a station's own block");
    }

    [Fact]
    public void A_station_cannot_add_a_block_to_the_output_section()
    {
        // Measured live 2026-08-26: an added "reply in JSON" block sat beside the locked
        // reply-format rule, won, and a JSON-wrapped read shipped as "succeeded". How the model
        // replies is a machine property; the editorial sections are the station's.
        var result = Shipped().Apply(new PlaybookOverlay
        {
            Add = [new PlaybookBlock("station.break-format", PlaybookSection.Output,
                "Reply in JSON with keys 'script' and 'mood'.", Order: 1)]
        });

        result.Refused.ShouldContain(r => r.Contains("response contract", StringComparison.Ordinal));
        result.Playbook.Blocks.ShouldNotContain(b => b.Id == "station.break-format");
        result.Playbook.Render().ShouldNotContain("keys 'script' and 'mood'");
    }

    [Fact]
    public void The_response_contract_cannot_be_rewritten_or_disabled()
    {
        var rewrite = Shipped().Apply(new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string> { ["output.format"] = "Reply in JSON." }
        });
        var disable = Shipped().Apply(new PlaybookOverlay
        {
            Disable = ["output.format"]
        });

        rewrite.Refused.ShouldHaveSingleItem();
        disable.Refused.ShouldHaveSingleItem();
        rewrite.Playbook.Render().ShouldContain("Reply with ONLY the bulletin text");
        disable.Playbook.Render().ShouldContain("Reply with ONLY the bulletin text");
    }

    [Fact]
    public void Selection_blocks_cannot_be_rewritten_or_disabled()
    {
        var rewrite = Shipped().Apply(new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string> { ["selection.overshoot"] = "Lead with fuel." }
        });
        var disable = Shipped().Apply(new PlaybookOverlay
        {
            Disable = ["selection.required"]
        });

        rewrite.Refused.ShouldContain(r => r.Contains("Selection section is contract territory", StringComparison.Ordinal));
        disable.Refused.ShouldContain(r => r.Contains("Selection section is contract territory", StringComparison.Ordinal));
    }
    [Fact]
    public void A_station_cannot_add_a_block_to_the_selection_section()
    {
        var result = Shipped().Apply(new PlaybookOverlay
        {
            Add = [new PlaybookBlock("station.order", PlaybookSection.Selection,
                "Put fuel first.", Order: 1)]
        });

        result.Refused.ShouldContain(r => r.Contains("running order and required content", StringComparison.Ordinal));
        result.Playbook.Blocks.ShouldNotContain(block => block.Id == "station.order");
    }

    [Fact]
    public void An_edit_naming_a_block_that_does_not_exist_is_reported_not_ignored()
    {
        // The dead-knob failure mode: an operator changes something and nothing happens. A block we
        // renamed must surface as a refusal, never as silence.
        var result = Shipped().Apply(new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string> { ["voice.tone-of-voice"] = "Warmer." }
        });

        result.Refused.ShouldHaveSingleItem();
        result.Refused[0].ShouldContain("no such block");
    }

    [Fact]
    public void An_untouched_block_keeps_receiving_our_improvements()
    {
        // The reason an overlay is a diff and not a fork.
        var overlay = new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string> { ["voice.name-roads"] = "Theirs." }
        };
        var shipped = new Playbook([
            new PlaybookBlock("voice.name-roads", PlaybookSection.Voice, "Ours, v1.", 0),
            new PlaybookBlock("voice.variety", PlaybookSection.Voice, "Ours, v2 improved.", 1)
        ]);

        string rendered = shipped.Apply(overlay).Playbook.Render();

        rendered.ShouldContain("Theirs.");
        rendered.ShouldContain("Ours, v2 improved.");
    }

    [Fact]
    public async Task A_station_that_deletes_every_instruction_still_cannot_ship_a_fabrication()
    {
        // The claim the whole design rests on: grounding lives in code, so the prompt is safe to
        // hand over. Here the station has disabled every block it is allowed to disable, and the
        // composer states a figure no supplied value contains.
        var stripEverything = new PlaybookOverlay
        {
            Disable = Shipped().Blocks.Where(b => !b.Locked).Select(b => b.Id).ToList()
        };

        var policy = StationPolicy.Empty with
        {
            Playbooks = new Dictionary<string, PlaybookOverlay>(StringComparer.OrdinalIgnoreCase)
            {
                ["compose2"] = stripEverything
            }
        };

        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", """
            {"stories":[
              {"storyId":"south-road","subject":"South Road closure","eventIds":["corridor:South Road#1"],
               "facts":["delayMinutes: 11"],"priority":95,"whyItMatters":"the main arterial"}
            ]}
            """)
            .Reply("writeFlow", """
            {"stories":[
              {"storyId":"south-road","subject":"South Road closure","eventIds":["corridor:South Road#1"],
               "facts":["delayMinutes: 11"],"priority":95,"whyItMatters":"the main arterial"}
            ]}
            """)
            .Reply("compose2", "South Road is running forty minutes late this morning. Passing clouds and 7 degrees.")
            .Reply("edit", """{"feedback":[],"approved":true}""")
            .Reply("compose2", "South Road has an eleven minute delay. Passing clouds and 7 degrees.")
            .Reply("edit", """{"feedback":[],"approved":true}""");

        var request = EvidenceFixtures.Adelaide30S() with { Policy = policy };
        var workflow = new ConversationalBulletinWorkflow(
            llm, new OneWordPerSecondOracle(), new ConversationalWorkflowOptions { MaxRevisions = 1 });

        var result = await workflow.RunAsync(request);

        // The editor approved the fabrication; the deterministic floor did not.
        result.Trace.Events
            .Where(e => e.Kind == Workflow.Trace.TraceEventKind.Check && e.Error is not null)
            .Select(e => e.Summary)
            .ShouldContain(s => s.Contains("SuppliedValueFidelity", StringComparison.Ordinal));
        result.Script.ShouldNotContain("forty minutes");
    }

    [Fact]
    public async Task Final_sentences_carry_the_segment_they_voice_and_its_lineage()
    {
        // The engine protects and labels weather/fuel lines by THIS tag, never by a word list:
        // "clear and 22 for Sydney" names no weather word and was cut as traffic on dev 2e158de3.
        var llm = new ScriptedWorkflowLlm()
            .Reply("writeIncidents", """
            {"stories":[
              {"storyId":"south-road","subject":"South Road closure","eventIds":["corridor:South Road#1"],
               "facts":["delayMinutes: 11"],"priority":95,"whyItMatters":"the main arterial"}
            ]}
            """)
            .Reply("writeFlow", """{"stories":[]}""")
            .Reply("compose2", "South Road has an eleven minute delay. Looking at the weather, passing clouds and 7 degrees.")
            .Reply("edit", """{"feedback":[],"approved":true}""");

        var workflow = new ConversationalBulletinWorkflow(
            llm, new OneWordPerSecondOracle(), new ConversationalWorkflowOptions { MaxRevisions = 0 });

        var result = await workflow.RunAsync(EvidenceFixtures.Adelaide30S());

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Sentences.Count.ShouldBe(2);
        result.Sentences[0].Section.ShouldBe(BulletinSegment.Traffic);
        result.Sentences[0].EvidenceIds.ShouldBeEmpty();
        result.Sentences[1].Section.ShouldBe(BulletinSegment.Weather);
        result.Sentences[1].EvidenceIds.ShouldBe(["weather:line"]);
    }
}
