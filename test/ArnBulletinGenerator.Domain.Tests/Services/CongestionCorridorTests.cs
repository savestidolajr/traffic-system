using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class CongestionCorridorTests
{
    private const double Threshold = 5.0;

    private static CongestionSegment Seg(string road, double jam, double len = 500, double speed = 3, double free = 12) =>
        new(road, jam, len, speed, free);

    private static TrafficCorridor IncidentCorridor(string road) =>
        new(road, 2, 2, 1000, 100, "accident", "desc",
            [new TrafficIncident("1", "d", 2, "loc", "accident")]);

    [Fact]
    public void PromotesHighJamRoadWithNoIncident()
    {
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("M4", 7.5)], [], Threshold);

        result.ShouldHaveSingleItem();
        result[0].RoadName.ShouldBe("M4");
        result[0].PrimaryType.ShouldBe("congestion");
        result[0].Incidents.ShouldBeEmpty();
    }

    [Fact]
    public void HiddenStandstillSubSegment_BecomesCongestion_NotAGoodRun()
    {
        // Issue 3. A parent segment averages over its length, so a standstill inside it reads as
        // free-flowing. Measured live on Sydney 2026-08-13: 21 roads carried ~9.9 km of high-jam
        // subsegments inside parents we handed to the good-run pool — HERE told us the road was
        // stopped and we voiced it as "moving nicely".
        var hidden = new CongestionSegment("Shiers Avenue", JamFactor: 2.0, LengthMeters: 828,
            SpeedMps: 9, FreeFlowMps: 11, MaxSubSegmentJamFactor: 8.4);

        var congestion = CorridorAggregator.BuildCongestionCorridors([hidden], [], Threshold);
        congestion.ShouldHaveSingleItem().RoadName.ShouldBe("Shiers Avenue");

        CorridorAggregator.RankFreeFlowingRoads([hidden], congestion, 5).ShouldBeEmpty();
    }

    [Fact]
    public void HistoricalReading_IsNeverVoicedAsACurrentCondition()
    {
        // Issue 4. HERE's `confidence` marks a reading real-time (>=0.7) or an estimate. Measured
        // live on Sydney: 41 segments were historical and 33 carried none, all voiced as current.
        var estimate = new CongestionSegment("Bringelly Road", JamFactor: 8.1, LengthMeters: 1836,
            SpeedMps: 2, FreeFlowMps: 14, Confidence: 0.45);
        var freeFlowEstimate = estimate with { JamFactor = 0.4, RoadName = "Banksia Street" };

        CorridorAggregator.BuildCongestionCorridors([estimate], [], Threshold).ShouldBeEmpty();
        CorridorAggregator.RankFreeFlowingRoads([freeFlowEstimate], [], 5).ShouldBeEmpty();
    }

    [Fact]
    public void FlowCorridorLength_IsNeverSummedAcrossDisconnectedSegments()
    {
        // Issue 6. Segments are grouped by ROAD NAME with no contiguity test, and HERE returns them
        // across the whole coverage area. Cairns 2026-08-09 published "the queue stretching about
        // forty-five kays" from 45.8 km of summed Port Douglas Road segments. The sum must not
        // survive anywhere it can be presented as one condition — including the ranking input.
        var segs = new[]
        {
            Seg("Port Douglas Road", 9.0, len: 900),
            Seg("Port Douglas Road", 8.0, len: 700),
            Seg("Port Douglas Road", 8.5, len: 1200)
        };

        var corridor = CorridorAggregator.BuildCongestionCorridors(segs, [], Threshold).ShouldHaveSingleItem();

        corridor.TotalLengthMeters.ShouldBe(1200);
        corridor.VoicedExtentMeters.ShouldBe(1200);
    }

    [Fact]
    public void FiltersNonAustralianRouteCodeRoadNames()
    {
        // "W5" is a HERE internal label, not a speakable AU route; "M80" is a real motorway.
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("W5", 8.0), Seg("M80", 7.0)], [], Threshold);

        result.Select(c => c.RoadName).ShouldBe(["M80"]);
    }


    [Fact]
    public void ExcludesSegmentsBelowThreshold()
    {
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("M4", 4.0)], [], Threshold);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void DedupsAgainstAnIncidentCorridorOnTheSameRoad()
    {
        // The M4 already has an incident corridor — congestion must not double-report it.
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("M4", 8.0), Seg("M2", 7.0)], [IncidentCorridor("M4")], Threshold);

        result.Select(c => c.RoadName).ShouldBe(["M2"]);
    }

    [Fact]
    public void SeverityScalesWithTheWeightedJamFactor()
    {
        var standstill = CorridorAggregator.BuildCongestionCorridors([Seg("M1", 9.0)], [], Threshold);
        var heavy = CorridorAggregator.BuildCongestionCorridors([Seg("M1", 7.0)], [], Threshold);
        // A road that is only mildly slow overall stays on the board when it holds a real queue —
        // severity follows the road, the queue earns it the airtime.
        var building = CorridorAggregator.BuildCongestionCorridors(
            [WeighedSeg("M1", lengthMeters: 2000, jammedMeters: 500, restJam: 4.5)], [], Threshold);

        standstill[0].Severity.ShouldBe(1);
        heavy[0].Severity.ShouldBe(2);
        building[0].Severity.ShouldBe(3);
    }

    [Fact]
    public void AggregatesSegmentsOfTheSameRoad()
    {
        // Flow returns many short segments per road; they collapse into one corridor. The corridor's
        // length is the LONGEST single segment, not their sum — the segments share only a road name,
        // so a sum is not a measurement of anything (Cairns 2026-08-09, 45.8 km "queue").
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("Eastern Distributor", 6.0, len: 300), Seg("Eastern Distributor", 7.0, len: 400)],
            [], Threshold);

        result.ShouldHaveSingleItem();
        result[0].TotalLengthMeters.ShouldBe(400);
        result[0].VoicedExtentMeters.ShouldBe(400);
        result[0].Severity.ShouldBe(2); // from the max jamFactor (7.0)
    }

    /// <summary>
    ///     A row carrying HERE's finer subSegment readings: <paramref name="jammedMeters" /> of the row
    ///     is at a standstill and the rest sits at <paramref name="restJam" />.
    /// </summary>
    private static CongestionSegment WeighedSeg(
        string road, double lengthMeters, double jammedMeters, double restJam) =>
        new(road, restJam, lengthMeters, SpeedMps: 3, FreeFlowMps: 12,
            MaxSubSegmentJamFactor: jammedMeters > 0 ? 8.6 : restJam,
            JamFactorMeters: 8.6 * jammedMeters + restJam * (lengthMeters - jammedMeters),
            StandstillMeters: jammedMeters);

    [Fact]
    public void OneStoppedStretch_DoesNotPutAMovingRoadOnTheBoard()
    {
        // Measured live (Sydney, 2026-08-15): the max-anywhere rule called 52 roads "at a standstill"
        // at once. Canterbury Road was one — 363 m of 7,683 m, 5% of its measured length, weighted
        // 2.5. ATN's band is "stopped ... FOR AN EXTENDED STRETCH". The road is moving and its queue
        // is shorter than the hot-spot floor, so it is not a traffic item at all.
        var result = CorridorAggregator.BuildCongestionCorridors(
            [WeighedSeg("Canterbury Road", lengthMeters: 7683, jammedMeters: 363, restJam: 2.2)],
            [], Threshold);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void AStoppedStretchStillCountsAsAHotSpot()
    {
        // The queue is real and a driver will sit in it: it survives as its own fact rather than
        // renaming the road. Gannons Road, same live sample: 665 m stopped inside 3,924 m.
        var result = CorridorAggregator.BuildCongestionCorridors(
            [WeighedSeg("Gannons Road", lengthMeters: 3924, jammedMeters: 665, restJam: 4.0)],
            [], Threshold);

        var road = result.ShouldHaveSingleItem();
        road.Description.ShouldNotBe("at a standstill");
        road.HotSpotMeters.HasValue.ShouldBeTrue();
        road.HotSpotMeters.GetValueOr(0).ShouldBe(665);
    }

    [Fact]
    public void AQueueAtTheLightsIsNotATrafficItem()
    {
        // Below the floor a high reading is a set of lights, not news — the median stretch behind a
        // road we used to call stopped was 250 m.
        var result = CorridorAggregator.BuildCongestionCorridors(
            [WeighedSeg("Lyons Road", lengthMeters: 2763, jammedMeters: 124, restJam: 2.7)],
            [], Threshold);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void AHiddenQueueStillKeepsTheRoadOutOfTheGoodRunPool()
    {
        // Dropping it from the traffic board must NOT promote it to "moving nicely": the good-run
        // pool still reads the worst reading, which is the whole point of tracking it.
        var hidden = WeighedSeg("Lyons Road", lengthMeters: 2763, jammedMeters: 124, restJam: 2.7);

        var corridors = CorridorAggregator.BuildCongestionCorridors([hidden], [], Threshold);

        corridors.ShouldBeEmpty();
        CorridorAggregator.RankFreeFlowingRoads([hidden], corridors, 5).ShouldBeEmpty();
    }

    [Fact]
    public void ARoadThatIsMostlyStoppedIsStillCalledStopped()
    {
        // The band is not gone, only earned: this road IS the queue, so it says so and carries no
        // hot spot beside it (that would be the same fact twice).
        var result = CorridorAggregator.BuildCongestionCorridors(
            [WeighedSeg("Rutledge Street", lengthMeters: 900, jammedMeters: 830, restJam: 6.0)],
            [], Threshold);

        var road = result.ShouldHaveSingleItem();
        road.Description.ShouldBe("at a standstill");
        road.Severity.ShouldBe(1);
        road.HotSpotMeters.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void AnUnweighedRowFallsBackToItsOwnJamFactor()
    {
        // A row built without subSegment weighting (older caller, replayed fixture) must weigh in at
        // its parent reading, never at zero — which would silently talk every road down to "slow".
        var result = CorridorAggregator.BuildCongestionCorridors([Seg("M1", 9.0)], [], Threshold);

        result.ShouldHaveSingleItem().Description.ShouldBe("at a standstill");
    }

    [Fact]
    public void CongestionCorridorCarriesAFlowDelay()
    {
        // 1000m at 2 m/s (500s) vs free 10 m/s (100s) = 400s ≈ 7 min.
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("M1", 8.0, len: 1000, speed: 2, free: 10)], [], Threshold);

        result[0].FlowDelayMinutes.HasValue.ShouldBeTrue();
        result[0].FlowDelayMinutes.GetValueOr(0).ShouldBe(7);
    }

    [Fact]
    public void DisabledWhenThresholdAboveAllSegments()
    {
        var result = CorridorAggregator.BuildCongestionCorridors(
            [Seg("M4", 6.0)], [], jamFactorThreshold: 11.0);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void FlowCorridor_TakesItsLocationFromTheLongestReading()
    {
        // ARN, 2026-08-24: every road in a congestion-led Sydney read aired with no suburb, because
        // a flow corridor reached the writer as a bare road name — "Haig Ave is a good one, I'm not
        // sure which it is". The location has to come from the SAME segment whose length the
        // corridor voices: these segments share only a road name, so the ends of any other pairing
        // could sit kilometres away on an unrelated stretch of it.
        var shortRun = Seg("Haig Avenue", 7.5, len: 300) with
        {
            StartPoint = (-33.90, 151.10),
            EndPoint = (-33.91, 151.11)
        };
        var longRun = Seg("Haig Avenue", 7.5, len: 1400) with
        {
            StartPoint = (-33.95, 151.18),
            EndPoint = (-33.96, 151.19)
        };

        var result = CorridorAggregator.BuildCongestionCorridors([shortRun, longRun], [], Threshold);

        var corridor = result.ShouldHaveSingleItem();
        corridor.StartPoint.GetValueOr(default).ShouldBe((-33.95, 151.18));
        corridor.EndPoint.GetValueOr(default).ShouldBe((-33.96, 151.19));
        corridor.Latitude.GetValueOr(0).ShouldBe(-33.95);
        corridor.Longitude.GetValueOr(0).ShouldBe(151.18);
    }

    [Fact]
    public void FlowCorridor_WithoutShapeGeometry_StaysUnlocated()
    {
        // HERE does not always return a usable shape. Geometry is garnish everywhere else in the
        // pipeline and stays garnish here: no coordinates, no suburb lookup, still a valid corridor.
        var result = CorridorAggregator.BuildCongestionCorridors([Seg("M4", 7.5)], [], Threshold);

        var corridor = result.ShouldHaveSingleItem();
        corridor.StartPoint.HasValue.ShouldBeFalse();
        corridor.EndPoint.HasValue.ShouldBeFalse();
        corridor.Latitude.HasValue.ShouldBeFalse();
    }
}
