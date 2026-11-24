using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class BulletinEditingBandTests
{
    [Fact]
    public void ThirtySecondSlot_At165Wpm_ProducesExpectedBand()
    {
        var band = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15);
        // 30s @ 165 wpm ≈ 82-word read (banker's rounding of 82.5). The ends are derived from the
        // SLOT, not from the already-rounded target, so they no longer compound its rounding error:
        // floor = 30/1.10 × 2.75 = 75.0 → 75; ceiling = 30 × 1.15 × 2.75 = 94.875 → 95 (was 94 when
        // computed as round(82 × 1.15)).
        band.TargetWordCount.ShouldBe(82);
        band.MinWordCount.ShouldBe(75);
        band.MaxWordCount.ShouldBe(95);
    }

    [Fact]
    public void SixtySecondSlot_At165Wpm_FloorsAndCeilsCleanly()
    {
        var band = BulletinEditingBand.Compute(165, 60, 1.0, 1.10, 1.15);
        // target = round(60*165/60) = 165; min = round(165/1.10) = 150; max = round(165*1.15) = 190.
        band.TargetWordCount.ShouldBe(165);
        band.MinWordCount.ShouldBe(150);
        band.MaxWordCount.ShouldBe(190);
    }

    [Fact]
    public void Band_IsOrdered_MinLeTargetLeMax()
    {
        var band = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15);
        band.MinWordCount.ShouldBeLessThanOrEqualTo(band.TargetWordCount);
        band.TargetWordCount.ShouldBeLessThanOrEqualTo(band.MaxWordCount);
    }

    [Fact]
    public void Speed_ScalesTargetUp()
    {
        // WordTargetCalculator applies speed, so a faster read packs more words into the slot.
        var normal = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15);
        var fast = BulletinEditingBand.Compute(165, 30, 2.0, 1.10, 1.15);
        fast.TargetWordCount.ShouldBeGreaterThan(normal.TargetWordCount);
    }

    [Fact]
    public void DegenerateFactors_ClampMaxToAtLeastTarget_AndMinToAtLeastOne()
    {
        // Factors ≤ 0 fall back to 1.0; max must never drop below target, min never below 1.
        var band = BulletinEditingBand.Compute(150, 4, 1.0, 0, 0);
        band.MaxWordCount.ShouldBeGreaterThanOrEqualTo(band.TargetWordCount);
        band.MinWordCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ZeroWpm_FallsToCalculatorFloor_NotADefaultWpm()
    {
        // The band uses whatever wpm it is handed; the WPM-0 → default-WPM fallback is
        // the caller's (trigger's) concern. wpm 0 → WordTargetCalculator minimum (10).
        var band = BulletinEditingBand.Compute(0, 30, 1.0, 1.10, 1.15);
        band.TargetWordCount.ShouldBe(10);
    }
    [Fact]
    public void PaceBoostWpm_GrowsTarget_AndWidensCeilingOnlyWhenItExceedsConfiguredHeadroom()
    {
        var baseline = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15, paceBoostWpm: 0);
        var smallBoost = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15, paceBoostWpm: 20);
        var bigBoost = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15, paceBoostWpm: 60);

        // Boosted target sits just inside the CEILING, not at the band midpoint and not on the slot:
        // speedUp = max(1.15, 185/165 = 1.121) = 1.15 → ceiling 34.5s; aim = 34.5 × 0.92 = 31.74s
        // (above the 27.27s floor) → round(31.74 × 2.75) = 87 words.
        smallBoost.TargetWordCount.ShouldBe(87);
        smallBoost.TargetWordCount.ShouldBeGreaterThan(baseline.TargetWordCount);

        // A boost SMALLER than the configured speed-up headroom cannot widen the ceiling —
        // ComputeEffectiveMaxSpeedUpFactor returns the configured 1.15 either way. The old
        // target-relative ceiling only appeared to widen because the target had grown.
        smallBoost.MaxWordCount.ShouldBe(baseline.MaxWordCount);
        // A boost that genuinely needs more headroom (225/165 = 1.364 → capped 1.35) does widen it.
        bigBoost.MaxWordCount.ShouldBeGreaterThan(baseline.MaxWordCount);
    }

    [Fact]
    public void PaceBoostWpm_KeepsBandOrdered()
    {
        var band = BulletinEditingBand.Compute(165, 30, 1.0, 1.10, 1.15, paceBoostWpm: 20);
        band.MinWordCount.ShouldBeLessThanOrEqualTo(band.TargetWordCount);
        band.TargetWordCount.ShouldBeLessThanOrEqualTo(band.MaxWordCount);
    }

    [Fact]
    public void PaceBoostWpm_RespectsAbsoluteCeiling_AndDoesNotApplyTheBoostTwice()
    {
        // wpm 150 + boost 60 on a 30s slot, configured 1.10 slow / 1.15 speed, speed ceiling 1.35,
        // slow ceiling 1.15:
        //   speedUp  = min(210/150 = 1.40, 1.35)                    = 1.35  → ceiling 40.5s
        //   slowDown = max(1.10, min(1 + 0.40/2 = 1.20, 1.15))      = 1.15  → floor   26.09s
        //   aim      = 40.5 × 0.92 = 37.26s                                 → 93 words
        //   max      = 40.5s × 2.5 words/s                                  → 101 words
        // The previous ceiling was round(target × 1.35) = 142 — the boost applied ONCE in the target
        // and AGAIN in the factor. 142 words at 150 wpm is a 56.8s read; compressed by 1.35 that is
        // 42s, so the operator was being told a 30s slot would take more than twice its real capacity.
        var band = BulletinEditingBand.Compute(150, 30, 1.0, 1.10, 1.15, paceBoostWpm: 60,
            absolutePaceBoostSpeedUpCeiling: 1.35, absolutePaceBoostSlowDownCeiling: 1.15);

        band.TargetWordCount.ShouldBe(93);
        band.MaxWordCount.ShouldBe(101);
        band.MinWordCount.ShouldBe(65);
        (band.MaxWordCount / 150.0 * 60.0).ShouldBeLessThanOrEqualTo(30 * 1.35 + 0.5);
    }

}
