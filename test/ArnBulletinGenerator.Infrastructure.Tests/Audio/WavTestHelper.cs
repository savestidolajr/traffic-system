using ArnBulletinGenerator.Infrastructure.Audio;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

/// <summary>
///     Shared helper for building minimal valid WAV byte arrays in tests.
/// </summary>
internal static class WavTestHelper
{
    public static byte[] CreateMinimalWav(
        short channels = 1,
        int sampleRate = 44100,
        short bitsPerSample = 16,
        int numSamples = 100)
    {
        int bytesPerSample = bitsPerSample / 8;
        int dataSize = numSamples * channels * bytesPerSample;
        byte[] pcmData = new byte[dataSize];
        return WavHeaderBuilder.WrapInWavHeader(pcmData, sampleRate, channels, bitsPerSample);
    }

    /// <summary>
    ///     A WAV that is silence except for a loud tone occupying its final
    ///     <paramref name="voicedTailSeconds" /> — the shape of a read whose LAST WORD sits right on
    ///     the slot boundary. Tests keep the durations small (2.00s slot / 2.12s read) purely for
    ///     speed; the production incident was a 22.00s slot against a 22.15s Cairns read.
    /// </summary>
    public static byte[] CreateWavWithVoicedTail(
        double totalSeconds,
        double voicedTailSeconds,
        int sampleRate = 44100,
        short channels = 1,
        short amplitude = 6000)
    {
        int totalFrames = (int)Math.Round(totalSeconds * sampleRate);
        int voicedFrames = Math.Min(totalFrames, (int)Math.Round(voicedTailSeconds * sampleRate));
        short[] samples = new short[totalFrames * channels];
        int voicedStart = totalFrames - voicedFrames;
        for (int frame = voicedStart; frame < totalFrames; frame++)
        {
            short sample = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * 200 * frame / sampleRate));
            for (int channel = 0; channel < channels; channel++)
                samples[(frame * channels) + channel] = sample;
        }

        byte[] pcm = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);

        // Production incident was a 22.00s/22.15s Cairns take; small durations keep this test fast.
        return WavHeaderBuilder.WrapInWavHeader(pcm, sampleRate, channels, 16);
    }

    /// <summary>
    ///     Loudest absolute sample in the final <paramref name="seconds" /> of a 16-bit WAV. Above
    ///     the amplitude threshold <c>TrimTrailingSilence</c> treats as silence (48) means the tail
    ///     still carries speech; 0 means it was discarded.
    /// </summary>
    public static int PeakAbsSampleInFinalSeconds(byte[] wav, double seconds)
    {
        var info = WavInfoParser.GetWavInfo(wav)!;
        int dataStart = (int)info.DataPosition;
        int dataEnd = dataStart + info.DataSize;
        int scanStart = Math.Max(dataStart, dataEnd - (int)Math.Round(seconds * info.ByteRate));
        int peak = 0;
        for (int offset = scanStart; offset + 1 < dataEnd; offset += 2)
            peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(wav, offset)));
        return peak;
    }
}
