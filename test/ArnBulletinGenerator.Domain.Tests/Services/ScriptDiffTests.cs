using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ScriptDiffTests
{
    [Fact]
    public void NormalizedEquals_WhitespaceOnlyDifference_IsEqual()
    {
        ScriptDiff.NormalizedEquals(
            "The M4 is  experiencing\ndelays",
            "  The M4 is experiencing delays ").ShouldBeTrue();
    }

    [Fact]
    public void NormalizedEquals_PunctuationChange_CountsAsEdit()
    {
        ScriptDiff.NormalizedEquals(
            "Delays on the M4, heavy toward the city",
            "Delays on the M4. Heavy toward the city").ShouldBeFalse();
    }

    [Fact]
    public void Compute_Substitution_ProducesSingleHunk()
    {
        var hunks = ScriptDiff.Compute(
            "The M4 is experiencing delays approaching Parramatta",
            "The M4 is crawling into Parramatta");

        var hunk = hunks.ShouldHaveSingleItem();
        hunk.Before.ShouldBe("experiencing delays approaching");
        hunk.After.ShouldBe("crawling into");
    }

    [Fact]
    public void Compute_PureInsertion_HasEmptyBefore()
    {
        var hunks = ScriptDiff.Compute(
            "Delays on the M4",
            "Delays on the M4 near Penrith");

        var hunk = hunks.ShouldHaveSingleItem();
        hunk.Before.ShouldBe(string.Empty);
        hunk.After.ShouldBe("near Penrith");
    }

    [Fact]
    public void Compute_PureDeletion_HasEmptyAfter()
    {
        var hunks = ScriptDiff.Compute(
            "Heavy delays on the M4 right now",
            "Heavy delays on the M4");

        var hunk = hunks.ShouldHaveSingleItem();
        hunk.Before.ShouldBe("right now");
        hunk.After.ShouldBe(string.Empty);
    }

    [Fact]
    public void Compute_WhitespaceOnlyChange_ProducesNoHunks()
    {
        ScriptDiff.Compute(
            "Delays on   the M4",
            " Delays on the M4 ").ShouldBeEmpty();
    }

    [Fact]
    public void Compute_FullRewrite_ProducesHunks()
    {
        var hunks = ScriptDiff.Compute(
            "Roads are quiet across the network",
            "Watch for water over Beach Road");

        hunks.ShouldNotBeEmpty();
        string.Join(' ', hunks.Select(h => h.Before)).Trim().ShouldNotBeEmpty();
        string.Join(' ', hunks.Select(h => h.After)).Trim().ShouldNotBeEmpty();
    }

    [Fact]
    public void Render_FormatsHunksOnePerLine()
    {
        string rendered = ScriptDiff.Render([
            new ScriptDiffHunk("experiencing delays", "crawling"),
            new ScriptDiffHunk(string.Empty, "right now")
        ]);

        rendered.ShouldBe("\"experiencing delays\" -> \"crawling\"\n(nothing) -> \"right now\"");
    }
}
