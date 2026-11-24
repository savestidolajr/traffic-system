using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Main Roads Western Australia ArcGIS TravelInformation open data feed. The incident and
///     roadwork layers are independent: one failing layer must not blank the other.
/// </summary>
public sealed class MainRoadsWaFeed(
    HttpClient httpClient,
    IOptions<OfficialFeedsOptions> options,
    ILogger<MainRoadsWaFeed> logger) : IOfficialTrafficFeed
{
    private readonly OfficialFeedsOptions _options = options.Value;

    public string SourceId => "mainroads-wa";
    /// <summary>
    ///     WA publishes incident/work type, traffic condition, authority level, and work dates
    ///     structurally. TrafficImpact/Description are prose; WA publishes no lane counts.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        "mainroads-wa",
        "mainroads-travelinformation-v1",
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.RoadName,
            ParsedField.Category,
            ParsedField.SubCategory,
            ParsedField.AuthorityPriority,
            ParsedField.StartTime,
            ParsedField.EndTime
        },
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.Direction,
            ParsedField.OtherAssets
        });

    public async Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        using var feedBudget = OfficialFeedCallBudget.Begin(OfficialFeedCallBudget.MaxFeedTimeout);
        var events = new List<OfficialTrafficEvent>();
        var successfulLayers = 0;
        foreach ((int layer, bool incidentClass) in new[] { (0, true), (1, false) })
        {
            string queryUrl =
                $"{_options.WaTravelMapUrl.TrimEnd('/')}/{layer}/query?where=1%3D1&outFields=*&returnGeometry=true&f=pjson";
            try
            {
                using var requestCts = OfficialFeedCallBudget.Current!
                    .CreateRequestCancellation(cancellationToken);
                using var response = await httpClient.GetAsync(queryUrl, requestCts.Token);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(requestCts.Token));
                events.AddRange(OfficialFeedGeoJson.ParseArcGis(
                    doc,
                    area,
                    (attributes, shape) => ParseEvent(attributes, shape, incidentClass)));
                successfulLayers++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[OfficialFeed] WA TravelInformation layer {Layer} fetch failed", layer);
            }
        }

        return successfulLayers == 0
            ? Result<IReadOnlyList<OfficialTrafficEvent>>.Failure("WA feed unavailable")
            : Result<IReadOnlyList<OfficialTrafficEvent>>.Success(events);
    }

    private OfficialTrafficEvent? ParseEvent(
        JsonElement attributes,
        IReadOnlyList<CoveragePoint> shape,
        bool incidentClass)
    {
        string? published = OfficialFeedGeoJson.StringProperty(attributes, "publishExternally");
        if (published is not null
            && !published.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase))
            return null;

        string category = OfficialFeedGeoJson.StringProperty(
            attributes,
            incidentClass ? "IncidentType" : "WorkType") ?? "";
        string? detail = OfficialFeedGeoJson.StringProperty(
            attributes,
            incidentClass ? "TrafficImpact" : "Description");
        string? road = OfficialFeedGeoJson.StringProperty(attributes, "Road");
        string? suburb = OfficialFeedGeoJson.StringProperty(attributes, "Suburb");
        string? headline = OfficialFeedGeoJson.StringProperty(attributes, "Location");
        if (string.IsNullOrWhiteSpace(headline))
            headline = string.IsNullOrWhiteSpace(road) ? category : $"{category}: {road}";

        return new OfficialTrafficEvent(
            SourceId,
            headline!,
            incidentClass,
            shape,
            Id: OfficialFeedGeoJson.StringProperty(attributes, "Id")
                ?? OfficialFeedGeoJson.StringProperty(attributes, "OBJECTID"),
            RoadName: road,
            Suburb: suburb,
            Description: incidentClass
                ? OfficialFeedGeoJson.StringProperty(attributes, "TrafficImpact")
                : detail,
            AuthorityPriority: incidentClass
                ? OfficialFeedGeoJson.StringProperty(attributes, "IncidentLevel")
                : null,
            Category: category,
            SubCategory: OfficialFeedGeoJson.StringProperty(
                attributes,
                incidentClass ? "ClosureType" : "WorkStatus"),
            IsPlanned: incidentClass ? null : true,
            ImpactType: OfficialFeedGeoJson.StringProperty(
                attributes,
                incidentClass ? "TrafficCondition" : "TrafficImpact"),
            StartTime: incidentClass
                ? null
                : OfficialFeedGeoJson.ArcGisDateProperty(attributes, "DateStarted"),
            EndTime: incidentClass
                ? null
                : OfficialFeedGeoJson.ArcGisDateProperty(attributes, "EstimatedCompletionDate"),
            PublishedTime: OfficialFeedGeoJson.ArcGisDateProperty(attributes, "EntryDate"),
            LastUpdated: OfficialFeedGeoJson.ArcGisDateProperty(attributes, "UpdateDateTime"),
            Detail: detail);
    }
}
