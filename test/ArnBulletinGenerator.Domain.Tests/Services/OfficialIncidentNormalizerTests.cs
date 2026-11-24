using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class OfficialIncidentNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StateRoad_IsNormalizedAsSignificantRoad()
    {
        var incident = OfficialIncidentNormalizer.ToIncident(
            Event("M4", isStateRoad: true), Now);

        incident.FunctionalClass.ShouldBe(2);
    }

    [Fact]
    public void MotorwayName_IsNormalizedAsSignificantWhenAuthorityClassIsAbsent()
    {
        var incident = OfficialIncidentNormalizer.ToIncident(
            Event("M4 Western Motorway", isStateRoad: null), Now);

        incident.FunctionalClass.ShouldBe(2);
    }

    [Fact]
    public void SourceDetailUsesImpactDetailNotAdviceDescription()
    {
        const string advice = "Allow extra travel time";
        const string detail = "The left lane is closed while crews clear the crash.";
        var incident = OfficialIncidentNormalizer.ToIncident(
            new OfficialTrafficEvent(
                SourceId: "livetraffic-nsw",
                Headline: "Crash on M4",
                IsIncidentClass: true,
                Shape: [new CoveragePoint(-33.8, 151.2)],
                RoadName: "M4",
                Description: advice,
                Detail: detail),
            Now);

        incident.Fact.ShouldNotBeNull();
        incident.Fact!.SourceDetail.Value.ShouldBe(detail);
        incident.Fact.SourceDetail.Value.ShouldNotBe(advice);
        new[]
        {
            "Allow extra travel time", "Check signage", "Exercise caution",
            "Use an alternative route", "Plan your journey", "Reduce your speed",
            "Follow directions on site"
        }.ShouldNotContain(incident.Fact.SourceDetail.Value);
    }

    [Fact]
    public void OfficialMetadataFlowsOntoIncidentFact()
    {
        var incident = OfficialIncidentNormalizer.ToIncident(
            new OfficialTrafficEvent(
                SourceId: "qldtraffic",
                Headline: "Crash on M1",
                IsIncidentClass: true,
                Shape: [new CoveragePoint(-27.5, 153.0)],
                RoadName: "M1",
                AuthorityPriority: "High",
                Category: "Crash",
                SubCategory: "Multi-vehicle",
                ImpactType: "Lane blocked",
                QualitativeDelay: "Major delays",
                SpeedLimitKph: 40,
                LanesClosed: 1),
            Now);

        var fact = incident.Fact.ShouldNotBeNull();
        fact.AuthorityPriority.Value.ShouldBe("High");
        fact.AuthorityPriorityRank.Value.ShouldBe(1);
        fact.Category.Value.ShouldBe("Crash");
        fact.SubCategory.Value.ShouldBe("Multi-vehicle");
        fact.ImpactType.Value.ShouldBe("Lane blocked");
        fact.QualitativeDelay.Value.ShouldBe("Major delays");
        fact.SpeedLimitKph.Value.ShouldBe(40);
        fact.LanesClosed.Value.ShouldBe(1);
    }

    [Fact]
    public void OfficialWithoutNativeId_DoesNotInventSourceIdentity()
    {
        var incident = OfficialIncidentNormalizer.ToIncident(Event("M4", isStateRoad: true), Now);

        incident.SourceRecordIdentities.ShouldBeNull();
    }
    [Fact]
    public void CanonicalClass_TakesPrecedenceOverCrashWordsInProse()
    {
        var incident = OfficialIncidentNormalizer.ToIncident(
            new OfficialTrafficEvent(
                SourceId: "livetraffic-nsw",
                Headline: "Scheduled roadwork on M4",
                IsIncidentClass: true,
                Shape: [new CoveragePoint(-33.8, 151.2)],
                RoadName: "M4",
                Description: "Crews are clearing a crash nearby.",
                CanonicalClass: IncidentClass.Roadworks),
            Now);

        incident.Type.ShouldBe("construction");
        incident.Severity.ShouldBe(2);
    }


    [Theory]
    [InlineData("LOCAL ROAD")]
    [InlineData("Unknown Location")]
    public void ToIncident_PlaceholderRoadName_RemainsUnvoiceable(string roadName)
    {
        var incident = OfficialIncidentNormalizer.ToIncident(Event(roadName, isStateRoad: null), Now);

        incident.Location.ShouldBeEmpty();
        incident.Fact.ShouldNotBeNull();
        incident.Fact!.RoadName.ShouldBeNull();
        incident.Fact.Identity.ShouldBe(IdentityVerdict.Unnamed);
    }

    private static OfficialTrafficEvent Event(string roadName, bool? isStateRoad) =>
        new(
            SourceId: "livetraffic-nsw",
            Headline: $"Crash on {roadName}",
            IsIncidentClass: true,
            Shape: [new CoveragePoint(-33.8, 151.2)],
            RoadName: roadName,
            IsStateRoad: isStateRoad);
}
