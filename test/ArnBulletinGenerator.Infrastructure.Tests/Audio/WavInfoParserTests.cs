using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class WavInfoParserTests
{
    [Fact]
    public void GetWavInfo_ValidWav_ReturnsCorrectInfo()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav();

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        info!.SampleRate.ShouldBe(44100);
        ((int)info.Channels).ShouldBe(1);
        ((int)info.BitsPerSample).ShouldBe(16);
        info.DataSize.ShouldBe(100 * 2); // 100 samples * 16 bits / 8 = 200 bytes
    }

    [Fact]
    public void GetWavInfo_Stereo_ReturnsCorrectChannels()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(2, 48000, 16, 50);

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        ((int)info!.Channels).ShouldBe(2);
        info.SampleRate.ShouldBe(48000);
        info.DataSize.ShouldBe(50 * 2 * 2); // 50 samples * 2 channels * 2 bytes
    }

    [Fact]
    public void GetWavInfo_InvalidData_ReturnsNull()
    {
        byte[] data = new byte[] { 1, 2, 3, 4 };
        WavInfoParser.GetWavInfo(data).ShouldBeNull();
    }

    [Fact]
    public void GetWavInfo_EmptyArray_ReturnsNull()
    {
        WavInfoParser.GetWavInfo(Array.Empty<byte>()).ShouldBeNull();
    }

    [Fact]
    public void GetWavInfo_ByteRate_IsComputed()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(2);

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        // 44100 * 2 * 16 / 8 = 176400
        info!.ByteRate.ShouldBe(176400);
    }

    [Fact]
    public void GetWavInfo_Duration_IsComputed()
    {
        int numSamples = 44100; // 1 second of mono 16-bit @ 44100
        byte[] wav = WavTestHelper.CreateMinimalWav(1, 44100, 16, numSamples);

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        info!.DurationSeconds.ShouldBe(1.0, 0.01);
    }

    [Fact]
    public void GetWavInfo_DataPosition_PointsAfterDataChunkHeader()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(numSamples: 100);

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        // Standard wav: 12 (RIFF header) + 8 (fmt id+size) + 16 (fmt data) + 8 (data id+size) = 44
        info!.DataPosition.ShouldBe(44);
    }

    [Fact]
    public void GetWavInfo_8Bit_ParsesCorrectly()
    {
        byte[] wav = WavTestHelper.CreateMinimalWav(1, 22050, 8, 200);

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        ((int)info!.BitsPerSample).ShouldBe(8);
        info.SampleRate.ShouldBe(22050);
        info.DataSize.ShouldBe(200); // 200 samples * 1 byte
    }
}
