using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class SpokenPunctuationNormalizerTests
{
    // Verbatim from dev jobs 027729aac1 and d94648edf6 (both sydney, 22s, E2): a delivery tag
    // sat between the last body sentence and the station outro; stripping the tag collapsed the
    // punctuation to ". ." and it persisted into the aired script.
    [Theory]
    [InlineData(
        "Passing clouds, 14 degrees. . That's KIIS traffic and weather together.",
        "Passing clouds, 14 degrees. That's KIIS traffic and weather together.")]
    [InlineData(
        "Mostly cloudy, fifteen degrees. . That's KIIS traffic and weather together.",
        "Mostly cloudy, fifteen degrees. That's KIIS traffic and weather together.")]
    public void Normalize_WhitespaceSeparatedTerminators_CollapsesToFirst(string input, string expected) =>
        SpokenPunctuationNormalizer.Normalize(input).ShouldBe(expected);

    // Verbatim from dev job 0bb35fef24 (brisbane, E1-current): an em dash (U+2014) used as a
    // clause break must become a comma.
    [Fact]
    public void Normalize_EmDashClauseBreak_BecomesComma()
    {
        const string input = "The Pacific Motorway into the city is about five minutes slower than usual, " +
            "with construction at Beenleigh \u2014 traffic's under the pump.";
        const string expected = "The Pacific Motorway into the city is about five minutes slower than usual, " +
            "with construction at Beenleigh, traffic's under the pump.";

        SpokenPunctuationNormalizer.Normalize(input).ShouldBe(expected);
    }

    // Verbatim from dev job 026b6b1d (Mildura): the Unicode ellipsis (U+2026) is a deliberate
    // delivery device and must survive untouched, while the em dash clause break becomes a comma.
    [Fact]
    public void Normalize_FullCorpusScript_KeepsEllipsesAndFixesDash()
    {
        const string input = "Walker Road's closed outbound… Lock 10 Road's also closed outbound, " +
            "and Old Mail Road's blocked citybound. Werrimull North Road and Tapaulin Road are both " +
            "moving nicely… It's cloudy in Mildura, with temperatures on seventeen degrees. Unleaded's " +
            "cheapest at two-oh-two a litre in Mildura, with diesel at two-forty-eight there too. " +
            "A reminder that speed restrictions apply through the signed roadworks zones \u2014 take it " +
            "easy through the cones.";
        const string expected = "Walker Road's closed outbound… Lock 10 Road's also closed outbound, " +
            "and Old Mail Road's blocked citybound. Werrimull North Road and Tapaulin Road are both " +
            "moving nicely… It's cloudy in Mildura, with temperatures on seventeen degrees. Unleaded's " +
            "cheapest at two-oh-two a litre in Mildura, with diesel at two-forty-eight there too. " +
            "A reminder that speed restrictions apply through the signed roadworks zones, take it " +
            "easy through the cones.";

        SpokenPunctuationNormalizer.Normalize(input).ShouldBe(expected);
    }

    // A genuine "..." is a deliberate delivery device (prompts/director.md) and must survive
    // untouched. Verbatim from dev jobs 31ebe372e0, b6e0335408 and 2778fdb330.
    [Theory]
    [InlineData("The Warringah Freeway's CLOSED outbound between Miller Street and the Ht Toll Plaza... " +
        "and the Falcon Street ramp's CLOSED as well.")]
    [InlineData("Big traffic impacts right now... the Greensborough Bypass is CLOSED southbound at the " +
        "M80 Metropolitan Ring Road.")]
    [InlineData("Big changes out there right now... the Greensborough Bypass is CLOSED southbound at the " +
        "M80 Metropolitan Ring Road. CityLink's heavy eastbound between Toorak Road and Warrigal Road... " +
        "with works also at the West Gate Freeway interchange. Plenty Road has roadworks northbound " +
        "approaching the M80. Mostly cloudy, 13 degrees. Unleaded's cheapest at a dollar ninety-two in " +
        "Kingsville... diesel's two dollars thirty-five there too.")]
    public void Normalize_DeliberateEllipsis_ReturnedUnchanged(string script) =>
        SpokenPunctuationNormalizer.Normalize(script).ShouldBe(script);

    [Theory]
    [InlineData("It's clear with eleven degrees.. Next update shortly.",
        "It's clear with eleven degrees. Next update shortly.")]
    public void Normalize_DoubledPeriodNotAnEllipsis_CollapsesToOne(string input, string expected) =>
        SpokenPunctuationNormalizer.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("The M1 is slow \u2013 keep left.", "The M1 is slow, keep left.")]       // en dash clause break
    [InlineData("Allow 5\u201310 minutes.", "Allow 5\u201310 minutes.")]                 // dash between digits is a range
    [InlineData("Allow 5 \u2014 10 minutes.", "Allow 5 \u2014 10 minutes.")]             // spaced range also preserved
    [InlineData("It's slow \u2014, keep left.", "It's slow, keep left.")]                 // dash beside an existing comma
    [InlineData("It's slow . at Kent Road.", "It's slow. at Kent Road.")]                 // space before period removed
    [InlineData("It's slow , at Kent Road.", "It's slow, at Kent Road.")]                 // space before comma removed
    public void Normalize_PunctuationEdges(string input, string expected) =>
        SpokenPunctuationNormalizer.Normalize(input).ShouldBe(expected);

    [Fact]
    public void Normalize_IsIdempotent()
    {
        const string input = "The Western Motorway's chockers at Church Street with heavy delays. . " +
            "The Pacific Motorway is slow \u2014 keep left. It's clear with eleven degrees.. Stay safe . out there";
        const string expected = "The Western Motorway's chockers at Church Street with heavy delays. " +
            "The Pacific Motorway is slow, keep left. It's clear with eleven degrees. Stay safe. out there";

        string once = SpokenPunctuationNormalizer.Normalize(input);
        once.ShouldBe(expected);
        SpokenPunctuationNormalizer.Normalize(once).ShouldBe(once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Traffic is flowing well on the M1 this morning.")]
    public void Normalize_NoArtifacts_ReturnedUnchanged(string script) =>
        SpokenPunctuationNormalizer.Normalize(script).ShouldBe(script);
}
