using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Deterministic, offline tests for the fabrication scorer — the highest-value quality gate.
///     No network, no LLM: crafted scripts against a known traffic vocabulary.
/// </summary>
public sealed class FabricationScorerTests
{
    private static TrafficCorridor Corridor(string road, string description = "")
        => new(road, 1, 1, 0, 0, "congestion", description, []);

    [Fact]
    public void Clean_ScriptUsesOnlyRoadsInData_NoFabrication()
    {
        TrafficCorridor[] corridors = [Corridor("M4"), Corridor("Monash Freeway")];

        var result = FabricationScorer.Evaluate(
            "Heavy delays on the M4 this morning, and slow going on the Monash Freeway.", corridors);

        result.Passed.ShouldBeTrue($"unexpected fabrication flags: {string.Join(", ", result.UnsupportedMentions)}");
    }

    [Fact]
    public void InventedRoadAndCode_AreFlagged()
    {
        TrafficCorridor[] corridors = [Corridor("M4"), Corridor("Monash Freeway")];

        var result = FabricationScorer.Evaluate(
            "A crash on the Pacific Highway is causing delays, and the M80 is closed.", corridors);

        result.Passed.ShouldBeFalse();
        result.UnsupportedMentions.ShouldContain("Pacific Highway");
        result.UnsupportedMentions.ShouldContain("M80");
    }

    [Fact]
    public void CodedRoad_WithSpokenSuffix_IsNotFalselyFlagged()
    {
        TrafficCorridor[] corridors = [Corridor("M4")];

        var result = FabricationScorer.Evaluate("The M4 Motorway is blocked citybound.", corridors);

        result.Passed.ShouldBeTrue($"unexpected fabrication flags: {string.Join(", ", result.UnsupportedMentions)}");
    }

    [Fact]
    public void ShortenedRoadName_PresentInData_IsSupported()
    {
        TrafficCorridor[] corridors = [Corridor("Sydney Harbour Bridge")];

        var result = FabricationScorer.Evaluate("Expect delays on the Harbour Bridge.", corridors);

        result.Passed.ShouldBeTrue($"unexpected fabrication flags: {string.Join(", ", result.UnsupportedMentions)}");
    }

    [Fact]
    public void RoadNameFromIncidentDescription_IsSupported()
    {
        TrafficCorridor[] corridors =
            [Corridor("Unidentified Road", "Broken-down vehicle on Victoria Road near the underpass.")];

        var result = FabricationScorer.Evaluate("A breakdown on Victoria Road is slowing traffic.", corridors);

        result.Passed.ShouldBeTrue($"unexpected fabrication flags: {string.Join(", ", result.UnsupportedMentions)}");
    }

    [Fact]
    public void EmptyScript_IsTreatedAsClean()
    {
        FabricationScorer.Evaluate("", [Corridor("M4")]).Passed.ShouldBeTrue();
    }

    // Regression: the live eval flagged "Meanwhile Pacific Motorway" because the extractor swallowed the
    // sentence-initial filler word. A real road preceded by filler must not be reported as fabricated.
    [Fact]
    public void LeadingSentenceFiller_BeforeRealRoad_IsNotFalselyFlagged()
    {
        TrafficCorridor[] corridors = [Corridor("Pacific Motorway"), Corridor("Smith Street")];

        var result = FabricationScorer.Evaluate(
            "Smith Street's a mess this arvo. Meanwhile Pacific Motorway is copping it too.", corridors);

        result.Passed.ShouldBeTrue($"unexpected fabrication flags: {string.Join(", ", result.UnsupportedMentions)}");
    }

    // Regression: the live eval flagged "Pacific Motorway" because the data labels it "Pacific Mtwy"
    // (HERE's revgeocode abbreviation). Expanding the abbreviation must keep the spelled-out road supported.
    [Fact]
    public void RoadTypeAbbreviationInData_ExpandedInScript_IsSupported()
    {
        TrafficCorridor[] corridors = [Corridor("Pacific Mtwy", "Closure between Smith Street and the exit ramp.")];

        var result = FabricationScorer.Evaluate("The Pacific Motorway has a closure causing delays.", corridors);

        result.Passed.ShouldBeTrue($"unexpected fabrication flags: {string.Join(", ", result.UnsupportedMentions)}");
    }
}
