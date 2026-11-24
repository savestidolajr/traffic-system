using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class WordCounterTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Count_ReturnsZero_ForNullOrWhitespace(string? text, int expected)
    {
        WordCounter.Count(text).ShouldBe(expected);
    }

    [Fact]
    public void Count_ReturnsTwoWords()
    {
        WordCounter.Count("hello world").ShouldBe(2);
    }

    [Fact]
    public void Count_IgnoresBracketedPauseTags()
    {
        WordCounter.Count("hello [pause] world").ShouldBe(2);
    }

    [Fact]
    public void Count_IgnoresEllipses()
    {
        WordCounter.Count("hello ... world").ShouldBe(2);
    }

    [Fact]
    public void Count_IgnoresMultiplePauseTags()
    {
        WordCounter.Count("one [short pause] two [long pause] three").ShouldBe(3);
    }

    [Fact]
    public void Count_SingleWord()
    {
        WordCounter.Count("hello").ShouldBe(1);
    }

    [Fact]
    public void Count_OnlyPauseTag_ReturnsZero()
    {
        WordCounter.Count("[pause]").ShouldBe(0);
    }

    [Fact]
    public void StripTtsTags_RemovesBracketedTags()
    {
        WordCounter.StripTtsTags("hello [short pause] world").ShouldNotContain("[short pause]");
    }

    [Fact]
    public void StripTtsTags_RemovesEllipsis()
    {
        WordCounter.StripTtsTags("hello... world").ShouldNotContain("...");
    }

    [Fact]
    public void StripTtsTags_PreservesSinglePeriod()
    {
        WordCounter.StripTtsTags("hello. world").ShouldContain(".");
    }

    [Fact]
    public void StripTtsTags_IsCaseInsensitive()
    {
        WordCounter.StripTtsTags("hello [PAUSE] world").ShouldNotContain("[PAUSE]");
    }
}
