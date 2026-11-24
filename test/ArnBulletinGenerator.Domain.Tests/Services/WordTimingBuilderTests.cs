using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class WordTimingBuilderTests
{
    private static AudioAlignment Align(
        IReadOnlyList<string> chars,
        IReadOnlyList<double> starts,
        IReadOnlyList<double> ends) => new(chars, starts, ends);

    [Fact]
    public void SimpleTwoWordAlignment()
    {
        var alignment = Align(
            ["h", "i", " ", "g", "o"],
            [0.0, 0.3, 0.6, 0.7, 1.0],
            [0.3, 0.6, 0.7, 1.0, 1.2]);

        var result = WordTimingBuilder.Build(alignment, 1.0, 10.0);

        result.Count.ShouldBe(2);
        result[0].Word.ShouldBe("hi");
        result[0].StartSeconds.ShouldBe(0.0);
        result[0].EndSeconds.ShouldBe(0.6);
        result[1].Word.ShouldBe("go");
        result[1].StartSeconds.ShouldBe(0.7);
        result[1].EndSeconds.ShouldBe(1.2);
    }

    [Fact]
    public void ScaleFactor_HalvesAllTimes()
    {
        var alignment = Align(
            ["h", "i", " ", "g", "o"],
            [0.0, 0.3, 0.6, 0.7, 1.0],
            [0.3, 0.6, 0.7, 1.0, 1.2]);

        var result = WordTimingBuilder.Build(alignment, 0.5, 10.0);

        result.Count.ShouldBe(2);
        result[0].StartSeconds.ShouldBe(0.0);
        result[0].EndSeconds.ShouldBe(0.3);
        result[1].StartSeconds.ShouldBe(0.35);
        result[1].EndSeconds.ShouldBe(0.6);
    }

    [Fact]
    public void Clamping_EndBeyondMax_CapsToMaxDuration()
    {
        var alignment = Align(["h", "i"], [0.0, 0.3], [0.3, 0.6]);
        var result = WordTimingBuilder.Build(alignment, 1.0, 0.4);

        result[0].StartSeconds.ShouldBe(0.0);
        result[0].EndSeconds.ShouldBe(0.4);
    }

    [Fact]
    public void Clamping_StartNeverExceedsEnd_WhenRawEndIsBelowStart()
    {
        // When Clamp(end, start, max) uses start as lower bound, start can never exceed end.
        var alignment = Align(["a"], [0.8], [0.2]);
        var result = WordTimingBuilder.Build(alignment, 1.0, 0.5);

        result[0].StartSeconds.ShouldBeLessThanOrEqualTo(result[0].EndSeconds);
    }

    [Fact]
    public void MismatchedArrayLengths_ReturnsEmpty()
    {
        var alignment = Align(["h", "i"], [0.0], [0.3, 0.6]);
        WordTimingBuilder.Build(alignment, 1.0, 10.0).ShouldBeEmpty();
    }

    [Fact]
    public void ZeroOrNegativeScale_ReturnsEmpty()
    {
        var alignment = Align(["h", "i"], [0.0, 0.3], [0.3, 0.6]);

        WordTimingBuilder.Build(alignment, 0.0, 10.0).ShouldBeEmpty();
        WordTimingBuilder.Build(alignment, -1.0, 10.0).ShouldBeEmpty();
    }

    [Fact]
    public void NonPositiveMaxDuration_ReturnsEmpty()
    {
        var alignment = Align(["h", "i"], [0.0, 0.3], [0.3, 0.6]);

        WordTimingBuilder.Build(alignment, 1.0, 0.0).ShouldBeEmpty();
        WordTimingBuilder.Build(alignment, 1.0, -0.1).ShouldBeEmpty();
    }

    [Fact]
    public void EmptyCharacterList_ReturnsEmpty()
    {
        var alignment = Align([], [], []);
        WordTimingBuilder.Build(alignment, 1.0, 10.0).ShouldBeEmpty();
    }

    [Fact]
    public void WhitespaceSeparators_NoEmptyWordsGenerated()
    {
        // Leading space, multiple consecutive spaces, trailing space
        var alignment = Align(
            [" ", "h", "i", " ", " ", "g", "o", " "],
            [0.0, 0.1, 0.4, 0.7, 0.8, 0.9, 1.2, 1.5],
            [0.1, 0.4, 0.7, 0.8, 0.9, 1.2, 1.5, 1.6]);

        var result = WordTimingBuilder.Build(alignment, 1.0, 10.0);

        result.Count.ShouldBe(2);
        result[0].Word.ShouldBe("hi");
        result[1].Word.ShouldBe("go");
    }

    [Fact]
    public void AllWhitespace_ReturnsEmpty()
    {
        var alignment = Align(
            [" ", "  ", "\t"],
            [0.0, 0.1, 0.2],
            [0.1, 0.2, 0.3]);

        WordTimingBuilder.Build(alignment, 1.0, 10.0).ShouldBeEmpty();
    }

    [Fact]
    public void RoundsTimesToThreeDecimalPlaces()
    {
        var alignment = Align(["h", "i"], [0.12345, 0.4], [0.4, 0.67891]);

        var result = WordTimingBuilder.Build(alignment, 1.0, 10.0);

        result[0].StartSeconds.ShouldBe(0.123);
        result[0].EndSeconds.ShouldBe(0.679);
    }

    [Fact]
    public void InlineAudioTagIsNotAWord_AndDoesNotShiftLaterHighlights()
    {
        // Measured on dev 2026-08-02: the provider aligns the text it was SENT, so a tagged
        // read returned one extra "word" per tag and every later highlight drifted. The
        // operator reads the plain script, so tags must not appear or consume a slot.
        var alignment = Align(
            ["[", "h", "m", "]", " ", "h", "i", " ", "g", "o"],
            [0.0, 0.01, 0.02, 0.03, 0.04, 0.1, 0.3, 0.6, 0.7, 1.0],
            [0.01, 0.02, 0.03, 0.04, 0.1, 0.3, 0.6, 0.7, 1.0, 1.2]);

        var result = WordTimingBuilder.Build(alignment, 1.0, 10.0);

        result.Count.ShouldBe(2);
        result[0].Word.ShouldBe("hi");
        result[0].StartSeconds.ShouldBe(0.1);
        result[1].Word.ShouldBe("go");
    }

    [Fact]
    public void TagBetweenWordsIsDropped_LeavingTheWordsEitherSideIntact()
    {
        var alignment = Align(
            ["h", "i", " ", "[", "x", "]", " ", "g", "o"],
            [0.0, 0.3, 0.6, 0.7, 0.71, 0.72, 0.73, 0.8, 1.0],
            [0.3, 0.6, 0.7, 0.71, 0.72, 0.73, 0.8, 1.0, 1.2]);

        var result = WordTimingBuilder.Build(alignment, 1.0, 10.0);

        result.Select(t => t.Word).ShouldBe(["hi", "go"]);
        result[1].StartSeconds.ShouldBe(0.8);
    }
}
