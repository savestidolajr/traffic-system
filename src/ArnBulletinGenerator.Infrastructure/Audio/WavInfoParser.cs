using System.Text;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Parsed WAV header metadata (sample rate, channels, bit depth, data chunk location) that
///     <see cref="WavInfoParser" /> extracts for duration math and sample-accurate editing.
/// </summary>
public record WavInfo(int SampleRate, short Channels, short BitsPerSample, long DataPosition, int DataSize)
{
    public int ByteRate => SampleRate * Channels * BitsPerSample / 8;
    public double DurationSeconds => ByteRate == 0 ? 0 : (double)DataSize / ByteRate;
}

public static class WavInfoParser
{
    public static WavInfo? GetWavInfo(byte[] audioData)
    {
        if (!WavValidator.IsValidWavFile(audioData)) return null;

        using var ms = new MemoryStream(audioData);
        using var reader = new BinaryReader(ms);

        // Skip RIFF header (12 bytes)
        ms.Position = 12;

        short channels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        int dataSize = 0;
        long dataPosition = 0;

        while (ms.Position < ms.Length)
        {
            byte[] subChunkIdBytes = reader.ReadBytes(4);
            if (subChunkIdBytes.Length < 4) break;

            string subChunkId = Encoding.ASCII.GetString(subChunkIdBytes);
            int subChunkSize = reader.ReadInt32();

            if (subChunkId == "fmt ")
            {
                var fmtInfo = ParseFmtChunk(reader, subChunkSize);
                channels = fmtInfo.channels;
                sampleRate = fmtInfo.sampleRate;
                bitsPerSample = fmtInfo.bitsPerSample;
            }
            else if (subChunkId == "data")
            {
                dataPosition = ms.Position;

                // Handle streaming WAV files where data size is unknown (0xFFFFFFFF or -1)
                // In this case, compute actual data size from remaining bytes
                if (subChunkSize < 0 || subChunkSize > audioData.Length)
                    dataSize = (int)(audioData.Length - dataPosition);
                else
                    dataSize = subChunkSize;
                break;
            }
            else
            {
                // Skip unknown chunks (ensure we don't go past EOF)
                // Handle negative subChunkSize from streaming WAV headers
                if (subChunkSize < 0 || ms.Position + subChunkSize > ms.Length) break;
                ms.Position += subChunkSize;
            }
        }

        if (sampleRate == 0 || channels == 0 || bitsPerSample == 0) return null;

        return new WavInfo(sampleRate, channels, bitsPerSample, dataPosition, dataSize);
    }

    private static (short channels, int sampleRate, short bitsPerSample) ParseFmtChunk(BinaryReader reader,
        int chunkSize)
    {
        long startPos = reader.BaseStream.Position;

        reader.ReadInt16(); // AudioFormat
        short channels = reader.ReadInt16();
        int sampleRate = reader.ReadInt32();
        reader.ReadInt32(); // ByteRate
        reader.ReadInt16(); // BlockAlign
        short bitsPerSample = reader.ReadInt16();

        // Skip remaining chunk data if any
        long bytesRead = reader.BaseStream.Position - startPos;
        if (chunkSize > bytesRead) reader.BaseStream.Position += chunkSize - bytesRead;

        return (channels, sampleRate, bitsPerSample);
    }
}
