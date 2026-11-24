using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Bench;

/// <summary>
///     Runs <see cref="WavDurationProcessor.TrimAfterSpeech" /> over a REAL render and prints the
///     tail energy profile either side, so the trailing-inhale fix can be checked against actual
///     ElevenLabs output rather than synthesised shapes.
///     <para>
///         Gated behind <c>ARN_BENCH_TAILTRIM=1</c>; a no-op in CI.
///         <c>ARN_BENCH_TAILTRIM_WAV</c> is the render, <c>ARN_BENCH_TAILTRIM_BOUNDARY</c> the end
///         time (seconds) of the last alphanumeric character from that render's own alignment —
///         both obtained from the timestamps endpoint. Writes the trimmed take beside the source.
///     </para>
/// </summary>
public sealed class SpeechTailTrimBench(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Bench")]
    public void TrimAfterSpeech_PrintsTheTailProfileEitherSide()
    {
        if (Environment.GetEnvironmentVariable("ARN_BENCH_TAILTRIM") != "1")
        {
            output.WriteLine("Skipped: set ARN_BENCH_TAILTRIM=1 to run the tail-trim bench.");
            return;
        }

        string? wavPath = Environment.GetEnvironmentVariable("ARN_BENCH_TAILTRIM_WAV");
        string? boundaryRaw = Environment.GetEnvironmentVariable("ARN_BENCH_TAILTRIM_BOUNDARY");
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath)
            || !double.TryParse(boundaryRaw, out double boundary))
        {
            output.WriteLine("Skipped: ARN_BENCH_TAILTRIM_WAV (existing file) and "
                             + "ARN_BENCH_TAILTRIM_BOUNDARY (seconds) are both required.");
            return;
        }

        var processor = new WavDurationProcessor(NullLogger<WavDurationProcessor>.Instance);
        byte[] source = File.ReadAllBytes(wavPath);
        byte[] trimmed = processor.TrimAfterSpeech(source, boundary);

        string outPath = Path.Combine(Path.GetDirectoryName(wavPath)!,
            Path.GetFileNameWithoutExtension(wavPath) + ".trimmed.wav");
        File.WriteAllBytes(outPath, trimmed);

        output.WriteLine($"boundary (last alphanumeric char): {boundary:F3}s");
        output.WriteLine($"before: {processor.GetDuration(source):F3}s   after: {processor.GetDuration(trimmed):F3}s");
        output.WriteLine($"removed: {(processor.GetDuration(source) - processor.GetDuration(trimmed)) * 1000:F0}ms");
        output.WriteLine($"written: {outPath}");
        output.WriteLine("");
        Profile(output, "SOURCE  tail", source);
        output.WriteLine("");
        Profile(output, "TRIMMED tail", trimmed);
    }

    private static void Profile(ITestOutputHelper output, string label, byte[] wav)
    {
        var info = WavInfoParser.GetWavInfo(wav);
        if (info is null) return;
        int dataStart = (int)info.DataPosition;
        int dataEnd = dataStart + info.DataSize;
        int win = info.SampleRate / 100 * 2; // 10ms

        output.WriteLine($"{label} — last 300ms:");
        for (int i = Math.Max(dataStart, dataEnd - (win * 30)); i + win <= dataEnd; i += win)
        {
            double sum = 0;
            int n = 0;
            int zc = 0;
            short prev = 0;
            for (int k = i; k + 1 < i + win; k += 2)
            {
                short s = BitConverter.ToInt16(wav, k);
                sum += (double)s * s;
                if (n > 0 && (prev < 0) != (s < 0)) zc++;
                prev = s;
                n++;
            }

            double rms = n == 0 ? 0 : Math.Sqrt(sum / n);
            double db = rms > 0 ? 20 * Math.Log10(rms / 32768.0) : -120;
            double ms = (dataEnd - i) * 1000.0 / info.ByteRate;
            output.WriteLine($"   -{ms,6:F0}ms  {db,7:F1} dB   {zc * info.SampleRate / (double)Math.Max(1, n) / 2,6:F0}Hz");
        }
    }
}
