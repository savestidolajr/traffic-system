using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public sealed class VariantFittingTests
{
    // (Ratchet_commits_defect_clearing_short_variant... was deleted 2026-08-21: the whole-script
    //  ratchet it pinned went with the whole-script refiner. Variant selection now happens only at
    //  the ceiling fit below.)

    [Fact]
    public void Ceiling_uses_short_variant_before_dropping_grouped_sentence()
    {
        const int targetWords = 50;
        var sentences = new List<DraftedSentence>
        {
            new(
                1,
                "Emergency works are causing delays on the Gore Hill Freeway ramp to the M1, Mitchell Road and Pittwater Road, with queues approaching the tunnel and slowing traffic through the surrounding approaches today.",
                [],
                [new DraftedVariant("concise", "Emergency works are delaying the Gore Hill Freeway ramp to the M1, Mitchell Road and Pittwater Road.")]),
            new(
                2,
                "Traffic is moving slowly on the M5 through the western suburbs with delays at several interchanges and heavier conditions approaching the city this afternoon, with queues remaining heavy across the city tonight.",
                [])
        };
        string expanded = string.Join(" ", sentences.Select(sentence => sentence.Text));
        int ceiling = ScriptWordCeiling.For(targetWords);

        ScriptReviewLoop.CeilingVariantSelection? selected = ScriptReviewLoop.SelectCeilingVariants(
            expanded, sentences, ceiling);

        selected.ShouldNotBeNull();
        selected!.Value.Script.ShouldContain("Emergency works are delaying the Gore Hill Freeway ramp");
        selected.Value.Script.ShouldNotContain("Emergency works are causing delays");
        WordCounter.Count(selected.Value.Script).ShouldBeLessThanOrEqualTo(ceiling);
        int droppedSentenceWords = WordCounter.Count(sentences[1].Text);
        Math.Abs(targetWords - WordCounter.Count(selected.Value.Script)).ShouldBeLessThan(
            Math.Abs(targetWords - droppedSentenceWords));
    }

    [Fact]
    public void Ceiling_prefers_the_longest_fitting_variant_not_the_shortest()
    {
        // Regression, live job fa743d36 (2026-08-19): the ceiling pass selected the SHORTEST
        // fitting phrasing, which pushed every generation attempt under the word floor and
        // failed the bulletin with "not enough content to fill the slot" — the slot then aired
        // the standby cart. A ceiling is an upper bound: when two phrasings both fit, the one
        // that fills more of the slot is the better read, so the longer must win.
        const int targetWords = 50;
        var sentences = new List<DraftedSentence>
        {
            new(
                1,
                "Emergency works are causing lengthy delays right now on the Gore Hill Freeway ramp heading through to the M1, and also along Mitchell Road and further north on Pittwater Road, where queues have been building steadily and consistently through every single one of the surrounding approaches and side streets right across the wider area throughout the whole of this morning so far.",
                [],
                [
                    // Both of these fit under the ceiling. The longer one must be chosen.
                    new DraftedVariant("standard", "Emergency works are delaying the Gore Hill Freeway ramp to the M1, Mitchell Road and Pittwater Road, with queues building through the approaches."),
                    new DraftedVariant("concise", "Emergency works are delaying the Gore Hill Freeway ramp.")
                ])
        };
        string expanded = sentences[0].Text;
        int ceiling = ScriptWordCeiling.For(targetWords);

        ScriptReviewLoop.CeilingVariantSelection? selected = ScriptReviewLoop.SelectCeilingVariants(
            expanded, sentences, ceiling);

        selected.ShouldNotBeNull();
        int chosen = WordCounter.Count(selected!.Value.Script);
        chosen.ShouldBeLessThanOrEqualTo(ceiling);
        selected.Value.Script.ShouldContain("with queues building through the approaches");
        chosen.ShouldBeGreaterThan(WordCounter.Count("Emergency works are delaying the Gore Hill Freeway ramp."));
    }

    [Fact]
    public void Ceiling_selects_variants_when_script_has_bookends_and_filler_statements()
    {
        // Script is composed with bookends and filler:
        // Intro: "Now looking over Sydney."
        // Body S1: "Emergency works are delaying the Gore Hill Freeway ramp to the M1, Mitchell Road and Pittwater Road, with queues building through the approaches." (concise: "Emergency works are delaying the Gore Hill Freeway ramp.")
        // Body S2: "Traffic on the M5 is moving slowly through the western suburbs with delays." (concise: "The M5 is slow.")
        // Filler: "Download the iHeartRadio app for more details."
        // Outro: "That's KIIS traffic and weather together."
        var sentences = new List<DraftedSentence>
        {
            new(
                1,
                "Emergency works are delaying the Gore Hill Freeway ramp to the M1, Mitchell Road and Pittwater Road, with queues building through the approaches.",
                [],
                [new DraftedVariant("concise", "Emergency works are delaying the Gore Hill Freeway ramp.")]),
            new(
                2,
                "Traffic on the M5 is moving slowly through the western suburbs with delays.",
                [],
                [new DraftedVariant("concise", "The M5 is slow.")])
        };

        var stationLines = new[]
        {
            "Now looking over Sydney.",
            "Download the iHeartRadio app for more details.",
            "That's KIIS traffic and weather together."
        };

        string composedScript = "Now looking over Sydney. " +
            "Emergency works are delaying the Gore Hill Freeway ramp to the M1, Mitchell Road and Pittwater Road, with queues building through the approaches. " +
            "Traffic on the M5 is moving slowly through the western suburbs with delays. " +
            "Download the iHeartRadio app for more details. " +
            "That's KIIS traffic and weather together.";

        // Set ceiling so that the expanded body (with bookends/filler) exceeds it, but with concise variants it fits
        int composedWords = WordCounter.Count(composedScript); // ~46 words
        int ceiling = 35;

        ScriptReviewLoop.CeilingVariantSelection? selected = ScriptReviewLoop.SelectCeilingVariants(
            composedScript, sentences, ceiling, stationLines);

        selected.ShouldNotBeNull();
        selected!.Value.Script.ShouldStartWith("Now looking over Sydney.");
        selected.Value.Script.ShouldEndWith("That's KIIS traffic and weather together.");
        selected.Value.Script.ShouldContain("Download the iHeartRadio app for more details.");
        selected.Value.Script.ShouldContain("Emergency works are delaying the Gore Hill Freeway ramp.");
        selected.Value.Script.ShouldContain("The M5 is slow.");
        WordCounter.Count(selected.Value.Script).ShouldBeLessThanOrEqualTo(ceiling);
    }

    private static BroadcastWithTraffic Context(int targetWords) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 30,
        TargetWordCountOverride = Option<int>.Some(targetWords),
        JobId = "variant-fitting-test",
        Location = "sydney",
        VoiceId = Option<string>.Some("voice"),
        Incidents = [],
        Corridors = [new TrafficCorridor(
            "M4", 2, 1, 1000, 40, "congestion", "Slow traffic", [], Option<string>.None)]
    };
}
