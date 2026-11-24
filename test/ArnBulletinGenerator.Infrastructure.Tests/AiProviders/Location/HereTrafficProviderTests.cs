using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public class HereTrafficProviderTests
{
    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly MockHttpMessageHandler _mockHandler = new();
    private readonly HereTrafficProvider _provider;
    private readonly IRoutingProvider _routingProvider = Substitute.For<IRoutingProvider>();
    private readonly IMapAttributesProvider _mapAttributes = Substitute.For<IMapAttributesProvider>();

    public HereTrafficProviderTests()
    {
        var httpClient = new HttpClient(_mockHandler);
        var hereOptions = Options.Create(new HereOptions { ApiKey = "test_key" });
        var broadcastOptions = Options.Create(new BroadcastOptions { DefaultTimezone = "Australia/Sydney" });
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad>()));
        _provider = new HereTrafficProvider(httpClient, hereOptions, _geocodingProvider, _routingProvider,
            broadcastOptions, Substitute.For<ILogger<HereTrafficProvider>>(), _mapAttributes);
    }

    private void MockGeocodingEmpty()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(new Dictionary<int, GeocodedPlace>()));
    }

    [Fact]
    public async Task GetIncidentsAsync_ValidResponse_ReturnsMappedIncidents()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "accident", "Accident on Pacific Hwy", 2, 1500, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(1);
        var incident = result.Value.First();
        incident.Id.ShouldBe("INC001");
        incident.Type.ShouldBe("accident");
        incident.Description.ShouldBe("Accident on Pacific Hwy");
    }

    [Fact]
    public async Task GetIncidentsAsync_HereCriticalityFlowsToFactPriorityRank()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """{"results":[{"location":{"shape":{"links":[{"points":[{"lat":-33.87,"lng":151.21}],"length":1500,"functionalClass":2}]},"segmentRef":{"segments":[{"ref":"seg-1","length":1500}]}},"incidentDetails":{"id":"INC-CRITICAL","type":"accident","criticality":"critical","description":{"value":"Crash"}}}]}""",
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.Single();
        incident.Fact.ShouldNotBeNull();
        incident.Fact!.AuthorityPriority.Value.ShouldBe("critical");
        incident.Fact.AuthorityPriorityRank.Value.ShouldBe(1);
    }

    [Fact]
    public async Task GetIncidentsAsync_EmptyResults_ReturnsEmptyList()
    {
        _mockHandler.SetResponse("""{"results": []}""", HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetIncidentsAsync_HttpError_ReturnsFailure()
    {
        _mockHandler.SetResponse("error", HttpStatusCode.InternalServerError);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetIncidentsAsync_NullResults_ReturnsFailure()
    {
        _mockHandler.SetResponse("""{"results": null}""", HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetIncidentsAsync_MapsLatLng_FromFirstPointOfFirstLink()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "accident", "Accident somewhere", 3, 500, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.Latitude.HasValue.ShouldBeTrue();
        incident.Latitude.Value.ShouldBe(-33.87, 0.001);
        incident.Longitude.HasValue.ShouldBeTrue();
        incident.Longitude.Value.ShouldBe(151.21, 0.001);
    }

    [Fact]
    public async Task GetIncidentsAsync_FreewayAccident_ReturnsSeverity1()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "accident", "Accident on freeway", 1, 1500, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.Severity.ShouldBe(1);
        incident.FunctionalClass.ShouldBe(1);
    }

    [Fact]
    public async Task GetIncidentsAsync_LocalRoadHazard_ReturnsSeverity4()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "roadHazard", "Hazard on local road", 5, 200, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.Severity.ShouldBe(4);
        incident.FunctionalClass.ShouldBe(5);
    }

    /// <summary>
    ///     HERE returns segment references in a COMPACT form with the catalog and segment prefixes
    ///     factored out into the response's <c>refReplacements</c> map. Stored unexpanded they cannot be
    ///     looked up at all — which is why segment corroboration silently never fired on dev
    ///     (2026-08-13, RoadNameVerified false on every incident).
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_ExpandsCompactSegmentReferences()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """
            {"sourceUpdated":"2026-08-13T16:17:24Z",
             "refReplacements":{"0":"hrn:here:data::olp-here:rib-2:9203:","1":"here:cm:segment"},
             "results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-16.81,"lng":145.63}],"length":268,"functionalClass":2}]},
                "segmentRef":{"segments":[{"ref":"$0:367219725:$1:221129749#+0.35801..0.98732","length":268}]}},
              "incidentDetails":{"id":"K1","type":"roadHazard","criticality":"minor",
                "description":{"value":"Hazardous driving conditions"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-16.81, 145.63, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.HereSegmentSpans.ShouldNotBeNull();
        var span = incident.HereSegmentSpans.ShouldHaveSingleItem();
        span.Ref.ShouldBe(
            "hrn:here:data::olp-here:rib-2:9203::367219725:here:cm:segment:221129749#+0.35801..0.98732");
        span.LengthMeters.ShouldBe(268);
    }

    /// <summary>
    ///     Issue 1, the M7 defect at its root. HERE gives every incident a <c>segmentRef</c>, and the
    ///     map's name for THAT segment is the road identity belonging to the incident's own geometry. It
    ///     wins outright over the proximity answer — no corroboration is asked for, because reverse
    ///     geocoding named a different road than HERE's own map on 36% of measured records and so cannot
    ///     arbitrate. This is how a closure whose segment is Wallgrove Rd stops airing as a closed
    ///     Westlink M7.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_SegmentRoadWinsOverProximityName()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(
                new Dictionary<int, GeocodedPlace>
                {
                    [0] = new(Option<string>.Some("Westlink M7"), Option<string>.Some("Cecil Park"),
                        ["Wallgrove Rd", "Westlink M7"])
                }));
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad>
                {
                    ["hrn:here:data::olp-here:rib-2:9203::365362057:here:cm:segment:909906003#+0.06319..0.9361"] =
                        new("Wallgrove Rd", 5, false)
                }));
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.86676,"lng":150.84729},{"lat":-33.86817,"lng":150.84716}],
                    "length":157,"functionalClass":5}]},
                "segmentRef":{"segments":[
                    {"ref":"hrn:here:data::olp-here:rib-2:9203::365362057:here:cm:segment:909906003#+0.06319..0.9361","length":137}]}},
              "incidentDetails":{"id":"4429929314728820194","originalId":"4429929314728820192",
                "type":"roadClosure","criticality":"critical","roadClosed":true,
                "description":{"value":"Closed"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.Location.ShouldBe("Wallgrove Rd");
        incident.RoadNameVerified.ShouldBeTrue();
        incident.FunctionalClass.ShouldBe(5);
    }

    /// <summary>
    ///     When the map and the proximity answer disagree, the MAP wins and the proximity name never
    ///     appears: adopting whichever name happened to be nearby is the behaviour that named events
    ///     after the arterial beside them.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_SegmentRoadDisagreesWithProximityName_UsesTheMapName()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(
                new Dictionary<int, GeocodedPlace>
                {
                    [0] = new(Option<string>.Some("Bruce Hwy"), default, ["Bruce Hwy"])
                }));
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad> { ["seg-1"] = new("Foster Rd", 4, false) }));
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-16.9,"lng":145.75}],"length":300,"functionalClass":2}]},
                "segmentRef":{"segments":[{"ref":"seg-1","length":300}]}},
              "incidentDetails":{"id":"C1","type":"accident","criticality":"major",
                "description":{"value":"Crash"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-16.9, 145.75, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.Location.ShouldBe("Foster Rd");
        incident.RoadNameVerified.ShouldBeTrue();
    }

    /// <summary>
    ///     Issue 2, end to end: when HERE gives no usable street the incident must NOT be named after
    ///     the locality. "Arundel", "Greensborough" and "Laverton" all aired as roads this way. The
    ///     suburb still travels — as a suburb.
    /// </summary>
    [Fact]
    public async Task GetCorridorsAsync_LocalityWithNoStreet_IsNotVoicedAsARoad()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(
                new Dictionary<int, GeocodedPlace>
                {
                    [0] = new(default, Option<string>.Some("Arundel"))
                }));
        // "accident" deliberately: construction and plannedEvent are suppressed overnight
        // (GetEffectiveAllowedTypes), which would make this assertion time-of-day dependent.
        _mockHandler.SetResponseFor("/incidents",
            BuildTrafficJson(("INC001", "accident", "Crash", 3, 530, -27.94, 153.36)));
        _mockHandler.SetResponseFor("/flow", """{"results":[]}""");

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-27.94, 153.36, 10.0).Value, 5);

        var corridor = result.Value.ShouldHaveSingleItem();
        corridor.RoadName.ShouldNotBe("Arundel");
        corridor.Suburb.GetValueOr("").ShouldBe("Arundel");
    }

    /// <summary>
    ///     Sydney, 2026-08-13 (ARN operator report, "M7 ISN'T currently closed"): a 157 m closure whose
    ///     two HERE links are BOTH FunctionalClass 5 reverse-geocoded to "Westlink M7" — the
    ///     motorway 20 m away, not the 0 m street (Wallgrove Rd, which HERE Map Attributes confirms
    ///     for the incident's own segment 909906003). The name match promoted it 5 -> 1, it
    ///     out-ranked every real Sydney incident, and the bulletin aired "The M7's still closed
    ///     southbound". A proximity name that contradicts HERE's own road class is refused outright:
    ///     the incident keeps HERE's class and carries no road name, so it can never be voiced as
    ///     that motorway.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_GeocodedMotorwayNameContradictingHereRoadClass_IsRefused()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(
                new Dictionary<int, GeocodedPlace> { [0] = new(Option<string>.Some("Westlink M7"), default) }));
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "roadClosure", "Closed", 5, 157, -33.86676, 150.84729)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.Location.ShouldNotContain("M7");
        incident.FunctionalClass.ShouldBe(5);
    }

    /// <summary>
    ///     A reverse-geocoded road name is never adopted as identity at all, however plausible it looks
    ///     beside HERE's own road class. It answers "what is near this point", and where HERE gave no
    ///     segments to resolve, the record stays unnamed and is withheld rather than voiced.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_PointOfInterestMapName_RemainsUnvoiceable()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad> { ["seg-poi"] = new("Attenborough Park", 4, false) }));
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.87,"lng":151.21}],"length":300,"functionalClass":4}]},
                "segmentRef":{"segments":[{"ref":"seg-poi","length":300}]}},
              "incidentDetails":{"id":"POI-1","type":"accident","description":{"value":"Crash"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.Location.ShouldBe("Unidentified Road");
        incident.RoadNameVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task GetIncidentsAsync_GeocodedRoadName_IsNeverAdoptedAsIdentity()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(
                new Dictionary<int, GeocodedPlace> { [0] = new(Option<string>.Some("M2 Hills Mwy"), default) }));
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "construction", "Road construction", 1, 800, -33.79, 151.05)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.Location.ShouldBe("Unidentified Road");
        incident.RoadNameVerified.ShouldBeFalse();
        incident.FunctionalClass.ShouldBe(1);
    }

    [Fact]
    public async Task GetIncidentsAsync_MultipleIncidents_AllReturned()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(
                ("INC001", "accident", "Accident one", 2, 1500, -33.87, 151.21),
                ("INC002", "congestion", "Congestion two", 3, 3000, -33.86, 151.20),
                ("INC003", "roadClosure", "Closure three", 4, 500, -33.85, 151.19)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(3);
    }

    [Fact]
    public async Task GetIncidentsAsync_GeocodingSuppliesSuburbNotRoad()
    {
        _geocodingProvider.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(
                new Dictionary<int, GeocodedPlace>
                {
                    { 0, new GeocodedPlace(Option<string>.Some("George Street"), Option<string>.Some("Sydney")) }
                }));

        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "accident", "Accident somewhere", 3, 500, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.Suburb.GetValueOr("").ShouldBe("Sydney");
        incident.Location.ShouldNotBe("George Street");
    }

    // ── GetCorridorsAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetCorridorsAsync_WithIncidents_ReturnsCorridors()
    {
        MockGeocodingEmpty();
        _routingProvider.GetDriveTimeAsync(
                Arg.Any<(double, double)>(), Arg.Any<(double, double)>(), Arg.Any<CancellationToken>())
            .Returns(Result<DriveTimeResult>.Failure("no route"));

        _mockHandler.SetResponse(
            BuildTrafficJson(
                ("C1", "congestion", "Congestion on M4", 1, 3000, -33.82, 151.10),
                ("C2", "congestion", "Congestion on M4 ramp", 1, 2000, -33.83, 151.11)),
            HttpStatusCode.OK);

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 15.0).Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GetCorridorsAsync_EmptyIncidents_ReturnsEmptyList()
    {
        _mockHandler.SetResponse("""{"results": []}""", HttpStatusCode.OK);

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCorridorsAsync_HttpFailure_ReturnsFailure()
    {
        _mockHandler.SetResponse("server error", HttpStatusCode.InternalServerError);

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCorridorsAsync_DriveTimeEnrichment_SetsMinutes()
    {
        MockGeocodingEmpty();
        // Two incidents on same road so they aggregate into a corridor with start/end points
        _mockHandler.SetResponse(
            BuildTrafficJson(
                ("D1", "congestion", "Congestion on Pacific Highway", 2, 4000, -33.80, 151.18),
                ("D2", "congestion", "Congestion on Pacific Highway", 2, 3000, -33.84, 151.20)),
            HttpStatusCode.OK);

        _routingProvider.GetDriveTimeAsync(
                Arg.Any<(double, double)>(), Arg.Any<(double, double)>(), Arg.Any<CancellationToken>())
            .Returns(Result<DriveTimeResult>.Success(new DriveTimeResult(18, 12, 8.5)));

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 15.0).Value);

        result.IsSuccess.ShouldBeTrue();
        // Any corridor with valid start/end should attempt drive time.
        var enriched = result.Value.Where(c => c.DriveTimeMinutes.HasValue).ToList();
        if (enriched.Count > 0)
        {
            enriched.First().DriveTimeMinutes.Value.ShouldBe(18);
            enriched.First().NormalDriveTimeMinutes.Value.ShouldBe(12);
        }
    }

    [Fact]
    public async Task GetCorridorsAsync_DriveTimeEnrichment_IncludesLocalFunctionalClasses()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(
                ("L1", "congestion", "At Local Road", 5, 800, -33.80, 151.18),
                ("L2", "congestion", "At Local Road", 5, 700, -33.804, 151.181)),
            HttpStatusCode.OK);

        _routingProvider.GetDriveTimeAsync(
                Arg.Any<(double, double)>(), Arg.Any<(double, double)>(), Arg.Any<CancellationToken>())
            .Returns(Result<DriveTimeResult>.Success(new DriveTimeResult(12, 8, 2.0)));

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 15.0).Value);

        result.IsSuccess.ShouldBeTrue();

        await _routingProvider.Received().GetDriveTimeAsync(
            Arg.Any<(double, double)>(), Arg.Any<(double, double)>(), Arg.Any<CancellationToken>());
    }

    // ── Polygon post-filter ──────────────────────────────────────────

    [Fact]
    public async Task GetIncidentsAsync_PolygonFilter_DropsOutsideIncident()
    {
        MockGeocodingEmpty();
        // Triangle polygon in Sydney CBD — bbox ~(-33.89, 151.20) → (-33.85, 151.22)
        var polygon = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        // A: shape point inside the polygon   → must be kept
        // B: shape point inside bbox but outside polygon → must be dropped
        _mockHandler.SetResponse(
            BuildTrafficJson(
                ("A", "accident", "Inside polygon", 2, 1000, -33.86, 151.205),
                ("B", "accident", "Outside polygon", 2, 500, -33.88, 151.215)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(polygon);

        result.IsSuccess.ShouldBeTrue();
        var incidents = result.Value.ToList();
        incidents.Count.ShouldBe(1);
        incidents[0].Id.ShouldBe("A");
    }

    [Fact]
    public async Task GetIncidentsAsync_PolygonFilter_KeepsInsideIncident()
    {
        MockGeocodingEmpty();
        var polygon = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        // Single incident with shape point inside the polygon
        _mockHandler.SetResponse(
            BuildTrafficJson(
                ("C", "congestion", "Inside polygon", 2, 2000, -33.86, 151.205)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(polygon);

        result.IsSuccess.ShouldBeTrue();
        var incidents = result.Value.ToList();
        incidents.Count.ShouldBe(1);
        incidents[0].Id.ShouldBe("C");
    }

    [Fact]
    public async Task GetIncidentsAsync_PolygonFilter_DropsOutsideCongestion()
    {
        MockGeocodingEmpty();
        var polygon = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        _mockHandler.SetResponse(
            BuildTrafficJson(("C-OUT", "congestion", "Outside polygon congestion", 2, 2000, -33.88, 151.215)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(polygon);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }


    [Fact]
    [Trait("Category", "BoundaryTrial")]
    public async Task GetIncidentsAsync_PolygonFilter_DropsUnlocatedIncident()
    {

        MockGeocodingEmpty();
        var polygon = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        _mockHandler.SetResponse(
            """
            {"results":[{"location":{"shape":{"links":[{"points":[],"length":500,"functionalClass":3}]}},
              "incidentDetails":{"id":"HERE-SHAPELESS","type":"congestion",
              "description":{"value":"Unlocated congestion"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(polygon);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(0);
    }
    [Fact]
    public async Task GetIncidentsAsync_LargePolygonSpanningMultipleTiles_DedupesAcrossTiles()
    {
        MockGeocodingEmpty();
        // Enclosing box ~2.5 deg lat x 2 deg lng — over the 1-degree per-tile limit,
        // so BuildTiles fans this out to multiple HERE requests. The mock handler
        // returns the same incident payload for every request (it can't distinguish
        // tiles), simulating the same incident being returned by every intersecting
        // tile — the provider must merge and de-duplicate by incident id.
        var polygon = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.0, 134.5),
            new(-33.0, 136.5),
            new(-35.5, 135.5)
        }).Value;

        _mockHandler.SetResponse(
            BuildTrafficJson(("DUP1", "accident", "Same incident from every tile", 2, 1000, -34.0, 135.5)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(polygon);

        result.IsSuccess.ShouldBeTrue();
        var incidents = result.Value.ToList();
        incidents.Count.ShouldBe(1);
        incidents[0].Id.ShouldBe("DUP1");
    }

    // ── Suppression with local timezone ─────────────────────────────

    [Fact]
    public void GetEffectiveAllowedTypes_AuDaytime_KeepsConstructionAndPlannedEvent()
    {
        // 10am AEST = midnight UTC June 15 (AEST, no DST) — not in 22:00-05:00 window
        var utcNow = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var result = _provider.GetEffectiveAllowedTypes(utcNow, "Australia/Sydney");

        result.ShouldNotBeNull();
        result.ShouldContain("construction");
        result.ShouldContain("plannedEvent");
    }

    [Fact]
    public void GetEffectiveAllowedTypes_AuOvernight_RemovesConstructionAndPlannedEvent()
    {
        // 11pm AEST = 1pm UTC June 15 — inside the 22:00-05:00 overnight window
        var utcNow = new DateTime(2025, 6, 15, 13, 0, 0, DateTimeKind.Utc);
        var result = _provider.GetEffectiveAllowedTypes(utcNow, "Australia/Sydney");

        result.ShouldNotBeNull();
        result.ShouldNotContain("construction");
        result.ShouldNotContain("plannedEvent");
    }

    // ── ResolveDefaultTimeZone (corridor peak-hour weighting) ─────────

    [Fact]
    public void ResolveDefaultTimeZone_ConfiguredSydneyTimezone_ReturnsMatchingTimeZoneInfo()
    {
        // _provider is constructed with DefaultTimezone = "Australia/Sydney" (default config)
        var result = _provider.ResolveDefaultTimeZone();

        result.ShouldNotBeNull();
        result.Id.ShouldBe("Australia/Sydney");
        result.HasSameRules(TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney")).ShouldBeTrue();
    }

    [Fact]
    public void ResolveDefaultTimeZone_BlankTimezoneId_ReturnsNull()
    {
        var provider = CreateProviderWithTimezone("");

        var result = provider.ResolveDefaultTimeZone();

        result.ShouldBeNull();
    }

    [Fact]
    public void ResolveDefaultTimeZone_UnresolvableTimezoneId_ReturnsNullWithoutThrowing()
    {
        var provider = CreateProviderWithTimezone("Not/ARealZone");

        var result = Should.NotThrow(() => provider.ResolveDefaultTimeZone());

        result.ShouldBeNull();
    }

    private HereTrafficProvider CreateProviderWithTimezone(string? timezoneId)
    {
        var httpClient = new HttpClient(_mockHandler);
        var hereOptions = Options.Create(new HereOptions { ApiKey = "test_key" });
        var broadcastOptions = Options.Create(new BroadcastOptions { DefaultTimezone = timezoneId! });
        return new HereTrafficProvider(httpClient, hereOptions, _geocodingProvider, _routingProvider,
            broadcastOptions, Substitute.For<ILogger<HereTrafficProvider>>());
    }

    // ── Fail-soft mapping ────────────────────────────────────────────

    [Fact]
    public async Task GetIncidentsAsync_NullDescriptionValue_DoesNotThrow()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse("""{"results": [{"location": {"shape": {"links": [{"points": [{"lat": -33.87, "lng": 151.21}], "length": 500, "functionalClass": 3}]}}, "incidentDetails": {"id": "N1", "type": "accident", "criticality": "major", "description": {"value": null}}}]}""", HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(1);
        result.Value.First().Description.ShouldBe("Unknown traffic event");
    }

    [Fact]
    public async Task GetIncidentsAsync_OperatorComment_PopulatesAdvisoryStrippedComment()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """{"results": [{"location": {"shape": {"links": [{"points": [{"lat": -33.87, "lng": 151.21}], "length": 500, "functionalClass": 3}]}}, "incidentDetails": {"id": "C1", "type": "accident", "criticality": "major", "description": {"value": "Road closed due to accidents"}, "comment": "Pilatus St eastbound between Centurion Pl and Berrigan D - Road Crash Road closed. Exercise Extreme Caution."}}]}""",
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.Comment.HasValue.ShouldBeTrue();
        incident.Comment.Value.ShouldContain("Pilatus St eastbound");
        incident.Comment.Value.ShouldNotContain("Exercise Extreme Caution");
    }

    [Fact]
    public async Task GetIncidentsAsync_RoadClosedAndEntryTime_MapsToTrafficIncident()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """{"results": [{"location": {"shape": {"links": [{"points": [{"lat": -33.87, "lng": 151.21}], "length": 500, "functionalClass": 3}]}}, "incidentDetails": {"id": "R1", "type": "accident", "criticality": "major", "description": {"value": "Major crash"}, "roadClosed": true, "entryTime": "2026-06-05T02:22:46Z"}}]}""",
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.RoadClosed.ShouldBeTrue();
        incident.EntryTimeUtc.ShouldBe(new DateTimeOffset(2026, 6, 5, 2, 22, 46, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetIncidentsAsync_AbsentRoadClosedAndEntryTime_DefaultsToFalseAndNull()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(("D1", "construction", "Roadworks", 3, 500, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.First();
        incident.RoadClosed.ShouldBeFalse();
        incident.EntryTimeUtc.ShouldBeNull();
    }

    /// <summary>
    ///     HERE returns segmentRef segment lengths as FRACTIONAL numbers. Binding that member to
    ///     an int threw FormatException part-way through the results array, so the whole fetch
    ///     failed and every affected job died at HereFetched (dev, 2026-08-05). Only `ref` is read,
    ///     so a fractional length must deserialize and the incident must still surface.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_FractionalSegmentRefLength_StillParses()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.87,"lng":151.21}],"length":500,"functionalClass":3}]},
                "segmentRef":{"segments":[
                    {"ref":"here:cm:segment:1","length":1013.4759521484375},
                    {"ref":"here:cm:segment:2","length":0.5}]}},
              "incidentDetails":{"id":"SEG1","type":"accident","criticality":"major",
                "description":{"value":"Crash on the Monash"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        var incident = result.Value.ShouldHaveSingleItem();
        incident.Id.ShouldBe("SEG1");
        incident.SegmentRefs.ShouldBe(["here:cm:segment:1", "here:cm:segment:2"]);
    }

    /// <summary>
    ///     HERE Routing v8's <c>avoid[segments]</c> accepts HMC topology segment ids only
    ///     (<c>here:cm:segment:&lt;id&gt;[#&lt;direction&gt;]</c>), but the Traffic API returns refs as
    ///     compact link-pair ids (<c>$0:&lt;tile&gt;:$1:&lt;segment&gt;#&lt;dir&gt;&lt;off&gt;..&lt;off&gt;</c>) or
    ///     full catalog refs. Feeding those straight into avoid[segments] 400s every detour
    ///     request ("Invalid topology segment", E605001). The mapping must convert both shapes
    ///     to the routing-consumable form, strip the offset ranges, and leave already-correct
    ///     identifiers untouched.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_ConvertsSegmentRefsToRoutingAvoidFormat()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.87,"lng":151.21}],"length":500,"functionalClass":3}]},
                "segmentRef":{"segments":[
                    {"ref":"$0:367132262:$1:496745322#+0..0.88379","length":0.88379},
                    {"ref":"hrn:here:data::olp-here:rib-2:9203::367132262:here:cm:segment:764042816#-0.41295..1","length":0.58705},
                    {"ref":"here:cm:segment:844532819#+","length":1},
                    {"ref":"here:cm:segment:3","length":1}]}},
              "incidentDetails":{"id":"SEG2","type":"construction","criticality":"major",
                "description":{"value":"Roadworks"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.IsSuccess.ShouldBeTrue();
        var incident = result.Value.ShouldHaveSingleItem();
        incident.SegmentRefs.ShouldBe([
            "here:cm:segment:496745322#+",
            "here:cm:segment:764042816#-",
            "here:cm:segment:844532819#+",
            "here:cm:segment:3"]);
    }

    // ── HERE-fidelity guarantees ────────────────────────────────────────────

    /// <summary>
    ///     Issue 7. HERE stamps the response envelope with <c>sourceUpdated</c>. We never read it, so
    ///     <c>DataTimestampUtc</c> was null on every incident across all 212 corpus jobs and
    ///     <c>TrafficDataMaxAgeMinutes</c> filtered nothing — a cleared or superseded incident could
    ///     still air. HERE's own <c>originalId</c> is likewise the only stable handle on an event
    ///     (its <c>id</c> changes with every revision), so both must survive into the domain.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_CarriesHereFreshnessAndStableIdentity()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            """
            {"sourceUpdated":"2026-08-13T11:38:11Z","results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.87,"lng":151.21}],"length":500,"functionalClass":3}]}},
              "incidentDetails":{"id":"4429929314728820194","originalId":"4429929314728820192",
                "type":"accident","criticality":"major","description":{"value":"Crash"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.OriginalId.ShouldBe("4429929314728820192");
        incident.DataTimestampUtc.ShouldBe(new DateTimeOffset(2026, 8, 13, 11, 38, 11, TimeSpan.Zero));
    }

    /// <summary>
    ///     Issue 8. HERE's own functional class must reach the domain unaltered and be preserved for
    ///     audit. Deriving road importance from words in the description let a name match promote a
    ///     local road to motorway class (Sydney 2026-08-13: FC5 links aired as a closed M7), and the
    ///     record kept no trace of what HERE actually said.
    /// </summary>
    [Fact]
    public async Task GetIncidentsAsync_KeepsHereFunctionalClass_AndDoesNotPromoteFromDescriptionText()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponse(
            BuildTrafficJson(("INC001", "construction", "Construction near M1 interchange", 4, 1000, -33.87, 151.21)),
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.FunctionalClass.ShouldBe(4);
        incident.HereFunctionalClass.ShouldBe(4);
    }

    /// <summary>
    ///     Issues 3 and 4. The flow reading we act on must be HERE's worst reading for the segment,
    ///     including <c>subSegments</c> (a parent averages over its length, so a standstill inside it
    ///     reads as free-flowing), and must be ignored entirely when HERE's <c>confidence</c> says the
    ///     value is a historical or speed-limit estimate rather than live probe data.
    /// </summary>
    [Fact]
    public async Task GetFreeFlowingRoadsAsync_ExcludesHiddenStandstillAndNonRealTimeReadings()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponseFor("/flow",
            """
            {"sourceUpdated":"2026-08-13T11:38:11Z","results":[
              {"location":{"description":"Shiers Avenue","length":828},
               "currentFlow":{"speed":9.0,"freeFlow":11.0,"jamFactor":2.0,"confidence":0.95,
                 "subSegments":[{"length":200,"speed":0.5,"freeFlow":11.0,"jamFactor":8.4}]}},
              {"location":{"description":"Bringelly Road","length":1836},
               "currentFlow":{"speed":13.0,"freeFlow":14.0,"jamFactor":0.4,"confidence":0.45}},
              {"location":{"description":"Victoria Road","length":900},
               "currentFlow":{"speed":13.5,"freeFlow":14.0,"jamFactor":0.3,"confidence":0.92}}]}
            """);
        _mockHandler.SetResponse("""{"results":[]}""", HttpStatusCode.OK);

        var result = await _provider.GetFreeFlowingRoadsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 10);

        var roads = result.Value.Select(f => f.RoadName).ToList();
        roads.ShouldContain("Victoria Road");
        roads.ShouldNotContain("Shiers Avenue");   // hidden standstill inside the parent segment
        roads.ShouldNotContain("Bringelly Road");  // confidence 0.45 — not a live reading
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_ExcludesRoadWithSeparateCongestedSegment()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        _ => new SegmentRoad("Pacific Motorway", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/flow",
            """
            {"results":[
              {"location":{"description":"Free-flow label","length":1200,
                "segmentRef":{"segments":[{"ref":"free-segment","length":1200}]}},
               "currentFlow":{"speed":13.0,"freeFlow":14.0,"jamFactor":1.0,"confidence":0.95}},
              {"location":{"description":"Different congested label","length":900,
                "segmentRef":{"segments":[{"ref":"congested-segment","length":900}]}},
               "currentFlow":{"speed":1.0,"freeFlow":14.0,"jamFactor":8.0,"confidence":0.95}}]}
            """);

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 10);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    /// <summary>
    ///     Issue 5. Corridor-to-flow matching was a two-way substring test, so a flow row for one road
    ///     could hand its delay to a different corridor whose name it merely contains. Matching is now
    ///     segment identity first, then abbreviation-aware equality on the row's RESOLVED name — and
    ///     never on its free-text label.
    /// </summary>
    [Fact]
    public async Task GetCorridorsAsync_DoesNotAttachAnotherRoadsFlowDelayByNameSubstring()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad>
                {
                    ["incident-seg"] = new("Bay Street", 4, false),
                    ["flow-seg"] = new("Botany Bay Street", 3, false)
                }));
        _mockHandler.SetResponseFor("/incidents",
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.87,"lng":151.21}],"length":400,"functionalClass":4}]},
                "segmentRef":{"segments":[{"ref":"incident-seg","length":400}]}},
              "incidentDetails":{"id":"INC001","type":"accident","criticality":"major",
                "description":{"value":"Crash"}}}]}
            """);
        _mockHandler.SetResponseFor("/flow",
            """
            {"results":[{"location":{"description":"Botany Bay Street","length":1500,
               "segmentRef":{"segments":[{"ref":"flow-seg","length":1500}]}},
               "currentFlow":{"speed":1.0,"freeFlow":14.0,"jamFactor":9.5,"confidence":0.95}}]}
            """);

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        var bayStreet = result.Value.FirstOrDefault(c => c.RoadName == "Bay Street");
        bayStreet.ShouldNotBeNull();
        bayStreet.FlowDelayMinutes.HasValue.ShouldBeFalse();
        // Botany Bay Street is a real, separately congested road and keeps its own corridor.
        result.Value.ShouldContain(c => c.RoadName == "Botany Bay Street");
    }

    [Fact]
    public async Task GetIncidentsAsync_HereSegmentDirection_IsDeterministic()
    {
        MockGeocodingEmpty();
        // F3 — job 8997e426; byte-identical 457 m input aired citybound 15x and outbound 1x. HERE stamps a direction character on 100% of refs (2,209/2,209 corpus; 123/123 live).
        _mockHandler.SetResponse(BuildF3ClosurePayload('-'), HttpStatusCode.OK);

        var area = CoverageArea.Create(-33.8688, 151.2093, 10.0).Value;
        var first = await _provider.GetIncidentsAsync(area);
        var second = await _provider.GetIncidentsAsync(area);

        var firstIncident = first.Value.ShouldHaveSingleItem();
        var secondIncident = second.Value.ShouldHaveSingleItem();
        firstIncident.Direction.ShouldBe(secondIncident.Direction);
    }

    [Fact]
    public async Task GetIncidentsAsync_HereSegmentDirection_ChangesWithSegmentSign()
    {
        MockGeocodingEmpty();
        // F3 — job 8997e426; byte-identical input aired citybound 15x and outbound 1x. HERE stamps a direction character on 100% of refs (2,209/2,209 corpus; 123/123 live).
        var area = CoverageArea.Create(-33.8688, 151.2093, 10.0).Value;
        _mockHandler.SetResponse(BuildF3ClosurePayload('-'), HttpStatusCode.OK);
        var minus = (await _provider.GetIncidentsAsync(area)).Value.ShouldHaveSingleItem();

        _mockHandler.SetResponse(BuildF3ClosurePayload('+'), HttpStatusCode.OK);
        var plus = (await _provider.GetIncidentsAsync(area)).Value.ShouldHaveSingleItem();

        minus.Direction.ShouldNotBe(plus.Direction);
    }

    [Fact]
    public async Task GetIncidentsAsync_UndirectedHereSegment_HasNoDirection()
    {
        MockGeocodingEmpty();
        // F3 — job 8997e426; byte-identical input aired citybound 15x and outbound 1x. HERE's `*` ref sign is undirected, so no direction may be guessed.
        _mockHandler.SetResponse(BuildF3ClosurePayload('*'), HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        result.Value.ShouldHaveSingleItem().Direction.HasValue.ShouldBeFalse();
    }


    [Fact]
    public async Task GetIncidentsAsync_MapsScheduledTimesAndAlertCodes()
    {
        MockGeocodingEmpty();
        // F8 — live job/by-ID record originalId=1872491318168830332; its long-term closure aired as current news.
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{"shape":{"links":[{"points":[],"length":292,"functionalClass":5}]}},
              "incidentDetails":{"id":"1872491318168830332","originalId":"1872491318168830332","type":"roadClosure","criticality":"critical","roadClosed":true,
                "startTime":"2026-07-27T20:39:00Z","endTime":"2026-08-17T21:00:00Z","codes":[401],"description":{"value":"Closed"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.StartTimeUtc.ShouldBe(new DateTimeOffset(2026, 7, 27, 20, 39, 0, TimeSpan.Zero));
        incident.EndTimeUtc.ShouldBe(new DateTimeOffset(2026, 8, 17, 21, 0, 0, TimeSpan.Zero));
        incident.Codes.ShouldBe([401]);
    }

    [Fact]
    public async Task GetIncidentsAsync_MapsRampFlagAndClosedAssetFromHereRecord()
    {
        MockGeocodingEmpty();
        const string segmentRef = "hrn:here:data::olp-here:rib-2:9203::365362849:here:cm:segment:914839809#-0.02183..0.97805";
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad> { [segmentRef] = new("Warringah Fwy", 1, true) }));
        // F2 — job cbeb6402; aired “Warringah Freeway's CLOSED southbound near Cammeray at the ramp to Alfred Street North.”
        _mockHandler.SetResponse(
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.82718,"lng":151.21387}],"length":1677,"functionalClass":1}]},
                "segmentRef":{"segments":[{"ref":"hrn:here:data::olp-here:rib-2:9203::365362849:here:cm:segment:914839809#-0.02183..0.97805","length":1677}]}},
              "incidentDetails":{"id":"F2","type":"roadClosure","criticality":"critical","roadClosed":true,
                "description":{"value":"Ramp to Alfred StreetNorth - Closed"}}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value);

        var incident = result.Value.ShouldHaveSingleItem();
        incident.SegmentIsRamp.ShouldBeTrue();
        incident.ClosedAsset.HasValue.ShouldBeTrue();
        incident.ClosedAsset.Value.ShouldBe("Ramp to Alfred StreetNorth");
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_RequestsSegmentReferencesOnFlow()
    {
        MockGeocodingEmpty();
        // F5 — job 5a0db2eb; aired “Fitzwilliam Road and Wiley Park are slow going”; this Paddys row was 14 km away.
        _mockHandler.SetResponseFor("/flow", """{"results":[]}""");

        await _provider.GetFreeFlowingRoadsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        _mockHandler.LastUrl.ShouldNotBeNull();
        _mockHandler.LastUrl.ShouldContain("locationReferencing=shape,segmentRef");
    }

    [Fact]
    public async Task GetCorridorsAsync_UsesMapIdentityAndFunctionalClassForFlowSegment()
    {
        MockGeocodingEmpty();
        const string segmentRef = "hrn:here:data::olp-here:rib-2:9203::365362127:here:cm:segment:218046787#-0.75701..1";
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad> { [segmentRef] = new("A3 / Centenary Dr", 2, false) }));
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        // F5 — job 5a0db2eb; aired “Fitzwilliam Road and Wiley Park are slow going”; the live Paddys row resolves to A3.
        _mockHandler.SetResponseFor("/flow",
            """
            {"results":[{"location":{"description":"Paddys Market Sydney","length":568,
              "shape":{"links":[{"points":[{"lat":-33.8587,"lng":151.06928}],"length":568,"functionalClass":2}]},
              "segmentRef":{"segments":[{"ref":"hrn:here:data::olp-here:rib-2:9203::365362127:here:cm:segment:218046787#-0.75701..1"}]}},
              "currentFlow":{"speed":1.0,"freeFlow":14.0,"jamFactor":7.6,"confidence":0.95}}]}
            """
        );

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        var road = result.Value.ShouldHaveSingleItem();
        road.RoadName.ShouldBe("A3 / Centenary Dr");
        road.FunctionalClass.ShouldBe(2);
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_UsesMapIdentityAndMarksNameFromSegment()
    {
        MockGeocodingEmpty();
        const string segmentRef = "hrn:here:data::olp-here:rib-2:9203::365362127:here:cm:segment:218046787#-0.75701..1";
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad> { [segmentRef] = new("A3 / Centenary Dr", 2, false) }));
        // F5 — job 5a0db2eb; aired “Fitzwilliam Road and Wiley Park are slow going”; the place label must not become a road.
        _mockHandler.SetResponseFor("/flow",
            """
            {"results":[{"location":{"description":"Australian Post Delivery Centre","length":688,
              "shape":{"links":[{"points":[{"lat":-33.8587,"lng":151.06928}],"length":688,"functionalClass":2}]},
              "segmentRef":{"segments":[{"ref":"hrn:here:data::olp-here:rib-2:9203::365362127:here:cm:segment:218046787#-0.75701..1"}]}},
              "currentFlow":{"speed":13.0,"freeFlow":14.0,"jamFactor":2.0,"confidence":0.95}}]}
            """
        );

        var result = await _provider.GetFreeFlowingRoadsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        var road = result.Value.ShouldHaveSingleItem();
        road.RoadName.ShouldBe("A3 / Centenary Dr");
        road.NameFromSegment.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCorridorsAsync_FlowLabelNamingALocality_NeverBecomesARoad()
    {
        MockGeocodingEmpty();
        // F5 — job 5a0db2eb aired "Fitzwilliam Road and Wiley Park are slow going". "Wiley Park" is a
        // SUBURB; the row's own segment resolves to Fitzwilliam Rd, which is the road it measures.
        const string segmentRef =
            "hrn:here:data::olp-here:rib-2:9203::365362127:here:cm:segment:218046999#-0.1..0.9";
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad> { [segmentRef] = new("Fitzwilliam Rd", 3, false) }));
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow",
            """
            {"results":[{"location":{"description":"Wiley Park","length":842,
              "shape":{"links":[{"points":[{"lat":-33.92,"lng":151.06}],"length":842,"functionalClass":3}]},
              "segmentRef":{"segments":[{"ref":"hrn:here:data::olp-here:rib-2:9203::365362127:here:cm:segment:218046999#-0.1..0.9","length":842}]}},
              "currentFlow":{"speed":1.2,"freeFlow":13.0,"jamFactor":8.4,"confidence":0.95}}]}
            """);

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        result.Value.ShouldHaveSingleItem().RoadName.ShouldBe("Fitzwilliam Rd");
        result.Value.ShouldNotContain(c => c.RoadName.Contains("Wiley Park", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetCorridorsAsync_FlowRowWithUnresolvableSegments_IsNotVoicedUnderItsLabel()
    {
        MockGeocodingEmpty();
        // The row spans two named roads, so it has no single identity. It is a real measurement of
        // somewhere we cannot name — and its label is not that name.
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad>
                {
                    ["split-a"] = new("Railway Pde", 4, false),
                    ["split-b"] = new("Atchison Rd", 4, false)
                }));
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow",
            """
            {"results":[{"location":{"description":"Australian Post Delivery Centre","length":944,
              "segmentRef":{"segments":[{"ref":"split-a","length":504},{"ref":"split-b","length":440}]}},
              "currentFlow":{"speed":1.0,"freeFlow":14.0,"jamFactor":9.1,"confidence":0.95}}]}
            """);

        var result = await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_MapsEveryBoundedContradictionBeforeCallingRoadClear()
    {
        MockGeocodingEmpty();
        var requested = new List<string>();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                requested.AddRange(refs);
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad(reference switch
                        {
                            "free-a" or "moderate-a" => "Target A Road",
                            "free-b" or "historical-b" => "Target B Road",
                            _ => $"Blocked {reference} Road"
                        }, 1, false),
                        StringComparer.Ordinal));
            });
        var fixtures = new List<FlowFixture>
        {
            new("free-a", 1.0, Length: 5_000),
            new("free-b", 1.0, Length: 4_000),
            new("moderate-a", 4.5, Length: 500),
            new("historical-b", 1.0, Confidence: 0.2, Length: 500)
        };
        fixtures.AddRange(Enumerable.Range(0, 25)
            .Select(index => new FlowFixture($"blocked-{index}", 8.0, Length: 2_000 + index)));
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(fixtures));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(road => road.RoadName).ShouldNotContain("Target A Road");
        result.Value.Select(road => road.RoadName).ShouldNotContain("Target B Road");
        requested.ShouldContain("moderate-a");
        requested.ShouldContain("historical-b");
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_SafetyMapOmissionOnUnrelatedSegmentPreservesVerifiedRoad()
    {
        MockGeocodingEmpty();
        int mapCall = 0;
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                mapCall++;
                return mapCall == 1
                    ? Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                        refs.ToDictionary(reference => reference, _ => new SegmentRoad("Target Road", 1, false)))
                    : Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                        new Dictionary<string, SegmentRoad>());
            });
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(
            new FlowFixture("free", 1.0, Length: 5_000),
            new FlowFixture("slow", 8.0, Length: 1_000)));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().RoadName.ShouldBe("Target Road");
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_UnresolvedContradictionOnSameSegmentWithholdsRoad()
    {
        MockGeocodingEmpty();
        int mapCall = 0;
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                mapCall++;
                return mapCall == 1
                    ? Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                        refs.ToDictionary(reference => reference, _ => new SegmentRoad("Target Road", 1, false)))
                    : Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                        new Dictionary<string, SegmentRoad>());
            });
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(
            new FlowFixture("here:cm:segment:123456789+0.0..0.5", 1.0, Length: 5_000),
            new FlowFixture("here:cm:segment:123456789+0.5..1.0", 8.0, Length: 1_000)));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        mapCall.ShouldBe(2);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_TruncatedContradictionBandFailsClosed()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad($"Mapped {reference} Road", 1, false),
                        StringComparer.Ordinal));
            });
        var fixtures = new List<FlowFixture>
        {
            new("free", 1.0, Length: 10_000)
        };
        fixtures.AddRange(Enumerable.Range(0, 321)
            .Select(index => new FlowFixture($"slow-{index}", 8.0, Length: 1_000 + index)));
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(fixtures));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_ContradictionReferenceBudgetExhaustionFailsClosed()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad(
                            reference == "free" ? "Target Road" : "Unrelated Slow Road",
                            1,
                            false),
                        StringComparer.Ordinal));
            });
        var fixtures = new List<FlowFixture>
        {
            new("free", 1.0, Length: 10_000)
        };
        fixtures.AddRange(Enumerable.Range(0, 320)
            .Select(index => new FlowFixture(
                $"slow-{index}",
                8.0,
                Length: 1_000 + index,
                SegmentsPerRow: 10)));
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(fixtures));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCorridorsAsync_ContinuesAfterSuccessfulEmptyMapWave()
    {
        MockGeocodingEmpty();
        int mapCall = 0;
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                mapCall++;
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    mapCall == 1
                        ? new Dictionary<string, SegmentRoad>()
                        : refs.ToDictionary(
                            reference => reference,
                            reference => new SegmentRoad($"Mapped Road {reference.Split('-')[1]}", 1, false),
                            StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(25, jamFactor: 8.0));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        mapCall.ShouldBe(2);
        result.Value.Count().ShouldBe(5);
    }

    [Fact]
    public async Task GetCorridorsAsync_NonAdmissibleHotspotsDoNotHideLaterWholeRoadCongestion()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad(
                            reference == "whole-road" ? "Whole Road" : $"Lights {reference} Road",
                            1,
                            false),
                        StringComparer.Ordinal));
            });
        var fixtures = Enumerable.Range(0, 20)
            .Select(index => new FlowFixture(
                $"lights-{index}",
                1.0,
                Length: 1_000,
                SubSegmentJamFactor: 9.0,
                SubSegmentLength: 100))
            .Append(new FlowFixture("whole-road", 6.5, Length: 5_000))
            .ToList();
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(fixtures));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 1);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().RoadName.ShouldBe("Whole Road");
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_InspectsAtMostTop320Rows()
    {
        MockGeocodingEmpty();
        var mapCalls = new List<IReadOnlyList<string>>();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                mapCalls.Add(refs);
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        _ => new SegmentRoad("One Duplicate Road", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(400, jamFactor: 1.0));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 200);

        result.IsSuccess.ShouldBeTrue();
        mapCalls.SelectMany(call => call).Count().ShouldBe(320);
        mapCalls.SelectMany(call => call)
            .Select(reference => int.Parse(reference.Split('-')[1]))
            .ShouldAllBe(index => index >= 80);
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_MapResolvesOnlyBoundedCandidateShortlist()
    {
        MockGeocodingEmpty();
        var mapCalls = new List<IReadOnlyList<string>>();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var requestedRefs = call.Arg<IEnumerable<string>>().ToList();
                mapCalls.Add(requestedRefs);
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    requestedRefs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad($"Mapped {reference.Split('-')[1]} Street", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(200, jamFactor: 1.0, segmentsPerRow: 4));

        var result = await _provider.GetFreeFlowingRoadsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 200);

        result.IsSuccess.ShouldBeTrue();
        mapCalls.Count.ShouldBe(1);
        mapCalls[0].Count.ShouldBe(128);
        mapCalls[0]
            .Select(reference => int.Parse(reference.Split('-')[1]))
            .ShouldAllBe(index => index >= 168);
        result.Value.Count.ShouldBe(24);
        result.Value.ShouldAllBe(road => road.NameFromSegment);
    }

    [Fact]
    public async Task GetFreeFlowingRoadsAsync_RefillsPastExcludedTopRoadsFromCachedSnapshot()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad($"Mapped {reference.Split('-')[1]} Street", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(60, jamFactor: 1.0));
        var area = CoverageArea.Create(-37.8136, 144.9631, 50.0).Value;

        var first = await _provider.GetFreeFlowingRoadsAsync(area, 40);
        var excluded = first.Value.Select(road => road.RoadName).ToList();
        var refill = await _provider.GetFreeFlowingRoadsAsync(
            area, 40, excludedRoadNames: excluded);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Count.ShouldBe(24);
        refill.IsSuccess.ShouldBeTrue();
        refill.Value.Count.ShouldBe(24);
        refill.Value.Select(road => road.RoadName).ShouldAllBe(name => !excluded.Contains(name));
        _mockHandler.RequestedUrls.Count(url => url.Contains("/flow", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task GetCorridorsAsync_MapResolvesOnlyCongestionCandidates()
    {
        MockGeocodingEmpty();
        IReadOnlyList<string> requestedRefs = [];
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestedRefs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    requestedRefs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad($"Mapped {reference} Street", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(200, jamFactor: 8.0));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        requestedRefs.Count.ShouldBe(20);
        requestedRefs
            .Select(reference => int.Parse(reference.Split('-')[1]))
            .ShouldAllBe(index => index >= 180);
        result.Value.Count().ShouldBe(5);
    }

    [Fact]
    public async Task GetCorridorsAsync_FullyMapsRowBeyondNormalReferenceCeiling()
    {
        MockGeocodingEmpty();
        IReadOnlyList<string> requestedRefs = [];
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestedRefs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    requestedRefs.ToDictionary(
                        reference => reference,
                        _ => new SegmentRoad("Long Motorway", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(
            count: 1, jamFactor: 8.0, segmentsPerRow: 200));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        requestedRefs.Count.ShouldBe(200);
        result.Value.ShouldHaveSingleItem().RoadName.ShouldBe("Long Motorway");
    }

    [Fact]
    public async Task GetCorridorsAsync_MisleadingSharedFlowLabelDoesNotCollapseDistinctRoads()
    {
        MockGeocodingEmpty();
        var mapCalls = new List<IReadOnlyList<string>>();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                mapCalls.Add(refs);
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference =>
                        {
                            int index = int.Parse(reference.Split('-')[1]);
                            return new SegmentRoad(
                                index >= 5
                                    ? $"Pacific Motorway (Exit {index})"
                                    : $"Mapped {reference} Street",
                                1,
                                false);
                        },
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor(
            "/flow",
            BuildFlowJson(
                25,
                jamFactor: 8.0,
                description: "Australian Post Delivery Centre"));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        mapCalls.Count.ShouldBe(2);
        mapCalls[0].Count.ShouldBe(20);
        mapCalls[1].Count.ShouldBe(5);
        result.Value.Count().ShouldBe(5);
    }

    [Fact]
    public async Task GetCorridorsAsync_ExactIncidentMatchUsesFlowRowOutsideIdentityShortlist()
    {
        MockGeocodingEmpty();
        var mapCalls = new List<IReadOnlyList<string>>();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                mapCalls.Add(refs);
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        reference => new SegmentRoad(
                            reference == "segment-0-0" ? "Pacific Motorway" : $"Mapped {reference} Street",
                            1,
                            false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/incidents",
            """
            {"results":[{"location":{
              "shape":{"links":[{"points":[{"lat":-37.8,"lng":144.9}],"length":1000,"functionalClass":1}]},
              "segmentRef":{"segments":[{"ref":"segment-0-0","length":1000}]}},
              "incidentDetails":{"id":"target","type":"accident","criticality":"major",
                "description":{"value":"Crash"}}}]}
            """);
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(2, jamFactor: 8.0, segmentsPerRow: 129));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(corridor => corridor.RoadName).ShouldContain("Pacific Motorway");
        var target = result.Value.Single(corridor => corridor.RoadName == "Pacific Motorway");
        target.FlowDelayMinutes.HasValue.ShouldBeTrue();
        mapCalls.ShouldAllBe(call => call.Count <= 320);
        mapCalls.SelectMany(call => call).ShouldContain("segment-0-0");
    }


    [Fact]
    public async Task FlowAndIncidentRequests_PinTheLanguageAndAskForSegmentReferences()
    {
        MockGeocodingEmpty();
        // `lang` unset gives en-US and `en-AU` is silently downgraded to it (measured); `en-GB` returns
        // the Australian register. The point is that the language is PINNED, and that every row comes
        // back with the segment references its identity is resolved from.
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", """{"results":[]}""");

        await _provider.GetCorridorsAsync(CoverageArea.Create(-33.8688, 151.2093, 10.0).Value, 5);

        _mockHandler.RequestedUrls.ShouldContain(u =>
            u.Contains("/incidents", StringComparison.Ordinal)
            && u.Contains("locationReferencing=shape,segmentRef", StringComparison.Ordinal)
            && u.Contains("lang=en-GB", StringComparison.Ordinal));
        _mockHandler.RequestedUrls.ShouldContain(u =>
            u.Contains("/flow", StringComparison.Ordinal)
            && u.Contains("locationReferencing=shape,segmentRef", StringComparison.Ordinal)
            && u.Contains("lang=en-GB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorridorAndGoodRunReads_ShareOneBroadFlowSnapshot()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", """{"results":[]}""");
        var area = CoverageArea.Create(-33.8688, 151.2093, 10.0).Value;

        await _provider.GetCorridorsAsync(area, 5);
        await _provider.GetFreeFlowingRoadsAsync(area, 20);

        var flowRequests = _mockHandler.RequestedUrls
            .Where(url => url.Contains("/flow", StringComparison.Ordinal))
            .ToList();
        flowRequests.Count.ShouldBe(1);
        flowRequests[0].ShouldContain("minJamFactor=0");
    }

    [Fact]
    public async Task FlowSnapshotCache_DoesNotCollideForDifferentCoverageShapes()
    {
        MockGeocodingEmpty();
        _mockHandler.SetResponseFor("/flow", """{"results":[]}""");
        var box = CoverageArea.CreateBoundingBox(144.8, -37.9, 145.0, -37.7).Value;
        var circle = CoverageArea.Create(box.Latitude, box.Longitude, box.RadiusKm).Value;

        await _provider.GetFreeFlowingRoadsAsync(circle, 5);
        await _provider.GetFreeFlowingRoadsAsync(box, 5);

        var flowRequests = _mockHandler.RequestedUrls
            .Where(url => url.Contains("/flow", StringComparison.Ordinal))
            .ToList();
        flowRequests.Count.ShouldBe(2);
        flowRequests.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetCorridorsAsync_SuspiciousMapRoadNameIsNotVoiced()
    {
        MockGeocodingEmpty();
        _mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var refs = call.Arg<IEnumerable<string>>().ToList();
                return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                    refs.ToDictionary(
                        reference => reference,
                        _ => new SegmentRoad("Adjungbillt-Threeway Road", 1, false),
                        StringComparer.Ordinal));
            });
        _mockHandler.SetResponseFor("/incidents", """{"results":[]}""");
        _mockHandler.SetResponseFor("/flow", BuildFlowJson(1, jamFactor: 8.0));

        var result = await _provider.GetCorridorsAsync(
            CoverageArea.Create(-37.8136, 144.9631, 50.0).Value, 5);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    private static string BuildF3ClosurePayload(char direction)
    {
        const string payload =
            """
            {"results":[{"location":{
                "shape":{"links":[{"points":[{"lat":-33.82718,"lng":151.21387},{"lat":-33.8244,"lng":151.21041}],"length":457,"functionalClass":1}]},
                "segmentRef":{"segments":[{"ref":"hrn:here:data::olp-here:rib-2:9203::365362849:here:cm:segment:914839809#DIRECTION0.02183..0.97805","length":457}]}},
              "incidentDetails":{"id":"3306792291936031092","originalId":"3306792291936031092","type":"roadClosure","criticality":"critical","roadClosed":true,
                "description":{"value":"Closed"}}}]}
            """;
        return payload.Replace("DIRECTION", direction.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HereRequestBudget_WorkerWideMapPermitsNeverExceedTwoConcurrentRequests()
    {
        int active = 0;
        int maximum = 0;
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var permit = await HereRequestBudget.AcquireAsync(
                HereRequestKind.Map, CancellationToken.None);
            int current = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = maximum;
                if (observed >= current)
                    break;
            } while (Interlocked.CompareExchange(ref maximum, current, observed) != observed);

            await Task.Delay(10);
            Interlocked.Decrement(ref active);
        });

        await Task.WhenAll(tasks);
        maximum.ShouldBe(HereRequestBudget.MapPermitCount);
    }

    private static string BuildFlowJson(
        int count,
        double jamFactor,
        int segmentsPerRow = 1,
        string? description = null)
    {
        var rows = Enumerable.Range(0, count).Select(index => new
        {
            location = new
            {
                description = description ?? $"Candidate Road {index}",
                length = 1_000 + index,
                shape = new
                {
                    links = new[]
                    {
                        new
                        {
                            points = new[] { new { lat = -37.8, lng = 144.9 } },
                            length = 1_000 + index,
                            functionalClass = 1
                        }
                    }
                },
                segmentRef = new
                {
                    segments = Enumerable.Range(0, segmentsPerRow)
                        .Select(segment => new { @ref = $"segment-{index}-{segment}", length = 1_000 + index })
                }
            },
            currentFlow = new
            {
                speed = jamFactor >= 5 ? 1.0 : 13.0,
                freeFlow = 14.0,
                jamFactor,
                confidence = 0.95
            }
        });
        return JsonSerializer.Serialize(new { results = rows });
    }

    private static string BuildFlowJson(params FlowFixture[] fixtures) =>
        BuildFlowJson((IReadOnlyList<FlowFixture>)fixtures);

    private static string BuildFlowJson(IReadOnlyList<FlowFixture> fixtures)
    {
        var rows = fixtures.Select((fixture, index) => new
        {
            location = new
            {
                description = fixture.Description ?? $"Candidate Road {index}",
                length = fixture.Length,
                shape = new
                {
                    links = new[]
                    {
                        new
                        {
                            points = new[] { new { lat = -37.8, lng = 144.9 } },
                            length = fixture.Length,
                            functionalClass = fixture.FunctionalClass
                        }
                    }
                },
                segmentRef = new
                {
                    segments = Enumerable.Range(0, fixture.SegmentsPerRow)
                        .Select(segment => new
                        {
                            @ref = fixture.SegmentsPerRow == 1
                                ? fixture.Ref
                                : $"{fixture.Ref}-{segment}",
                            length = fixture.Length / fixture.SegmentsPerRow
                        })
                }
            },
            currentFlow = new
            {
                speed = fixture.JamFactor >= 5 ? 1.0 : 13.0,
                freeFlow = 14.0,
                jamFactor = fixture.JamFactor,
                confidence = fixture.Confidence,
                subSegments = fixture.SubSegmentJamFactor.HasValue
                    ? new[]
                    {
                        new
                        {
                            jamFactor = fixture.SubSegmentJamFactor.Value,
                            length = fixture.SubSegmentLength
                        }
                    }
                    : null
            }
        });
        return JsonSerializer.Serialize(new { results = rows });
    }

    private sealed record FlowFixture(
        string Ref,
        double JamFactor,
        double Confidence = 0.95,
        double Length = 1_000,
        int FunctionalClass = 1,
        double? SubSegmentJamFactor = null,
        double SubSegmentLength = 0,
        string? Description = null,
        int SegmentsPerRow = 1);

    private static string BuildTrafficJson(
        params (string id, string type, string description, int fc, double length, double lat, double lng)[] incidents)
    {
        var items = incidents.Select(i => new
        {
            location = new
            {
                shape = new
                {
                    links = new[]
                    {
                        new
                        {
                            points = new[] { new { i.lat, i.lng } },
                            i.length,
                            functionalClass = i.fc
                        }
                    }
                }
            },
            incidentDetails = new
            {
                i.id,
                i.type,
                criticality = "major",
                description = new { value = i.description }
            }
        });

        return JsonSerializer.Serialize(new { results = items });
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private string _content = "";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
        public string? LastUrl { get; private set; }

        /// <summary>Every URL requested, so a test can assert the shape of BOTH endpoints' requests
        ///  from one <c>GetCorridorsAsync</c> call.</summary>
        public List<string> RequestedUrls { get; } = [];

        public void SetResponse(string content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        /// <summary>
        ///     Response for requests whose URL contains <paramref name="pathFragment" />. One
        ///     <c>GetCorridorsAsync</c> calls the incidents AND flow endpoints, which return different
        ///     shapes, so a single body cannot exercise both.
        /// </summary>
        public void SetResponseFor(string pathFragment, string content) => _byPath[pathFragment] = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? "";
            LastUrl = url;
            RequestedUrls.Add(url);
            string? match = _byPath
                .Where(kv => url.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .FirstOrDefault();
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(match ?? _content)
            });
        }
    }
}
