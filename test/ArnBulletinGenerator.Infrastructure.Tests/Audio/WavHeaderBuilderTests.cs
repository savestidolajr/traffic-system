using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class WavHeaderBuilderTests
{
    [Fact]
    public void WrapInWavHeader_ProducesValidWav()
    {
        byte[] pcm = new byte[200];
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);

        WavValidator.IsValidWavFile(wav).ShouldBeTrue();
    }

    [Fact]
    public void WrapInWavHeader_HasCorrectLength()
    {
        byte[] pcm = new byte[200];
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);

        wav.Length.ShouldBe(44 + 200);
    }

    [Fact]
    public void WrapInWavHeader_RoundTripsWithParser()
    {
        byte[] pcm = new byte[400]; // 100 stereo 16-bit samples
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 48000, 2, 16);

        var info = WavInfoParser.GetWavInfo(wav);

        info.ShouldNotBeNull();
        info!.SampleRate.ShouldBe(48000);
        ((int)info.Channels).ShouldBe(2);
        ((int)info.BitsPerSample).ShouldBe(16);
        info.DataSize.ShouldBe(400);
    }

    [Fact]
    public void WrapInWavHeader_EmptyPcm_StillValid()
    {
        byte[] pcm = Array.Empty<byte>();
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);

        WavValidator.IsValidWavFile(wav).ShouldBeTrue();
        wav.Length.ShouldBe(44);
    }

    [Fact]
    public void WrapInWavHeader_ContainsCorrectRiffSize()
    {
        byte[] pcm = new byte[200];
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);

        // RIFF size at offset 4, little-endian int32 = 36 + dataSize
        int riffSize = BitConverter.ToInt32(wav, 4);
        riffSize.ShouldBe(36 + 200);
    }

    [Fact]
    public void WrapInWavHeader_ContainsCorrectByteRate()
    {
        byte[] pcm = new byte[100];
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 2, 16);

        // ByteRate at offset 28 = sampleRate * channels * bitsPerSample / 8
        int byteRate = BitConverter.ToInt32(wav, 28);
        byteRate.ShouldBe(44100 * 2 * 16 / 8);
    }

    [Fact]
    public void WrapInWavHeader_ContainsPcmFormat()
    {
        byte[] pcm = new byte[100];
        byte[] wav = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);

        // Audio format at offset 20 = 1 (PCM)
        short audioFormat = BitConverter.ToInt16(wav, 20);
        ((int)audioFormat).ShouldBe(1);
    }
}
