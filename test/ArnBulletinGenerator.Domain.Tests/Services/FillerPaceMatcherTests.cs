using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class FillerPaceMatcherTests
{
    // The measured case this exists for — dev job 53c160ad (2026-08-10): the traffic read aired at
    // 4.75 syllables/second and the stitched filler clip at 6.41, a 35% jump mid-bulletin.
    private const double ReadSps = 4.75;
    private const double ClipSps = 6.41;

    [Fact]
    public void RetimedSeconds_ClipFasterThanTheRead_StretchesTowardTheReadsRate()
    {
        const double cached = 2.03;

        double retimed = FillerPaceMatcher.RetimedSeconds(cached, ClipSps, ReadSps);

        retimed.ShouldBeGreaterThan(cached);
        // 6.41/4.75 = x1.35, held at the clamp.
        (retimed / cached).ShouldBe(FillerPaceMatcher.MaxRetimeFactor, 0.001);
        // The point of the whole exercise: the aired gap shrinks. 35% -> 13%.
        double airedSps = ClipSps * cached / retimed;
        (airedSps / ReadSps - 1).ShouldBeLessThan(0.15);
    }

    [Fact]
    public void RetimedSeconds_MixingArticulationWithGrossDuration_WouldInvertTheCorrection()
    {
        // The bug this contract exists to prevent, caught live on dev job 026b6b1d (2026-08-10):
        // the clip was compressed 0.950x when it needed stretching ~1.17x.
        // It ARTICULATES at 5.08 syl/s against a read at 4.36, so it must be STRETCHED. But its
        // 6.76s stored duration also carries its own lead, trail and internal silence, so dividing
        // syllables by that gross figure reads as 4.14 syl/s — SLOWER than the read — and asks for
        // a compression instead. Rates must be compared like for like; the shipped call sites pair
        // articulation with articulation (stitch) and gross with gross (reservation).
        const double cached = 6.76, clipArticulation = 5.08, readArticulation = 4.36;
        double clipGross = FillerPaceMatcher.GrossRate(28, cached);

        FillerPaceMatcher.RetimedSeconds(cached, clipArticulation, readArticulation)
            .ShouldBeGreaterThan(cached);                       // correct: stretch
        FillerPaceMatcher.RetimedSeconds(cached, clipGross, readArticulation)
            .ShouldBeLessThan(cached);                          // the inversion, if units are mixed
    }

    [Fact]
    public void RetimedSeconds_ClipAlreadyInStepWithTheRead_IsLeftAlone()
    {
        // Four of the six measured jobs sat within 9% of the read. An exactly-matched clip must come
        // back untouched (bit-identical cached bytes downstream), not re-encoded for nothing.
        FillerPaceMatcher.RetimedSeconds(3.0, ReadSps, ReadSps).ShouldBe(3.0, 0.0001);
    }

    [Fact]
    public void RetimedSeconds_ClipSlowerThanTheRead_CompressesAndIsClampedToo()
    {
        // The mirror case: a long, slow cached clip against a heavily-compressed boosted read must
        // be sped up, and the same clamp applies so it can never be crushed into unintelligibility.
        double retimed = FillerPaceMatcher.RetimedSeconds(10.0, 3.0, ReadSps);

        retimed.ShouldBeLessThan(10.0);
        (10.0 / retimed).ShouldBe(FillerPaceMatcher.MaxRetimeFactor, 0.001);
    }

    [Theory]
    [InlineData(0, 6.41, 4.75)]    // clip has no duration
    [InlineData(3.0, 0, 4.75)]     // clip cached without word timings — rate unmeasurable
    [InlineData(3.0, 6.41, 0)]     // read rate unknown (no timings / no calibration)
    public void RetimedSeconds_DegenerateInput_FallsBackToTheCachedDuration(
        double cached, double clipRate, double readRate)
    {
        // Every one of these is reachable in production. None may throw or zero out a clip — they
        // degrade to stitch-as-is, which is exactly the behaviour that shipped before pace matching.
        FillerPaceMatcher.RetimedSeconds(cached, clipRate, readRate).ShouldBe(cached);
    }

    [Fact]
    public void GrossRate_CountsSilenceAsTime_AndIsSafeOnEmptyInput()
    {
        FillerPaceMatcher.GrossRate(28, 6.76).ShouldBe(4.14, 0.01);
        FillerPaceMatcher.GrossRate(0, 6.76).ShouldBe(0);
        FillerPaceMatcher.GrossRate(28, 0).ShouldBe(0);
    }

    [Fact]
    public void ArticulationRate_ExcludesInterWordSilence()
    {
        // A traffic read carries sentence pauses a three-second filler clip does not. Counting them
        // would understate the read's rate and buy a slow-down the listener never needed: these two
        // words span 2.0s wall-clock but only 1.0s of speech.
        var timings = new List<WordTiming>
        {
            new("Greensborough", 0.0, 0.5),
            new("Bypass", 1.5, 2.0)
        };

        // Greensborough 3 + Bypass 2 = 5 syllables over 1.0s of spoken time.
        FillerPaceMatcher.ArticulationRate(timings).ShouldBe(5.0, 0.5);
        // Gross over the same span would read half that — the number the stitch must NOT use.
        FillerPaceMatcher.ArticulationRate(timings).ShouldBeGreaterThan(
            FillerPaceMatcher.GrossRate(5, 2.0));
    }

    [Fact]
    public void ArticulationRate_NoTimings_IsZeroSoTheMatchIsSkipped()
    {
        FillerPaceMatcher.ArticulationRate([]).ShouldBe(0);
        FillerPaceMatcher.ArticulationRate(null!).ShouldBe(0);
    }

    [Fact]
    public void MatchingTheReadsCompressionFactorWouldMakeItWorse_NotBetter()
    {
        // Guards the design decision itself. The intuitive fix — apply the read's compression to the
        // clip so "everything gets the same treatment" — cancels out of the ratio and restores the
        // UNCOMPRESSED mismatch. Measured across the six aired jobs: +8% median becomes +28%.
        const double readCompression = 1.19;           // the boost's aim factor on this voice
        double airedGapNow = ClipSps / ReadSps;
        double airedGapIfSameFactor = (ClipSps * readCompression) / ReadSps;

        airedGapIfSameFactor.ShouldBeGreaterThan(airedGapNow);

        // Retiming against the compression is what actually closes it.
        const double cached = 2.03;
        double retimed = FillerPaceMatcher.RetimedSeconds(cached, ClipSps, ReadSps);
        double airedGapMatched = (ClipSps * cached / retimed) / ReadSps;
        airedGapMatched.ShouldBeLessThan(airedGapNow);
    }
}
