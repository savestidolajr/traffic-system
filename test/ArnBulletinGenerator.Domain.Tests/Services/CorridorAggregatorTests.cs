using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CorridorAggregatorTests
{
    // ── Enhanced overload (AggregateByRoad with time/options) ──

    private static readonly TimeZoneInfo AestZone =
        TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    private static TrafficIncident Incident(
        string id, string desc, int severity, string location, string type,
        int lengthM = 1000, int fc = 1,
        double? lat = null, double? lon = null, bool roadClosed = false)
    {
        return new TrafficIncident(id, desc, severity, location, type, lengthM, fc,
            Option.OfNullable(lat), Option.OfNullable(lon), RoadClosed: roadClosed);
    }

    private static TrafficIncident HereEvent(
        string id, string originalId, string desc, string location, string type,
        int lengthM = 1000, bool roadClosed = false, params string[] segments) =>
        new(id, desc, 2, location, type, lengthM, 3, default, default,
            SegmentRefs: segments.Length > 0 ? segments : null, RoadClosed: roadClosed,
            OriginalId: originalId);

    [Fact]
    public void SameHereEventReportedTwice_CountsOnce()
    {
        // Issue 10. HERE revises an incident in place and `id` changes each time (measured live: the
        // same Kennedy Hwy hazard changed id between two fetches three hours apart), while
        // `originalId` is stable. Counting both revisions inflates IncidentCount, which feeds
        // CalculateImpactScore's density term — so one event makes a road rank as two.
        var corridors = Aggregate([
            HereEvent("111", "ORIG-1", "Road construction", "Kennedy Hwy", "construction", 5665),
            HereEvent("222", "ORIG-1", "Road construction", "Kennedy Hwy", "construction", 5665)
        ]);

        corridors.ShouldHaveSingleItem().Incidents.Count.ShouldBe(1);
    }

    [Fact]
    public void SameConditionOnSameSegments_CountsOnce()
    {
        // Issue 10, second shape: no shared originalId, but identical type, closure state, wording
        // and physical segments — HERE reporting one works zone per carriageway. 24 corpus jobs
        // carried records with byte-identical segment sets inside one corridor.
        var corridors = Aggregate([
            HereEvent("1", "A", "At Myola Road - Road construction", "Kennedy Hwy", "construction", 5523,
                false, "here:cm:segment:503327347#+", "here:cm:segment:503574942#+"),
            HereEvent("2", "B", "At Myola Road - Road construction", "Kennedy Hwy", "construction", 5523,
                false, "here:cm:segment:503574942#+", "here:cm:segment:503327347#+")
        ]);

        corridors.ShouldHaveSingleItem().Incidents.Count.ShouldBe(1);
    }

    [Fact]
    public void DistinctEventsOnOneRoad_AreStillCountedSeparately()
    {
        // The guard on the two tests above: a genuine hazard and a genuine works zone on the same
        // road are two events and must both survive.
        var corridors = Aggregate([
            HereEvent("1", "A", "Hazardous driving conditions", "Kennedy Hwy", "roadHazard", 268,
                false, "here:cm:segment:221129749#+"),
            HereEvent("2", "B", "Road construction", "Kennedy Hwy", "construction", 5665,
                false, "here:cm:segment:779318821#+")
        ]);

        corridors.ShouldHaveSingleItem().Incidents.Count.ShouldBe(2);
    }

    [Fact]
    public void UncappedAggregation_IntMaxCorridors_DoesNotThrow()
    {
        // The good-run exclusion path passes int.MaxValue for "no cap"; List.ctor(int.MaxValue)
        // threw OutOfMemory and silently disabled the exclusion on EVERY fetch (prod,
        // 2026-08-17T14:58Z). Capacity is a hint, never the cap.
        var corridors = Aggregate([
            HereEvent("1", "A", "Road construction", "Kennedy Hwy", "construction", 268,
                false, "here:cm:segment:221129749#+"),
            HereEvent("2", "B", "Road construction", "Mulgrave Rd", "construction", 5665,
                false, "here:cm:segment:779318821#+")
        ], max: int.MaxValue);

        corridors.Count.ShouldBe(2);
    }

    private static IReadOnlyList<TrafficCorridor> Aggregate(List<TrafficIncident> incidents, int max = 5)
    {
        return CorridorAggregator.AggregateByRoad(incidents, max);
    }

    [Fact]
    public void SameRoad_IncidentsFiveKilometresApart_CreateTwoCorridors()
    {
        var corridors = Aggregate([
            Incident("near", "Crash near Lidcombe", 1,
                "Parramatta Road, Lidcombe NSW 2141, Australia", "accident", 500,
                lat: -33.8640, lon: 151.0640),
            Incident("far", "Crash near Auburn", 1,
                "Parramatta Road, Auburn NSW 2144, Australia", "accident", 500,
                lat: -33.9090, lon: 151.0640)
        ], max: 5);

        corridors.Count.ShouldBe(2);
        corridors.ShouldAllBe(c => c.Incidents.Count == 1);
        corridors.Select(c => c.Suburb.Value).ShouldBe(["Lidcombe", "Auburn"], ignoreOrder: true);
    }

    [Fact]
    public void SameRoad_IncidentsFourHundredMetresApart_StayOneCorridor()
    {
        var corridors = Aggregate([
            Incident("one", "Crash near Lidcombe", 1,
                "Parramatta Road, Lidcombe NSW 2141, Australia", "accident", 500,
                lat: -33.8640, lon: 151.0640),
            Incident("two", "Breakdown near Lidcombe", 2,
                "Parramatta Road, Lidcombe NSW 2141, Australia", "disabledVehicle", 500,
                lat: -33.8676, lon: 151.0640)
        ]);

        corridors.ShouldHaveSingleItem().Incidents.Count.ShouldBe(2);
    }

    [Fact]
    public void SameRoad_ChainWithinGap_StaysOneCorridorEvenWhenEndToEndExceedsGap()
    {
        var corridors = Aggregate([
            Incident("one", "Crash", 1, "Parramatta Road, Lidcombe NSW 2141, Australia",
                "accident", 500, lat: -33.8640, lon: 151.0640),
            Incident("two", "Breakdown", 2, "Parramatta Road, Lidcombe NSW 2141, Australia",
                "disabledVehicle", 500, lat: -33.8720, lon: 151.0640),
            Incident("three", "Hazard", 2, "Parramatta Road, Lidcombe NSW 2141, Australia",
                "roadHazard", 500, lat: -33.8800, lon: 151.0640)
        ]);

        corridors.ShouldHaveSingleItem().Incidents.Count.ShouldBe(3);
    }

    [Fact]
    public void SameRoad_SameClosureEventBeyondGap_MergesByDescription()
    {
        var corridors = Aggregate([
            Incident("cammeray", "Road closure and hazard on road northbound", 1,
                "Warringah Freeway, Cammeray NSW 2062, Australia", "roadClosure", 100,
                lat: -33.8200, lon: 151.2100, roadClosed: true),
            Incident("sydney", "Road closure", 1,
                "Warringah Freeway, Sydney NSW 2000, Australia", "roadClosure", 100,
                lat: -33.8500, lon: 151.2100, roadClosed: true)
        ]);

        var corridor = corridors.ShouldHaveSingleItem();
        corridor.Incidents.Count.ShouldBe(2);
        corridor.PrimaryType.ShouldBe("roadClosure");
        corridor.Description.ShouldBe("road closure northbound");
        corridor.Suburb.HasValue.ShouldBeTrue();
    }

    [Fact]
    public void SameRoad_DifferentIncidentTypesBeyondGap_StaySeparate()
    {
        var corridors = Aggregate([
            Incident("crash", "Crash blocking lanes", 1,
                "Warringah Freeway, Cammeray NSW 2062, Australia", "accident", 100,
                lat: -33.8200, lon: 151.2100),
            Incident("works", "Road construction", 1,
                "Warringah Freeway, Sydney NSW 2000, Australia", "construction", 100,
                lat: -33.8500, lon: 151.2100)
        ]);

        corridors.Count.ShouldBe(2);
        corridors.Select(c => c.PrimaryType).ShouldBe(["accident", "construction"], ignoreOrder: true);
    }

    [Fact]
    public void SameRoad_SameTypeBeyondGap_WithActionableDelay_StaysSeparate()
    {
        var left = new TrafficCorridor(
            "Warringah Freeway", 1, 1, 100, 100, "construction", "roadworks",
            [Incident("left", "Roadworks", 1, "Warringah Freeway", "construction", 100,
                lat: -33.8200, lon: 151.2100)],
            FlowDelayMinutes: Option<int>.Some(5));
        var right = new TrafficCorridor(
            "Warringah Freeway", 1, 1, 100, 100, "construction", "roadworks",
            [Incident("right", "Roadworks", 1, "Warringah Freeway", "construction", 100,
                lat: -33.8500, lon: 151.2100)]);

        var method = typeof(CorridorAggregator).GetMethod(
            "MergeSameRoadCorridors",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var merged = (IReadOnlyList<TrafficCorridor>)method.Invoke(
            null, [new List<TrafficCorridor> { left, right }, 1000d])!;

        merged.Count.ShouldBe(2);
    }

    [Fact]
    public void SameEventMerge_PreservesRicherFieldsAndUnionsProvenance()
    {
        var left = new TrafficCorridor(
            "Warringah Freeway", 1, 1, 100, 100, "roadClosure",
            "road closure and hazard on road northbound",
            [Incident("left", "Road closure and hazard on road northbound", 1,
                "Warringah Freeway", "roadClosure", 100, lat: -33.8200, lon: 151.2100,
                roadClosed: true)],
            Suburb: Option<string>.Some("Cammeray"),
            Extent: Option<string>.Some("near Cammeray"),
            Sources: ["here"],
            OfficialEvidences: [new OfficialEvidence("here", "Closure", true)]);
        var right = new TrafficCorridor(
            "Warringah Freeway", 1, 1, 100, 100, "roadClosure", "road closure",
            [Incident("right", "Road closure", 1, "Warringah Freeway", "roadClosure", 100,
                lat: -33.8500, lon: 151.2100, roadClosed: true)],
            Sources: ["livetraffic-nsw"],
            OfficialEvidences: [new OfficialEvidence("livetraffic-nsw", "Works", true)]);

        var method = typeof(CorridorAggregator).GetMethod(
            "MergeSameRoadCorridors",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var merged = (IReadOnlyList<TrafficCorridor>)method.Invoke(
            null, [new List<TrafficCorridor> { left, right }, 1000d])!;

        var corridor = merged.ShouldHaveSingleItem();
        corridor.Description.ShouldBe("road closure and hazard on road northbound");
        corridor.Suburb.Value.ShouldBe("Cammeray");
        corridor.Extent.Value.ShouldBe("near Cammeray");
        corridor.Sources.ShouldBe(["here", "livetraffic-nsw"]);
        corridor.OfficialEvidences!.Count.ShouldBe(2);
    }

    [Fact]
    public void SameRoad_CoordinateLessIncident_IsRetainedInPrimaryCluster()
    {
        var corridors = Aggregate([
            Incident("known-one", "Crash near Lidcombe", 1,
                "Parramatta Road, Lidcombe NSW 2141, Australia", "accident", 500,
                lat: -33.8640, lon: 151.0640),
            Incident("known-two", "Crash near Auburn", 2,
                "Parramatta Road, Auburn NSW 2144, Australia", "accident", 500,
                lat: -33.9090, lon: 151.0640),
            Incident("unknown", "Road hazard", 1,
                "Parramatta Road, Lidcombe NSW 2141, Australia", "roadHazard", 500)
        ], max: 5);

        corridors.Count.ShouldBe(2);
        corridors.SelectMany(c => c.Incidents).Select(i => i.Id)
            .ShouldBe(["known-one", "known-two", "unknown"], ignoreOrder: true);
        corridors.Single(c => c.Incidents.Any(i => i.Id == "unknown"))
            .Incidents.Count.ShouldBe(2);
    }

    [Fact]
    public void CongestionBelowTheFloor_IsNotATrafficItem_OnEitherOverload()
    {
        // Prod 2026-08-18 (pilot day-2 feedback): a 130 m "Queuing traffic" record at James Ruse
        // Drive anchored an aired "M4 chockers at Clyde" claim while the 419 m record at the M7
        // (Eastern Creek — the real story) sat beside it. Sub-300 m congestion mirrors the flow
        // path's queue-at-the-lights rule: dropped from corridors entirely.
        var records = new List<TrafficIncident>
        {
            Incident("1", "At James Ruse Drive - Queuing traffic", 2, "M4 Motorway", "congestion", 130),
            Incident("2", "At M7 Motorway - Traffic congestion", 2, "M4 Motorway", "congestion", 419)
        };

        var simple = Aggregate(records);
        var enhanced = CorridorAggregator.AggregateByRoad(records, 5, null, null);

        foreach (var corridors in new[] { simple, enhanced })
        {
            var c = corridors.ShouldHaveSingleItem();
            var kept = c.Incidents.ShouldHaveSingleItem();
            kept.LengthMeters.ShouldBe(419);
        }
    }

    [Fact]
    public void ShortCrashOrClosure_IsNeverFloored()
    {
        // The floor is congestion-only: a 50 m crash record is still news.
        var corridors = Aggregate([
            Incident("1", "Crash at Spit Road", 1, "Military Road", "accident", 50)
        ]);

        corridors.ShouldHaveSingleItem().Incidents.ShouldHaveSingleItem();
    }

    [Fact]
    public void CorridorIncidents_AreStoredWorstFirst_SoTheLeadAnchorsTheRoad()
    {
        // The stored list order is the corridor's editorial order: the narrative, anchors, and
        // suburb all lead on Incidents[0]. A crash outranks roadworks outranks congestion.
        var corridors = Aggregate([
            Incident("1", "At Church Street - Traffic congestion", 3, "Victoria Road", "congestion", 800),
            Incident("2", "Road construction near Rydalmere", 2, "Victoria Road", "construction", 900),
            Incident("3", "Crash at Rydalmere", 2, "Victoria Road", "accident", 400)
        ]);

        var c = corridors.ShouldHaveSingleItem();
        c.Incidents.Select(i => i.Type).ShouldBe(["accident", "construction", "congestion"]);
    }

    [Fact]
    public void SameRoad_GroupsTogether()
    {
        var corridors = Aggregate([
            Incident("1", "Crash citybound causing delays", 1, "West Gate Freeway", "accident", 2000, lat: -37.82,
                lon: 144.88),
            Incident("2", "Breakdown citybound blocking left lane", 2, "West Gate Freeway", "disabledVehicle", 500,
                lat: -37.823, lon: 144.883)
        ]);

        var c = corridors.ShouldHaveSingleItem();
        c.RoadName.ShouldContain("West Gate");
        c.Direction.HasValue.ShouldBeTrue();
        c.Direction.Value.ShouldBe("citybound");
        c.Incidents.Count.ShouldBe(2);
        c.TotalLengthMeters.ShouldBe(2500);
        c.Severity.ShouldBe(1);
    }

    [Fact]
    public void DifferentRoads_CreatesSeparateCorridors()
    {
        var names = Aggregate([
            Incident("1", "Crash on motorway", 1, "M4", "accident", 3000),
            Incident("2", "Roadworks causing delays", 2, "Tullamarine Freeway", "construction", 2000)
        ]).Select(c => c.RoadName).ToList();

        names.Count().ShouldBe(2);
        names.ShouldContain(n => n.Contains("M4"));
        names.ShouldContain(n => n.Contains("Tullamarine"));
    }

    [Fact]
    public void RespectsMaxCorridors()
    {
        string[] roads = new[]
        {
            "M1 Motorway", "M4 Motorway", "Tullamarine Freeway", "West Gate Freeway",
            "Monash Freeway", "Eastern Freeway", "Punt Road", "St Kilda Road",
            "Hoddle Street", "Sydney Road"
        };
        var incidents = roads.Select((r, i) => Incident(i.ToString(), $"Incident on {r}", 2, r, "accident", fc: 3))
            .ToList();

        Aggregate(incidents, 3).Count().ShouldBe(3);
    }

    [Fact]
    public void PrioritizesByImpactScore()
    {
        var corridors = Aggregate([
            // 800 m keeps this genuine congestion above the 300 m floor — this test is about
            // ranking, not the floor (CongestionBelowTheFloor_IsNotATrafficItem covers that).
            Incident("1", "Minor delay", 4, "Local Street", "congestion", 800, 5),
            Incident("2", "Major crash", 1, "M1", "accident", 5000)
        ]);

        corridors.Count().ShouldBe(2);
        corridors[0].RoadName.ShouldContain("M1");
        corridors[0].ImpactScore.ShouldBeGreaterThan(corridors[1].ImpactScore);
    }

    [Fact]
    public void EmptyList_ReturnsEmpty()
    {
        Aggregate([]).ShouldBeEmpty();
    }

    [Fact]
    public void ExtractsDirectionFromDescription()
    {
        var corridor = Aggregate([Incident("1", "Delays eastbound on the motorway", 2, "M4", "congestion", 3000)])
            .Single();

        corridor.Direction.HasValue.ShouldBeTrue();
        corridor.Direction.Value.ShouldBe("eastbound");
    }

    [Fact]
    public void DeterminesPrimaryTypeByPriority()
    {
        var corridor = Aggregate([
            Incident("1", "Congestion on freeway", 3, "West Gate Freeway", "congestion", 2000),
            Incident("2", "Crash on freeway", 1, "West Gate Freeway", "accident", 500)
        ]).Single();

        corridor.PrimaryType.ShouldBe("accident");
    }

    // ── BuildCorridorDescription (via Description) ──

    [Fact]
    public void Description_SingleIncident_NamesTypeNoCount()
    {
        var corridor = Aggregate([
            Incident("1", "Crash on freeway", 1, "M1", "accident", 3000)
        ]).Single();

        corridor.Description.ShouldBe("crash");
    }

    [Fact]
    public void Description_MultipleIncidentsSameType_NamesTypeNoCount()
    {
        // Observed live (Perth, Reid Highway): "4 incidents including road closure" told the
        // writer there were 3 unaccounted-for incidents with no type to name them — never state a
        // bare number the writer cannot back with concrete types.
        var corridor = Aggregate([
            Incident("1", "Closed at ramp A", 1, "Reid Highway", "roadClosure", 500),
            Incident("2", "Closed at ramp B", 1, "Reid Highway", "roadClosure", 500),
            Incident("3", "Closed at ramp C", 1, "Reid Highway", "roadClosure", 500),
            Incident("4", "Closed at ramp D", 1, "Reid Highway", "roadClosure", 500)
        ]).Single();

        corridor.Description.ShouldBe("road closure");
        corridor.Description.ShouldNotContain("4");
        corridor.Description.ShouldNotContain("incidents");
    }

    [Fact]
    public void Description_TwoDistinctTypes_NamesBoth()
    {
        // Observed live (Perth, Tonkin Highway): "3 incidents including crash" hid that the second
        // incident was congestion — the writer had no name for the "extra" incident and filled the
        // gap with vague filler ("another incident"). Naming both real types gives it concrete
        // material instead.
        var corridor = Aggregate([
            Incident("1", "Crash blocking lane", 1, "Tonkin Highway", "accident", 500),
            Incident("2", "Congestion nearby", 2, "Tonkin Highway", "congestion", 500)
        ]).Single();

        corridor.Description.ShouldBe("crash and congestion");
    }

    [Fact]
    public void Description_ThreeOrMoreDistinctTypes_CapsAtTwoNoCount()
    {
        var corridor = Aggregate([
            Incident("1", "Crash blocking lane", 1, "M1", "accident", 500),
            Incident("2", "Roadworks nearby", 2, "M1", "construction", 500),
            Incident("3", "Hazard on road", 3, "M1", "roadHazard", 500)
        ]).Single();

        // Worst-first order (2026-08-18): the two most NEWSWORTHY types describe the corridor —
        // crash then hazard — and the third (roadworks) is capped out, never counted.
        corridor.Description.ShouldContain("crash");
        corridor.Description.ShouldContain("hazard");
        corridor.Description.ShouldNotContain("roadworks");
        corridor.Description.ShouldNotContain("3");
    }

    [Fact]
    public void Description_IncludesDirectionSuffix()
    {
        var corridor = Aggregate([
            Incident("1", "Crash citybound on freeway", 1, "M1", "accident", 3000)
        ]).Single();

        corridor.Description.ShouldBe("crash citybound");
    }

    private static IReadOnlyList<TrafficCorridor> AggregateEnhanced(
        List<TrafficIncident> incidents,
        int max = 5,
        DateTime? utc = null,
        TimeZoneInfo? tz = null,
        CorridorSelectionOptions? options = null)
    {
        return CorridorAggregator.AggregateByRoad(incidents, max, utc, tz, options);
    }

    private static TrafficIncident NewIncident(
        string id, string desc, int severity, string location, string type,
        int lengthM = 1000, int fc = 1,
        double? lat = null, double? lon = null)
    {
        return new TrafficIncident(id, desc, severity, location, type, lengthM, fc,
            Option.OfNullable(lat), Option.OfNullable(lon), true);
    }

    [Fact]
    public void EnhancedOverload_EmptyList_ReturnsEmpty()
    {
        AggregateEnhanced([]).ShouldBeEmpty();
    }

    [Fact]
    public void EnhancedOverload_DefaultOptions_ReturnsCorridors()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on motorway", 1, "M1", "accident", 3000)
        ]);

        result.ShouldHaveSingleItem();
    }

    // ── FilterReportable ──

    [Fact]
    public void FilterReportable_ExcludesIncidentsBelowAllThresholds()
    {
        // Severity 4 == MinSeverity, FC 5 >= MaxFunctionalClass, length 100 < 200
        // The filter passes if ANY threshold met: severity<=4 OR fc<=4 OR length>=200
        // severity=4 <= MinSeverity(4) → passes. roadHazard: the congestion floor is a separate,
        // unconditional rule (CongestionBelowTheFloor_IsNotATrafficItem) — this test exercises
        // the OR-chain thresholds alone.
        var result = AggregateEnhanced([
            Incident("1", "Minor issue", 4, "M1", "roadHazard", 100, 5)
        ]);

        result.ShouldHaveSingleItem();
    }

    [Fact]
    public void FilterReportable_IncludesBySeverity()
    {
        // severity 3 <= MinSeverity(4) → passes
        var result = AggregateEnhanced([
            Incident("1", "Moderate crash", 3, "M1", "accident", 50, 5)
        ]);

        result.ShouldHaveSingleItem();
    }

    [Fact]
    public void FilterReportable_IncludesByFunctionalClass()
    {
        // fc 3 <= MaxFunctionalClass(5)-1=4 → passes (roadHazard: clear of the congestion floor)
        var result = AggregateEnhanced([
            Incident("1", "Issue on arterial", 4, "M1", "roadHazard", 50, 3)
        ]);

        result.ShouldHaveSingleItem();
    }

    [Fact]
    public void FilterReportable_IncludesByLength()
    {
        // length 500 >= MinLengthMeters(200) → passes
        var result = AggregateEnhanced([
            Incident("1", "Long delay", 4, "M1", "congestion", 500, 5)
        ]);

        result.ShouldHaveSingleItem();
    }

    // ── ApplyTimeWeighting ──

    [Fact]
    public void TimeWeighting_MorningPeak_BoostsScore()
    {
        // 7am AEST is peak morning (6-9)
        var morningUtc = new DateTime(2025, 6, 15, 21, 0, 0, DateTimeKind.Utc); // 7am AEST
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on motorway", 1, "M1", "accident", 3000)
        };

        var peakResult = AggregateEnhanced(incidents, utc: morningUtc, tz: AestZone);
        var noPeakResult = AggregateEnhanced(incidents, utc: morningUtc, tz: AestZone,
            options: new CorridorSelectionOptions { EnableTimeWeighting = false });

        peakResult.Single().ImpactScore.ShouldBeGreaterThan(noPeakResult.Single().ImpactScore);
    }

    [Fact]
    public void TimeWeighting_EveningPeak_BoostsScore()
    {
        // 5pm AEST is peak evening (15-19)
        var eveningUtc = new DateTime(2025, 6, 15, 7, 0, 0, DateTimeKind.Utc); // 5pm AEST
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on motorway", 1, "M1", "accident", 3000)
        };

        var peakResult = AggregateEnhanced(incidents, utc: eveningUtc, tz: AestZone);
        var noPeakResult = AggregateEnhanced(incidents, utc: eveningUtc, tz: AestZone,
            options: new CorridorSelectionOptions { EnableTimeWeighting = false });

        peakResult.Single().ImpactScore.ShouldBeGreaterThan(noPeakResult.Single().ImpactScore);
    }

    [Fact]
    public void TimeWeighting_NonPeak_NoBoost()
    {
        // 11am AEST is not peak
        var midDayUtc = new DateTime(2025, 6, 15, 1, 0, 0, DateTimeKind.Utc); // 11am AEST
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on motorway", 1, "M1", "accident", 3000)
        };

        var withTimeWeighting = AggregateEnhanced(incidents, utc: midDayUtc, tz: AestZone);
        var without = AggregateEnhanced(incidents, utc: midDayUtc, tz: AestZone,
            options: new CorridorSelectionOptions { EnableTimeWeighting = false });

        withTimeWeighting.Single().ImpactScore.ShouldBe(without.Single().ImpactScore);
    }

    [Fact]
    public void TimeWeighting_MorningCitybound_GetsDirectionBoost()
    {
        var morningUtc = new DateTime(2025, 6, 15, 21, 0, 0, DateTimeKind.Utc); // 7am AEST
        var cityboundIncidents = new List<TrafficIncident>
        {
            Incident("1", "Crash citybound on motorway", 1, "M1", "accident", 3000)
        };
        var outboundIncidents = new List<TrafficIncident>
        {
            Incident("2", "Crash outbound on motorway", 1, "M2", "accident", 3000)
        };

        var citybound = AggregateEnhanced(cityboundIncidents, utc: morningUtc, tz: AestZone);
        var outbound = AggregateEnhanced(outboundIncidents, utc: morningUtc, tz: AestZone);

        citybound.Single().ImpactScore.ShouldBeGreaterThan(outbound.Single().ImpactScore);
    }

    [Fact]
    public void TimeWeighting_EveningOutbound_GetsDirectionBoost()
    {
        var eveningUtc = new DateTime(2025, 6, 15, 7, 0, 0, DateTimeKind.Utc); // 5pm AEST
        var outboundIncidents = new List<TrafficIncident>
        {
            Incident("1", "Crash outbound on motorway", 1, "M1", "accident", 3000)
        };
        var cityboundIncidents = new List<TrafficIncident>
        {
            Incident("2", "Crash citybound on motorway", 1, "M2", "accident", 3000)
        };

        var outbound = AggregateEnhanced(outboundIncidents, utc: eveningUtc, tz: AestZone);
        var citybound = AggregateEnhanced(cityboundIncidents, utc: eveningUtc, tz: AestZone);

        outbound.Single().ImpactScore.ShouldBeGreaterThan(citybound.Single().ImpactScore);
    }

    // ── ApplyDeltaBoost ──

    [Fact]
    public void DeltaBoost_NewIncidents_BoostsScore()
    {
        var incidents = new List<TrafficIncident>
        {
            NewIncident("1", "New crash", 1, "M1", "accident", 3000)
        };

        var boosted = AggregateEnhanced(incidents);
        var unboosted = AggregateEnhanced(incidents,
            options: new CorridorSelectionOptions { EnableDeltaBoost = false });

        boosted.Single().ImpactScore.ShouldBeGreaterThan(unboosted.Single().ImpactScore);
    }

    [Fact]
    public void DeltaBoost_NoNewIncidents_NoBoost()
    {
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Old crash", 1, "M1", "accident", 3000)
        };

        var withDelta = AggregateEnhanced(incidents);
        var without = AggregateEnhanced(incidents,
            options: new CorridorSelectionOptions { EnableDeltaBoost = false });

        withDelta.Single().ImpactScore.ShouldBe(without.Single().ImpactScore);
    }

    [Fact]
    public void DeltaBoost_PartialNew_ProportionalBoost()
    {
        var allNew = new List<TrafficIncident>
        {
            NewIncident("1", "New crash", 1, "M1", "accident", 3000),
            NewIncident("2", "New breakdown", 2, "M1", "disabledVehicle", 500)
        };
        var halfNew = new List<TrafficIncident>
        {
            NewIncident("1", "New crash", 1, "M1", "accident", 3000),
            Incident("2", "Old breakdown", 2, "M1", "disabledVehicle", 500)
        };

        double allNewScore = AggregateEnhanced(allNew).Single().ImpactScore;
        double halfNewScore = AggregateEnhanced(halfNew).Single().ImpactScore;

        allNewScore.ShouldBeGreaterThan(halfNewScore, "100% new ratio should boost more than 50%");
    }

    // ── ApplyGeographicDiversity ──

    [Fact]
    public void GeographicDiversity_FiltersTooCloseCorridors()
    {
        // Two corridors ~0.5km apart (well within 2km default threshold)
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on M1", 1, "M1", "accident", 5000, lat: -33.8688, lon: 151.2093),
            Incident("2", "Crash on M2", 1, "M2", "accident", 5000, lat: -33.8700, lon: 151.2100),
            Incident("3", "Crash on M4", 1, "M4", "accident", 5000, lat: -33.95, lon: 151.05)
        };

        var withDiversity = AggregateEnhanced(incidents, 3,
            options: new CorridorSelectionOptions { EnableGeographicDiversity = true });

        // M1 and M2 are close, so one may be filtered (but backfill ensures count)
        withDiversity.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public void GeographicDiversity_BackfillsWhenFilterTooAggressive()
    {
        // All corridors close together — filter would remove too many, backfill kicks in
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on M1", 1, "M1", "accident", 5000, lat: -33.868, lon: 151.209),
            Incident("2", "Crash on M2", 1, "M2", "accident", 4000, lat: -33.869, lon: 151.210),
            Incident("3", "Crash on M4", 1, "M4", "accident", 3000, lat: -33.870, lon: 151.211)
        };

        var result = AggregateEnhanced(incidents, 3,
            options: new CorridorSelectionOptions { EnableGeographicDiversity = true });

        // Backfill ensures we get up to maxCorridors even if proximity filter is aggressive
        // First 2 guaranteed (selected.Count < 2 bypass), third backfilled
        result.Count.ShouldBe(3);
    }

    [Fact]
    public void GeographicDiversity_CorridorsWithoutCoordinates_AlwaysIncluded()
    {
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on M1", 1, "M1", "accident", 5000, lat: -33.868, lon: 151.209),
            Incident("2", "Issue on local road", 2, "Punt Road", "congestion", 2000), // no coords
            Incident("3", "Crash on M4", 1, "M4", "accident", 3000, lat: -33.870, lon: 151.211)
        };

        var result = AggregateEnhanced(incidents, 3,
            options: new CorridorSelectionOptions { EnableGeographicDiversity = true });

        result.ShouldContain(c => c.RoadName.Contains("Punt"));
    }

    [Fact]
    public void GeographicDiversity_Disabled_TakesTopN()
    {
        var incidents = new List<TrafficIncident>
        {
            Incident("1", "Crash on M1", 1, "M1", "accident", 5000, lat: -33.868, lon: 151.209),
            Incident("2", "Crash on M2", 1, "M2", "accident", 4000, lat: -33.869, lon: 151.210),
            Incident("3", "Crash on M4", 2, "M4", "accident", 3000, lat: -33.870, lon: 151.211)
        };

        var result = AggregateEnhanced(incidents, 2,
            options: new CorridorSelectionOptions { EnableGeographicDiversity = false });

        result.Count().ShouldBe(2);
    }

    // ── ExtractSuburb (tested via corridor Suburb field) ──

    [Fact]
    public void ExtractSuburb_CommaSeparated_ExtractsSuburb()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on road", 1, "Punt Road, Richmond", "accident", 3000)
        ]);

        result.Single().Suburb.HasValue.ShouldBeTrue();
        result.Single().Suburb.Value.ShouldBe("Richmond");
    }

    [Fact]
    public void ExtractSuburb_NoComma_ReturnsNone()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on road", 1, "M1", "accident", 3000)
        ]);

        result.Single().Suburb.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ExtractSuburb_UnidentifiedRoad_ReturnsNone()
    {
        // "Unidentified Road" falls through to Unknown-{id} in ExtractRoadKey
        // and ExtractSuburb returns None for "Unidentified Road"
        var result = AggregateEnhanced([
            Incident("1", "Something happened", 1, "Unidentified Road", "accident", 3000)
        ]);

        result.Single().Suburb.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ExtractSuburb_UnknownLocation_ReturnsNone()
    {
        var result = AggregateEnhanced([
            Incident("1", "Something happened", 1, "Unknown Location", "accident", 3000)
        ]);

        result.Single().Suburb.HasValue.ShouldBeFalse();
    }

    // ── NormalizeRoadName (tested via corridor RoadName) ──

    [Fact]
    public void NormalizeRoadName_ExpandsRdToRoad()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on Punt Rd", 1, "Punt Rd", "accident", 3000)
        ]);

        result.Single().RoadName.ShouldContain("Road");
        result.Single().RoadName.ShouldNotContain(" Rd");
    }

    [Fact]
    public void NormalizeRoadName_ExpandsStToStreet()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on Collins St", 1, "Collins St", "accident", 3000)
        ]);

        result.Single().RoadName.ShouldContain("Street");
    }

    [Fact]
    public void NormalizeRoadName_ExpandsHwyToHighway()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on Pacific Hwy", 1, "Pacific Hwy", "accident", 3000)
        ]);

        result.Single().RoadName.ShouldContain("Highway");
    }

    [Fact]
    public void NormalizeRoadName_ExpandsFwyToFreeway()
    {
        var result = AggregateEnhanced([
            Incident("1", "Crash on Calder Fwy", 1, "Calder Fwy", "accident", 3000)
        ]);

        result.Single().RoadName.ShouldContain("Freeway");
    }

    [Fact]
    public void NormalizeRoadName_ExpandsWestgateToWestGate()
    {
        var result = AggregateEnhanced([
            Incident("1", "Delays on Westgate Bridge", 1, "Westgate Bridge", "congestion", 3000)
        ]);

        result.Single().RoadName.ShouldContain("West Gate");
    }

    // ── CalculateImpactScore edge cases ──

    [Fact]
    public void ImpactScore_ZeroLength_NoLengthContribution()
    {
        var result = AggregateEnhanced([
            Incident("1", "Point incident", 1, "M1", "accident", 0)
        ]);

        // Score should be roadScore(100) + severityScore(80) + lengthScore(0) + densityScore(0) = 180
        result.Single().ImpactScore.ShouldBe(180.0);
    }

    [Fact]
    public void ImpactScore_SingleIncident_NoDensityBonus()
    {
        var single = AggregateEnhanced([
            Incident("1", "Crash", 1, "M1", "accident")
        ]);
        var multiple = AggregateEnhanced([
            Incident("1", "Crash", 1, "M1", "accident", 500),
            Incident("2", "Another crash", 1, "M1", "accident", 500)
        ]);

        // Multiple incidents get density bonus, single does not
        multiple.Single().ImpactScore.ShouldBeGreaterThan(single.Single().ImpactScore);
    }

    [Fact]
    public void ImpactScore_HighFC_LowerRoadScore()
    {
        var fc1 = AggregateEnhanced([
            Incident("1", "Crash on M1", 2, "M1", "accident")
        ]);
        var fc5 = AggregateEnhanced([
            Incident("2", "Crash on local road", 2, "Some Road", "accident", 1000, 5)
        ]);

        fc1.Single().ImpactScore.ShouldBeGreaterThan(fc5.Single().ImpactScore);
    }

    // ── Unknown road handling ──

    [Fact]
    public void UnknownRoad_CreatesIndividualCorridors()
    {
        // Unidentified incidents are keyed internally as Unknown-{id} so they never merge,
        // but the displayed RoadName must be speakable — the placeholder is never voiced on air.
        var result = AggregateEnhanced([
            Incident("abc", "Something strange", 1, "Unidentified Road", "accident", 3000),
            Incident("def", "Another mystery", 1, "Unidentified Road", "accident", 3000)
        ]);

        result.Count().ShouldBe(2, "each unidentified incident becomes its own corridor");
        result.ShouldAllBe(c => c.Incidents.Count == 1);
        result.SelectMany(c => c.Incidents).Select(i => i.Id).ShouldBe(["abc", "def"], ignoreOrder: true);
        result.ShouldAllBe(c => !c.RoadName.Contains("Unknown-"));
        result.ShouldAllBe(c => c.RoadName == "an unnamed road");
    }

    [Fact]
    public void UnknownLocation_CreatesIndividualCorridors()
    {
        var result = AggregateEnhanced([
            Incident("x1", "Event", 1, "Unknown Location", "accident", 3000),
            Incident("x2", "Event", 1, "Unknown Location", "accident", 3000)
        ]);

        result.Count().ShouldBe(2, "unknown locations must not merge into one corridor");
        result.ShouldAllBe(c => c.Incidents.Count == 1);
        result.SelectMany(c => c.Incidents).Select(i => i.Id).ShouldBe(["x1", "x2"], ignoreOrder: true);
        result.ShouldAllBe(c => !c.RoadName.Contains("Unknown-"));
    }

    // ── Numeric route streets + HERE label parsing ──

    [Fact]
    public void NumericRouteStreet_NotVoicedAsNumberedRoad()
    {
        // HERE reverse-geocodes a state route to a bare number ("99"); concatenating the
        // description ("Road construction") used to fabricate "99 Road". It must not, and a
        // bare number is not speakable, so it degrades to the honest fail-soft.
        var corridor = Aggregate([
            Incident("1", "Road construction", 2, "99", "construction", 5000)
        ]).Single();

        corridor.RoadName.ShouldNotContain("99");
        corridor.RoadName.ShouldBe("an unnamed road");
    }

    [Fact]
    public void NumericRouteStreet_WithLabelSuburb_VoicesSuburbArea()
    {
        // When geocoding falls back to the full HERE label, the suburb is recoverable and
        // voiced as "the {suburb} area" instead of a placeholder or a fabricated number.
        var corridor = Aggregate([
            Incident("1", "Road construction", 2, "99, Springbrook QLD 4213, Australia", "construction", 5000)
        ]).Single();

        corridor.RoadName.ShouldBe("the Springbrook area");
        corridor.RoadName.ShouldNotContain("99");
    }

    [Fact]
    public void DescriptionRoadWords_DoNotLeakIntoRoadName()
    {
        // The road name comes from the location only. Road-type words in the description
        // ("Garden Grove", "Chisholm Road", "Road construction") must not become the name.
        var corridor = Aggregate([
            Incident("1", "Between Garden Grove and Chisholm Road - Road construction", 2, "99", "construction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("an unnamed road");
        corridor.RoadName.ShouldNotContain("Grove");
        corridor.RoadName.ShouldNotContain("Chisholm");
    }

    // ── Multi-word road-name truncation (TDEMO-001 / RISK-32, confirmed live 2026-07-02) ──
    // GenericRoadRegex used to capture only the single word immediately before the road-type
    // suffix, silently truncating real multi-word road names to their last word — "Gold Coast
    // Highway" became the nonexistent "Coast Highway" (ARN operator bug report). Reproduced live
    // via ./scripts/eval.sh --live-here and fixed by widening the captured word-run with a
    // connector-word exclusion. Cases below are drawn directly from live HERE data across all 6
    // markets, reported by an ARN operator on 2026-07-02.

    [Fact]
    public void MultiWordRoadName_RecoveredFromRampAnchor_KeepsFullName()
    {
        // Live Gold Coast repro: description "Ramp to Gold Coast Highway (Eastbound) - Lane
        // blocked" with an unresolved location — the old regex returned "Coast Highway".
        var corridor = Aggregate([
            Incident("1", "Ramp to Gold Coast Highway (Eastbound) - Lane blocked", 2,
                "Unidentified Road", "laneRestriction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Gold Coast Highway");
    }

    [Fact]
    public void MultiWordRoadName_FromGeocodedLocation_KeepsFullName()
    {
        // Live Gold Coast repro: a reverse-geocoded location carries the full HERE label.
        var corridor = Aggregate([
            Incident("1", "Traffic congestion", 3,
                "Gold Coast Hwy, Burleigh Heads QLD 4220, Australia", "congestion", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Gold Coast Highway");
    }

    [Fact]
    public void MultiWordRoadName_ThreeLeadingWords_KeepsFullName()
    {
        // Live Adelaide repro: "Sir Donald Bradman Drive" was truncated to "Bradman Drive".
        var corridor = Aggregate([
            Incident("1", "At Sir Donald Bradman Drive - Road construction", 2,
                "Unidentified Road", "construction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Sir Donald Bradman Drive");
    }

    [Fact]
    public void MultiWordRoadName_LeadingAtAnchor_ExcludesConnectorWord()
    {
        // Live Adelaide repro: "At Avenue Road" used to match "At" as the name + "Avenue" as the
        // suffix ("At Avenue"), losing "Road" entirely. Must resolve to "Avenue Road".
        var corridor = Aggregate([
            Incident("1", "At Avenue Road - Road construction", 2, "Unidentified Road",
                "construction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Avenue Road");
    }

    [Fact]
    public void MultiWordRoadName_StKilda_NotTruncatedToConnectorPlusSt()
    {
        // Live Melbourne repro: "At St Kilda Road" used to match "At St" (treating "St" as the
        // Street abbreviation suffix), losing "Kilda Road" entirely.
        var corridor = Aggregate([
            Incident("1", "At St Kilda Road - Lane blocked", 2, "Unidentified Road",
                "laneRestriction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("St Kilda Road");
    }

    [Fact]
    public void MultiWordRoadName_BetweenAnchor_StillStaysUnnamed()
    {
        // The connector-word fix must NOT reopen "Between X and Y" cross-street mining — that
        // stays the honest "an unnamed road" fail-soft (UnidentifiedLocation_BetweenAnchor_StaysUnnamed).
        var corridor = Aggregate([
            Incident("1", "Between Green Gully Road and Keilor Park Drive - Lane blocked", 2,
                "Unidentified Road", "laneRestriction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("an unnamed road");
    }

    [Fact]
    public void ExtractSuburb_FullHereLabel_StripsStateAndPostcode()
    {
        var corridor = Aggregate([
            Incident("1", "Crash", 1, "Smith St, Parkwood QLD 4214, Australia", "accident", 3000)
        ]).Single();

        corridor.RoadName.ShouldContain("Smith Street");
        corridor.Suburb.HasValue.ShouldBeTrue();
        corridor.Suburb.Value.ShouldBe("Parkwood");
    }

    // ── Commuter relevance: ambient (roadworks/event) extent must not outrank live queues ──

    [Fact]
    public void LongDenseRuralRoadworks_DoesNotLeadOverArterialQueue()
    {
        // A long, multi-segment rural (FC4) works zone used to win on length+density.
        // Its extent is ambient, not a queue, so a major-arterial (FC3) congestion leads.
        var corridors = Aggregate([
            Incident("r1", "Road construction", 3, "Mountain Road", "construction", 6000, fc: 4),
            Incident("r2", "Road construction", 3, "Mountain Road", "construction", 6000, fc: 4),
            Incident("r3", "Road construction", 3, "Mountain Road", "construction", 6000, fc: 4),
            Incident("a1", "Heavy congestion", 3, "Smith Street", "congestion", 1200, fc: 3)
        ]);

        corridors[0].RoadName.ShouldContain("Smith");
        corridors[0].ImpactScore.ShouldBeGreaterThan(
            corridors.First(c => c.RoadName.Contains("Mountain")).ImpactScore);
    }

    [Fact]
    public void SevereAccidentOnMinorRoad_NotSuppressedByAmbientDiscount()
    {
        // The discount only touches ambient types. A critical accident (never ambient) on a
        // minor road keeps full weight and still outranks roadworks on a more major road.
        var corridors = Aggregate([
            Incident("crash", "Serious crash blocking lanes", 1, "Quiet Street", "accident", 800, fc: 4),
            Incident("works", "Road construction", 4, "Main Road", "construction", 5000, fc: 3)
        ]);

        corridors[0].RoadName.ShouldContain("Quiet");
    }

    // ── Road-type abbreviation expansion (spoken on air) ──

    [Fact]
    public void NormalizeRoadName_ExpandsMtwyToMotorway()
    {
        // HERE geocodes the M1 surface road as "Pacific Mtwy"; "Mtwy" must be spoken as
        // "Motorway", not left as the abbreviation, and not rely on the LLM to expand it.
        var corridor = Aggregate([
            Incident("1", "Crash", 1, "Pacific Mtwy", "accident", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Pacific Motorway");
    }

    [Fact]
    public void NormalizeRoadName_ExpandsMtwyInFullHereLabel()
    {
        var corridor = Aggregate([
            Incident("1", "Crash", 1, "Pacific Mtwy, Oxenford QLD 4210, Australia", "accident", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Pacific Motorway");
        corridor.Suburb.Value.ShouldBe("Oxenford");
    }

    // ── Recover road name from description anchor when HERE returns a placeholder location ──

    [Fact]
    public void UnidentifiedLocation_RecoversRoadFromAtAnchor()
    {
        // HERE reverse-geocodes ramps/segments to "Unidentified Road"; the description anchors
        // the incident to a real road ("At Victoria Road"). Recover it, don't voice "an unnamed road".
        var corridor = Aggregate([
            Incident("1", "At Victoria Road - Major event", 1, "Unidentified Road", "plannedEvent", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Victoria Road");
    }

    [Fact]
    public void SameRoad_RecoveredDirectionless_MergesWithDirectional()
    {
        // Live Perth shape: a citybound closure on Reid Highway + a directionless closure whose
        // location geocoded to "Unidentified Road" but recovered to "Reid Highway" from the
        // description. Both display as "Reid Highway" — must merge, not voice the road twice.
        var corridors = Aggregate([
            Incident("1", "Road closed citybound", 1, "Reid Highway", "roadClosure", 2000),
            Incident("2", "At Reid Highway - Closed", 1, "Unidentified Road", "roadClosure", 1500)
        ]);

        corridors.ShouldHaveSingleItem();
        corridors[0].RoadName.ShouldBe("Reid Highway");
    }

    [Fact]
    public void UnidentifiedLocation_RecoversRoadFromRampAnchor()
    {
        var corridor = Aggregate([
            Incident("1", "Ramp to Albany Highway - Road construction", 2, "Unidentified Road", "construction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Albany Highway");
    }

    [Fact]
    public void UnidentifiedLocation_BetweenAnchor_StaysUnnamed()
    {
        // "Between X and Y" names ambiguous cross-streets, not the incident's own road — must NOT
        // be mined for a name (that was the description-bleed bug). Stays the honest fail-soft.
        var corridor = Aggregate([
            Incident("1", "Between Garden Grove and Chisholm Road - Road construction", 2, "Unidentified Road", "construction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("an unnamed road");
    }
    [Fact]
    public void ExtractRoadKey_NonSpeakableGeocodedLocation_RecoversRoadNameFromDescription()
    {
        // Live Melbourne bug: HERE reverse-geocodes an incident to "W5" -- an internal freeway
        // link code that fails IsSpeakableRoadName (not a known M/A/B/C route class). The
        // description still anchors it to a real road ("At Todd Road / Cook Street - ...").
        // The comma-split fallback produces "W5", IsPlaceholderRoadName catches it, the At-
        // anchor recovery fires, and ExtractRoadFromText pulls "Todd Road" from the description.
        var corridor = Aggregate([
            Incident("w5-todd",
                "At Todd Road / Cook Street - Backed-up traffic with average speeds of 10 km/h. Approach with care",
                2, "W5", "congestion", 800, fc: 1, lat: -37.83137, lon: 144.91611)
        ]).Single();

        corridor.RoadName.ShouldBe("Todd Road");
        corridor.RoadName.ShouldNotContain("unnamed");
        corridor.RoadName.ShouldNotStartWith("Unknown-");
    }

    [Fact]
    public void ExtractRoadKey_NonSpeakableGeocodedLocation_NoDescriptionAnchor_StaysUnresolved()
    {
        // Same non-speakable code "W5", but the description has no "At"/"Ramp to" anchor --
        // the recovery path is correctly gated and does NOT fire unconditionally. The incident
        // falls through to the placeholder: no suburb from "W5", so the display name is
        // "an unnamed road".
        var corridor = Aggregate([
            Incident("w5-heavy",
                "Heavy congestion, delays likely",
                3, "W5", "congestion", 600, fc: 1, lat: -37.83137, lon: 144.91611)
        ]).Single();

        corridor.RoadName.ShouldBe("an unnamed road");
        corridor.RoadName.ShouldNotStartWith("Unknown-");
    }

    [Fact]
    public void NullLocation_DoesNotThrow()
    {
        var corridor = Aggregate([
            Incident("1", "Crash on road", 1, null!, "accident", 3000)
        ]);

        corridor.ShouldHaveSingleItem();
        corridor[0].RoadName.ShouldBe("an unnamed road");
    }

    [Fact]
    public void NormalizeRoadName_ExpandsBypToBypass()
    {
        var corridor = Aggregate([
            Incident("1", "Roadworks", 2, "Greensborough Byp", "construction", 3000)
        ]).Single();

        corridor.RoadName.ShouldBe("Greensborough Bypass");
    }

    // ── CalculateDelayScore (delay-aware final ranking) ──

    private static TrafficCorridor CorridorWith(double impactScore, int? delayMinutes) =>
        new("Test Road", 1, 1, 1000, impactScore, "accident", "x", [],
            FlowDelayMinutes: delayMinutes is null ? default : Option<int>.Some(delayMinutes.Value));

    [Theory]
    [InlineData(3, 15.0)]
    [InlineData(5, 15.0)]
    [InlineData(6, 40.0)]
    [InlineData(15, 40.0)]
    [InlineData(16, 70.0)]
    [InlineData(30, 70.0)]
    [InlineData(31, 100.0)]
    [InlineData(90, 100.0)]
    public void CalculateDelayScore_MapsDelayToMagnitudeBand(int delay, double expected)
    {
        CorridorAggregator.CalculateDelayScore(Option<int>.Some(delay)).ShouldBe(expected);
    }

    [Fact]
    public void CalculateDelayScore_NoDelayOrZero_IsZero()
    {
        CorridorAggregator.CalculateDelayScore(default).ShouldBe(0.0);
        CorridorAggregator.CalculateDelayScore(Option<int>.Some(0)).ShouldBe(0.0);
    }

    [Fact]
    public void DelayScore_LiftsHighDelayCorridorAboveHigherStructuralScore()
    {
        // The selection fix: a 40-min jam on a lower structural-impact road must out-rank an idle
        // higher-impact corridor that wins on road class alone.
        var idleMajor = CorridorWith(impactScore: 150, delayMinutes: null);
        var jam = CorridorWith(impactScore: 120, delayMinutes: 40);

        var ranked = new[] { idleMajor, jam }
            .OrderByDescending(c => c.ImpactScore + CorridorAggregator.CalculateDelayScore(c.DelayMinutes))
            .ToList();

        ranked[0].ShouldBe(jam);
    }

    private static CongestionSegment Seg(string road, double jam, double lengthM) =>
        new(road, jam, lengthM, 15, 25);

    [Fact]
    public void RankFreeFlowingRoads_ReturnsFlowRoads_OrderedByLength_WithWorstJam()
    {
        var segments = new List<CongestionSegment>
        {
            Seg("M1 Pacific Motorway", 1, 5000),
            Seg("M1 Pacific Motorway", 3, 2000),
            Seg("Gold Coast Highway", 2, 3000),
        };

        var result = CorridorAggregator.RankFreeFlowingRoads(segments, [], max: 5);

        result.Select(f => f.RoadName).ShouldBe(["M1 Pacific Motorway", "Gold Coast Highway"]);
        result[0].LengthMeters.ShouldBe(7000);
        result[0].JamFactor.ShouldBe(3); // worst jam across the road's segments
        result[0].IsWeighted.ShouldBeFalse(); // weighting is applied downstream
    }

    // ── Same-road dedup: merge corridors with identical display name + direction ──

    [Fact]
    public void SameRoadSameDirection_AggregatesToSingleCorridor()
    {
        // Two incidents on the same road, same direction — one geocoded, one with a
        // slightly different location format — must still aggregate to one corridor.
        var corridors = Aggregate([
            Incident("1", "Road closed citybound", 1, "Reid Highway", "roadClosure", 2000),
            Incident("2", "Road closed citybound", 1, "Reid Highway", "roadClosure", 1500)
        ]);

        corridors.ShouldHaveSingleItem();
        corridors[0].RoadName.ShouldContain("Reid Highway");
        corridors[0].Incidents.Count.ShouldBe(2);
        corridors[0].TotalLengthMeters.ShouldBe(3500);
    }

    [Fact]
    public void SameRoadDifferentDirection_CollapsesToOneCorridor()
    {
        // The road — not road+direction — is the bulletin/curation/voicing unit, so a citybound
        // and an outbound incident on the same road collapse to ONE corridor (the script voices
        // the road once; two rows would duplicate it and waste a slot). Per-incident direction
        // survives in each incident's description.
        var corridors = Aggregate([
            Incident("1", "Crash citybound", 1, "Reid Highway", "accident", 2000),
            Incident("2", "Crash outbound", 1, "Reid Highway", "accident", 1500)
        ]);

        var c = corridors.ShouldHaveSingleItem();
        c.RoadName.ShouldContain("Reid Highway", Case.Insensitive);
        c.Incidents.Count.ShouldBe(2);
    }

    [Fact]
    public void SameRoadConflictingDirections_StatesNoDirection()
    {
        // Cairns, 2026-08-13: HERE reported Kennedy Highway works/hazards heading citybound,
        // outbound, northbound AND southbound. The merge inherited the highest-impact corridor's
        // heading and stated it for the whole road, so the bulletin placed the (outbound/southbound)
        // Myola Road works "northbound". With several conflicting headings the road has no single
        // broadcastable direction — each incident keeps its own in its description.
        var corridors = Aggregate([
            Incident("1", "Road construction citybound", 2, "Kennedy Highway", "construction", 5665),
            Incident("2", "Road construction outbound", 2, "Kennedy Highway", "construction", 5523),
            Incident("3", "Hazardous driving conditions northbound", 2, "Kennedy Highway", "roadHazard", 268),
            Incident("4", "Hazardous driving conditions southbound", 2, "Kennedy Highway", "roadHazard", 247)
        ]);

        var c = corridors.ShouldHaveSingleItem();
        c.Direction.HasValue.ShouldBeFalse();
        c.Incidents.Count.ShouldBe(4);
        // The voiced extent is the longest SINGLE observation of the reported type, never the
        // 11,703 m sum that aired as "queues back about ten kays".
        c.VoicedExtentMeters.ShouldBeLessThanOrEqualTo(5665);
    }

    [Fact]
    public void SameRoadClosedBothWays_StatesBothDirections()
    {
        // Mildura, 2026-07-06: Walker Road closed citybound AND outbound aired as "citybound is
        // shut", inviting listeners onto the closed side. When every observation is a closure and
        // the headings differ, the only broadcastable truth is both directions.
        var corridors = Aggregate([
            Incident("1", "Road closed citybound", 1, "Walker Road", "roadClosure", 400, roadClosed: true),
            Incident("2", "Road closed outbound", 1, "Walker Road", "roadClosure", 350, roadClosed: true)
        ]);

        var c = corridors.ShouldHaveSingleItem();
        c.Direction.Value.ShouldBe("both directions");
    }

    [Fact]
    public void SameRoadDirectionlessIncident_FoldsIntoDirectionalCorridor()
    {
        // A directional closure plus a directionless one on the same road (e.g. recovered from
        // "At Reid Highway" without a heading) must merge — never voiced as two "Reid Highway" items.
        var corridors = Aggregate([
            Incident("1", "Road closed outbound", 1, "Reid Highway", "roadClosure", 2000),
            Incident("2", "Road closed", 1, "Reid Highway", "roadClosure", 1500)
        ]);

        corridors.ShouldHaveSingleItem();
        corridors[0].RoadName.ShouldContain("Reid Highway");
        corridors[0].Incidents.Count.ShouldBe(2);
    }

    // ── CalculateFlowScore — damped band below incidents ──

    [Fact]
    public void CalculateFlowScore_MajorRoad_OutranksMinorRoad()
    {
        double motorway = CorridorAggregator.CalculateFlowScore(1, 5000);
        double arterial = CorridorAggregator.CalculateFlowScore(3, 5000);
        motorway.ShouldBeGreaterThan(arterial);
    }

    [Fact]
    public void CalculateFlowScore_IsDampedBelowIncidentBand()
    {
        // Flow is scaled by FlowImpactFactor (0.5): a clear road reads as filler, not a problem.
        // FC1 road score (6-1)*20 = 100, ×0.5 = 50 at zero length.
        CorridorAggregator.CalculateFlowScore(1, 0).ShouldBe(50.0);
        // Even a long motorway flow stays below the band real incidents occupy (>= ~100).
        CorridorAggregator.CalculateFlowScore(1, 5000).ShouldBeLessThan(100.0);
    }

    [Fact]
    public void CalculateFlowScore_LongerRoad_ScoresHigher()
    {
        double shortRoad = CorridorAggregator.CalculateFlowScore(2, 100);
        double longRoad = CorridorAggregator.CalculateFlowScore(2, 5000);
        longRoad.ShouldBeGreaterThan(shortRoad);
    }

    [Fact]
    public void RoadClosedIncident_WithConstructionType_ClassifiesCorridorAsClosure()
    {
        // HERE reports construction-CAUSED closures with Type "construction" (type-priority 6) plus
        // RoadClosed=true. Without honouring the flag the corridor's PrimaryType stayed "construction"
        // and rendered as roadworks/delays, dropping the closure (observed live: Flinders St "Closed
        // due to road construction" aired as "moderate delays"). The flag is authoritative.
        var closed = new TrafficIncident(
            "1", "Closed due to road construction", 1, "At St Kilda Road", "construction",
            LengthMeters: 96, FunctionalClass: 3, RoadClosed: true);

        var corridor = Aggregate([closed]).ShouldHaveSingleItem();

        corridor.PrimaryType.ShouldBe("roadClosure");
    }

    [Fact]
    public void NoRoadClosedIncident_KeepsReportedType()
    {
        // Guard the inverse: a construction corridor with RoadClosed=false (the Brisbane
        // Gilchrist Avenue case) must NOT be promoted to a closure.
        var works = new TrafficIncident(
            "1", "Lane blocked", 2, "At St Kilda Road", "construction",
            LengthMeters: 215, FunctionalClass: 3, RoadClosed: false);

        var corridor = Aggregate([works]).ShouldHaveSingleItem();

        corridor.PrimaryType.ShouldBe("construction");
    }

}
