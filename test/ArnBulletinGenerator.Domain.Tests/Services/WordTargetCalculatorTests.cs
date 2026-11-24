using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class WordTargetCalculatorTests
{
    [Fact]
    public void FullMinute_At150Wpm_Returns150()
    {
        WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 60).ShouldBe(150);
    }

    [Fact]
    public void HalfMinute_At150Wpm_Returns75()
    {
        WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30).ShouldBe(75);
    }

    [Theory]
    [InlineData(30.0, 1.15, 32.25)]
    [InlineData(30.0, 1.0, 30.0)]
    [InlineData(30.0, 0.9, 30.0)]
    public void MidpointAimSeconds_ClampsSpeedUpBelowOne(double bodyTargetSeconds,
        double maxSpeedUpFactor, double expected)
    {
        WordTargetCalculator.MidpointAimSeconds(bodyTargetSeconds, maxSpeedUpFactor)
            .ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void FloorWords_AccountsForSilencePadAndSlowDown()
    {
        WordTargetCalculator.FloorWords(30.0, 0.3, 1.0, 0.4).ShouldBe(75);
    }

    [Fact]
    public void FloorWords_WithNoMeasuredPace_ReturnsZero()
    {
        WordTargetCalculator.FloorWords(30.0, 0.3, 1.0, 0).ShouldBe(0);
    }

    [Fact]
    public void SpeedMultiplier_DoublesTarget()
    {
        WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 2.0).ShouldBe(150);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(150, 0)]
    [InlineData(150, -5)]
    [InlineData(0, 0)]
    [InlineData(-1, 60)]
    public void InvalidInputs_ReturnMinimum(double wpm, double duration)
    {
        WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(wpm, duration).ShouldBe(10);
    }

    [Fact]
    public void NegativeSpeed_TreatedAsDefault()
    {
        // speed <= 0 should use 1.0 as fallback
        int result = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 60, -1.0);
        result.ShouldBe(150);
    }

    [Fact]
    public void VerySmallValues_ClampedToMinimum()
    {
        WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(1, 1).ShouldBe(10);
    }

    [Fact]
    public void PaceBoostWpm_Positive_IncreasesTarget()
    {
        // 150 WPM + 20 boost = 170 effective → 30s read: round(30 * 170 / 60) = 85.
        var withoutBoost = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.0, 0);
        var withBoost = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.0, 20);
        withBoost.ShouldBe(85);
        withBoost.ShouldBeGreaterThan(withoutBoost);
    }

    [Fact]
    public void PaceBoostWpm_Negative_ClampedToZero_NoOp()
    {
        // Negative boost is clamped to 0 via Math.Max — same result as no boost.
        var baseline = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.0, 0);
        var negativeBoost = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.0, -10);
        negativeBoost.ShouldBe(baseline);
    }

    [Fact]
    public void PaceBoostWpm_CombinedWithSpeed_Multiplies()
    {
        // Effective WPM = 150 + 30 = 180; 30s × 180 / 60 = 90; × speed 1.5 = 135.
        WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.5, 30).ShouldBe(135);
    }

    [Fact]
    public void PaceBoostedTarget_SitsJustInsideTheCeiling_WithMoreThanRenderVarianceToSpare()
    {
        // The regression this guards: a W-word script read at `wpm` takes W/wpm×60 seconds naturally,
        // and the pipeline can only compress by ComputeEffectiveMaxSpeedUpFactor. Job b6e03354's exact
        // numbers: with overshoot stacked the aim was 48.5s against a 38.0s ceiling; unstacked but
        // ceiling-aimed it was 38.10s vs 38.02s (integer rounding alone put it out of reach). The aim
        // must clear the ceiling by more than the ±2.3s render-to-render variance measured on
        // eleven_v3, and stay above the slow-fill floor.
        const double wpm = 149.6, boost = 40, slot = 30.0;
        double speedUp = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, boost, 1.15, 1.35);
        double slowDown = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(wpm, boost, 1.0, 1.35);

        int target = WordTargetCalculator.ComputeBoostedWordTarget(wpm, slot, 1.0, speedUp, slowDown);
        double naturalSeconds = target / wpm * 60.0;

        target.ShouldBe(87);
        naturalSeconds.ShouldBeLessThanOrEqualTo(slot * speedUp - 2.3);
        naturalSeconds.ShouldBeGreaterThanOrEqualTo(slot / slowDown + 2.3);
    }

    [Fact]
    public void PaceBoostedTarget_ActuallyDeliversMostOfTheRequestedBoost()
    {
        // Behaviour contract, not plumbing. Aiming at the band MIDPOINT passed every reachability
        // check and still broke the feature: on dev it delivered 36% / -3% / 6% of a PaceBoostWpm=40
        // ask (jobs bfcc2f61 / 992209ef / f08e3fb4), and 992209ef aired SLOWER than natural pace.
        // The aim must realise at least ~60% of the requested boost.
        const double wpm = 137.8, boost = 40, slot = 30.0;
        double speedUp = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, boost, 1.15, 1.35);
        double slowDown = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(wpm, boost, 1.0, 1.35);

        int target = WordTargetCalculator.ComputeBoostedWordTarget(wpm, slot, 1.0, speedUp, slowDown);
        double compression = target / wpm * 60.0 / slot;   // natural read / slot = compression applied
        double deliveredBoostWpm = (compression - 1.0) * wpm;

        target.ShouldBe(82);
        compression.ShouldBeGreaterThan(1.0);              // never SLOWER than natural on a boosted voice
        (deliveredBoostWpm / boost).ShouldBeGreaterThan(0.60);
    }

    [Fact]
    public void ComputeBoostedWordTarget_HoldsBackEightPercentFromTheCeiling()
    {
        // 150 wpm, 30s slot, 1.20x speed-up → ceiling 36.0s; aim = 36.0 × 0.92 = 33.12s →
        // round(33.12 × 150/60) = 83 words.
        WordTargetCalculator.ComputeBoostedWordTarget(150, 30, 1.0, 1.20, 1.10).ShouldBe(83);
    }

    [Fact]
    public void ComputeBoostedWordTarget_NeverAimsBelowTheSlowFillFloor()
    {
        // Degenerate config: no speed-up headroom and no slow-fill headroom, so ceiling == floor ==
        // the slot. The 8% margin would aim at 27.6s, which the pipeline could not stretch back to
        // 30s — clamp to the floor instead. round(30 × 150/60) = 75 words.
        WordTargetCalculator.ComputeBoostedWordTarget(150, 30, 1.0, 1.0, 1.0).ShouldBe(75);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ComputeBoostedWordTarget_InvalidSlot_ReturnsMinimum(double slot)
    {
        WordTargetCalculator.ComputeBoostedWordTarget(150, slot, 1.0, 1.20, 1.10).ShouldBe(10);
    }

    [Fact]
    public void ComputeEffectiveMaxSlowDownFactor_NoBoost_ReturnsConfiguredValue()
    {
        WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(150, 0, 1.0, 1.35).ShouldBe(1.0);
        WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(150, -10, 1.0, 1.35).ShouldBe(1.0);
    }

    [Fact]
    public void ComputeEffectiveMaxSlowDownFactor_GrantsHalfTheBoostWidening()
    {
        // Boost needing 1.20x speed-up (150 → 180 wpm) earns 1.10x slow-down.
        WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(150, 30, 1.0, 1.35)
            .ShouldBe(1.10, 1e-9);
    }

    [Fact]
    public void ComputeEffectiveMaxSlowDownFactor_NeverBelowConfigured()
    {
        // A tiny boost must not shrink an operator's already-generous configured allowance.
        WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(150, 3, 1.10, 1.35).ShouldBe(1.10);
    }

    [Fact]
    public void ComputeEffectiveMaxSlowDownFactor_CappedAtItsOwnAbsoluteCeiling()
    {
        // An extreme boost cannot license an arbitrarily draggy read. The cap is the slow-down
        // ceiling ITSELF, not half the speed-up ceiling — see the decoupling test below.
        WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(100, 200, 1.0, 1.15)
            .ShouldBe(1.15, 1e-9);
    }

    [Fact]
    public void SlowDownCeiling_IsIndependentOfTheSpeedUpCeiling()
    {
        // The regression this prevents: the slow-down cap used to be derived as half the speed-up
        // ceiling, so raising the speed-up ceiling from 1.35 to 1.60 to let a +60 boost through
        // would have silently licensed stretching a boosted UNDERRUN from 1.175x to 1.30x — a
        // draggy read shipped as a side effect of chasing a faster one. The two ends must not move
        // together: the same slow-down ceiling gives the same answer at either speed-up ceiling.
        const double wpm = 135.8, boost = 60, slowCeiling = 1.15;
        double atOldCeiling = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(
            wpm, boost, 1.0, slowCeiling);
        double atNewCeiling = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(
            wpm, boost, 1.0, slowCeiling);

        atOldCeiling.ShouldBe(atNewCeiling);
        atNewCeiling.ShouldBe(slowCeiling, 1e-9);   // earned 1.221 clipped to the ceiling

        // …while the SPEED-UP side does move, which is the whole point of raising it.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, boost, 1.15, 1.60)
            .ShouldBeGreaterThan(
                WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, boost, 1.15, 1.35));
    }

    [Fact]
    public void PaceBoostOfSixty_IsFullyRealisableAtTheShippedCeiling()
    {
        // The operator-facing cap (VoiceProfile.MaxPaceBoostWpm) must be the ONLY cap an operator
        // can hit. Two caps in series is what produced the earlier bug where +60 quietly read
        // as +48. At the shipped 1.60 ceiling a +60 boost is realised in full on any voice at or
        // above 100 wpm — the slowest a calibrated voice plausibly gets.
        foreach (double wpm in new[] { 100.0, 120.0, 135.8, 149.3, 164.1 })
        {
            double required = (wpm + 60) / wpm;
            double effective = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, 60, 1.15, 1.60);
            effective.ShouldBe(required, 1e-9,
                $"a +60 boost must not be truncated at {wpm} wpm");
        }
    }

    [Fact]
    public void PaceBoostWpm_Zero_IsNoOp()
    {
        var withoutBoostParam = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.0);
        var explicitZero = WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(150, 30, 1.0, 0);
        withoutBoostParam.ShouldBe(explicitZero);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_NoBoost_ReturnsConfiguredValue()
    {
        // paceBoostWpm <= 0 → passthrough, no computation.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(150, 0, 1.25, 1.35).ShouldBe(1.25);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_NegativeBoost_ReturnsConfiguredValue()
    {
        // Negative boost treated as ≤ 0 → passthrough.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(150, -5, 1.25, 1.35).ShouldBe(1.25);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_ZeroWpm_ReturnsConfiguredValue()
    {
        // wordsPerMinute <= 0 → passthrough.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(0, 20, 1.25, 1.35).ShouldBe(1.25);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_BoostWithinConfiguredCap_NoWidening()
    {
        // Required factor = (150+10)/150 = 1.0667, configured = 1.25 → max(1.25, 1.0667) = 1.25, clamp = 1.25.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(150, 10, 1.25, 1.35).ShouldBe(1.25);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_BoostRequiringWidening_UnderCeiling()
    {
        // Required factor = (150+40)/150 = 1.2667, configured = 1.25 → max = 1.2667, clamp = min(1.2667, 1.35) = 1.2667.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(150, 40, 1.25, 1.35).ShouldBe(1.2667, 0.001);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_BoostExceedingCeiling_ClampedToCeiling()
    {
        // Required factor = (100+50)/100 = 1.50, configured = 1.25 → widened 1.50, clamp = min(1.50, 1.35) = 1.35.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(100, 50, 1.25, 1.35).ShouldBe(1.35);
    }

    [Fact]
    public void ComputeEffectiveMaxSpeedUpFactor_AbsoluteCeilingBelowConfigured_NeverNarrows()
    {
        // ceiling (1.20) < configured (1.25) → max(absoluteCeiling, configured) = 1.25 → widened stays 1.25.
        // Key invariant: the effective factor is NEVER less than what the operator configured.
        WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(150, 40, 1.25, 1.20).ShouldBe(1.25);
    }

    // ── Fitter ceiling: variance headroom that does NOT move the aim ──

    [Fact]
    public void ComputeFitterMaxSpeedUpFactor_NoBoost_IsIndistinguishableFromTheConfiguredCap()
    {
        // Unboosted voices must be untouched by the boost's variance allowance.
        WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(150, 0, 1.15, 1.35).ShouldBe(1.15);
        WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(0, 40, 1.15, 1.35).ShouldBe(1.15);
    }

    [Fact]
    public void ComputeFitterMaxSpeedUpFactor_Boosted_IsWiderThanTheAim_ButNeverPastTheAbsoluteCeiling()
    {
        double aim = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(150, 40, 1.15, 1.35);
        double fitter = WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(150, 40, 1.15, 1.35);

        fitter.ShouldBeGreaterThan(aim);                 // the whole point: room for render variance
        fitter.ShouldBeLessThanOrEqualTo(1.35);          // intelligibility guard still binds

        // A boost big enough to saturate the aim leaves the fitter pinned at the same ceiling —
        // the margin can never buy compression past what an ARN listening check has cleared.
        WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(100, 50, 1.15, 1.35).ShouldBe(1.35);
    }

    [Fact]
    public void FitterHeadroom_DoesNotGrowTheWordTarget()
    {
        // The regression that would silently undo the fix: sizing the script off the FITTER ceiling
        // instead of the aim ceiling over-delivers the boost (more words than the operator asked
        // for) and re-opens the overrun problem one notch higher. The target is the aim's alone.
        const double wpm = 135.8, boost = 40, slot = 22.0;
        double aim = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, boost, 1.15, 1.35);
        double fitter = WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(wpm, boost, 1.15, 1.35);
        double slowDown = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(wpm, boost, 1.0, 1.35);

        WordTargetCalculator.ComputeBoostedWordTarget(wpm, slot, 1.0, fitter, slowDown)
            .ShouldBeGreaterThan(WordTargetCalculator.ComputeBoostedWordTarget(wpm, slot, 1.0, aim, slowDown));
        WordTargetCalculator.ComputeBoostedWordTarget(wpm, slot, 1.0, aim, slowDown).ShouldBe(59);
    }

    [Fact]
    public void FitterHeadroom_AbsorbsTheMeasuredElevenV3RenderSpread()
    {
        // Dev job a1d40b4c (2026-08-10), the case this exists for: voice EF2rFRNHqbfaMctIvS2k
        // calibrates at 135.8 wpm, PaceBoostWpm=40, 22.0s slot. 40 real eleven_v3 renders of the
        // resulting 59-word target spread 25.20-29.20s of natural read. The aim ceiling alone
        // (22 x 1.2946 = 28.48s) re-rendered the top of that spread; the fitter ceiling must not.
        const double wpm = 135.8, boost = 40, slot = 22.0;
        const double worstMeasuredRender = 29.20;

        double aimCap = slot * WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(wpm, boost, 1.15, 1.35);
        double fitterCap = slot * WordTargetCalculator.ComputeFitterMaxSpeedUpFactor(wpm, boost, 1.15, 1.35);

        aimCap.ShouldBeLessThan(worstMeasuredRender);            // the bug
        fitterCap.ShouldBeGreaterThan(worstMeasuredRender);      // the fix
    }

    // ── Where the boost stops buying anything ──

    [Fact]
    public void SaturatingPaceBoostWpm_IsWhereTheEffectiveFactorStopsGrowing()
    {
        // Behaviour contract for the operator-facing cap. Below the saturating boost each extra wpm
        // widens the ceiling; at and above it the clamp takes over and the request is a no-op.
        const double wpm = 135.8, ceiling = 1.35;
        double saturating = WordTargetCalculator.SaturatingPaceBoostWpm(wpm, ceiling);

        saturating.ShouldBe(47.5, 0.1);

        double justUnder = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(
            wpm, saturating - 5, 1.15, ceiling);
        double atSaturation = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(
            wpm, saturating, 1.15, ceiling);
        double wellPast = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(
            wpm, saturating + 40, 1.15, ceiling);

        justUnder.ShouldBeLessThan(atSaturation);   // still buying headroom
        atSaturation.ShouldBe(ceiling, 0.001);      // exactly at the cap
        wellPast.ShouldBe(atSaturation, 0.001);     // and past it, nothing more
    }

    [Fact]
    public void SaturatingPaceBoostWpm_ScalesWithTheVoice_SoOneFlatCapCannotFitEveryVoice()
    {
        // The ceiling is a RATIO, so the absolute boost it buys tracks the voice's own rate. This is
        // why the operator-facing MaxPaceBoostWpm is set past the FASTEST calibrated voice's
        // saturation point rather than at some voice's exact figure: a single flat cap either
        // blocks a fast voice (the old 40 blocked every one of them) or over-promises on a slow one,
        // and the second failure is the one the job log warns about.
        double slow = WordTargetCalculator.SaturatingPaceBoostWpm(135.8, 1.35);
        double fast = WordTargetCalculator.SaturatingPaceBoostWpm(164.1, 1.35);

        fast.ShouldBeGreaterThan(slow);
        slow.ShouldBeGreaterThan(40);                                   // the old cap bound first
        fast.ShouldBeLessThanOrEqualTo(
            ArnBulletinGenerator.Domain.Models.VoiceProfile.MaxPaceBoostWpm); // the new one does not
    }

    [Fact]
    public void SaturatingPaceBoostWpm_DegenerateInput_IsZero()
    {
        WordTargetCalculator.SaturatingPaceBoostWpm(0, 1.35).ShouldBe(0);
        WordTargetCalculator.SaturatingPaceBoostWpm(150, 1.0).ShouldBe(0);
    }

}
