using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     An abbreviation the writer can expand but the provenance mapper cannot costs ARN the evidence
///     trail for a voiced incident.
/// </summary>
/// <remarks>
///     Live dev Sydney job, 2026-08-23: HERE returned the corridor as "Light Horse Intr", the writer
///     correctly read "the Light Horse Interchange", and the bulletin aired — but the evidence panel
///     matches spoken text back to the source road name, so ARN saw an incident on air with nothing
///     behind it. The 2026-08-03 "Koonung Creek Trl" case was the same cause with a worse symptom: the
///     grounding gate binned a finished KIIS 101.1 bulletin. These two assertions together are the
///     contract that matters — expandable AND matchable.
/// </remarks>
public sealed class RoadNameAbbreviationProvenanceTests
{
    [Theory]
    [InlineData("Light Horse Intr", "Light Horse Interchange")]
    [InlineData("Koonung Creek Trl", "Koonung Creek Trail")]
    public void AnAbbreviationIsSpeakable(string source, string spoken) =>
        RoadNameFormatter.ToSpeakable(source).ShouldBe(spoken);

    [Fact]
    public void TheSpokenFormStillMatchesItsSourceCorridor()
    {
        var corridors = new List<TrafficCorridor>
        {
            new("Light Horse Intr", 1, 2, 500, 40, "roadHazard", "queue to the M4 ramp", [])
        };

        var matched = ScriptProvenanceMapper.MentionedRoadNames(
            "Westbound at the Light Horse Interchange, a short queue reaches the M4 ramp.", corridors);

        matched.ShouldContain("Light Horse Intr",
            "The read voices the expanded name; the evidence panel resolves it back to the source "
            + "corridor. If this fails, a voiced incident shows on air with no evidence behind it.");
    }
}
