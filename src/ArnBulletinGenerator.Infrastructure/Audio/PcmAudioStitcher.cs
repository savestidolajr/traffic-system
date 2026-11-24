namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Concatenates whole WAV clips (traffic synthesis + resolved filler audio) into one file,
///     inserting a fixed silence pause between each. Sibling to <see cref="WavHeaderBuilder" />/
///     <see cref="WavInfoParser" />; the silence gap is a zero-initialized byte array, exactly
///     like <see cref="WavDurationProcessor.EnsureDuration" /> already produces.
/// </summary>
public static class PcmAudioStitcher
{
    public static byte[] Concatenate(IReadOnlyList<byte[]> wavClips, double pauseSecondsBetweenClips)
    {
        if (wavClips.Count == 0) return [];
        if (wavClips.Count == 1) return wavClips[0];

        var infos = wavClips.Select(WavInfoParser.GetWavInfo).ToList();
        if (infos.Any(i => i is null))
            throw new InvalidOperationException("PcmAudioStitcher: one or more clips are not valid WAV data.");

        var first = infos[0]!;
        if (infos.Any(i => i!.SampleRate != first.SampleRate || i.Channels != first.Channels || i.BitsPerSample != first.BitsPerSample))
            throw new InvalidOperationException(
                $"PcmAudioStitcher: clips have mismatched format (expected {first.SampleRate}Hz/{first.Channels}ch/{first.BitsPerSample}bit).");

        int silenceBytes = AudioMathHelper.AlignToBlockBoundary(
            (int)(pauseSecondsBetweenClips * first.SampleRate * first.Channels * (first.BitsPerSample / 8)),
            first.Channels, first.BitsPerSample);

        using var pcm = new MemoryStream();
        for (int i = 0; i < wavClips.Count; i++)
        {
            var info = infos[i]!;
            pcm.Write(wavClips[i], (int)info.DataPosition, info.DataSize);
            if (i < wavClips.Count - 1)
                pcm.Write(new byte[silenceBytes], 0, silenceBytes);
        }

        return WavHeaderBuilder.WrapInWavHeader(pcm.ToArray(), first.SampleRate, first.Channels, first.BitsPerSample);
    }
}
