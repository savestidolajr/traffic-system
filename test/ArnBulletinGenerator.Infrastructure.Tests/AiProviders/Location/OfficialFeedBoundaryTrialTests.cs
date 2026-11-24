using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;


namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

/// <summary>
///     Permanent regression tests for the official-feed boundary contract. A polygon station must
///     not accept an event that is outside the polygon merely because it is inside the enclosing
///     radius.
/// </summary>
[Trait("Category", "BoundaryTrial")]
public sealed class OfficialFeedBoundaryTrialTests(ITestOutputHelper output)
{
    private static readonly CoverageArea Sydney = CoverageArea.CreatePolygon(
    [
        new CoveragePoint(-33.5924, 151.3233),
        new CoveragePoint(-33.72, 151.2985),
        new CoveragePoint(-33.7947, 151.2872),
        new CoveragePoint(-33.8887, 151.2779),
        new CoveragePoint(-33.9415, 151.2383),
        new CoveragePoint(-34.0552, 151.1533),
        new CoveragePoint(-34.0637, 150.8169),
        new CoveragePoint(-33.7531, 150.6884),
        new CoveragePoint(-33.6095, 150.8185)
    ]).Value;

    private const double BoundaryLeakLatitude = -33.616701;
    private const double BoundaryLeakLongitude = 151.328165;

    [Fact]
    public async Task LiveNswIncident_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        var feed = new LiveTrafficNswFeed(
            new HttpClient(new RoutedHandler(_ => NswPointPayload(
                BoundaryLeakLatitude, BoundaryLeakLongitude))),
            Options.Create(new OfficialFeedsOptions()),
            Substitute.For<ILogger<LiveTrafficNswFeed>>());

        var result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task LiveNswRoadwork_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        var feed = new LiveTrafficNswFeed(
            new HttpClient(new RoutedHandler(_ => Asset("nsw-roadwork-2026-08-19.json"))),
            Options.Create(new OfficialFeedsOptions()),
            Substitute.For<ILogger<LiveTrafficNswFeed>>());

        var result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldNotContain(e => e.Id == "276112");
    }

    [Fact]
    public void QldGeoJson_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        using var document = JsonDocument.Parse(QldBoundaryPayload());
        var events = OfficialFeedGeoJson.ParseQld(document, "qldtraffic", Sydney);

        events.ShouldBeEmpty();
    }

    [Fact]
    public void VicGeoJson_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        using var document = JsonDocument.Parse(VicBoundaryPayload());
        var events = OfficialFeedGeoJson.ParseVic(document, "victraffic", Sydney);

        events.ShouldBeEmpty();
    }

    [Fact]
    public void ArcGisPoint_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        using var document = JsonDocument.Parse(ArcGisBoundaryPayload());
        var events = OfficialFeedGeoJson.ParseArcGis(
            document,
            Sydney,
            (attributes, shape) => new OfficialTrafficEvent(
                SourceId: attributes.GetProperty("SOURCE").GetString() ?? "official",
                Headline: "boundary trial",
                IsIncidentClass: true,
                Shape: shape,
                Id: attributes.GetProperty("OBJECTID").GetString()));

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task MainRoadsWa_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        var feed = new MainRoadsWaFeed(
            new HttpClient(new RoutedHandler(_ => WaBoundaryPayload())),
            Options.Create(new OfficialFeedsOptions { WaTravelMapUrl = "https://wa.test/TravelInformation/MapServer" }),
            Substitute.For<ILogger<MainRoadsWaFeed>>());

        var result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task TrafficSa_OutsideSydneyPolygonInsideEnclosingCircle_IsRejected()
    {

        var feed = new TrafficSaFeed(
            new HttpClient(new RoutedHandler(_ => SaBoundaryPayload())),
            Options.Create(new OfficialFeedsOptions { SaTrafficUrl = "https://sa.test/query" }),
            Substitute.For<ILogger<TrafficSaFeed>>());

        var result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public void RadiusOutsideControl_IsRejectedByCurrentParser()
    {

        using var document = JsonDocument.Parse(NswPointPayload(-34.50, 151.50));
        var events = OfficialFeedGeoJson.ParseNsw(document, "livetraffic-nsw", true, Sydney);

        events.ShouldBeEmpty();
    }

    [Fact]
    public void PolygonBoundaryPoint_IsRetainedByCurrentParser()
    {

        var boundary = Sydney.PolygonPoints![0];
        using var document = JsonDocument.Parse(NswPointPayload(boundary.Lat, boundary.Lng));
        var events = OfficialFeedGeoJson.ParseNsw(document, "livetraffic-nsw", true, Sydney);

        events.ShouldContain(e => e.Shape.Any(p => p.Lat == boundary.Lat && p.Lng == boundary.Lng));
    }

    [Fact]
    public void ImpactReport_ReportsCurrentCircleOnlyOfficialFeedExposure()
    {

        var nswIncident = ParseNsw("nsw-incident-2026-08-19.json", incidentClass: true);
        var nswRoadwork = ParseNsw("nsw-roadwork-2026-08-19.json", incidentClass: false);
        var qld = ParseQld("qld-events-2026-08-19.json");
        var waIncident = ParseArcGis("wa-incident-2026-08-19.json", "mainroads-wa");
        var waRoadwork = ParseArcGis("wa-roadwork-2026-08-19.json", "mainroads-wa");
        var sa = ParseArcGis("sa-traffic-2026-08-19.json", "trafficsa");

        Report("livetraffic-nsw incident", nswIncident);
        Report("livetraffic-nsw roadwork", nswRoadwork);
        Report("qldtraffic mixed", qld);
        Report("mainroads-wa incident", waIncident);
        Report("mainroads-wa roadwork", waRoadwork);
        Report("trafficsa", sa);

        nswRoadwork.ShouldNotContain(e => IsCircleOnly(e));
    }


    private bool IsCircleOnly(OfficialTrafficEvent eventRecord) =>
        eventRecord.Shape.Count > 0 && eventRecord.Shape.All(p => !Sydney.ContainsPoint(p.Lat, p.Lng));

    private void Report(string source, IReadOnlyList<OfficialTrafficEvent> events)
    {
        int circleOnly = events.Count(IsCircleOnly);
        int exactPolygon = events.Count(e => e.Shape.Any(p => Sydney.ContainsPoint(p.Lat, p.Lng)));
        output.WriteLine(
            $"{source}: returned={events.Count}, exactPolygonVertexInside={exactPolygon}, circleOnly={circleOnly}");
    }

    private IReadOnlyList<OfficialTrafficEvent> ParseNsw(string file, bool incidentClass)
    {
        using var document = JsonDocument.Parse(Asset(file));
        return OfficialFeedGeoJson.ParseNsw(document, "livetraffic-nsw", incidentClass, Sydney);
    }

    private IReadOnlyList<OfficialTrafficEvent> ParseQld(string file)
    {
        using var document = JsonDocument.Parse(Asset(file));
        return OfficialFeedGeoJson.ParseQld(document, "qldtraffic", Sydney);
    }

    private IReadOnlyList<OfficialTrafficEvent> ParseArcGis(string file, string source)
    {
        using var document = JsonDocument.Parse(Asset(file));
        return OfficialFeedGeoJson.ParseArcGis(
            document,
            Sydney,
            (_, shape) => new OfficialTrafficEvent(
                source,
                "boundary trial",
                true,
                shape,
                Id: "fixture"));
    }

    private static string Asset(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "OfficialFeeds", name));

    private static string NswPointPayload(double latitude, double longitude) => $$"""
        {
          "features": [
            {
              "id": "boundary-trial",
              "geometry": { "type": "Point", "coordinates": [{{longitude}}, {{latitude}}] },
              "properties": {
                "ended": false,
                "displayName": "HAZARD Boundary trial",
                "roads": [{ "mainStreet": "Boundary Road", "suburb": "Sydney" }]
              }
            }
          ]
        }
        """;

    private static string QldBoundaryPayload() => $$"""
        {
          "features": [
            {
              "id": "boundary-trial",
              "geometry": {
                "type": "MultiLineString",
                "coordinates": [[[{{BoundaryLeakLongitude}}, {{BoundaryLeakLatitude}}], [{{BoundaryLeakLongitude}}, {{BoundaryLeakLatitude}}]]]
              },
              "properties": {
                "event_type": "Hazard",
                "description": "Boundary trial",
                "road_summary": { "district": "Metropolitan", "road_name": "Boundary Road", "locality": "Sydney" }
              }
            }
          ]
        }
        """;

    private static string VicBoundaryPayload() => $$"""
        {
          "features": [
            {
              "id": "boundary-trial",
              "geometry": { "type": "Point", "coordinates": [{{BoundaryLeakLongitude}}, {{BoundaryLeakLatitude}}] },
              "properties": {
                "title": "Boundary trial",
                "road_name": "Boundary Road",
                "suburb": "Sydney",
                "description": "Boundary trial",
                "type": "hazard"
              }
            }
          ]
        }
        """;

    private static string WaBoundaryPayload() => $$"""
        {
          "features": [
            {
              "attributes": {
                "OBJECTID": "boundary-trial",
                "Id": "boundary-trial",
                "publishExternally": "Yes",
                "IncidentType": "Road Crash",
                "TrafficImpact": "Boundary trial",
                "Road": "Boundary Road",
                "Suburb": "Sydney",
                "IncidentLevel": "Level 2",
                "ClosureType": "Lane closure",
                "WorkType": "Road resurfacing",
                "Description": "Boundary trial",
                "WorkStatus": "In progress",
                "TrafficCondition": "Left Lane Blocked",
                "DateStarted": "2026-08-24T00:00:00Z",
                "EstimatedCompletionDate": "2026-08-26T00:00:00Z",
                "EntryDate": "25/08/2026 00:00:00",
                "UpdateDateTime": "25/08/2026 00:00:00"
              },
              "geometry": { "x": {{BoundaryLeakLongitude}}, "y": {{BoundaryLeakLatitude}} }
            }
          ]
        }
        """;

    private static string SaBoundaryPayload() => $$"""
        {
          "features": [
            {
              "attributes": {
                "roadworks_and_incidents_id": "boundary-trial",
                "incident_id": "boundary-trial",
                "rec_type": "INCIDENT",
                "description": "Boundary trial",
                "local_road": "Boundary Road",
                "suburb": "Sydney",
                "active": "Y",
                "timeframe_type": "CURRENT",
                "latitude": {{BoundaryLeakLatitude}},
                "longitude": {{BoundaryLeakLongitude}},
                "start_date": 1787616000000,
                "end_date": 1787702400000
              },
              "geometry": { "x": {{BoundaryLeakLongitude}}, "y": {{BoundaryLeakLatitude}} }
            }
          ]
        }
        """;

    private static string ArcGisBoundaryPayload() => $$"""
        {
          "features": [
            {
              "attributes": {
                "OBJECTID": "boundary-trial",
                "SOURCE": "official",
                "EVENT_TYPE": "INCIDENT",
                "DESCRIPTION": "Boundary trial",
                "ROAD_NAME": "Boundary Road",
                "latitude": {{BoundaryLeakLatitude}},
                "longitude": {{BoundaryLeakLongitude}}
              },
              "geometry": { "x": {{BoundaryLeakLongitude}}, "y": {{BoundaryLeakLatitude}} }
            }
          ]
        }
        """;

    private sealed class RoutedHandler(Func<string, string> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(route(request.RequestUri!.ToString()))
            });
    }
}
