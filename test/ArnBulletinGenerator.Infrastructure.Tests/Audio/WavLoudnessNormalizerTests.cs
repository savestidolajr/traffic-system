using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class WavLoudnessNormalizerTests
{
    private readonly WavLoudnessNormalizer _norm = new(Substitute.For<ILogger<WavLoudnessNormalizer>>());

    // Sine tone at a given peak amplitude (RMS = amp / sqrt(2)).
    private static byte[] Tone(double peakAmplitude, int sampleRate = 44100, int numSamples = 44100, double freq = 200.0)
    {
        short[] s = new short[numSamples];
        for (int i = 0; i < numSamples; i++)
            s[i] = (short)Math.Round(peakAmplitude * Math.Sin(2 * Math.PI * freq * i / sampleRate));
        byte[] pcm = new byte[s.Length * 2];
        Buffer.BlockCopy(s, 0, pcm, 0, pcm.Length);
        return WavHeaderBuilder.WrapInWavHeader(pcm, sampleRate, 1, 16);
    }

    private static (double rmsDb, double peakDb) Measure(byte[] wav)
    {
        var info = WavInfoParser.GetWavInfo(wav)!;
        int start = (int)info.DataPosition;
        int n = info.DataSize / 2;
        double sum = 0;
        int peak = 0;
        for (int i = 0; i < n; i++)
        {
            int v = BitConverter.ToInt16(wav, start + (i * 2));
            sum += (double)v * v;
            if (Math.Abs(v) > peak) peak = Math.Abs(v);
        }
        double rms = Math.Sqrt(sum / n);
        return (20 * Math.Log10(rms / 32768.0), 20 * Math.Log10(peak / 32768.0));
    }

    [Fact]
    public void Normalize_QuietInput_RaisesToTarget()
    {
        byte[] quiet = Tone(2000); // ~ -27 dBFS RMS
        byte[] result = _norm.Normalize(quiet, targetRmsDbFs: -14.5, peakCeilingDbFs: -1.0);

        Measure(result).rmsDb.ShouldBe(-14.5, 0.4);
    }

    [Fact]
    public void Normalize_LoudInput_LowersToTarget()
    {
        byte[] loud = Tone(20000); // ~ -4.3 dBFS RMS
        byte[] result = _norm.Normalize(loud, targetRmsDbFs: -14.5, peakCeilingDbFs: -1.0);

        Measure(result).rmsDb.ShouldBe(-14.5, 0.4);
    }

    [Fact]
    public void Normalize_DifferentLevels_ConvergeToSameLoudness()
    {
        byte[] a = _norm.Normalize(Tone(3000), targetRmsDbFs: -14.5);
        byte[] b = _norm.Normalize(Tone(15000), targetRmsDbFs: -14.5);

        // Two very different inputs should end within ~0.5 dB of each other.
        Math.Abs(Measure(a).rmsDb - Measure(b).rmsDb).ShouldBeLessThan(0.5);
    }

    [Fact]
    public void Normalize_NeverExceedsPeakCeiling()
    {
        // Quiet body with rare full-scale spikes: RMS gain would push peaks over the ceiling.
        short[] s = new short[44100];
        for (int i = 0; i < s.Length; i++) s[i] = 400;
        for (int i = 0; i < s.Length; i += 4000) s[i] = 30000;
        byte[] pcm = new byte[s.Length * 2];
        Buffer.BlockCopy(s, 0, pcm, 0, pcm.Length);
        byte[] spiky = WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);

        byte[] result = _norm.Normalize(spiky, targetRmsDbFs: -14.5, peakCeilingDbFs: -1.0);

        Measure(result).peakDb.ShouldBeLessThanOrEqualTo(-1.0 + 0.1);
    }

    [Fact]
    public void Normalize_Silence_ReturnsUnchanged()
    {
        byte[] silent = WavTestHelper.CreateMinimalWav(sampleRate: 44100, numSamples: 44100);
        byte[] result = _norm.Normalize(silent);

        result.ShouldBeSameAs(silent);
    }

    [Fact]
    public void Normalize_InvalidWav_ReturnsUnchanged()
    {
        byte[] data = [1, 2, 3];
        _norm.Normalize(data).ShouldBeSameAs(data);
    }

    [Fact]
    public void Normalize_PreservesSampleRateAndLength()
    {
        byte[] input = Tone(2000, sampleRate: 24000, numSamples: 24000);
        byte[] result = _norm.Normalize(input);

        var before = WavInfoParser.GetWavInfo(input)!;
        var after = WavInfoParser.GetWavInfo(result)!;
        after.SampleRate.ShouldBe(before.SampleRate);
        result.Length.ShouldBe(input.Length);
    }
}
