using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class AudioMathHelperTests
{
    [Theory]
    [InlineData(100, 4, 100)] // already aligned
    [InlineData(101, 4, 100)] // rounds down by 1
    [InlineData(102, 4, 100)] // rounds down by 2
    [InlineData(103, 4, 100)] // rounds down by 3
    [InlineData(104, 4, 104)] // next boundary
    [InlineData(0, 4, 0)] // zero bytes
    [InlineData(3, 4, 0)] // smaller than block
    public void AlignToBlockBoundary_RoundsDownCorrectly(int byteCount, int blockAlign, int expected)
    {
        AudioMathHelper.AlignToBlockBoundary(byteCount, blockAlign)
            .ShouldBe(expected);
    }

    [Fact]
    public void AlignToBlockBoundary_ZeroBlockAlign_ReturnsOriginal()
    {
        AudioMathHelper.AlignToBlockBoundary(100, 0).ShouldBe(100);
    }

    [Theory]
    [InlineData(1, 16)] // blockAlign = 1*16/8 = 2
    [InlineData(2, 16)] // blockAlign = 2*16/8 = 4
    [InlineData(1, 8)] // blockAlign = 1*8/8 = 1
    [InlineData(2, 24)] // blockAlign = 2*24/8 = 6
    public void AlignToBlockBoundary_FromChannelsAndBits_ComputesCorrectly(short channels, short bitsPerSample)
    {
        int blockAlign = channels * bitsPerSample / 8;
        int byteCount = 101;
        int expected = byteCount / blockAlign * blockAlign;

        AudioMathHelper.AlignToBlockBoundary(byteCount, channels, bitsPerSample)
            .ShouldBe(expected);
    }

    [Fact]
    public void AlignToBlockBoundary_Stereo16Bit_Aligns()
    {
        // blockAlign = 2 * 16 / 8 = 4
        AudioMathHelper.AlignToBlockBoundary(100, 2, 16)
            .ShouldBe(100); // 100 is divisible by 4
    }

    [Fact]
    public void AlignToBlockBoundary_Stereo16Bit_Unaligned_RoundsDown()
    {
        // blockAlign = 2 * 16 / 8 = 4, 101 / 4 = 25 → 100
        AudioMathHelper.AlignToBlockBoundary(101, 2, 16)
            .ShouldBe(100);
    }
}
