using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
/// Aired on dev 2026-08-25: "The Gateway Mwy through Nudgee is clear" (brisbane 2b0d8fc6) and
/// "Smith St in Southport, Ferry Rd in Bundall" (goldcoast 9d3abf9c). ElevenLabs has no reading for
/// "Mwy" and is free to read "St" as "Saint".
/// </summary>
public sealed class RoadTypeAbbreviationTests
{
    [Theory]
    [InlineData("Gateway Mwy", "Gateway Motorway")]
    [InlineData("Logan Mwy", "Logan Motorway")]
    [InlineData("Smith St", "Smith Street")]
    [InlineData("Ferry Rd", "Ferry Road")]
    [InlineData("Warrego Hwy", "Warrego Highway")]
    [InlineData("Hedges Ave", "Hedges Avenue")]
    [InlineData("Bermuda St", "Bermuda Street")]
    // Trailing full stop is part of the abbreviation, not the sentence.
    [InlineData("Ferry Rd.", "Ferry Road")]
    public void ToSpeakable_ExpandsTheAbbreviatedRoadType(string raw, string expected)
    {
        RoadNameFormatter.ToSpeakable(raw).ShouldBe(expected);
    }

    [Theory]
    // "St" as a saint, not a street - the classic false positive this must not create.
    [InlineData("St Kilda Road")]
    // Already spelled out.
    [InlineData("Gateway Motorway")]
    [InlineData("Captain Cook Highway")]
    // Not a road-type token at all.
    [InlineData("Sturt Highway")]
    public void ToSpeakable_LeavesTheseAlone(string raw)
    {
        RoadNameFormatter.ToSpeakable(raw).ShouldBe(raw);
    }

    [Fact]
    public void ToSpeakable_ExpandsEveryAbbreviationInOneString()
    {
        RoadNameFormatter.ToSpeakable("Smith St in Southport, Ferry Rd in Bundall")
            .ShouldBe("Smith Street in Southport, Ferry Road in Bundall");
    }
}
