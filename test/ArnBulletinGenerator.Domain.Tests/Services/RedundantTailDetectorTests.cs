using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class RedundantTailDetectorTests
{
    [Fact]
    public void Detect_flags_clear_road_restatement_tail()
    {
        const string script =
            "The M1 and M2 are moving nicely. Walker Road is shut outbound. " +
            "Those routes are all looking good with no dramas.";
        RedundantTailDetector.Detect(script).HasRedundantTail.ShouldBeTrue();
    }

    [Fact]
    public void Detect_flags_filler_close()
    {
        const string script =
            "Walker Road is shut outbound. The M1 is moving well. No other incidents in the current update.";
        RedundantTailDetector.Detect(script).HasRedundantTail.ShouldBeTrue();
    }

    /// <summary>
    ///     Job fa754637 (Gold Coast, 30s, 0 corridors). The expand-refine padded a correct 30-word
    ///     all-clear draft out to 100 words and closed on this tail; the operator deleted the
    ///     sentence by hand. Two independent near-misses let it through: the back-reference pattern
    ///     required the determiner to sit directly against the noun ("those roads", not "those FOUR
    ///     roads"), and the filler-close pattern only matched "for the latest ... traffic" when
    ///     "traffic" came last, never "for the latest traffic picture".
    /// </summary>
    [Fact]
    public void Detect_flags_the_gold_coast_padding_tail()
    {
        const string script =
            "No major delays across the Gold Coast overnight — Gooding Drive and Markeri Street are " +
            "moving nicely, with a clear run along Burleigh Road. Benowa Road's looking good as well. " +
            "So, for the latest traffic picture, those four roads are all moving nicely, giving " +
            "drivers a clear run across the Gold Coast.";

        var result = RedundantTailDetector.Detect(script);

        result.HasRedundantTail.ShouldBeTrue();
        RedundantTailDetector.Repair(script)
            .ShouldEndWith("Benowa Road's looking good as well.");
    }

    [Theory]
    [InlineData("Those four roads are all moving nicely.")]
    [InlineData("All four routes are looking good.")]
    [InlineData("For the latest traffic picture, keep listening.")]
    public void Detect_flags_the_widened_tail_shapes(string tail) =>
        RedundantTailDetector.Detect($"Walker Road is shut outbound. {tail}")
            .HasRedundantTail.ShouldBeTrue();

    /// <summary>
    ///     The widened back-reference must stay a CLOSED set: a final sentence that names a real road
    ///     and states its own condition is the normal clear close, never a padding tail.
    /// </summary>
    [Theory]
    [InlineData("Walker Road is shut outbound. Four lanes on the M1 are moving nicely.")]
    [InlineData("Walker Road is shut outbound. The Gooding Drive run is moving nicely at Markeri Street.")]
    public void Detect_leaves_a_close_that_carries_a_road(string script) =>
        RedundantTailDetector.Detect(script).HasRedundantTail.ShouldBeFalse();

    [Fact]
    public void Detect_leaves_a_normal_clear_close_naming_new_roads()
    {
        const string script =
            "Walker Road is shut outbound. The M1 and M2 are moving nicely.";
        RedundantTailDetector.Detect(script).HasRedundantTail.ShouldBeFalse();
    }

    [Fact]
    public void Detect_ignores_single_sentence() =>
        RedundantTailDetector.Detect("The M1 is moving nicely.").HasRedundantTail.ShouldBeFalse();

    [Fact]
    public void Repair_drops_the_redundant_tail()
    {
        const string script =
            "Walker Road is shut outbound. The M1 is moving well. No other incidents in the current update.";
        string repaired = RedundantTailDetector.Repair(script);
        repaired.ShouldBe("Walker Road is shut outbound. The M1 is moving well.");
        RedundantTailDetector.Detect(repaired).HasRedundantTail.ShouldBeFalse();
    }
}
