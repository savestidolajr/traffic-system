using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

/// <summary>
/// The incident Summary reaches the writer as the "lanes" evidence unit and is spoken verbatim, so
/// it needs the same normalisation as every other voiced HERE string.
/// </summary>
public sealed class LaneSummarySpeakableTests
{
    [Fact]
    public void ToSpeakable_UnglusRoadTypeInALaneSummary()
    {
        // Verbatim from the 2026-08-25 panel fixture; it aired as "the closed ramp to Alfred
        // StreetNorth", which TTS reads as one garbled word.
        RoadNameFormatter.ToSpeakable("Ramp to Alfred StreetNorth - Closed")
            .ShouldContain("Alfred Street North");
    }

    [Fact]
    public void ToSpeakable_LeavesGenuineCompoundNamesAlone()
    {
        // A compound that does not begin with a full road-type word must pass through untouched.
        RoadNameFormatter.ToSpeakable("CityLink closed").ShouldContain("CityLink");
    }
}
