using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class DurationControllerTests
{
    [Fact]
    public void CorrectableWindow_IsTargetOverSlowDown_ToTargetTimesSpeedUp()
    {
        var (lower, upper) = DurationController.CorrectableWindow(30.0, 1.10, 1.15);
        lower.ShouldBe(30.0 / 1.10, 0.001);
        upper.ShouldBe(30.0 * 1.15, 0.001);
    }

    [Fact]
    public void Plan_ConfidentUnderrun_CanExtend_NoFiller_SkipsToExtend()
    {
        // p90 (upper) 25.5s < window lower 27.27s → even the optimistic read underruns the slot.
        var plan = DurationController.Plan(
            predictedLowerSeconds: 23.0, predictedUpperSeconds: 25.5, targetSeconds: 30.0,
            maxSlowDownFactor: 1.10, maxSpeedUpFactor: 1.15, hasFiller: false, canExtend: true);
        plan.ShouldBe(FitPlan.SkipUnderrunExtend);
    }

    [Fact]
    public void Plan_UpperInsideCorrectableWindow_Synthesizes()
    {
        // p90 28.5s is inside [27.27, 34.5] — stretch can pull it to slot, so spend the synthesis.
        var plan = DurationController.Plan(26.0, 28.5, 30.0, 1.10, 1.15, hasFiller: false, canExtend: true);
        plan.ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_HasFiller_AlwaysSynthesizes()
    {
        // Filler can bridge a short read, so never skip when filler is available — even a low p90.
        var plan = DurationController.Plan(20.0, 24.0, 30.0, 1.10, 1.15, hasFiller: true, canExtend: true);
        plan.ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_NoExtendRemaining_AlwaysSynthesizes()
    {
        var plan = DurationController.Plan(20.0, 24.0, 30.0, 1.10, 1.15, hasFiller: false, canExtend: false);
        plan.ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_NoPrediction_Synthesizes()
    {
        // Zero/absent prediction (no calibrated data) must not skip — synthesize and measure.
        DurationController.Plan(0, 0, 30.0, 1.10, 1.15, false, true).ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_ConfidentOverrun_CanTrim_NoFiller_TrimsBeforeSynthesis()
    {
        // p10 (lower) 40.0s > window upper 34.5s → even the SHORTEST plausible read is past what
        // compression can absorb. This is the direction a pace-boosted voice fails in, and before the
        // overrun branch existed CorrectableWindow's upper bound was computed and thrown away, so it
        // cost a full eleven_v3 render (13-16s) to discover.
        var plan = DurationController.Plan(
            predictedLowerSeconds: 40.0, predictedUpperSeconds: 46.0, targetSeconds: 30.0,
            maxSlowDownFactor: 1.10, maxSpeedUpFactor: 1.15, hasFiller: false, canExtend: true,
            canTrim: true);
        plan.ShouldBe(FitPlan.TrimBeforeSynthesis);
    }

    [Fact]
    public void Plan_OverrunButTrimNotOffered_Synthesizes()
    {
        // Default canTrim:false preserves the pre-existing two-outcome behaviour for every caller
        // that has not opted in.
        DurationController.Plan(40.0, 46.0, 30.0, 1.10, 1.15, hasFiller: false, canExtend: true)
            .ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_OverrunWithFiller_Synthesizes()
    {
        // Filler pre-sizing has already reduced the synth target, so the window here is not the one
        // the read will actually be judged against — leave it to the measured path.
        DurationController.Plan(40.0, 46.0, 30.0, 1.10, 1.15, hasFiller: true, canExtend: true, canTrim: true)
            .ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_OverrunOnlyAtTheOptimisticEnd_Synthesizes()
    {
        // p10 33.0s is still inside [27.27, 34.5]: the interval STRADDLES the ceiling, so a trim
        // might discard content the compression could have absorbed. Strictly conservative — only
        // trim when the whole interval is outside.
        DurationController.Plan(33.0, 40.0, 30.0, 1.10, 1.15, hasFiller: false, canExtend: true, canTrim: true)
            .ShouldBe(FitPlan.Synthesize);
    }

    [Fact]
    public void Plan_PaceBoostedCeiling_WidensTheOverrunThreshold()
    {
        // A boosted voice earns a 1.29x compression budget, so a 36s read that would trip the trim at
        // the unboosted 1.15x ceiling (34.5s) is fine at the boosted one (38.7s).
        DurationController.Plan(36.0, 37.0, 30.0, 1.145, 1.15, false, true, canTrim: true)
            .ShouldBe(FitPlan.TrimBeforeSynthesis);
        DurationController.Plan(36.0, 37.0, 30.0, 1.145, 1.29, false, true, canTrim: true)
            .ShouldBe(FitPlan.Synthesize);
    }
}
