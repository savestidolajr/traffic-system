using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Tests.Replay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Bench;

/// <summary>
///     FAST, deterministic, LLM-free slot-fill bench. Drives the REAL audio-composition primitives
///     (<see cref="AudioPipelineService" /> FineTune/Stitch/IsSlotFilled + <see cref="WavDurationProcessor" />
///     + the real <see cref="FillerStatementSelector" />) across a read-duration distribution — the
///     EMPIRICAL one recovered from the fallback corpus plus a dense synthetic sweep — under pluggable
///     fill strategies, and scores how accurately each hits the 30s slot.
///     <para>
///     This is the fast inner loop for iterating the "measure-then-place" compositor: it runs in
///     milliseconds (no Azure OpenAI, no ElevenLabs), so an algorithm change is measured instantly,
///     while the real-LLM <see cref="FallbackReplayTests" /> remains the slow end-to-end integration gate.
///     </para>
///     <para>Gated behind <c>ARN_BENCH_COMPOSITOR=1</c>; a no-op in CI. The companion
///     <see cref="SlotFitInvariantTests" /> runs unconditionally as the regression guard.</para>
/// </summary>
public sealed class SlotFitBench(ITestOutputHelper output)
{
    internal const double Target = 30.0;
    internal const double FillerPause = 0.35; // matches BroadcastOrchestrator.FillerPauseSeconds
    internal const double Wps = 2.85;         // representative calibrated words/sec (measured on
                                              // ElevenLabs "Peter Michael - Australian Male VO")
    internal const int MaxFillerClips = 3;    // matches the orchestrator's rescue cap

    [Fact]
    [Trait("Category", "Bench")]
    public void SlotFit_Sweep_ScoresStrategies()
    {
        if (Environment.GetEnvironmentVariable("ARN_BENCH_COMPOSITOR") != "1")
        {
            output.WriteLine("Skipped: set ARN_BENCH_COMPOSITOR=1 to run the slot-fill bench.");
            return;
        }

        var reads = BuildReadDistribution();
        int nCorpus = reads.Count(r => r.Source == "corpus");
        int nSweep = reads.Count(r => r.Source == "sweep");
        output.WriteLine($"Read-duration scenarios: {reads.Count} (empirical {nCorpus} + synthetic {nSweep})");

        var lib = FillerLibrary.Build(Clip);
        var strategies = new[]
        {
            new Strategy("current (word-select, slow=1.0)",
                BuildPipeline(maxSlowDown: 1.0, maxSpeedUp: 1.1, pad: 0.3), ComposeCurrent),
            new Strategy("measure-then-place (dur-select, slow=1.08)",
                BuildPipeline(maxSlowDown: 1.08, maxSpeedUp: 1.1, pad: 0.3), ComposeMeasuredPlace),
            new Strategy("bed-outro (trimmable closer, read untouched)",
                BuildPipeline(maxSlowDown: 1.0, maxSpeedUp: 1.1, pad: 0.3), ComposeBed),
            new Strategy("dynamic-slot (nearest 15/20/30 + bed)",
                BuildPipeline(maxSlowDown: 1.0, maxSpeedUp: 1.1, pad: 0.3), ComposeDynamicSlot),
        };

        output.WriteLine("");
        output.WriteLine("Fill% = reached the AIMED slot within a filler/bed-fraction cap (any / <=50% / <=33% / <=20% of the aimed slot).");
        output.WriteLine("A fill that needs >50% filler is really the filler cart — not a bulletin.");
        output.WriteLine("");
        foreach (var s in strategies)
        {
            var samples = Score(s, reads, lib);
            output.WriteLine(FillLine(s.Name + "  [corpus]", samples.Where(x => x.Source == "corpus").ToList()));
            output.WriteLine(FillLine(new string(' ', s.Name.Length) + "  [sweep] ", samples.Where(x => x.Source == "sweep").ToList()));
        }

        output.WriteLine("");
        output.WriteLine("SUFFICIENT-CONTENT fill (reads 24-33s) — this is the number that MUST be 100%:");
        foreach (var s in strategies)
        {
            var samples = Score(s, reads, lib);
            output.WriteLine(SufficientLine(s.Name, samples));
        }
    }

    // ── Fill strategies (the thing under iteration) ─────────────────────

    /// <summary>Mirrors today's deployed behaviour: size the gap from the read, select filler by the
    /// word-count estimate (real <see cref="FillerStatementSelector" />), stitch, fine-tune under the
    /// deployed asymmetric caps (no slow-fill).</summary>
    private static ComposeResult ComposeCurrent(byte[] read, AudioPipelineService pipeline, FillerLibrary lib)
    {
        double readDur = pipeline.GetDuration(read);
        double gap = Math.Max(0, Target - readDur);
        var picked = FillerStatementSelector.Select(
            lib.Statements, gap, previousSameDayScript: null,
            maxStatements: MaxFillerClips, wordsPerSecond: Wps, overshootToleranceSeconds: 1.5);
        return Stitch(read, picked.Select(lib.ClipFor).ToList(), pipeline);
    }

    /// <summary>Candidate: select filler by its MEASURED clip duration to minimise the residual to
    /// the slot (not a word estimate), then let the symmetric fine-tune close the small remainder.
    /// A production build would add a trimmable outro bed for the final sub-second residual; here the
    /// symmetric micro-stretch models that closer's effect on duration.</summary>
    private static ComposeResult ComposeMeasuredPlace(byte[] read, AudioPipelineService pipeline, FillerLibrary lib)
    {
        double readDur = pipeline.GetDuration(read);
        double residual = Target - readDur;
        return Stitch(read, SelectByMeasuredDuration(residual, lib), pipeline);
    }

    /// <summary>Greedy best-fit over MEASURED clip durations: each round takes the largest clip whose
    /// duration+pause still fits the remaining residual; if none fits, take the smallest (a small
    /// overshoot the fine-tune compresses) and stop.</summary>
    private static List<byte[]> SelectByMeasuredDuration(double residual, FillerLibrary lib)
    {
        var chosen = new List<byte[]>();
        double remaining = residual;
        while (chosen.Count < MaxFillerClips && remaining > 0.6)
        {
            var fit = lib.Clips.Where(c => c.Seconds + FillerPause <= remaining + 0.4)
                               .OrderByDescending(c => c.Seconds)
                               .FirstOrDefault();
            if (fit is null)
            {
                var smallest = lib.Clips.OrderBy(c => c.Seconds).First();
                chosen.Add(smallest.Wav);
                break;
            }
            chosen.Add(fit.Wav);
            remaining -= fit.Seconds + FillerPause;
        }
        return chosen;
    }

    /// <summary>Models the trimmable music-bed/outro closer (ArchitectCritique): the traffic read is
    /// left UNTOUCHED and a per-station bed, trimmable to any millisecond, closes the residual to the
    /// aimed slot EXACTLY. Over-content (read longer than the slot) compresses within the speed-up cap.
    /// Duration is exact by construction; the only reported limit is the BED FRACTION (an editorial call).</summary>
    private static ComposeResult BedClose(byte[] read, AudioPipelineService pipeline, double aimed)
    {
        double readDur = pipeline.GetDuration(read);
        double residual = aimed - readDur;
        if (residual >= 0)
        {
            byte[] bed = Clip(residual);
            byte[] stitched = pipeline.StitchAudioClips(
                new List<byte[]> { pipeline.TrimToSpokenDuration(read), bed }, 0.0);
            double dur = pipeline.GetDuration(stitched);
            return new ComposeResult(dur, Math.Abs(dur - aimed) <= 0.15, residual, aimed);
        }
        byte[] compressed = pipeline.FineTuneToTargetDuration(read, aimed);
        double cdur = pipeline.GetDuration(compressed);
        return new ComposeResult(cdur, pipeline.IsSlotFilled(compressed, aimed), 0, aimed);
    }

    private static ComposeResult ComposeBed(byte[] read, AudioPipelineService pipeline, FillerLibrary lib)
        => BedClose(read, pipeline, Target);

    /// <summary>Models FrameCritique's dynamic-slot rule: after measuring the honest read D, air the
    /// standard slot the content justifies (nearest of 30/20/15), then close with the trimmable bed.
    /// A thin market airs a shorter, honest bulletin instead of being padded to 30s or dropped.</summary>
    private static ComposeResult ComposeDynamicSlot(byte[] read, AudioPipelineService pipeline, FillerLibrary lib)
    {
        double d = pipeline.GetDuration(read);
        double aimed = d >= 24 ? 30.0 : d >= 18 ? 20.0 : d >= 13 ? 15.0 : 0.0;
        if (aimed == 0.0) return new ComposeResult(d, false, 0, 15.0); // genuinely thin → fallback cart
        return BedClose(read, pipeline, aimed);
    }

    private static ComposeResult Stitch(byte[] read, IReadOnlyList<byte[]> clips, AudioPipelineService pipeline)
    {
        double fillerSec = clips.Sum(c => pipeline.GetDuration(c) + FillerPause);
        byte[] stitched = clips.Count > 0
            ? pipeline.StitchAudioClips(
                new List<byte[]> { pipeline.TrimToSpokenDuration(read) }.Concat(clips).ToList(), FillerPause)
            : read;
        byte[] final = pipeline.FineTuneToTargetDuration(stitched, Target);
        double finalDur = pipeline.GetDuration(final);
        bool filled = pipeline.IsSlotFilled(final, Target);
        return new ComposeResult(finalDur, filled, fillerSec);
    }

    // ── Scoring ─────────────────────────────────────────────────────────

    private static List<Sample> Score(Strategy s, IReadOnlyList<ReadScenario> reads, FillerLibrary lib)
    {
        var samples = new List<Sample>(reads.Count);
        foreach (var r in reads)
        {
            var res = s.Compose(r.Wav, s.Pipeline, lib);
            samples.Add(new Sample(r.Source, r.Seconds, res.Filled, res.FillerSeconds, Math.Abs(res.FinalDuration - res.AimedTarget), res.AimedTarget));
        }
        return samples;
    }

    // ── Read-duration distribution ──────────────────────────────────────

    private static IReadOnlyList<ReadScenario> BuildReadDistribution()
    {
        var list = new List<ReadScenario>();
        foreach (var f in FallbackReplayFixture.LoadAll())
            foreach (var sec in f.DevOutcome.NaturalSecSeq)
                if (sec is > 0 and < 45)
                    list.Add(new ReadScenario(sec.Value, Clip(sec.Value), "corpus"));
        for (double d = 6.0; d <= 38.0; d += 0.5)
            list.Add(new ReadScenario(d, Clip(d), "sweep"));
        return list;
    }

    // ── WAV generation ──────────────────────────────────────────────────

    /// <summary>16 kHz mono 16-bit low-amplitude sine of exactly <paramref name="seconds" /> — a
    /// non-silent WAV so <see cref="WavDurationProcessor" />'s spoken-duration scan treats it as
    /// voiced (the shape <see cref="SimulatedElevenLabsSynthesizer" /> emits).</summary>
    internal static byte[] Clip(double seconds)
    {
        const int sr = 16000;
        int samples = Math.Max(1, (int)(seconds * sr));
        var pcm = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * 220 * i / sr) * 3000);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return WrapWav(pcm, sr, 1, 16);
    }

    private static byte[] WrapWav(byte[] pcm, int sampleRate, short channels, short bits)
    {
        int byteRate = sampleRate * channels * bits / 8;
        short blockAlign = (short)(channels * bits / 8);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bits);
        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    // ── Service construction ────────────────────────────────────────────

    internal static AudioPipelineService BuildPipeline(double maxSlowDown, double maxSpeedUp, double pad,
        bool useBedCloser = false)
    {
        var duration = Options.Create(new SpeechDurationOptions
        {
            MaxSlowDownFactor = maxSlowDown,
            MaxSpeedUpFactor = maxSpeedUp,
            MaxSilencePadSeconds = pad,
            UseTrimmedBedCloser = useBedCloser,
        });
        return new AudioPipelineService(
            Substitute.For<IAudioSynthesizer>(),
            new WavDurationProcessor(NullLogger<WavDurationProcessor>.Instance),
            Substitute.For<ISpeechDurationEstimator>(),
            Substitute.For<ISynthesisHistoryRepository>(),
            Array.Empty<IAudioPostSynthesisDurationAdjustmentStrategy>(),
            Options.Create(new BroadcastOptions()),
            duration,
            new WavLoudnessNormalizer(NullLogger<WavLoudnessNormalizer>.Instance),
            Options.Create(new AudioLoudnessOptions { Enabled = false }),
            NullLogger<AudioPipelineService>.Instance);
    }

    // ── Records ─────────────────────────────────────────────────────────

    private sealed record ReadScenario(double Seconds, byte[] Wav, string Source);
    private sealed record ComposeResult(double FinalDuration, bool Filled, double FillerSeconds, double AimedTarget = Target);
    private sealed record Strategy(string Name, AudioPipelineService Pipeline,
        Func<byte[], AudioPipelineService, FillerLibrary, ComposeResult> Compose);

    private sealed record Sample(string Source, double ReadSeconds, bool Filled, double FillerSec, double AbsResidual, double AimedTarget = Target);

    /// <summary>Fill-rate conditioned on SUFFICIENT content — reads in [24, 33]s, where the honest
    /// traffic read is close-or-over and 30s is reachable by duration ops alone (small filler /
    /// modest slow-fill / compression). This is the number that MUST be 100%; anything less is a
    /// bug, not a content limit. Reads &lt;24s are content-insufficient (policy/fallback);
    /// reads &gt;33s need content TRIMMING (sentence-level, out of this duration-only bench's scope).</summary>
    private static string SufficientLine(string name, IReadOnlyList<Sample> samples)
    {
        var band = samples.Where(s => s.ReadSeconds is >= 24 and <= 33).ToList();
        if (band.Count == 0) return name.PadRight(44) + "(no sufficient-content reads)";
        int ok = band.Count(s => s.Filled);
        var misses = band.Where(s => !s.Filled).Select(s => s.ReadSeconds).OrderBy(x => x).ToList();
        string miss = misses.Count == 0 ? "" : "  MISSES@ " + string.Join(",", misses.Select(m => $"{m:F1}s"));
        return name.PadRight(44) + $"sufficient-content fill: {ok}/{band.Count} ({100.0 * ok / band.Count,3:F0}%)" + miss;
    }

    /// <summary>A fill only counts if it reached the slot AND used no more than the filler-fraction
    /// cap — so "filled to 30s with 22s of generic filler" is NOT scored as a win (that is the
    /// filler cart, which is what a genuinely-thin market should fall back to anyway).</summary>
    private static string FillLine(string name, IReadOnlyList<Sample> samples)
    {
        string Pct(double frac)
        {
            if (samples.Count == 0) return "  - ";
            int ok = samples.Count(s => s.Filled && s.FillerSec <= frac * s.AimedTarget + 1e-9);
            return $"{100.0 * ok / samples.Count,3:F0}%";
        }
        double meanFiller = samples.Count == 0 ? 0 : samples.Average(s => s.FillerSec);
        return name.PadRight(44) +
            $"any:{Pct(9)}  <=50%:{Pct(0.50)}  <=33%:{Pct(0.33)}  <=20%:{Pct(0.20)}   meanFiller~{meanFiller,4:F1}s";
    }

    /// <summary>A graded filler ladder: real texts (word-count → estimated seconds for the current
    /// word-based selector) paired with a pre-rendered clip of the MEASURED matching duration.</summary>
    internal sealed class FillerLibrary
    {
        public required IReadOnlyList<FillerStatement> Statements { get; init; }
        public required IReadOnlyList<FillerClip> Clips { get; init; }
        public required IReadOnlyDictionary<string, byte[]> ById { get; init; }

        public byte[] ClipFor(FillerStatement s) => ById[s.Id];

        public static FillerLibrary Build(Func<double, byte[]> clip)
        {
            int[] wordCounts = [5, 8, 11, 14, 17, 20, 23];
            var statements = new List<FillerStatement>();
            var clips = new List<FillerClip>();
            var byId = new Dictionary<string, byte[]>();
            foreach (int w in wordCounts)
            {
                string id = $"f{w}";
                double sec = w / Wps;
                var wav = clip(sec);
                statements.Add(new FillerStatement(id, string.Join(' ', Enumerable.Repeat("road", w))));
                clips.Add(new FillerClip(sec, wav));
                byId[id] = wav;
            }
            return new FillerLibrary { Statements = statements, Clips = clips, ById = byId };
        }
    }

    internal sealed record FillerClip(double Seconds, byte[] Wav);
}
