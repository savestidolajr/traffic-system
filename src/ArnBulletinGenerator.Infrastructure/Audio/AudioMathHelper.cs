namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Shared audio alignment math used by WAV processing components.
/// </summary>
internal static class AudioMathHelper
{
    /// <summary>
    ///     Rounds <paramref name="byteCount" /> down to the nearest block-aligned boundary.
    /// </summary>
    public static int AlignToBlockBoundary(int byteCount, int blockAlign)
    {
        return blockAlign > 0 ? byteCount / blockAlign * blockAlign : byteCount;
    }

    /// <summary>
    ///     Convenience overload that computes block-align from channel count and bit depth.
    /// </summary>
    public static int AlignToBlockBoundary(int byteCount, short channels, short bitsPerSample)
    {
        return AlignToBlockBoundary(byteCount, channels * bitsPerSample / 8);
    }
}
