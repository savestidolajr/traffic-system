using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The data boundary that retired the pilot's phantom-closure class: every transform is the
///     documented answer to a measured defect (Warringah Freeway scheduled sub-closure aired as live
///     closure news; ramp closures aired as mainline; direction invented from the coverage centre).
/// </summary>
public sealed class IncidentEligibilityTests
{
    private static IncidentFact Fact(
        string road = "Warringah Freeway",
        int code = 401,
        AffectedAsset asset = AffectedAsset.Carriageway,
        IdentityVerdict identity = IdentityVerdict.Unambiguous,
        bool roadClosed = true,
        WindowClass window = WindowClass.Fresh,
        Option<string> rampTarget = default,
        Option<string> anchor = default,
        Option<string> suburb = default,
        DirectionSource directionSource = DirectionSource.HereDescription) =>
        new(
            IncidentId: "INC1",
            OriginalId: "INC1",
            Condition: IncidentConditionCatalog.FromCodes([code]),
            Asset: asset,
            RoadName: road,
            Identity: identity,
            Anchor: anchor,
            SecondaryAnchor: default,
            RampTarget: rampTarget,
            Direction: Option<string>.Some("southbound"),
            DirectionSource: directionSource,
            Suburb: suburb,
            RoadClosed: roadClosed,
            Window: window,
            LaneDetail: default,
            SourceDetail: default);

    private static TrafficIncident Incident(IncidentFact? fact, string type = "roadClosure",
        bool roadClosed = true, string location = "Warringah Freeway",
        Option<string> direction = default) =>
        new("INC1", "Closed", 1, location, type,
            RoadClosed: roadClosed, Direction: direction, Fact: fact);

    [Fact]
    public void ScheduledProjectClosure_IsDemotedToRoadworks_NeverClosureNews()
    {
        // The Warringah class: months-long worksite sub-closure, entered the feed weeks ago.
        var incident = Incident(Fact(window: WindowClass.Scheduled, suburb: Option<string>.Some("Cammeray")));

        var outcome = IncidentEligibility.Transform([incident]);

        var kept = outcome.Incidents.ShouldHaveSingleItem();
        kept.RoadClosed.ShouldBeFalse();
        kept.Type.ShouldBe("construction");
        IncidentEligibility.IsWithheld(kept).ShouldBeFalse();
        outcome.Ledger.ShouldHaveSingleItem().Reason.ShouldBe(ClaimGate.ScheduledWindow);
    }

    [Fact]
    public void FreshPlacedCarriagewayClosure_IsKeptAsClosureNews()
    {
        var incident = Incident(Fact(anchor: Option<string>.Some("Alfred Street North")));

        var outcome = IncidentEligibility.Transform([incident]);

        var kept = outcome.Incidents.ShouldHaveSingleItem();
        kept.RoadClosed.ShouldBeTrue();
        kept.Type.ShouldBe("roadClosure");
        outcome.Ledger.ShouldBeEmpty();
    }

    [Fact]
    public void RampClosure_IsScopedToAHazardNamingTheRamp_NeverTheRoad()
    {
        // 129 ramp-closure records measured; 122 aired as mainline closures of the roads they leave.
        var incident = Incident(Fact(
            asset: AffectedAsset.Ramp,
            rampTarget: Option<string>.Some("Alfred Street North"),
            suburb: Option<string>.Some("Cammeray")));

        var outcome = IncidentEligibility.Transform([incident]);

        var kept = outcome.Incidents.ShouldHaveSingleItem();
        kept.RoadClosed.ShouldBeFalse();
        kept.Type.ShouldBe("roadHazard");
        outcome.Ledger.ShouldHaveSingleItem().Reason.ShouldBe("ramp-scope");
    }

    [Fact]
    public void UnnamedIdentityClosure_IsWithheldFromCorridors_NothingHonestCanBeSaid()
    {
        var incident = Incident(Fact(identity: IdentityVerdict.Unnamed));

        var outcome = IncidentEligibility.Transform([incident]);

        var kept = outcome.Incidents.ShouldHaveSingleItem();
        IncidentEligibility.IsWithheld(kept).ShouldBeTrue();
        kept.WithheldReason.Value.ShouldBe($"withheld:{ClaimGate.IdentityUnnamed}");
    }

    [Fact]
    public void UnnamedIdentityNonClosure_StillFlowsForRanking_CarryingTheAuditFlag()
    {
        // ClaimGate.CanVoice: an unresolved record "may inform ranking, but it may not be claimed".
        // Corridor exclusion would change selection behaviour; the per-claim gate lands with the
        // script-generation wave. Until then the record flows, flagged.
        var incident = Incident(
            Fact(identity: IdentityVerdict.Unnamed, roadClosed: false, code: 701),
            type: "construction", roadClosed: false);

        var outcome = IncidentEligibility.Transform([incident]);

        var kept = outcome.Incidents.ShouldHaveSingleItem();
        IncidentEligibility.IsWithheld(kept).ShouldBeFalse();
        kept.WithheldReason.Value.ShouldBe($"flagged:{ClaimGate.IdentityUnnamed}");
    }

    [Fact]
    public void ClosureWithUnknownAsset_IsWithheld_NothingHonestCanBeSaid()
    {
        var incident = Incident(Fact(asset: AffectedAsset.Unknown));

        var outcome = IncidentEligibility.Transform([incident]);

        IncidentEligibility.IsWithheld(outcome.Incidents.ShouldHaveSingleItem()).ShouldBeTrue();
        outcome.Ledger.ShouldHaveSingleItem().Reason.ShouldBe(ClaimGate.AssetUnknown);
    }

    [Fact]
    public void UnplacedClosure_IsDemoted_UnlessASiblingOnTheSameRoadCarriesALocator()
    {
        // "Closed" with no locator anywhere claims the entire road is shut (Canning Hwy, Perth:
        // a 14 km arterial reported shut over 45 m of it).
        var bare = Incident(Fact());
        var outcomeAlone = IncidentEligibility.Transform([bare]);
        outcomeAlone.Incidents.ShouldHaveSingleItem().RoadClosed.ShouldBeFalse();
        outcomeAlone.Ledger.ShouldHaveSingleItem().Reason.ShouldBe(ClaimGate.ClosureUnplaced);

        // The same record survives when a voiceable sibling on the SAME road can place the line.
        var sibling = Incident(
            Fact(code: 701, roadClosed: false, anchor: Option<string>.Some("Falcon Street")),
            type: "construction", roadClosed: false);
        var outcomeTogether = IncidentEligibility.Transform([bare, sibling]);
        outcomeTogether.Incidents.First(i => i.Id == "INC1" && i.Type == "roadClosure")
            .RoadClosed.ShouldBeTrue();
    }

    [Fact]
    public void UnsourcedDirection_IsStripped_NeverInvented()
    {
        // One 457 m record aired citybound 15 times and outbound once from byte-identical input.
        var incident = Incident(
            Fact(roadClosed: false, code: 701, directionSource: DirectionSource.None),
            type: "construction", roadClosed: false,
            direction: Option<string>.Some("citybound"));

        var outcome = IncidentEligibility.Transform([incident]);

        outcome.Incidents.ShouldHaveSingleItem().Direction.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void FactlessRecord_PassesThroughUntouched()
    {
        // A persisted job or regression fixture rebuilt outside the live pipeline: absence of a
        // fact is absence of the measurement, not proof of a defect.
        var incident = Incident(null, direction: Option<string>.Some("northbound"));

        var outcome = IncidentEligibility.Transform([incident]);

        var kept = outcome.Incidents.ShouldHaveSingleItem();
        kept.ShouldBe(incident);
        outcome.Ledger.ShouldBeEmpty();
    }
}
