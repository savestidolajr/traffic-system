using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class SlotFitPlannerTests
{
    [Theory]
    [InlineData(75, 75, false)] // at target
    [InlineData(63, 75, false)] // at the accept floor (75 - max(5, round(75*0.16)) = 63)
    [InlineData(62, 75, true)]  // below the floor -> expand
    [InlineData(60, 75, true)]  // well short -> expand
    [InlineData(76, 90, false)] // ~84% of a 30s target fills the slot (verified live) -> no LLM padding
    [InlineData(75, 90, true)]  // below the 90-target floor of 76 -> expand
    [InlineData(90, 75, false)] // over target is NOT expanded (audio trims overshoot)
    public void NeedsExpansion_TriggersOnlyBelowScaledAcceptFloor(int words, int target, bool expected)
    {
        SlotFitPlanner.NeedsExpansion(words, target).ShouldBe(expected);
    }

    [Fact]
    public void ExpansionFeedback_StatesTargetRangeAndNoFabrication()
    {
        string feedback = SlotFitPlanner.ExpansionFeedback(75);

        feedback.ShouldContain("75 words");
        feedback.ShouldContain($"{75 - SlotFitPlanner.Tolerance} to {75 + SlotFitPlanner.Tolerance}");
        feedback.ShouldContain("never invent");
    }
}
