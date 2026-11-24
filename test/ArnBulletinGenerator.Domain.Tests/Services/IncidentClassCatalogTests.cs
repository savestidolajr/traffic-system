using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The catalogue is a published contract, not an internal enum. Its ids key a market's boost
///     sliders in the incident-priorities screen AND the per-condition voicing blocks a station may
///     rewrite in the playbook studio, so an id that drifts orphans both at once — silently, because
///     an overlay naming an unknown block is refused rather than failed.
/// </summary>
public sealed class IncidentClassCatalogTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArnBulletinGenerator.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void CatalogueMatchesTheListTheConsoleShows()
    {
        // The web ships its own copy so the screen renders without a round trip. Two copies is the
        // accepted cost; two DIFFERENT copies is a market whose boosts key off ids the backend never
        // produces, which is a silent no-op rather than an error.
        string web = File.ReadAllText(Path.Combine(
            RepoRoot, "web", "src", "features", "schedules", "data", "incident-landscape-data.ts"));

        var webIds = Regex.Matches(web, @"id:\s*'([A-Za-z]+)'").Select(m => m.Groups[1].Value).ToList();

        webIds.ShouldBe(IncidentClassCatalog.All.Select(c => c.Id).ToList());
    }

    // The REAL provider vocabulary, taken from TrafficTypeMapper.MapType — the single map for raw
    // HERE incident types. The first version of this theory invented "brokenDownVehicle" and duly
    // failed: HERE emits "disabledVehicle", and Classify tests Contains("break") which "broken" does
    // not satisfy anyway. Inventing fixture input only confirms the author's guess about production.
    [Theory]
    [InlineData("accident", "Crash")]
    [InlineData("roadClosure", "Closure")]
    [InlineData("disabledVehicle", "Breakdown")]
    [InlineData("roadHazard", "Hazard")]
    [InlineData("construction", "Roadworks")]
    [InlineData("plannedEvent", "SpecialEvent")]
    [InlineData("congestion", "Congestion")]
    // laneRestriction has no closer home than the catalogue's catch-all, and Hazard's own
    // description covers "cautionary notices".
    [InlineData("laneRestriction", "Hazard")]
    [InlineData("", "Hazard")]
    [InlineData(null, "Hazard")]
    public void ClassifyMapsProviderTypesOntoTheCatalogue(string? providerType, string expected)
    {
        IncidentClassCatalog.Classify(providerType).ShouldBe(expected);
    }

    [Fact]
    public void EveryClassifiedValueIsACatalogueId()
    {
        // Classify falls back to Hazard rather than null, so this holds by construction — but it is
        // the property the voicing lookup depends on, and a future branch returning something
        // uncatalogued would produce a block id no prompt defines.
        var ids = IncidentClassCatalog.All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        foreach (string raw in new[]
                 { "accident", "roadClosure", "disabledVehicle", "roadHazard", "laneRestriction",
                   "construction", "plannedEvent", "congestion", "unknownFutureType", "" })
            ids.ShouldContain(IncidentClassCatalog.Classify(raw));
    }

    [Fact]
    public void EveryClassIsConfigurableAndDualDeskClassesSayDifferentThings()
    {
        // COVERAGE: ARN can only configure a class it can address, and an overlay naming a block
        // the prompt does not define is REFUSED — so a missing block presents as a saved edit that
        // quietly does nothing.
        //
        // This test originally demanded EXACTLY ONE desk per class, reasoning that two cards for one
        // class give ARN two places to set one thing. Dev evidence retired that half: BriefDivision
        // .DeskFor routes a corridor to the incidents desk when an incident sits on it, so perth's
        // Orrong Road arrived at writeIncidents classed Congestion at fraction 0.37 while the only
        // voice.congestion block was on writeFlow — the flow desk held the station's rule and never
        // saw the road. Congestion legitimately reaches both desks, so the invariant is now: every
        // class is configurable somewhere, and where a class appears on both desks the two texts
        // must DIFFER, because a queue an incident caused and a queue with nothing behind it are
        // different editorial situations. Identical text on both would be the duplication the
        // original test was right to forbid.
        string incidents = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "ArnBulletinGenerator.Workflow", "Prompts", "writeIncidents.md"));
        string flow = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "ArnBulletinGenerator.Workflow", "Prompts", "writeFlow.md"));

        foreach (string blockId in IncidentClassCatalog.VoicingBlockIds)
        {
            string? onIncidents = BlockBody(incidents, blockId);
            string? onFlow = BlockBody(flow, blockId);

            (onIncidents is not null || onFlow is not null).ShouldBeTrue(
                $"no desk defines {blockId}; ARN cannot configure that class");

            if (onIncidents is not null && onFlow is not null)
                onIncidents.ShouldNotBe(onFlow,
                    $"{blockId} is defined on both desks with the SAME text, which is duplication " +
                    "rather than two distinct editorial situations");
        }
    }

    /// <summary>The body of a marked block, or null when this prompt does not define it.</summary>
    private static string? BlockBody(string prompt, string blockId)
    {
        string marker = $"block: {blockId} ";
        int at = prompt.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return null;
        int bodyStart = prompt.IndexOf('\n', at);
        if (bodyStart < 0)
            return string.Empty;
        int next = prompt.IndexOf("<!-- block:", bodyStart, StringComparison.Ordinal);
        return (next < 0 ? prompt[bodyStart..] : prompt[bodyStart..next]).Trim();
    }

    [Fact]
    public void VoicingBlockIdsAreLowercasedClassIds()
    {
        IncidentClassCatalog.VoicingBlockId("SpecialEvent").ShouldBe("voice.specialevent");
        IncidentClassCatalog.VoicingBlockId("Crash").ShouldBe("voice.crash");
    }
}
