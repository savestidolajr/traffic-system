using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class WavValidatorTests
{
    [Fact]
    public void IsValidWavFile_ValidHeader_ReturnsTrue()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav();
        WavValidator.IsValidWavFile(wav).ShouldBeTrue();
    }

    [Fact]
    public void IsValidWavFile_TooShort_ReturnsFalse()
    {
        byte[] data = new byte[43]; // less than 44
        WavValidator.IsValidWavFile(data).ShouldBeFalse();
    }

    [Fact]
    public void IsValidWavFile_NoRiffMarker_ReturnsFalse()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav();
        // Corrupt "RIFF" marker
        wav[0] = (byte)'X';
        WavValidator.IsValidWavFile(wav).ShouldBeFalse();
    }

    [Fact]
    public void IsValidWavFile_NoWaveMarker_ReturnsFalse()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav();
        // Corrupt "WAVE" marker at offset 8
        wav[8] = (byte)'X';
        WavValidator.IsValidWavFile(wav).ShouldBeFalse();
    }

    [Fact]
    public void IsValidWavFile_EmptyArray_ReturnsFalse()
    {
        WavValidator.IsValidWavFile(Array.Empty<byte>()).ShouldBeFalse();
    }

    [Fact]
    public void IsValidWavFile_Exactly44Bytes_ReturnsTrue()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(numSamples: 0);
        wav.Length.ShouldBeGreaterThanOrEqualTo(44);
        WavValidator.IsValidWavFile(wav).ShouldBeTrue();
    }
}
