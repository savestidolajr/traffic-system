using System.Text;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>Confirms a byte array is a well-formed WAV file by checking the RIFF/WAVE signature bytes.</summary>
public static class WavValidator
{
    public static bool IsValidWavFile(byte[] audioData)
    {
        return audioData.Length >= 44
               && Encoding.ASCII.GetString(audioData, 0, 4) == "RIFF"
               && Encoding.ASCII.GetString(audioData, 8, 4) == "WAVE";
    }
}
