using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

/// <summary>
///     The exclusion contract, and the invariant that replaced post-hoc corridor filtering: a record
///     an operator ruled out must be gone BEFORE aggregation, so no corridor field can describe it.
///     <para>
///         Modelled on the live defect (dev job <c>e234aa5a</c>, 2026-08-24). Church Street carried a
///         639 m northbound roadworks record plus two 20 m records with no direction at all. Omitting
///         the big one and filtering the fetched corridor afterwards left a corridor of two 20 m
///         records still claiming <c>northbound</c> and 700 m — and the bulletin aired "Northbound on
///         Church Street at Granville". <c>ClaimGate</c> could not stop it: the claim came from the
///         corridor header, not from a surviving fact.
///     </para>
/// </summary>
public class IncidentExclusionsTests
{
    private const string BigRecordId = "4021163310322524631";
    private const string BigEventKey = $"here:original:{BigRecordId}";

    /// <summary>The 639 m northbound record — the only one of the three that supports a direction.</summary>
    private static TrafficIncident BigNorthboundRecord() =>
        Here(BigRecordId, BigRecordId, "Between M4 Western Motorway and Church Street - Roadworks",
            lengthMeters: 639, lat: -33.8270, lng: 151.0100, direction: "northbound");

    private static TrafficIncident[] SmallDirectionlessRecords() =>
    [
        Here("2811382038848349350", "546436053343998377", "Roadworks",
            lengthMeters: 20, lat: -33.8272, lng: 151.0102),
        Here("418437814960187338", "1460628680443623881", "Roadworks",
            lengthMeters: 20, lat: -33.8274, lng: 151.0104)
    ];

    private static TrafficIncident Here(
        string recordId, string originalId, string description,
        double lengthMeters, double lat, double lng, string? direction = null) =>
        new(recordId, description, 3, "Church Street", "construction", lengthMeters, 3,
            Option<double>.Some(lat), Option<double>.Some(lng),
            Direction: direction is null ? default : Option<string>.Some(direction),
            OriginalId: originalId,
            SourceRecordIdentities: [new SourceRecordIdentity("here", recordId, originalId)]);

    private static IReadOnlyList<TrafficCorridor> AggregateSurviving(
        IEnumerable<TrafficIncident> incidents, IncidentExclusions exclusions) =>
        CorridorAggregator.AggregateByRoad(
            incidents.Where(i => !exclusions.Excludes(i)).ToList(), maxCorridors: 20);

    [Fact]
    public void WithoutExclusion_TheCorridorTakesItsDirectionAndLengthFromTheBigRecord()
    {
        TrafficIncident[] all = [BigNorthboundRecord(), .. SmallDirectionlessRecords()];

        var corridor = AggregateSurviving(all, IncidentExclusions.None).ShouldHaveSingleItem();

        // Baseline: this is what the operator is looking at when they decide to omit.
        corridor.Direction.HasValue.ShouldBeTrue();
        corridor.Direction.Value.ShouldBe("northbound");
        corridor.TotalLengthMeters.ShouldBe(679);
    }

    [Fact]
    public void ExcludedRecord_TakesItsDirectionLengthAndImpactWithIt()
    {
        TrafficIncident[] all = [BigNorthboundRecord(), .. SmallDirectionlessRecords()];
        var exclusions = new IncidentExclusions([BigEventKey], []);

        var corridor = AggregateSurviving(all, exclusions).ShouldHaveSingleItem();

        // The survivors support no direction between them, so none may be claimed. Under the old
        // post-hoc filter this still read "northbound" and went to air.
        corridor.Direction.HasValue.ShouldBeFalse();
        corridor.TotalLengthMeters.ShouldBe(40);
        corridor.Incidents.Select(i => i.Id).ShouldNotContain(BigRecordId);
        corridor.ImpactScore.ShouldBeLessThan(
            AggregateSurviving(all, IncidentExclusions.None).Single().ImpactScore);
    }

    [Fact]
    public void ExcludedRecord_IsIndistinguishableFromNeverHavingBeenFetched()
    {
        var survivors = SmallDirectionlessRecords();
        TrafficIncident[] all = [BigNorthboundRecord(), .. survivors];

        var withExclusion = AggregateSurviving(all, new IncidentExclusions([BigEventKey], []));
        var neverFetched = AggregateSurviving(survivors, IncidentExclusions.None);

        // Every derived field, not just the member list: the whole point of excluding before
        // aggregation rather than filtering after it. (Projected rather than compared as records —
        // Option<T>.ToString() throws on None, so a record diff cannot even be rendered.)
        Describe(withExclusion).ShouldBe(Describe(neverFetched));
    }

    /// <summary>Every corridor field a bulletin can make a claim from, in printable form.</summary>
    private static IReadOnlyList<string> Describe(IEnumerable<TrafficCorridor> corridors) =>
        corridors.Select(c => string.Join(" | ",
            c.RoadName, c.Severity, c.FunctionalClass, c.TotalLengthMeters, c.ImpactScore,
            c.PrimaryType, c.Description,
            c.Direction.Match(d => d, () => "-"),
            c.Suburb.Match(s => s, () => "-"),
            c.Extent.Match(e => e, () => "-"),
            string.Join(",", c.Incidents.Select(i => i.Id)))).ToList();

    [Fact]
    public void EveryRecordExcluded_LeavesNoCorridorAtAll()
    {
        TrafficIncident[] all = [BigNorthboundRecord(), .. SmallDirectionlessRecords()];
        var exclusions = new IncidentExclusions(
            all.Select(i => $"here:original:{i.OriginalId}"), []);

        AggregateSurviving(all, exclusions).ShouldBeEmpty();
    }

    [Fact]
    public void DeskVerdicts_MatchTheLedgerKey_NotTheRevisedRecordId()
    {
        // HERE re-stamps Id on every revision; the Desk keyed its verdict on the stable original.
        var revised = Here("2374869809755945913", "1337132862205035110", "Closed",
            lengthMeters: 100, lat: -33.90, lng: 151.18);

        new IncidentExclusions(["here:original:1337132862205035110"], [])
            .Excludes(revised).ShouldBeTrue();
        new IncidentExclusions(["2374869809755945913"], [])
            .Excludes(revised).ShouldBeFalse();
    }

    [Fact]
    public void RundownExclusions_MatchTheRawRecordId_CaseInsensitively()
    {
        var record = Here("Abc-123", "Abc-123", "Roadworks", 100, -33.90, 151.18);

        new IncidentExclusions([], ["abc-123"]).Excludes(record).ShouldBeTrue();
        new IncidentExclusions([], ["something-else"]).Excludes(record).ShouldBeFalse();
    }

    [Fact]
    public void Drops_RecordReasonAndRoad_OnceEvenWhenFilteredTwice()
    {
        var record = BigNorthboundRecord();
        var exclusions = new IncidentExclusions([BigEventKey], ["418437814960187338"]);

        exclusions.Excludes(record).ShouldBeTrue();
        // Fusion normalizes and re-checks the same event; the audit line must not double up.
        exclusions.Excludes(record).ShouldBeTrue();
        exclusions.Excludes(SmallDirectionlessRecords()[1]).ShouldBeTrue();

        exclusions.Drops.Count.ShouldBe(2);
        exclusions.Drops[0].Reason.ShouldBe(IncidentExclusions.Reason.DeskVerdict);
        exclusions.Drops[0].Description.ShouldStartWith("Church Street:");
        exclusions.Drops[1].Reason.ShouldBe(IncidentExclusions.Reason.OperatorExcluded);
    }

    [Fact]
    public void FetchToken_IsStableRegardlessOfOrder_AndSeparatesDifferentSets()
    {
        var a = new IncidentExclusions(["here:original:1", "here:original:2"], ["r1"]);
        var b = new IncidentExclusions(["here:original:2", "here:original:1"], ["R1"]);
        var c = new IncidentExclusions(["here:original:1"], ["r1"]);

        a.FetchToken.ShouldBe(b.FetchToken);
        a.FetchToken.ShouldNotBe(c.FetchToken);
        IncidentExclusions.None.FetchToken.ShouldBe("-");
    }
}
