using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Tests.Fixtures;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>
///     The desk's `condition` is the one string the composer may voice without it having been cited,
///     and that licence covers qualitative state only.
///     <para>
///         It exists because the emitter used to choose the wording itself, one corridor at a time,
///         by hashing the road name into a two-option palette — which cannot help repeating itself
///         once three roads share a band. Brisbane and melbourne each aired two roads described in
///         identical words ("barely noticeable"), and the detectors that were supposed to catch that
///         are advisory, so it reached air. The desk sees every event at once and can say it once.
///     </para>
/// </summary>
public sealed class DeskConditionTests
{
    private static (BulletinBriefs Briefs, NormalizedInput Input) Fixture(string condition)
    {
        var request = EvidenceFixtures.Adelaide30S();
        var trace = new TraceRecorder("run", "wf", "desk condition", "job", "adelaide", "KIIS", 30.0);
        NormalizedInput input = Normalizer.Normalize(request, trace);
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "south-road", "South Road", ["corridor:South Road#1"], 95,
                "the main southbound arterial", Required: false, Condition: condition)
        ]);
        return (briefs, input);
    }

    private static CheckResult Check(string condition)
    {
        var (briefs, input) = Fixture(condition);
        var trace = new TraceRecorder("run", "wf", "desk condition", "job", "adelaide", "KIIS", 30.0);
        return ConversationalChecks
            .Run(input, "South Road is shut at Torrensville. Passing clouds overhead.", briefs, trace)
            .Single(c => c.Name == "DeskConditionCarriesNoFigure");
    }

    [Fact]
    public void A_qualitative_condition_passes()
    {
        Check("nothing is getting through, though the side streets are coping").Passed.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_condition_passes()
    {
        // Saying nothing is a legitimate answer: a road whose only news is that it is behaving
        // normally should not be announced in order to call it unremarkable.
        Check(string.Empty).Passed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("banked up for about two kays")]
    [InlineData("running about ten minutes behind")]
    [InlineData("down to about a third of its usual pace and holding")]
    public void A_condition_carrying_a_figure_is_rejected(string condition)
    {
        // A figure here would bypass citation completely: SuppliedValueFidelity grounds spoken
        // quantities against supplied fact VALUES, and a condition is not a fact value, so the number
        // would be voiced, found in the briefs, and never checked against the source.
        var result = Check(condition);

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("south-road");
    }

    [Fact]
    public void Desk_only_material_is_withheld_from_the_composer_but_shown_to_the_desk()
    {
        // The measurement says what was NOT measured, which the desk needs to judge whether a road
        // deserves a sentence and which is meaningless as copy. Offered to the composer as an
        // ordinary fact it was duly aired: brisbane df18a7d8 read "Roadworks are in place on Milton
        // Road and Frederick Street in Toowong, with no delay measured."
        var request = EvidenceFixtures.Adelaide30S() with
        {
            Evidence =
            [
                new EvidenceUnit("r1:detail", "corridor:South Road#1", "detail", "roadworks"),
                new EvidenceUnit("r1:suburb", "corridor:South Road#1", "suburb", "Torrensville"),
                new EvidenceUnit("r1:impact", "corridor:South Road#1", "measured impact",
                    "no delay measured", DeskOnly: true)
            ]
        };
        var trace = new TraceRecorder("run", "wf", "desk only", "job", "adelaide", "KIIS", 30.0);
        NormalizedInput input = Normalizer.Normalize(request, trace);

        // The desk reads the source block, so it must see the measurement.
        SourceDataRenderer.Render(input).ShouldContain("no delay measured");

        // The composer reads the briefs, so it must not.
        var briefs = new BulletinBriefs([
            new StoryBrief(1, "south-road", "South Road", ["corridor:South Road#1"], 95,
                "the main southbound arterial", Required: false,
                Condition: "the works are in place but nobody is being held up")
        ]);
        string rendered = briefs.Render(input);

        rendered.ShouldNotContain("no delay measured");
        rendered.ShouldContain("roadworks");
        // The desk's own phrasing IS offered, because that is the channel that replaced it.
        rendered.ShouldContain("nobody is being held up");
    }
}
