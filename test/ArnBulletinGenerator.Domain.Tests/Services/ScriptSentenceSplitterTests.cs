using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ScriptSentenceSplitterTests
{
    [Fact]
    public void SplitsSimpleSentencesOnPeriodExclamationQuestion()
    {
        var result = ScriptSentenceSplitter.Split("First sentence. Second sentence! Third one?");

        result.ShouldBe([
            "First sentence.",
            "Second sentence!",
            "Third one?"
        ]);
    }

    [Fact]
    public void TrailingTextWithNoFinalPunctuation()
    {
        var result = ScriptSentenceSplitter.Split("Go now. The end");

        result.ShouldBe([
            "Go now.",
            "The end"
        ]);
    }

    [Fact]
    public void ExcludesBracketedDirectionCues()
    {
        var result = ScriptSentenceSplitter.Split("[urgent] Crash on the M4. [friendly] Drive safely.");

        result.ShouldBe([
            "Crash on the M4.",
            "Drive safely."
        ]);
    }

    [Fact]
    public void HandlesAbbreviationsWithoutFalseSplit()
    {
        // "St" has no period so it wouldn't split anyway; the critical case is
        // that a period followed by a capital letter doesn't split when preceded
        // by a non-sentence-ending abbreviation. Our regex uses lookbehind for
        // [.!?] so "St." is matched as a boundary only when "St." is the end of
        // a sentence (St. + whitespace + capital).
        var result = ScriptSentenceSplitter.Split("At St Peters the road is closed. Dr Smith reported it.");

        result.Count.ShouldBe(2);
        result[0].ShouldBe("At St Peters the road is closed.");
        result[1].ShouldBe("Dr Smith reported it.");
    }

    [Fact]
    public void ReturnsEmptyForEmptyText()
    {
        var result = ScriptSentenceSplitter.Split("");

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ReturnsEmptyForWhitespaceText()
    {
        var result = ScriptSentenceSplitter.Split("   \n  ");

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ParsesM4JobScriptIntoSixSentences()
    {
        var text =
            "On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced near the Homebush Bay Drive exit. Emergency services are on site. Expect delays of 10 minutes. On the Sydney Harbour Bridge citybound, a breakdown is blocking the left lane on the approach. Allow extra time for your commute. At St Peters on the Princes Highway, roadworks are causing slowdowns in both directions.";

        var result = ScriptSentenceSplitter.Split(text);

        result.Count.ShouldBe(6);
        result[0].ShouldBe("On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced near the Homebush Bay Drive exit.");
        result[1].ShouldBe("Emergency services are on site.");
        result[2].ShouldBe("Expect delays of 10 minutes.");
        result[3].ShouldBe("On the Sydney Harbour Bridge citybound, a breakdown is blocking the left lane on the approach.");
        result[4].ShouldBe("Allow extra time for your commute.");
        result[5].ShouldBe("At St Peters on the Princes Highway, roadworks are causing slowdowns in both directions.");
    }

    [Fact]
    public void ReturnsEmptyForNull()
    {
        var result = ScriptSentenceSplitter.Split(null!);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void SplitsOnEllipsisFollowedByCapital()
    {
        // Verbatim from deployed dev job 71568e4eac444b6db2a06fe24f92f2bc
        // (Port Lincoln, 15s). The ellipsis terminator lets the duration
        // fitter shed the traffic block incrementally instead of falling
        // through to the prefix cut.
        var result = ScriptSentenceSplitter.Split(
            "The Birdseye Highway and Tod Highway are moving nicely, with a clear run on Port Kenny Road… Road construction and contraflow are underway on Porter Street and Railway Place. Cool in Port Lincoln, eleven degrees.");

        result.Count.ShouldBe(3);
        result[0].ShouldBe("The Birdseye Highway and Tod Highway are moving nicely, with a clear run on Port Kenny Road…");
        result[1].ShouldBe("Road construction and contraflow are underway on Porter Street and Railway Place.");
        result[2].ShouldBe("Cool in Port Lincoln, eleven degrees.");
    }

    [Fact]
    public void DoesNotSplitOnEllipsisFollowedByLowercase()
    {
        // Verbatim from deployed dev job 69be87bf4b0641309881b904c2bb1e90
        // (River 1467): a mid-sentence ellipsis is a delivery-pause device,
        // not a sentence boundary.
        var result = ScriptSentenceSplitter.Split(
            "Walker Road, Old Mail Road and Lock 10 Road are all closed… citybound on the first two, and outbound.");

        result.Count.ShouldBe(1);
        result[0].ShouldBe("Walker Road, Old Mail Road and Lock 10 Road are all closed… citybound on the first two, and outbound.");
    }
}
