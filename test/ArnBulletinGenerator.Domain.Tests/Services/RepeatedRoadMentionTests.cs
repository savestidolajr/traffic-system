using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
/// Pins the road analogue of the weather/fuel fixture guard. Measured on the 2026-08-25 station
/// panel: the sydney 30s read took the only extend attempt of any 30s fixture and was the only read
/// to duplicate a road, airing the Warringah Freeway closure three times.
/// </summary>
public sealed class RepeatedRoadMentionTests
{
    // The exact read that aired on the panel. Every sentence is individually grounded and well
    // formed, which is why only a whole-read comparison catches it.
    private const string PanelSydney30S =
        "Now looking over Sydney. The citybound Warringah Freeway is closed for about one and a half "
        + "kays, with the ramp to Alfred Street North closed. The M8 is clear, while the Warringah "
        + "Freeway is closed citybound. Traffic is running clear on Sydney Gateway, while the "
        + "citybound Warringah Freeway is closed at the ramp to Alfred Street North.";

    [Fact]
    public void CountRoadMentions_FindsTheDuplicationThatAired()
    {
        var counts = ContentSourceSignals.CountRoadMentions(PanelSydney30S);

        counts["Warringah Freeway"].ShouldBe(3);
        counts["Alfred Street"].ShouldBe(2);
        counts["M8"].ShouldBe(1);
        counts["Sydney Gateway"].ShouldBe(1);
    }

    [Fact]
    public void AddsRepeatedRoadMention_RejectsAGrowthPatchThatRestatesTheLeadClosure()
    {
        const string before =
            "The citybound Warringah Freeway is closed for about one and a half kays. The M8 is clear.";
        // The growth patch the refiner actually produced: it padded sentence 2 by restating the lead.
        const string after =
            "The citybound Warringah Freeway is closed for about one and a half kays. The M8 is "
            + "clear, while the Warringah Freeway is closed citybound.";

        ContentSourceSignals.AddsRepeatedRoadMention(before, after).ShouldBeTrue();
    }

    [Fact]
    public void AddsRepeatedRoadMention_AllowsANewCrossStreet()
    {
        // Exactly what the expansion instruction asks for: a cross-street not yet mentioned.
        const string before = "London Circuit is closed eastbound.";
        const string after = "London Circuit is closed eastbound at Petrie Plaza and Northbourne Avenue.";

        ContentSourceSignals.AddsRepeatedRoadMention(before, after).ShouldBeFalse();
    }

    [Fact]
    public void AddsRepeatedRoadMention_AllowsAGenuinelyNewRoad()
    {
        const string before = "The M8 is clear.";
        const string after = "The M8 is clear, and so is the Sydney Harbour Tunnel.";

        ContentSourceSignals.AddsRepeatedRoadMention(before, after).ShouldBeFalse();
    }

    [Fact]
    public void AddsRepeatedRoadMention_IgnoresAnUnchangedRead()
    {
        ContentSourceSignals.AddsRepeatedRoadMention(PanelSydney30S, PanelSydney30S).ShouldBeFalse();
    }

    [Theory]
    // Shapes that reach air in the ARN corpus and must be countable.
    [InlineData("Nerang-Broadbeach Road is closed.", "Nerang-Broadbeach Road")]
    [InlineData("Roadworks on Sir Donald Bradman Drive.", "Sir Donald Bradman Drive")]
    [InlineData("The M8 is clear.", "M8")]
    [InlineData("Delays on the Pacific Motorway.", "Pacific Motorway")]
    [InlineData("A hazard on Captain Cook Highway.", "Captain Cook Highway")]
    public void CountRoadMentions_MatchesRealRoadShapes(string script, string expected)
    {
        ContentSourceSignals.CountRoadMentions(script).Keys.ShouldContain(expected);
    }

    [Fact]
    public void CountRoadMentions_DoesNotCountProseAsARoad()
    {
        // A read with no road names at all must never block an expansion.
        const string filler =
            "Please stay alert out there and stick to the speed limit. Clear skies, 21 degrees.";

        ContentSourceSignals.CountRoadMentions(filler).ShouldBeEmpty();
    }
}
