using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Pilot day-2 operator feedback (KIIS Sydney, 2026-08-18): "Western Motorway is known to
///     everyone as just the M4". The alias boundary must produce the on-air designation from
///     every form HERE emits, unify grouping across mixed forms, and keep expert-entered
///     priority-road config matching.
/// </summary>
public sealed class RoadDesignationAliasesTests
{
    [Theory]
    [InlineData("Western Mwy", "M4")]
    [InlineData("Western Motorway", "M4")]
    [InlineData("M4 Western Motorway", "M4")]
    [InlineData("south western mwy", "M5")]
    [InlineData("M2 Hills Mwy", "M2")]
    [InlineData("Westlink M7", "M7")]
    [InlineData("M1 Pacific Motorway", "M1")]
    public void GazettedSydneyMotorwayNames_CanonicalizeToDesignations(string gazetted, string designation)
    {
        RoadDesignationAliases.Canonicalize(gazetted).ShouldBe(designation);
    }

    [Theory]
    [InlineData("Parramatta Road")]
    [InlineData("Pacific Motorway")] // ambiguous in Brisbane (M3 inner section) — deliberately unmapped
    [InlineData("Warringah Freeway")]
    [InlineData("")]
    public void UnmappedNames_PassThroughUnchanged(string name)
    {
        RoadDesignationAliases.Canonicalize(name).ShouldBe(name);
    }

    [Fact]
    public void Resolver_MixedGazettedFormsOfOneMotorway_VoteAsOneName_NeverSplit()
    {
        // A record spanning segments HERE names "Western Mwy" and "M4 Western Motorway" is one
        // road; before canonicalizing at the vote these two forms could split the verdict.
        var roads = new Dictionary<string, SegmentRoad>
        {
            ["a"] = new("Western Mwy", 1, false),
            ["b"] = new("M4 Western Motorway", 1, false)
        };

        var identity = RoadIdentityResolver.Resolve([("a", 400), ("b", 350)], roads);

        identity.Verdict.ShouldBe(IdentityVerdict.Unambiguous);
        identity.Name.ShouldBe("M4");
    }

    [Fact]
    public void PriorityRoadEnteredAsGazettedName_MatchesTheDesignationCorridor()
    {
        // ARN's priority-roads config may predate the alias map; "Western Motorway" must still
        // boost an "M4" corridor.
        PriorityRoads.IsMatch("M4", "Western Motorway").ShouldBeTrue();
        PriorityRoads.IsMatch("M4", "M4 Western Motorway").ShouldBeTrue();
        PriorityRoads.IsMatch("M4", "M40").ShouldBeFalse();
    }
}
