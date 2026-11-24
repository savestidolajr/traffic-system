using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class ClaimGateTests
{
    private static IncidentFact Fact(
        string road = "Warringah Freeway",
        int code = 401,
        AffectedAsset asset = AffectedAsset.Carriageway,
        IdentityVerdict identity = IdentityVerdict.Unambiguous,
        bool roadClosed = true,
        WindowClass window = WindowClass.Fresh,
        Option<string> rampTarget = default,
        DirectionSource directionSource = DirectionSource.HereDescription) =>
        new(
            IncidentId: "F2",
            OriginalId: "F2",
            Condition: IncidentConditionCatalog.FromCodes([code]),
            Asset: asset,
            RoadName: road,
            Identity: identity,
            Anchor: default,
            SecondaryAnchor: default,
            RampTarget: rampTarget,
            Direction: Option<string>.Some("southbound"),
            DirectionSource: directionSource,
            Suburb: Option<string>.Some("Cammeray"),
            RoadClosed: roadClosed,
            Window: window,
            LaneDetail: default,
            SourceDetail: default);

    [Fact]
    public void RampClosure_IsAllowedOnlyWhenTheRampIsNamed()
    {
        // F2, job cbeb6402: "Ramp to Alfred StreetNorth - Closed" aired as "the Warringah Freeway's
        // CLOSED southbound". A ramp closure may be claimed only when the claim can name the ramp.
        var named = Fact(asset: AffectedAsset.Ramp, rampTarget: Option<string>.Some("Alfred Street North"));
        var unnamed = Fact(asset: AffectedAsset.Ramp);

        ClaimGate.CanClaimClosure(named).Allowed.ShouldBeTrue();
        var denied = ClaimGate.CanClaimClosure(unnamed);
        denied.Allowed.ShouldBeFalse();
        denied.Reason.ShouldBe(ClaimGate.RampTargetUnknown);
    }

    [Fact]
    public void CarriagewayClosure_IsAllowedOnTheRoad()
    {
        ClaimGate.CanClaimClosure(Fact()).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void UnresolvedIdentity_CannotCarryAClosure()
    {
        var verdict = ClaimGate.CanClaimClosure(Fact(identity: IdentityVerdict.Split));

        verdict.Allowed.ShouldBeFalse();
        verdict.Reason.ShouldBe(ClaimGate.IdentityUnresolved);
    }

    [Fact]
    public void LongPlannedWorksite_IsNotThisBulletinsClosureNews()
    {
        // Measured live: windows of 693, 166, 165, 131 and 97 days were all being reported as current.
        var verdict = ClaimGate.CanClaimClosure(Fact(window: WindowClass.Scheduled));

        verdict.Allowed.ShouldBeFalse();
        verdict.Reason.ShouldBe(ClaimGate.ScheduledWindow);
    }

    [Fact]
    public void UnknownAsset_CannotCarryAClosure()
    {
        var verdict = ClaimGate.CanClaimClosure(Fact(asset: AffectedAsset.Unknown));

        verdict.Allowed.ShouldBeFalse();
        verdict.Reason.ShouldBe(ClaimGate.AssetUnknown);
    }

    [Fact]
    public void ARecordHereDoesNotReportAsClosed_IsNotAClosure()
    {
        var verdict = ClaimGate.CanClaimClosure(Fact(code: 701, roadClosed: false));

        verdict.Allowed.ShouldBeFalse();
        verdict.Reason.ShouldBe(ClaimGate.NotClosed);
    }

    [Fact]
    public void UnnameableOrUnknownRecords_AreWithheldEntirely()
    {
        ClaimGate.CanVoice(Fact(identity: IdentityVerdict.Split)).Reason.ShouldBe(ClaimGate.IdentitySplit);
        ClaimGate.CanVoice(Fact(identity: IdentityVerdict.Unnamed)).Reason.ShouldBe(ClaimGate.IdentityUnnamed);
        ClaimGate.CanVoice(Fact(code: 99999)).Reason.ShouldBe(ClaimGate.ConditionUnknown);
        ClaimGate.CanVoice(Fact()).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void DirectionNeedsASource()
    {
        // The compass-from-coverage-centre derivation made direction a property of which station
        // fetched the record: one 457 m record aired citybound 15 times and outbound once.
        ClaimGate.CanClaimDirection(Fact(directionSource: DirectionSource.None))
            .Reason.ShouldBe(ClaimGate.DirectionUnsourced);
        ClaimGate.CanClaimDirection(Fact(directionSource: DirectionSource.HereDescription))
            .Allowed.ShouldBeTrue();
        ClaimGate.CanClaimDirection(Fact(directionSource: DirectionSource.SegmentOriented))
            .Allowed.ShouldBeTrue();
    }
}
