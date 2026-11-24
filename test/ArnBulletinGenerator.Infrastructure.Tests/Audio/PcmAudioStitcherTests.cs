using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class PcmAudioStitcherTests
{
    private static byte[] SilenceWav(double seconds, int sampleRate = 44100, short channels = 1, short bitsPerSample = 16)
    {
        int sampleCount = (int)Math.Round(seconds * sampleRate);
        int bytesPerSample = bitsPerSample / 8;
        byte[] pcm = new byte[sampleCount * channels * bytesPerSample];
        return WavHeaderBuilder.WrapInWavHeader(pcm, sampleRate, channels, bitsPerSample);
    }

    [Fact]
    public void Concatenate_TwoClips_IncludesPauseInDuration()
    {
        var clip1 = SilenceWav(2.0);
        var clip2 = SilenceWav(1.5);
        double pause = 0.35;

        byte[] result = PcmAudioStitcher.Concatenate([clip1, clip2], pause);

        var info = WavInfoParser.GetWavInfo(result);
        info.ShouldNotBeNull();
        double expected = 2.0 + 0.35 + 1.5;
        info!.DurationSeconds.ShouldBe(expected, tolerance: 0.01);
    }

    [Fact]
    public void Concatenate_ThreeClips_TotalDurationMatchesSum()
    {
        var clip1 = SilenceWav(1.0);
        var clip2 = SilenceWav(0.5);
        var clip3 = SilenceWav(2.0);
        double pause = 0.2;

        byte[] result = PcmAudioStitcher.Concatenate([clip1, clip2, clip3], pause);

        var info = WavInfoParser.GetWavInfo(result);
        info.ShouldNotBeNull();
        double expected = 1.0 + 0.2 + 0.5 + 0.2 + 2.0;
        info!.DurationSeconds.ShouldBe(expected, tolerance: 0.01);
    }

    [Fact]
    public void Concatenate_SingleClip_ReturnsSameReference()
    {
        var clip = SilenceWav(1.0);

        byte[] result = PcmAudioStitcher.Concatenate([clip], 0.35);

        result.ShouldBeSameAs(clip);
    }

    [Fact]
    public void Concatenate_EmptyInput_ReturnsEmptyArray()
    {
        byte[] result = PcmAudioStitcher.Concatenate([], 0.35);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Concatenate_MismatchedSampleRate_ThrowsInvalidOperationException()
    {
        var clip1 = SilenceWav(1.0, sampleRate: 44100);
        var clip2 = SilenceWav(1.0, sampleRate: 22050);

        Should.Throw<InvalidOperationException>(() =>
            PcmAudioStitcher.Concatenate([clip1, clip2], 0.35));
    }

    [Fact]
    public void Concatenate_MismatchedChannels_ThrowsInvalidOperationException()
    {
        var clip1 = SilenceWav(1.0, channels: 1);
        var clip2 = SilenceWav(1.0, channels: 2);

        Should.Throw<InvalidOperationException>(() =>
            PcmAudioStitcher.Concatenate([clip1, clip2], 0.35));
    }

    [Fact]
    public void Concatenate_MismatchedBitDepth_ThrowsInvalidOperationException()
    {
        var clip1 = SilenceWav(1.0, bitsPerSample: 16);
        var clip2 = SilenceWav(1.0, bitsPerSample: 8);

        Should.Throw<InvalidOperationException>(() =>
            PcmAudioStitcher.Concatenate([clip1, clip2], 0.35));
    }
}
