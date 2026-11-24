using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.Tests.Bench;

/// <summary>
///     Regression guard for the slot-fit contract — runs unconditionally in CI (unlike the gated
///     <see cref="SlotFitBench" />). Locks the accuracy invariants of the real audio-composition
///     primitives so a future compositor change cannot silently regress them:
///     <list type="bullet">
///       <item>An overrun within the speed-up cap is compressed to the slot, never shipped long.</item>
///       <item>A read already at the slot stays at the slot (idempotent fine-tune).</item>
///       <item>A read in the achievable band, with a graded filler library, composes to the slot.</item>
///     </list>
///     These are the behaviours the "measure-then-place" work must preserve; if a change breaks one,
///     it breaks here in &lt;1s rather than only surfacing in the slow real-LLM replay.
/// </summary>
[Trait("Category", "Slow")]
public sealed class SlotFitInvariantTests
{
    private const double Target = SlotFitBench.Target;
    private const double Tol = 0.15;         // SpeechDurationOptions.EffectiveToleranceSeconds
    private static readonly SlotFitBench.FillerLibrary Lib = SlotFitBench.FillerLibrary.Build(SlotFitBench.Clip);

    // Deployed caps.
    private static AudioPipelineService Pipeline() =>
        SlotFitBench.BuildPipeline(maxSlowDown: 1.0, maxSpeedUp: 1.1, pad: 0.3);

    [Theory]
    [InlineData(30.0)]
    [InlineData(31.0)]
    [InlineData(32.0)]
    [InlineData(33.0)]
    public void OverrunWithinCap_IsCompressedToSlot_NeverShipsLong(double readSeconds)
    {
        var pipeline = Pipeline();
        byte[] final = pipeline.FineTuneToTargetDuration(SlotFitBench.Clip(readSeconds), Target);
        double dur = pipeline.GetDuration(final);
        Assert.True(dur <= Target + Tol,
            $"read {readSeconds}s composed to {dur:F3}s — must never ship longer than {Target + Tol:F2}s");
    }

    [Fact]
    public void ReadExactlyAtSlot_StaysAtSlot()
    {
        var pipeline = Pipeline();
        byte[] final = pipeline.FineTuneToTargetDuration(SlotFitBench.Clip(Target), Target);
        double dur = pipeline.GetDuration(final);
        Assert.True(Math.Abs(dur - Target) <= Tol, $"read at slot drifted to {dur:F3}s");
    }

    [Theory]
    [InlineData(24.0)]
    [InlineData(26.0)]
    [InlineData(27.2)] // the dead zone the current (no-slowdown) path drops
    [InlineData(27.5)]
    [InlineData(28.0)]
    [InlineData(29.0)]
    public void AchievableUnderrun_WithGradedFiller_ComposesToSlot(double readSeconds)
    {
        // measure-then-place caps (a modest slow-fill is permitted).
        var pipeline = SlotFitBench.BuildPipeline(maxSlowDown: 1.08, maxSpeedUp: 1.1, pad: 0.3);
        double residual = Target - readSeconds;

        // Greedy measured fill (mirrors the bench's candidate selector).
        var clips = new List<byte[]>();
        double remaining = residual;
        while (clips.Count < SlotFitBench.MaxFillerClips && remaining > 0.6)
        {
            var fit = Lib.Clips.Where(c => c.Seconds + SlotFitBench.FillerPause <= remaining + 0.4)
                               .OrderByDescending(c => c.Seconds).FirstOrDefault();
            if (fit is null) break;
            clips.Add(fit.Wav);
            remaining -= fit.Seconds + SlotFitBench.FillerPause;
        }

        byte[] stitched = pipeline.StitchAudioClips(
            new List<byte[]> { pipeline.TrimToSpokenDuration(SlotFitBench.Clip(readSeconds)) }
                .Concat(clips).ToList(),
            SlotFitBench.FillerPause);
        byte[] final = pipeline.FineTuneToTargetDuration(stitched, Target);

        Assert.True(pipeline.IsSlotFilled(final, Target),
            $"read {readSeconds}s + {clips.Count} filler clip(s) did not fill the slot " +
            $"(composed {pipeline.GetDuration(final):F3}s)");
    }

    /// <summary>Bed-outro closer: a trimmable per-station bed closes ANY underrun to the exact slot,
    /// leaving the traffic read untouched (no time-stretch of already-correct speech). Duration is
    /// exact by construction — this is the mechanism the shipping compositor relies on.</summary>
    [Theory]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(19.0)]
    [InlineData(24.0)]
    [InlineData(27.3)] // the dead zone the discrete-filler path drops — the bed closes it exactly
    [InlineData(29.7)]
    public void BedOutro_ClosesAnyUnderrunToExactSlot(double readSeconds)
    {
        var pipeline = Pipeline();
        byte[] read = SlotFitBench.Clip(readSeconds);
        double readDur = pipeline.GetDuration(read);
        byte[] bed = SlotFitBench.Clip(Target - readDur);
        byte[] composed = pipeline.StitchAudioClips(
            new List<byte[]> { pipeline.TrimToSpokenDuration(read), bed }, 0.0);
        double dur = pipeline.GetDuration(composed);
        Assert.True(Math.Abs(dur - Target) <= Tol,
            $"bed close of a {readSeconds}s read composed to {dur:F3}s, not {Target}s (additive stitch, no read stretch)");
    }

    /// <summary>Dynamic-slot policy: after measuring the honest read, air the standard slot the
    /// content justifies (nearest of 30/20/15) and close with the bed — so a thin read airs a
    /// SHORTER honest bulletin (bed fraction ≤20%) instead of being padded to 30s or dropped.</summary>
    [Theory]
    [InlineData(14.0, 15.0)]
    [InlineData(19.0, 20.0)]
    [InlineData(21.0, 20.0)]
    [InlineData(26.0, 30.0)]
    public void DynamicSlot_AirsJustifiedSlot_HonestlyAndExactly(double readSeconds, double expectedSlot)
    {
        var pipeline = Pipeline();
        double aimed = readSeconds >= 24 ? 30.0 : readSeconds >= 18 ? 20.0 : readSeconds >= 13 ? 15.0 : 0.0;
        Assert.Equal(expectedSlot, aimed);
        byte[] read = SlotFitBench.Clip(readSeconds);
        double readDur = pipeline.GetDuration(read);
        double residual = aimed - readDur;
        // BedClose mechanism: bed closes an underrun; a mild over-content read compresses (speed-up, not slow).
        byte[] composed = residual >= 0
            ? pipeline.StitchAudioClips(
                new List<byte[]> { pipeline.TrimToSpokenDuration(read), SlotFitBench.Clip(residual) }, 0.0)
            : pipeline.FineTuneToTargetDuration(read, aimed);
        double dur = pipeline.GetDuration(composed);
        Assert.True(Math.Abs(dur - aimed) <= Tol,
            $"dynamic slot {aimed}s for a {readSeconds}s read composed to {dur:F3}s");
        // An underrun must be an HONEST fill (≤20% bed); over-content is real speech compressed, so exempt.
        if (residual >= 0)
        {
            double bedFrac = residual / aimed;
            Assert.True(bedFrac <= 0.20 + 1e-6,
                $"{readSeconds}s read on a {aimed}s slot needs {bedFrac:P0} bed — not an honest fill (>20%)");
        }
    }

    /// <summary>The bed closer hits ANY standard slot exactly — the composition is slot-agnostic
    /// (15/20/30/60s), unlike discrete filler whose granularity varies per slot.</summary>
    [Theory]
    [InlineData(12.0, 15.0)]
    [InlineData(17.0, 20.0)]
    [InlineData(52.0, 60.0)]
    public void BedOutro_HitsAnyStandardSlotExactly(double readSeconds, double slot)
    {
        var pipeline = Pipeline();
        byte[] read = SlotFitBench.Clip(readSeconds);
        double readDur = pipeline.GetDuration(read);
        byte[] composed = pipeline.StitchAudioClips(
            new List<byte[]> { pipeline.TrimToSpokenDuration(read), SlotFitBench.Clip(slot - readDur) }, 0.0);
        double dur = pipeline.GetDuration(composed);
        Assert.True(Math.Abs(dur - slot) <= Tol,
            $"bed close to a {slot}s slot composed to {dur:F3}s");
    }

    /// <summary>The trimmed-bed closer fills the 27.2–27.5s DEAD ZONE (and thinner reads) at the
    /// DEPLOYED slow=1.0 — which the legacy slow-fill+pad path cannot (proven by the contrast
    /// below) — landing the slot exactly while leaving the spoken read byte-for-byte untouched (a
    /// WSOLA re-stretch would alter every sample). This is the real-pipeline proof of the mechanism.</summary>
    [Theory]
    [InlineData(27.3)]
    [InlineData(27.5)]
    [InlineData(24.0)]
    [InlineData(19.0)]
    public void BedCloser_FillsUnderrunAtNoSlowdown_LeavingReadUntouched(double readSeconds)
    {
        var bed = SlotFitBench.BuildPipeline(maxSlowDown: 1.0, maxSpeedUp: 1.1, pad: 0.3, useBedCloser: true);
        byte[] read = SlotFitBench.Clip(readSeconds);
        byte[] composed = bed.FineTuneToTargetDuration(read, Target);

        Assert.True(bed.IsSlotFilled(composed, Target),
            $"bed closer did not fill a {readSeconds}s read at slow=1.0 (composed {bed.GetDuration(composed):F3}s)");
        AssertReadPrefixPreserved(read, composed);
    }

    /// <summary>Contrast: at the deployed slow=1.0 the LEGACY slow-fill+pad closer cannot fill the
    /// 27.3s dead zone — the exact bug the bed closer removes.</summary>
    [Fact]
    public void Legacy_NoSlowdown_MissesDeadZone()
    {
        var legacy = Pipeline(); // slow=1.0, no bed closer
        byte[] composed = legacy.FineTuneToTargetDuration(SlotFitBench.Clip(27.3), Target);
        Assert.False(legacy.IsSlotFilled(composed, Target),
            "expected the legacy no-slowdown path to MISS the 27.3s dead zone (the bed closer is what fixes it)");
    }

    /// <summary>Asserts the read's PCM appears unchanged as the prefix of the composed audio (minus a
    /// small tail margin for trailing-silence trimming) — proving the read was appended-to, not
    /// time-stretched. A WSOLA stretch resamples throughout and would fail this immediately.</summary>
    private static void AssertReadPrefixPreserved(byte[] read, byte[] composed)
    {
        var ri = WavInfoParser.GetWavInfo(read)!;
        var ci = WavInfoParser.GetWavInfo(composed)!;
        int margin = ri.SampleRate * ri.Channels * ri.BitsPerSample / 8 / 4; // ~250ms
        int compare = Math.Min(ri.DataSize, ci.DataSize) - margin;
        Assert.True(compare > 0, "read too short to compare");
        for (int i = 0; i < compare; i++)
            Assert.True(composed[(int)ci.DataPosition + i] == read[(int)ri.DataPosition + i],
                $"read PCM diverged at byte {i} — the read was altered, not left untouched");
    }

    /// <summary>Dynamic-slot picks the smallest standard slot the measured read fits — so a thin read
    /// airs a shorter honest slot rather than being padded to the full slot with mostly bed.</summary>
    [Theory]
    [InlineData(26.0, 30.0)] // near-miss — honest at the full 30s
    [InlineData(19.0, 20.0)] // thin — down to 20s
    [InlineData(12.0, 15.0)] // thinner — down to 15s
    [InlineData(21.0, 20.0)] // mild over-20 — 20s (compresses within cap)
    [InlineData(31.0, 30.0)] // over the slot — keep 30s (compress downstream)
    public void DynamicSlot_PicksSmallestFittingStandardSlot(double read, double expected)
    {
        double chosen = DynamicSlotSelector.Select(read, 30.0, [15.0, 20.0, 30.0, 60.0]);
        Assert.Equal(expected, chosen);
    }

    /// <summary>A genuinely-thin read exceeds the 20% bed ceiling even at the smallest slot, so the
    /// caller falls back (airs the cart) rather than a mostly-bed bulletin — the honest boundary.</summary>
    [Fact]
    public void DynamicSlot_ThinRead_ExceedsBedCeiling_SoCallerFallsBack()
    {
        double chosen = DynamicSlotSelector.Select(8.6, 30.0, [15.0, 20.0, 30.0, 60.0]);
        double bedFraction = (chosen - 8.6) / chosen;
        Assert.True(bedFraction > 0.20,
            $"a thin 8.6s read must exceed the 20% bed ceiling even at the smallest slot (got {bedFraction:P0} at {chosen}s)");
    }
}
