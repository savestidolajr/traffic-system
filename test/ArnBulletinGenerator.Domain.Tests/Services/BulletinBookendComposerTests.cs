using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class BulletinBookendComposerTests
{
    private static Option<ResolvedBookends> Pair(string intro, string outro) =>
        Option<ResolvedBookends>.Some(new ResolvedBookends(intro, outro, "bk-1", 1));

    private static Option<ResolvedBookends> None => Option<ResolvedBookends>.None;

    private const string Body = "The M1 is slow through Beenleigh";

    // ── Order: the closer always has the last word ─────────────────────────

    [Fact]
    public void Compose_OrdersIntroThenBodyThenFillerThenOutro()
    {
        string result = BulletinBookendComposer.Compose(
            Pair("Looking over Melbourne today", "That's it for Melbourne traffic"),
            Body,
            ["Keep left unless overtaking"]);

        result.ShouldBe(
            "Looking over Melbourne today. The M1 is slow through Beenleigh. " +
            "Keep left unless overtaking. That's it for Melbourne traffic");
    }

    [Fact]
    public void Compose_FillerLandsBeforeTheOutro_NeverAfterIt()
    {
        // The whole point of composing filler inline: a stitched clip would play AFTER the closer,
        // so the bulletin would announce it was over and then keep talking.
        string result = BulletinBookendComposer.Compose(
            Pair("", "That's your traffic"), Body, ["Rest if you're tired"]);

        result.IndexOf("Rest if you're tired", StringComparison.Ordinal)
            .ShouldBeLessThan(result.IndexOf("That's your traffic", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_MultipleFillerStatements_KeepSelectionOrder()
    {
        BulletinBookendComposer.Compose(Pair("", "Done"), Body, ["First up", "Second up"])
            .ShouldBe("The M1 is slow through Beenleigh. First up. Second up. Done");
    }

    // ── Punctuation at the joins ───────────────────────────────────────────

    [Fact]
    public void Compose_AddsFullStop_WhenAPartHasNoTerminalPunctuation()
    {
        BulletinBookendComposer.Compose(Pair("Checking your KIIS traffic", ""), Body, [])
            .ShouldBe("Checking your KIIS traffic. The M1 is slow through Beenleigh");
    }

    [Fact]
    public void Compose_PreservesAuthoredComma_ForARunOnOpener()
    {
        // "Looking over Melbourne today, the M1 is slow" — the author's comma is the join, and
        // replacing it with a full stop would change how the read is phrased.
        BulletinBookendComposer.Compose(Pair("Looking over Melbourne today,", ""), Body, [])
            .ShouldBe("Looking over Melbourne today, The M1 is slow through Beenleigh");
    }

    [Fact]
    public void Compose_DoesNotDoublePunctuate_AnAuthoredFullStop()
    {
        BulletinBookendComposer.Compose(Pair("Here's your traffic.", ""), Body, [])
            .ShouldBe("Here's your traffic. The M1 is slow through Beenleigh");
    }

    [Fact]
    public void Compose_LeavesTheFinalPartExactlyAsAuthored()
    {
        BulletinBookendComposer.Compose(Pair("", "Stay safe out there"), Body, [])
            .ShouldEndWith("Stay safe out there");
    }

    // ── Absent parts ───────────────────────────────────────────────────────

    [Fact]
    public void Compose_NoBookend_ReturnsTheBodyUnchanged()
    {
        BulletinBookendComposer.Compose(None, Body, []).ShouldBe(Body);
    }

    [Fact]
    public void Compose_OneSidedPairs_OnlyReadTheSideTheyHave()
    {
        BulletinBookendComposer.Compose(Pair("Top", ""), Body, []).ShouldBe($"Top. {Body}");
        BulletinBookendComposer.Compose(Pair("", "Tail"), Body, []).ShouldBe($"{Body}. Tail");
    }

    [Fact]
    public void Compose_SkipsBlankParts_WithoutLeavingDoubleSpaces()
    {
        BulletinBookendComposer.Compose(Pair("Top", "Tail"), "   ", []).ShouldBe("Top. Tail");
    }

    [Fact]
    public void Compose_EverythingEmpty_ReturnsEmpty()
    {
        BulletinBookendComposer.Compose(None, "", []).ShouldBe("");
    }

    // ── The budget the writer's word target is reduced by ──────────────────

    [Fact]
    public void EstimateSeconds_CountsBothLines_AtTheVoicesPace()
    {
        // 4 + 4 = 8 words at 0.4 s/word.
        BulletinBookendComposer
            .EstimateSeconds(Pair("one two three four", "five six seven eight"), 0.4)
            .ShouldBe(3.2, tolerance: 0.0001);
    }

    [Fact]
    public void EstimateSeconds_NoBookend_IsZero_SoTheSlotIsUntouched()
    {
        BulletinBookendComposer.EstimateSeconds(None, 0.4).ShouldBe(0);
    }

    [Fact]
    public void WordCount_SumsBothLines()
    {
        BulletinBookendComposer.WordCount(Pair("a b c", "d e")).ShouldBe(5);
        BulletinBookendComposer.WordCount(None).ShouldBe(0);
    }

    // ── Placement predicates the engine branches on ────────────────────────

    [Fact]
    public void HasOutro_IsTrueOnlyWhenTheClosingLineIsPresent()
    {
        // This drives the inline-vs-stitched filler decision, so a one-sided pair must not claim
        // an outro it does not have.
        BulletinBookendComposer.HasOutro(Pair("Top", "Tail")).ShouldBeTrue();
        BulletinBookendComposer.HasOutro(Pair("Top", "")).ShouldBeFalse();
        BulletinBookendComposer.HasOutro(Pair("Top", "   ")).ShouldBeFalse();
        BulletinBookendComposer.HasOutro(None).ShouldBeFalse();
    }

    [Fact]
    public void HasAny_IsFalseForAnUnassignedOrBlankPair()
    {
        BulletinBookendComposer.HasAny(None).ShouldBeFalse();
        BulletinBookendComposer.HasAny(Pair("", "")).ShouldBeFalse();
        BulletinBookendComposer.HasAny(Pair("", "Tail")).ShouldBeTrue();
    }

    [Fact]
    public void IntroOf_And_OutroOf_ReadTheRespectiveLines()
    {
        BulletinBookendComposer.IntroOf(Pair("hello", "bye")).ShouldBe("hello");
        BulletinBookendComposer.OutroOf(Pair("hello", "bye")).ShouldBe("bye");
        BulletinBookendComposer.IntroOf(None).ShouldBe("");
        BulletinBookendComposer.OutroOf(None).ShouldBe("");
    }

    // ── The body carries content only: a body edge duplicating its bookend is stripped ─────

    [Fact]
    public void Compose_BodyOpeningWithTheIntro_AirsTheIntroOnce()
    {
        // Observed live (dev job 6727ce48, 2026-08-17): "Now looking over Sydney. Now looking
        // over Sydney." — the writer copied the locked opener into the body.
        string result = BulletinBookendComposer.Compose(
            Pair("Now looking over Sydney.", ""),
            "Now looking over Sydney. Botany Road has roadworks at Hale Street",
            []);

        result.ShouldBe("Now looking over Sydney. Botany Road has roadworks at Hale Street");
    }

    [Fact]
    public void Compose_DuplicateIntroWithDifferentPunctuationAndCase_IsStillStripped()
    {
        string result = BulletinBookendComposer.Compose(
            Pair("Now looking over Sydney.", ""),
            "now looking over Sydney! The M4 is heavy at Parramatta",
            []);

        result.ShouldBe("Now looking over Sydney. The M4 is heavy at Parramatta");
    }

    [Fact]
    public void Compose_BodyEndingWithTheOutro_AirsTheOutroOnce()
    {
        string result = BulletinBookendComposer.Compose(
            Pair("", "That's KIIS traffic and weather together"),
            "The M4 is heavy at Parramatta. That's KIIS traffic and weather together",
            []);

        result.ShouldBe("The M4 is heavy at Parramatta. That's KIIS traffic and weather together");
    }

    [Fact]
    public void Compose_ADistinctOpeningSentence_IsNotStripped()
    {
        // Only exact (normalized) duplicates are mechanical faults; different words are content.
        string result = BulletinBookendComposer.Compose(
            Pair("Now looking over Sydney.", ""),
            "A big afternoon on the roads. The M4 is heavy at Parramatta",
            []);

        result.ShouldBe("Now looking over Sydney. A big afternoon on the roads. The M4 is heavy at Parramatta");
    }

    [Fact]
    public void Compose_BodyThatIsOnlyTheIntro_ComposesToTheIntroAlone()
    {
        string result = BulletinBookendComposer.Compose(
            Pair("Now looking over Sydney.", ""), "Now looking over Sydney.", []);

        result.ShouldBe("Now looking over Sydney.");
    }

    // ARN, 2026-08-24, test b322bc0c: the read aired "…powered by. That's KIIS traffic and weather
    // together, powered by". A ceiling trim went content-limited, the re-fit re-entered generation
    // with the already-composed VOICED take, and the trailing-edge guard could not match because a
    // delivery tag sat on the closer — so the outro was appended a second time.
    private const string Kiis = "That's KIIS traffic and weather together, powered by";

    [Fact]
    public void Compose_ATaggedDuplicateCloser_IsStrippedNotDoubled()
    {
        string alreadyComposed = $"Now looking over Sydney. Mitchell Road is closed. [brightly] {Kiis}";

        string result = BulletinBookendComposer.Compose(
            Pair("Now looking over Sydney.", Kiis), alreadyComposed, []);

        result.Split(Kiis).Length.ShouldBe(2, $"the closer must appear exactly once — got: {result}");
        result.ShouldEndWith(Kiis);
    }

    [Fact]
    public void Compose_ATaggedDuplicateIntro_IsStrippedNotDoubled()
    {
        string result = BulletinBookendComposer.Compose(
            Pair("Now looking over Sydney.", ""),
            "[warmly] Now looking over Sydney. Mitchell Road is closed.",
            []);

        result.Split("Now looking over Sydney.").Length.ShouldBe(2,
            $"the opener must appear exactly once — got: {result}");
    }

    [Fact]
    public void Compose_ATaggedTailThatIsNotTheCloser_IsLeftAlone()
    {
        // Only exact duplicates are mechanical faults; a tag must not make a different tail match.
        string result = BulletinBookendComposer.Compose(
            Pair("", Kiis), "The M4 is heavy at Parramatta. [brightly] Drive safely out there.", []);

        result.ShouldContain("Drive safely out there.");
        result.ShouldEndWith(Kiis);
    }
}
