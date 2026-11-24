using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class AuthorityPriorityNormalizerTests
{
    [Theory]
    [InlineData("critical", 1)]
    [InlineData("major", 2)]
    [InlineData("minor", 3)]
    [InlineData("lowImpact", 4)]
    public void HereVocabularyMaps(string value, int expected) =>
        Rank("here", value).ShouldBe(expected);

    [Theory]
    [InlineData("High", 1)]
    [InlineData("Medium", 2)]
    [InlineData("Low", 3)]
    public void QueenslandVocabularyMaps(string value, int expected) =>
        Rank("qldtraffic", value).ShouldBe(expected);

    [Theory]
    [InlineData("Level 1", 1)]
    [InlineData("level 2", 2)]
    [InlineData(" Level 3 ", 3)]
    public void WesternAustraliaVocabularyMaps(string value, int expected) =>
        Rank("mainroads-wa", value).ShouldBe(expected);

    [Fact]
    public void NewSouthWalesMajorFlagMapsToHighestRank()
    {
        Rank("livetraffic-nsw", "Major").ShouldBe(1);
        AuthorityPriorityNormalizer.Normalize("livetraffic-nsw", null).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void SouthAustraliaHasNoPriorityVocabulary() =>
        AuthorityPriorityNormalizer.Normalize("trafficsa", "High").IsNone.ShouldBeTrue();

    [Theory]
    [InlineData("here", "High")]
    [InlineData("qldtraffic", "critical")]
    [InlineData("mainroads-wa", "Level 4")]
    [InlineData("livetraffic-nsw", "Minor")]
    [InlineData("unknown", "critical")]
    public void UnknownValueOrSourceReturnsNone(string source, string value) =>
        AuthorityPriorityNormalizer.Normalize(source, value).IsNone.ShouldBeTrue();

    private static int Rank(string source, string value)
    {
        var rank = AuthorityPriorityNormalizer.Normalize(source, value);
        rank.HasValue.ShouldBeTrue();
        return rank.Value;
    }
}
