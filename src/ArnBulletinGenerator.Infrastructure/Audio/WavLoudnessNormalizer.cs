using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Normalizes 16-bit PCM WAV loudness to a consistent target so every bulletin goes to air
///     at the same perceived level regardless of voice or content. Broadcast-style processing:
///     gated RMS measurement → makeup gain toward the target → a soft peak limiter (fast attack,
///     slow release) that catches the overs, so quiet-but-peaky voices can be brought up to level
///     without clipping. A final hard ceiling guarantees no sample exceeds the true-peak limit.
///     Pure-managed; sample rate and length are unchanged.
/// </summary>
public sealed class WavLoudnessNormalizer(ILogger<WavLoudnessNormalizer> logger)
{
    /// <param name="targetRmsDbFs">Desired gated RMS level (≈ −14 LUFS for these voices).</param>
    /// <param name="peakCeilingDbFs">True-peak ceiling the limiter holds the signal under.</param>
    /// <param name="silenceFloorDbFs">Samples quieter than this are excluded from the RMS measurement.</param>
    /// <param name="maxMakeupGainDb">Cap on upward gain, so near-silent input does not boost noise.</param>
    public byte[] Normalize(
        byte[] wavData,
        double targetRmsDbFs = -14.5,
        double peakCeilingDbFs = -1.0,
        double silenceFloorDbFs = -50.0,
        double maxMakeupGainDb = 20.0)
    {
        var info = WavInfoParser.GetWavInfo(wavData);
        if (info is null || info.BitsPerSample != 16)
            return wavData;

        int dataStart = (int)info.DataPosition;
        int dataBytes = Math.Min(info.DataSize, wavData.Length - dataStart);
        int sampleCount = dataBytes / 2;
        if (sampleCount == 0)
            return wavData;

        double silenceFloorAmp = Math.Pow(10, silenceFloorDbFs / 20.0) * 32768.0;
        double sumSquares = 0;
        long counted = 0;
        int peak = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            int s = BitConverter.ToInt16(wavData, dataStart + (i * 2));
            int a = Math.Abs(s);
            if (a > peak) peak = a;
            if (a >= silenceFloorAmp)
            {
                sumSquares += (double)s * s;
                counted++;
            }
        }

        if (counted == 0 || peak == 0)
        {
            logger.LogInformation("[Loudness] Skipping normalize — input is silent.");
            return wavData;
        }

        double currentRmsDb = 20.0 * Math.Log10(Math.Sqrt(sumSquares / counted) / 32768.0);
        double makeupDb = Math.Min(targetRmsDbFs - currentRmsDb, maxMakeupGainDb);
        double makeup = Math.Pow(10, makeupDb / 20.0);
        double ceiling = Math.Pow(10, peakCeilingDbFs / 20.0) * 32768.0;

        // Nothing to do: no meaningful gain change and already under the ceiling.
        if (Math.Abs(makeupDb) < 0.05 && peak <= ceiling)
            return wavData;

        // Soft peak limiter: fast attack pulls gain down before a peak clips, slow release recovers.
        double attack = Math.Exp(-1.0 / (0.002 * info.SampleRate));  // ~2 ms
        double release = Math.Exp(-1.0 / (0.080 * info.SampleRate)); // ~80 ms
        double env = 1.0;

        byte[] outBytes = (byte[])wavData.Clone();
        for (int i = 0; i < sampleCount; i++)
        {
            double x = BitConverter.ToInt16(wavData, dataStart + (i * 2)) * makeup;
            double mag = Math.Abs(x);
            double desired = mag > ceiling ? ceiling / mag : 1.0;
            env = desired < env
                ? (attack * env) + ((1 - attack) * desired)
                : (release * env) + ((1 - release) * desired);

            double y = Math.Clamp(x * env, -ceiling, ceiling); // hard ceiling backstops limiter overshoot
            short c = (short)Math.Clamp((int)Math.Round(y), short.MinValue, short.MaxValue);
            outBytes[dataStart + (i * 2)] = (byte)(c & 0xFF);
            outBytes[dataStart + (i * 2) + 1] = (byte)((c >> 8) & 0xFF);
        }

        logger.LogInformation(
            "[Loudness] RMS {Cur:F1} → ~{Tgt:F1} dBFS (makeup {Makeup:+0.0;-0.0} dB, soft-limited to {Ceil:F1} dBFS ceiling)",
            currentRmsDb, targetRmsDbFs, makeupDb, peakCeilingDbFs);
        return outBytes;
    }
}
