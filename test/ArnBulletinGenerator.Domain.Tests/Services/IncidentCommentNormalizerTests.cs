using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class IncidentCommentNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyOrWhitespace_ReturnsNone(string? comment)
    {
        IncidentCommentNormalizer.Normalize(comment, "any description").IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Normalize_StripsAdvisoryImperatives_KeepsFactualDetail()
    {
        // Real Main Roads WA comment shape: factual head + advisory tail.
        var result = IncidentCommentNormalizer.Normalize(
            "Beeliar Dr westbound before Kwinana Fwy, Banjup  - Road Crash Left Lane(s) Blocked.  No Known Congestion.  Exercise Extreme Caution.",
            "At Kwinana Freeway - Accident. Left lane blocked");

        result.IsSome.ShouldBeTrue();
        result.Value.ShouldContain("Beeliar Dr westbound");
        result.Value.ShouldContain("Left Lane(s) Blocked");
        result.Value.ShouldNotContain("Exercise Extreme Caution");
    }

    [Fact]
    public void Normalize_AllAdvisorySentences_ReturnsNone()
    {
        IncidentCommentNormalizer
            .Normalize("Exercise Extreme Caution. Seek Alternative Routes.", "desc")
            .IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Normalize_DecodesHtmlEntitiesAndCollapsesWhitespace()
    {
        // Captured comments embed "&#13;\n" between the location and the work window.
        var result = IncidentCommentNormalizer.Normalize(
            "Stirling Hwy at Vaucluse Av, Claremont - Maintenance&#13;\n7am to 5pm Lane closures and speed reduction in place",
            "At Stirling Highway - Road construction");

        result.IsSome.ShouldBeTrue();
        result.Value.ShouldNotContain("&#13;");
        result.Value.ShouldNotContain("\n");
        result.Value.ShouldNotContain("  "); // no double spaces
        result.Value.ShouldContain("Stirling Hwy at Vaucluse Av, Claremont");
    }

    [Fact]
    public void Normalize_StripsWebsiteReferralSentences()
    {
        var result = IncidentCommentNormalizer.Normalize(
            "South St at Fiona Wood Rd, Murdoch - Maintenance. Various closures and detour in place. Click 'Find out more' for a detour map.",
            "At South Street - Road construction");

        result.IsSome.ShouldBeTrue();
        result.Value.ShouldContain("South St at Fiona Wood Rd, Murdoch");
        result.Value.ShouldNotContain("Find out more");
    }

    [Fact]
    public void Normalize_DescriptionAlreadyContainsComment_ReturnsNone()
    {
        // HERE often repeats the summary as the comment ("Gas leak"); no point duplicating.
        IncidentCommentNormalizer
            .Normalize("Gas leak", "At Carrington Street - Gas leak")
            .IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Normalize_LongComment_CapsAtSentenceBoundaryUnderLimit()
    {
        var result = IncidentCommentNormalizer.Normalize(
            "Camfield Dr - Upgrades Temporary road crossing for construction personnel. " +
            "Bus transporting personnel will park in street parking bays. " +
            "Traffic controllers in place to ensure safe crossing. " +
            "Traffic management will operate Monday to Friday from 6am.",
            "At Camfield Drive - Road construction");

        result.IsSome.ShouldBeTrue();
        result.Value.Length.ShouldBeLessThanOrEqualTo(140);
        result.Value.ShouldStartWith("Camfield Dr");
        result.Value.ShouldNotEndWith("."); // joined on ". ", trailing period trimmed
    }
}
