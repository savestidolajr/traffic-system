using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class SyllableCounterTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void CountSyllables_ReturnsZero_ForNullOrWhitespace(string? text, int expected)
    {
        SyllableCounter.CountSyllables(text).ShouldBe(expected);
    }

    [Fact]
    public void CountSyllables_Hello_ReturnsTwo()
    {
        SyllableCounter.CountSyllables("hello").ShouldBe(2);
    }

    [Fact]
    public void CountSyllables_Catastrophe_ReturnsFour()
    {
        SyllableCounter.CountSyllables("catastrophe").ShouldBe(4);
    }

    [Fact]
    public void CountSyllables_Recipe_ReturnsThree()
    {
        SyllableCounter.CountSyllables("recipe").ShouldBe(3);
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("me", 1)]
    [InlineData("the", 1)]
    [InlineData("I", 1)]
    public void CountSyllables_ShortWords_ReturnOne(string word, int expected)
    {
        SyllableCounter.CountSyllables(word).ShouldBe(expected);
    }

    [Fact]
    public void CountSyllables_MultiWord_SumsAll()
    {
        SyllableCounter.CountSyllables("the cat sat on the mat").ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CountSyllables_PronouncedFinalE_NotSubtracted()
    {
        // "epitome" has 4 syllables (e-pi-to-me) — final 'e' is pronounced
        SyllableCounter.CountSyllables("epitome").ShouldBe(4);
    }

    [Fact]
    public void CountSyllables_SilentFinalE_Subtracted()
    {
        // "make" has 1 syllable — final 'e' is silent
        SyllableCounter.CountSyllables("make").ShouldBe(1);
    }
}
