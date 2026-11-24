using ArnBulletinGenerator.Application.Broadcasting;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class BroadcastWorkBudgetTests
{
    [Fact]
    public void Start_UsesEarliestAbsoluteDeadlineWithSafetyMargin()
    {
        var started = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var slotCutoff = started.AddSeconds(200);
        var invocationDeadline = started.AddSeconds(180);

        using var budget = BroadcastWorkBudget.Start(started, slotCutoff, invocationDeadline);

        budget.DeadlineUtc.ShouldBe(invocationDeadline.AddSeconds(-30));
    }

    [Fact]
    public void Start_EnforcesTwoTotalGenerationAndSynthesisAttempts()
    {
        using var budget = BroadcastWorkBudget.Start(
            DateTimeOffset.UtcNow,
            settings: new BroadcastWorkBudgetOptions());

        budget.TryStartGenerationAttempt(out var generation1).ShouldBeTrue();
        budget.TryStartGenerationAttempt(out var generation2).ShouldBeTrue();
        budget.TryStartGenerationAttempt(out _).ShouldBeFalse();
        budget.GenerationAttemptsUsed.ShouldBe(2);
        generation1.ShouldBe(1);
        generation2.ShouldBe(2);

        budget.TryStartSynthesisAttempt(out var synthesis1).ShouldBeTrue();
        budget.TryStartSynthesisAttempt(out var synthesis2).ShouldBeTrue();
        budget.TryStartSynthesisAttempt(out _).ShouldBeFalse();
        budget.SynthesisAttemptsUsed.ShouldBe(2);
        synthesis1.ShouldBe(1);
        synthesis2.ShouldBe(2);
    }

}
