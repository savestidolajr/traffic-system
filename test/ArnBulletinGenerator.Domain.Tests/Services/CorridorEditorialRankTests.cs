using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Regression coverage for the single editorial ordering shared by candidate selection and
///     evidence ranking. Fixtures are deliberately shaped like the Sydney live data that exposed
///     the old ImpactScore-only cut.
/// </summary>
public sealed class CorridorEditorialRankTests
{
    private static TrafficCorridor Corridor(
        string primaryType = "congestion",
        int functionalClass = 1,
        int? delayMinutes = null,
        double impactScore = 50,
        IReadOnlyList<TrafficIncident>? incidents = null,
        IReadOnlyList<OfficialEvidence>? officialEvidences = null,
        double? paceRatio = null) =>
        new(
            "Test Road",
            Severity: 1,
            FunctionalClass: functionalClass,
            TotalLengthMeters: 0,
            ImpactScore: impactScore,
            PrimaryType: primaryType,
            Description: "test",
            Incidents: incidents ?? [],
            DriveTimeMinutes: delayMinutes is null ? default : Option<int>.Some(20 + delayMinutes.Value),
            NormalDriveTimeMinutes: delayMinutes is null ? default : Option<int>.Some(20),
            OfficialEvidences: officialEvidences,
            PaceRatio: paceRatio is null ? default : Option<double>.Some(paceRatio.Value));

    private static IncidentFact FactWithLanesClosed(int lanes) => new(
        IncidentId: "incident-1",
        OriginalId: "incident-1",
        Condition: new(IncidentConditionKind.ConstructionWork, "construction", false, true),
        Asset: AffectedAsset.Carriageway,
        RoadName: "Test Road",
        Identity: IdentityVerdict.Unambiguous,
        Anchor: default,
        SecondaryAnchor: default,
        RampTarget: default,
        Direction: default,
        DirectionSource: DirectionSource.None,
        Suburb: default,
        RoadClosed: false,
        Window: WindowClass.Fresh,
        LaneDetail: default,
        SourceDetail: default,
        LanesClosed: Option<int>.Some(lanes));

    private static TrafficIncident Incident(string type, IncidentFact? fact = null) =>
        new("incident-1", "test", 1, "Test Road", type, Fact: fact);

    [Fact]
    public void Of_AccidentWithDelay_OutranksMinorSlowdown()
    {
        var forestWay = Corridor("accident", functionalClass: 2, delayMinutes: 0, impactScore: 160);
        var minorCongestion = Corridor("congestion", functionalClass: 3, delayMinutes: 1, paceRatio: 0.85);

        CorridorEditorialRank.Of(forestWay).ShouldBeGreaterThan(CorridorEditorialRank.Of(minorCongestion));
    }

    [Fact]
    public void Of_SevereDelay_OutranksLightDelayOnSameClass()
    {
        var parramattaRoad = Corridor("congestion", functionalClass: 2, delayMinutes: 15, paceRatio: 0.35);
        var generalHolmesDrive = Corridor("congestion", functionalClass: 2, delayMinutes: 3, paceRatio: 0.75);

        CorridorEditorialRank.Of(parramattaRoad).ShouldBeGreaterThan(CorridorEditorialRank.Of(generalHolmesDrive));
    }

    [Fact]
    public void Of_HeavyCrawl_ContributesSubstantialScore()
    {
        var heavyCrawl = Corridor("congestion", functionalClass: 3, delayMinutes: 1, paceRatio: 0.20);
        var lightSlowdown = Corridor("congestion", functionalClass: 3, delayMinutes: 1, paceRatio: 0.70);

        CorridorEditorialRank.Of(heavyCrawl).ShouldBeGreaterThan(CorridorEditorialRank.Of(lightSlowdown));
    }

    [Fact]
    public void IsBlockage_RecognisesEveryTypedBlockingShape()
    {
        CorridorEditorialRank.IsBlockage(Corridor("roadClosure")).ShouldBeTrue();
        CorridorEditorialRank.IsBlockage(Corridor("accident")).ShouldBeTrue();
        CorridorEditorialRank.IsBlockage(Corridor(
            "construction",
            incidents: [Incident("construction", FactWithLanesClosed(2))])).ShouldBeTrue();
        CorridorEditorialRank.IsBlockage(Corridor(
            "construction",
            officialEvidences: [new OfficialEvidence("authority", "lane closure", false, LanesClosed: 1)]))
            .ShouldBeTrue();
        CorridorEditorialRank.IsBlockage(Corridor("congestion")).ShouldBeFalse();
    }

    [Fact]
    public void Of_FlowRoad_DefaultScoreIsLowerThanMajorIncident()
    {
        var flow = new FlowRoad("M1 Pacific Motorway", JamFactor: 0.5, LengthMeters: 5000, FunctionalClass: 1);
        var majorCrash = Corridor("accident", functionalClass: 1, delayMinutes: 10);

        CorridorEditorialRank.Of(flow).ShouldBeLessThan(CorridorEditorialRank.Of(majorCrash));
    }

    [Fact]
    public void Of_FlowRoad_CustomFlowPriorityScoreBoostsClearRun()
    {
        var settings = new EditorialSettings(
            EditorialScope.Station, "2DAY",
            FlowPriorityBand: 120);
        var flow = new FlowRoad("M1 Pacific Motorway", JamFactor: 0.5, LengthMeters: 5000, FunctionalClass: 1);
        var minorQueue = Corridor("congestion", functionalClass: 4, delayMinutes: 1);

        var flowScore = CorridorEditorialRank.Of(flow, settings);
        var minorScore = CorridorEditorialRank.Of(minorQueue, settings);

        flowScore.ShouldBeGreaterThan(minorScore);
    }

    [Fact]
    public void Of_PriorityRoadBandBoost_AddsConfiguredPoints()
    {
        var settings = new EditorialSettings(
            EditorialScope.Station, "2DAY",
            PriorityRoadBandBoost: 50);
        var normalCorridor = Corridor("congestion", functionalClass: 2, delayMinutes: 5);
        var priorityCorridor = Corridor("congestion", functionalClass: 2, delayMinutes: 5) with { PriorityBoosted = true };

        var normalScore = CorridorEditorialRank.Of(normalCorridor, settings);
        var priorityScore = CorridorEditorialRank.Of(priorityCorridor, settings);

        priorityScore.ShouldBe(normalScore + 50);
    }
}
