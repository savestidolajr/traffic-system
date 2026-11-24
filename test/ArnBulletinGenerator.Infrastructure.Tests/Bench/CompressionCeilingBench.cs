using ArnBulletinGenerator.Infrastructure.Audio;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Bench;

/// <summary>
///     Renders one real read at a sweep of pitch-preserving compression factors through the SAME
///     <see cref="WavDurationProcessor" /> WSOLA path the pipeline uses, and writes each take to
///     disk so intelligibility can be scored (transcribe and diff against the known script) and
///     listened to.
///     <para>
///         This exists to answer one recurring question with evidence instead of caution:
///         <see cref="Domain.Configuration.SpeechDurationOptions.AbsolutePaceBoostSpeedUpCeiling" />
///         is 1.35 and its own doc calls that "a conservative starting point; raise only after an
///         ARN listening check". Nothing technical binds there — the processor itself clamps at
///         2.0x — so the ceiling is a judgement about where a compressed read stops sounding like
///         a person. Mining 162 shipped bulletins (2026-08-10) found no transcript-match
///         degradation up to 1.336x, but only 5 samples above 1.25x, so the deployed corpus cannot
///         answer it. This bench manufactures the missing samples on demand.
///     </para>
///     <para>
///         Gated behind <c>ARN_BENCH_COMPRESSION=1</c>; a no-op in CI. Point
///         <c>ARN_BENCH_COMPRESSION_WAV</c> at a natural-rate render of the voice under test (16-bit
///         PCM WAV) and <c>ARN_BENCH_COMPRESSION_OUT</c> at an output directory. Deliberately takes
///         a pre-rendered file rather than calling ElevenLabs: the TTS render is the expensive,
///         non-deterministic part, and holding ONE take constant is what makes the factors
///         comparable.
///     </para>
/// </summary>
public sealed class CompressionCeilingBench(ITestOutputHelper output)
{
    /// <summary>
    ///     Swept factors. Spans the deployed aim ceiling (1.295 for a +40 boost on a 136 wpm voice),
    ///     today's absolute ceiling (1.35), the fitter's variance margin above it, and on up to the
    ///     processor's own hard clamp so the failure mode is observed rather than assumed.
    /// </summary>
    private static readonly double[] Factors = [1.00, 1.15, 1.30, 1.35, 1.45, 1.55, 1.70, 1.85, 2.00];

    [Fact]
    [Trait("Category", "Bench")]
    public void CompressionSweep_WritesTakesForIntelligibilityScoring()
    {
        if (Environment.GetEnvironmentVariable("ARN_BENCH_COMPRESSION") != "1")
        {
            output.WriteLine("Skipped: set ARN_BENCH_COMPRESSION=1 to run the compression-ceiling bench.");
            return;
        }

        string? source = Environment.GetEnvironmentVariable("ARN_BENCH_COMPRESSION_WAV");
        string outDir = Environment.GetEnvironmentVariable("ARN_BENCH_COMPRESSION_OUT")
                        ?? Path.Combine(Path.GetTempPath(), "arn-compression-bench");
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            output.WriteLine($"Skipped: ARN_BENCH_COMPRESSION_WAV must point at an existing WAV (got '{source}').");
            return;
        }

        Directory.CreateDirectory(outDir);
        byte[] original = File.ReadAllBytes(source);

        // The real pipeline object, so the sweep exercises the deployed WSOLA settings (SoundTouch,
        // speech-tuned sequence/seek/overlap) and not a stand-in resampler.
        var pipeline = SlotFitBench.BuildPipeline(maxSlowDown: 1.0, maxSpeedUp: 2.0, pad: 0.0);
        double naturalSeconds = pipeline.GetDuration(original);
        output.WriteLine($"source: {source}  ({naturalSeconds:F2}s natural)");
        output.WriteLine($"out:    {outDir}");
        output.WriteLine("");
        output.WriteLine($"{"factor",7} {"target",8} {"actual",8}  file");

        foreach (double factor in Factors)
        {
            double target = naturalSeconds / factor;
            byte[] take = factor <= 1.0 ? original : pipeline.RetimeClip(original, target);
            double actual = pipeline.GetDuration(take);
            string path = Path.Combine(outDir, $"x{factor:0.00}.wav");
            File.WriteAllBytes(path, take);
            output.WriteLine($"{factor,7:0.00} {target,8:F2} {actual,8:F2}  {Path.GetFileName(path)}");
        }

        output.WriteLine("");
        output.WriteLine("Score by transcribing each take and diffing against the known script;");
        output.WriteLine("the deployed pipeline already does this per bulletin (AudioPreReview).");
    }
}
