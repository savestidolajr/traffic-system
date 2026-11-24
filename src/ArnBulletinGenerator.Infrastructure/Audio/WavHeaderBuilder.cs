namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>Wraps raw 16-bit PCM samples in a standard RIFF/WAVE header to produce a playable WAV file.</summary>
public static class WavHeaderBuilder
{
    public static byte[] WrapInWavHeader(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write("RIFF"u8);
        writer.Write(36 + pcmData.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // AudioFormat PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8);
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return ms.ToArray();
    }
}
