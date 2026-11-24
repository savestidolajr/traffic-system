using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

/// <summary>
///     <see cref="VerifiedPronunciation.NormalizeWord" /> is a CROSS-LANGUAGE contract: the web
///     client's <c>stripPunctuation</c> in <c>AudioPreReviewBadge.tsx</c> must strip the same
///     characters and case-fold the same way, or a word verified in the review cockpit keeps
///     flagging (the badge filters by its key, the trigger de-duplicates by this one).
/// </summary>
public sealed class VerifiedPronunciationTests
{
    [Theory]
    [InlineData("Keilor", "keilor")]
    [InlineData("keilor", "keilor")]
    [InlineData("KEILOR", "keilor")]
    // Possessives are routine in suspect words ("Gate's", "Unleaded's") — punctuation can
    // never be part of identity, or the same word verifies twice and mutes neither.
    [InlineData("Gate's", "gates")]
    [InlineData("Gates", "gates")]
    [InlineData("Unleaded's", "unleadeds")]
    [InlineData("Sir-Donald", "sirdonald")]
    [InlineData("M1", "m1")]
    [InlineData("  Toorak  ", "toorak")]
    public void NormalizeWord_StripsPunctuationAndCase(string input, string expected)
    {
        VerifiedPronunciation.NormalizeWord(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--")]
    public void NormalizeWord_NoAlphanumerics_IsEmpty(string? input)
    {
        VerifiedPronunciation.NormalizeWord(input).ShouldBeEmpty();
    }

    [Fact]
    public void Key_UsesNormalizedWord()
    {
        new VerifiedPronunciation("Gate's").Key.ShouldBe("gates");
    }

    [Fact]
    public void Key_IsEqualAcrossCasingAndPunctuationVariants()
    {
        new VerifiedPronunciation("Gate's").Key.ShouldBe(new VerifiedPronunciation("GATES").Key);
    }
}
