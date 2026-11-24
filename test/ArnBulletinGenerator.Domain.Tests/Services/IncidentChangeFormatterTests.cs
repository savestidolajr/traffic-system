using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class IncidentChangeFormatterTests
{
    private static IncidentDeltaItem MakeItem(
        string type = "accident",
        Option<string> roadName = default,
        Option<string> suburb = default,
        Option<string> direction = default,
        Option<string> description = default,
        int criticality = 1)
    {
        return new IncidentDeltaItem("inc-1", type, criticality, roadName, suburb, direction, 500, description);
    }

    private static IncidentDeltaContext MakeDelta(
        IReadOnlyList<IncidentDeltaItem>? newItems = null,
        IReadOnlyList<IncidentDeltaItem>? updated = null,
        IReadOnlyList<IncidentDeltaItem>? cleared = null,
        IReadOnlyList<IncidentDeltaItem>? unchanged = null)
    {
        return new IncidentDeltaContext(
            newItems ?? [],
            updated ?? [],
            cleared ?? [],
            unchanged ?? []);
    }

    [Fact]
    public void BuildSummary_None_ReturnsEmpty()
    {
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.None);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void BuildSummary_EmptyDelta_ReturnsEmpty()
    {
        var delta = MakeDelta();
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldBeEmpty();
    }

    [Fact]
    public void BuildSummary_NewIncidents_FormatsWithNewLabel()
    {
        var item = MakeItem("accident", "M1");
        var delta = MakeDelta([item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldContain("- New: accident on M1");
    }

    [Fact]
    public void BuildSummary_ClearedIncidents_FormatsWithClearedLabel()
    {
        var item = MakeItem("roadClosure", "Pacific Highway");
        var delta = MakeDelta(cleared: [item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldContain("- Cleared: roadClosure on Pacific Highway");
    }

    [Fact]
    public void BuildSummary_UpdatedIncidents_FormatsWithUpdatedLabel()
    {
        var item = MakeItem("construction", "King Street");
        var delta = MakeDelta(updated: [item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldContain("- Updated: construction on King Street");
    }

    [Fact]
    public void BuildSummary_NewIncidentsCapAtThree()
    {
        var items = Enumerable.Range(1, 5)
            .Select(i => MakeItem($"type{i}", $"Road{i}"))
            .ToList();
        var delta = MakeDelta(items);
        string result = IncidentChangeFormatter.BuildSummary(
            Option<IncidentDeltaContext>.Some(delta));

        string[] lines = result.Split('\n');
        lines.Length.ShouldBe(3);
    }

    [Fact]
    public void BuildSummary_LocationUsesRoadNameSuburbDirection()
    {
        var item = MakeItem(
            roadName: Option<string>.Some("M2"),
            suburb: Option<string>.Some("North Sydney"),
            direction: Option<string>.Some("Southbound"));
        var delta = MakeDelta([item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        result.ShouldContain("on M2, North Sydney, Southbound");
    }

    [Fact]
    public void BuildSummary_LocationFallsBackToDescription()
    {
        var item = MakeItem(description: Option<string>.Some("Near the bridge"));
        var delta = MakeDelta([item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        result.ShouldContain("on Near the bridge");
    }

    [Fact]
    public void BuildSummary_NoLocationInfo_ShowsUnknown()
    {
        var item = MakeItem();
        var delta = MakeDelta([item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        result.ShouldContain("on unknown location");
    }

    [Fact]
    public void BuildSummary_EmptyType_ShowsIncident()
    {
        var item = MakeItem("", "M1");
        var delta = MakeDelta([item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        result.ShouldContain("- New: incident on M1");
    }

    [Fact]
    public void BuildSummary_WhitespaceType_ShowsIncident()
    {
        var item = MakeItem("   ", "M1");
        var delta = MakeDelta([item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        result.ShouldContain("- New: incident on M1");
    }

    [Fact]
    public void BuildSummary_UnchangedSevereIncident_FormatsStillOngoing()
    {
        var item = MakeItem("accident", "M1", criticality: 1);
        var delta = MakeDelta(unchanged: [item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldContain("- Still ongoing: accident on M1");
    }

    [Fact]
    public void BuildSummary_UnchangedMinorIncident_Excluded()
    {
        var item = MakeItem("accident", "M1", criticality: 3);
        var delta = MakeDelta(unchanged: [item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldNotContain("Still ongoing");
    }

    [Fact]
    public void BuildSummary_UnchangedStandingFootprint_Excluded()
    {
        var item = MakeItem("construction", "King Street", criticality: 1);
        var delta = MakeDelta(unchanged: [item]);
        string result = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));
        result.ShouldNotContain("Still ongoing");
    }
}
