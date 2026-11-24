using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class CandidateGroundingTests
{
    private static TrafficCorridor Corridor(string roadName) =>
        new(
            roadName,
            Severity: 2,
            FunctionalClass: 3,
            TotalLengthMeters: 1_000,
            ImpactScore: 10,
            PrimaryType: "congestion",
            Description: $"{roadName} congestion",
            Incidents: []);

    private static BroadcastWithTraffic Context(
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<TrafficCorridor>? allCorridors = null,
        IReadOnlyList<string>? goodRunRoads = null,
        IReadOnlyList<FlowRoad>? flowPool = null,
        IReadOnlyList<MonitoredRouteStatus>? monitoredRoutes = null) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "candidate-grounding-test",
            Location = "Sydney",
            Traffic = string.Empty,
            Incidents = [],
            Corridors = corridors,
            AllCorridors = allCorridors ?? corridors,
            GoodRunRoads = goodRunRoads ?? [],
            FlowPool = flowPool ?? [],
            MonitoredRoutes = monitoredRoutes ?? []
        };

    [Fact]
    public void Flags_fetched_road_that_was_not_a_candidate_but_was_voiced()
    {
        // Real provenance: test job d4db0a5a910f4509977e7843d4532b8d (2026-08-28) fetched 23
        // roads; Forest Way ranked 18th, was excluded from candidates, and was nevertheless voiced
        // on air. The sentence persisted without corridor keys or evidence ids.
        var candidates = new[] { Corridor("M7"), Corridor("Mitchell Road") };
        var fetched = new[] { Corridor("M7"), Corridor("Mitchell Road"), Corridor("Forest Way") };

        var result = CandidateGrounding.Check(
            "A crash has closed two of three lanes northbound on the M7 at Oakhurst. " +
            "Forest Way is not getting through southbound at Belrose after a crash.",
            Context(candidates, fetched));

        result.HasNonCandidateRoad.ShouldBeTrue();
        result.NonCandidateRoads.ShouldContain("Forest Way");
        result.NonCandidateRoads.ShouldNotContain("M7");
    }

    [Fact]
    public void Does_not_reject_an_all_clear_bulletin_for_prose_words()
    {
        // A superseded prose road-name extractor treated "Traffic" and "Sydney" as road-like
        // names here and rejected a valid all-clear bulletin. Candidate grounding therefore checks
        // membership in our fetched vocabulary rather than extracting names from prose.
        var candidates = new[] { Corridor("M7") };
        var fetched = new[]
        {
            Corridor("M7"),
            Corridor("Forest Way"),
            Corridor("Parramatta Road"),
            Corridor("Old Northern Road")
        };

        var result = CandidateGrounding.Check(
            "Traffic is flowing smoothly across all major roads in Sydney today",
            Context(candidates, fetched));

        result.HasNonCandidateRoad.ShouldBeFalse();
        result.NonCandidateRoads.ShouldBeEmpty();
    }

    [Fact]
    public void Script_naming_only_candidates_is_clean_when_fetched_pool_is_larger()
    {
        var candidates = new[] { Corridor("M7"), Corridor("Mitchell Road") };
        var fetched = new[]
        {
            Corridor("M7"),
            Corridor("Mitchell Road"),
            Corridor("Forest Way"),
            Corridor("Parramatta Road"),
            Corridor("Old Northern Road"),
            Corridor("Pennant Hills Road")
        };

        var result = CandidateGrounding.Check(
            "The M7 is moving well, while Mitchell Road has a short delay.",
            Context(candidates, fetched));

        result.HasNonCandidateRoad.ShouldBeFalse();
    }

    [Fact]
    public void No_violation_when_all_fetched_roads_are_candidates()
    {
        var roads = new[] { Corridor("M7"), Corridor("Mitchell Road"), Corridor("Forest Way") };

        var result = CandidateGrounding.Check(
            "M7, Mitchell Road and Forest Way are all mentioned, plus fictional Unobtainium Road.",
            Context(roads, roads));

        result.HasNonCandidateRoad.ShouldBeFalse();
    }

    [Fact]
    public void Empty_fetched_pool_has_no_violation()
    {
        var result = CandidateGrounding.Check(
            "Forest Way and every other road are mentioned in this legacy regeneration script.",
            Context([Corridor("M7")], allCorridors: []));

        result.HasNonCandidateRoad.ShouldBeFalse();
        result.NonCandidateRoads.ShouldBeEmpty();
    }

    [Fact]
    public void Good_runs_flow_pool_and_monitored_routes_are_candidate_vocabulary()
    {
        var goodRunRoad = Corridor("Good Run Road");
        var flowPoolRoad = Corridor("Flow Pool Road");
        var monitoredRoad = Corridor("Monitored Route Road");
        var monitoredRoute = new MonitoredRouteStatus(
            "Monitored Route Road",
            new CoveragePoint(-33.8, 151.0),
            new CoveragePoint(-33.9, 151.2),
            DriveTimeMinutes: 30,
            NormalDriveTimeMinutes: 25,
            DistanceKm: 20);

        var result = CandidateGrounding.Check(
            "Good Run Road is clear, Flow Pool Road is also clear, and Monitored Route Road is " +
            "running five minutes late.",
            Context(
                corridors: [],
                allCorridors: [goodRunRoad, flowPoolRoad, monitoredRoad],
                goodRunRoads: ["Good Run Road"],
                flowPool: [new FlowRoad("Flow Pool Road", JamFactor: 0.5, LengthMeters: 1_000)],
                monitoredRoutes: [monitoredRoute]));

        result.HasNonCandidateRoad.ShouldBeFalse();
        result.NonCandidateRoads.ShouldBeEmpty();
    }

    /// <summary>
    ///     Reproduces dev job <c>5e613a74559f49c2a6580969af196ba7</c> (2026-08-29), which failed all
    ///     four generation attempts and produced no audio: the brief supplied the M4 drive time
    ///     through a monitored route named as a phrase, the writer voiced it as instructed, and the
    ///     bare corridor was outside the candidate cut.
    /// </summary>
    [Fact]
    public void Route_named_as_a_phrase_still_makes_its_corridor_a_candidate()
    {
        var monitoredRoute = new MonitoredRouteStatus(
            "the M4 from Parramatta to the city",
            new CoveragePoint(-33.8, 150.9),
            new CoveragePoint(-33.87, 151.2),
            DriveTimeMinutes: 28,
            NormalDriveTimeMinutes: 25,
            DistanceKm: 24);

        var result = CandidateGrounding.Check(
            "The M4 from Parramatta to the city is three minutes slower than typical.",
            Context(
                corridors: [],
                allCorridors: [Corridor("M4")],
                monitoredRoutes: [monitoredRoute]));

        result.HasNonCandidateRoad.ShouldBeFalse();
        result.NonCandidateRoads.ShouldBeEmpty();
    }

    /// <summary>
    ///     Reproduces dev job <c>cf352c34a4ee428bb2cbc7df6aafe77f</c> (2026-08-29), which failed all
    ///     four generation attempts and produced no audio on "James Congdon Drive". The evidence
    ///     supplied it as the cross street locating South Road's roadworks, so the read was stating
    ///     a position the way traffic radio states one, and the gate rejected the correct copy.
    /// </summary>
    [Fact]
    public void Cross_street_named_in_a_candidates_own_evidence_is_not_a_non_candidate_road()
    {
        var southRoad = Corridor("South Road") with
        {
            Incidents =
            [
                new TrafficIncident(
                    Id: "inc-1",
                    Description: "Roadworks at James Congdon Drive",
                    Severity: 2,
                    Location: "Richmond",
                    Type: "construction")
            ]
        };

        var result = CandidateGrounding.Check(
            "Roadworks on South Road at James Congdon Drive in Richmond.",
            Context(
                corridors: [southRoad],
                allCorridors: [southRoad, Corridor("James Congdon Drive")]));

        result.HasNonCandidateRoad.ShouldBeFalse();
        result.NonCandidateRoads.ShouldBeEmpty();
    }

    [Fact]
    public void Matching_is_case_and_whitespace_insensitive()
    {
        var result = CandidateGrounding.Check(
            "Mitchell Road is clear today.",
            Context(
                corridors: [Corridor(" mitchell road ")],
                allCorridors: [Corridor(" mitchell road ")]));

        result.HasNonCandidateRoad.ShouldBeFalse();
    }

    [Fact]
    public void Reports_two_distinct_non_candidate_roads_once_each()
    {
        var candidates = new[] { Corridor("M7") };
        var fetched = new[]
        {
            Corridor("M7"),
            Corridor("Forest Way"),
            Corridor("Governor Road")
        };

        var result = CandidateGrounding.Check(
            "Forest Way is blocked. Governor Road is slow. Forest Way remains blocked.",
            Context(candidates, fetched));

        result.HasNonCandidateRoad.ShouldBeTrue();
        result.NonCandidateRoads.Count.ShouldBe(2);
        result.NonCandidateRoads.ShouldContain("Forest Way");
        result.NonCandidateRoads.ShouldContain("Governor Road");
    }
}
