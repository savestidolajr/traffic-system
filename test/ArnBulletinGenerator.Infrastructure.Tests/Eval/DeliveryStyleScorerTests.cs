using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>Deterministic gates for the delivery director: words immutable, only allowed tags.</summary>
public sealed class DeliveryStyleScorerTests
{
    private const string Plain =
        "Melbourne's Ring Road eastbound is the worst of it, with roadworks and a lane blocked. " +
        "Plenty Road northbound has construction at the ramp to the M80.";

    private static DeliveryStylePolicy Policy(params string[] tags) =>
        new("Urgent drive-time read.", tags, 2);

    [Fact]
    public void WellFormedTagging_Passes()
    {
        var tagged =
            "[urgent] Melbourne's Ring Road eastbound is the WORST of it, with roadworks and a lane blocked. " +
            "Plenty Road northbound has construction at the ramp to the M80.";

        var result = DeliveryStyleScorer.Evaluate(Plain, tagged, Policy("[urgent]", "[serious]"));

        result.Passed.ShouldBeTrue(string.Join(" | ", result.Violations));
        result.TagCount.ShouldBe(1);
    }

    [Fact]
    public void ChangedWord_Fails()
    {
        // "worst" -> "WORSTEST" is a word change, not emphasis.
        var tagged =
            "[urgent] Melbourne's Ring Road eastbound is the WORSTEST of it, with roadworks and a lane blocked. " +
            "Plenty Road northbound has construction at the ramp to the M80.";

        var result = DeliveryStyleScorer.Evaluate(Plain, tagged, Policy("[urgent]"));

        result.Passed.ShouldBeFalse();
        result.Violations.ShouldContain(v => v.Contains("words changed"));
    }

    [Fact]
    public void DroppedWord_Fails()
    {
        var tagged =
            "[urgent] Melbourne's Ring Road eastbound is the worst, with roadworks and a lane blocked. " +
            "Plenty Road northbound has construction at the ramp to the M80.";

        var result = DeliveryStyleScorer.Evaluate(Plain, tagged, Policy("[urgent]"));

        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public void InventedTag_Fails()
    {
        var tagged =
            "[shouting] Melbourne's Ring Road eastbound is the worst of it, with roadworks and a lane blocked. " +
            "Plenty Road northbound has construction at the ramp to the M80.";

        var result = DeliveryStyleScorer.Evaluate(Plain, tagged, Policy("[urgent]", "[serious]"));

        result.Passed.ShouldBeFalse();
        result.Violations.ShouldContain(v => v.Contains("[shouting]"));
    }

    [Fact]
    public void CapsAndEllipsesOnly_Passes()
    {
        var tagged =
            "Melbourne's Ring Road eastbound is the WORST of it… with roadworks and a lane blocked. " +
            "Plenty Road northbound has construction at the ramp to the M80.";

        var result = DeliveryStyleScorer.Evaluate(Plain, tagged, Policy("[urgent]"));

        result.Passed.ShouldBeTrue(string.Join(" | ", result.Violations));
        result.TagCount.ShouldBe(0);
    }
}
