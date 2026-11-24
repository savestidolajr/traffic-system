using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     South Australia TrafficSATime layer-1 incidents open feed. The ArcGIS FeatureSet is filtered
///     locally to current, active records because the public endpoint also exposes historical rows.
/// </summary>
public sealed class TrafficSaFeed(
    HttpClient httpClient,
    IOptions<OfficialFeedsOptions> options,
    ILogger<TrafficSaFeed> logger) : IOfficialTrafficFeed
{
    private readonly OfficialFeedsOptions _options = options.Value;

    public string SourceId => "trafficsa";
    /// <summary>
    ///     SA supplies road, direction, dates, positive speed, and direction-specific lane metadata
    ///     structurally. The top-level <c>no_lanes_closed</c> is usually empty, but nested
    ///     <c>traffic_dir[].lanes_closed</c> is non-zero in live observations; the parser uses the
    ///     maximum positive directional value rather than summing opposing directions.
    ///     The description remains prose for secondary-asset enrichment.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        "trafficsa",
        "trafficsatime-v1",
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.RoadName,
            ParsedField.Category,
            ParsedField.Direction,
            ParsedField.LanesClosed,
            ParsedField.SpeedLimitKph,
            ParsedField.StartTime,
            ParsedField.EndTime
        },
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.OtherAssets
        });

    public async Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        using var feedBudget = OfficialFeedCallBudget.Begin(OfficialFeedCallBudget.MaxFeedTimeout);
        try
        {
            using var requestCts = OfficialFeedCallBudget.Current!
                .CreateRequestCancellation(cancellationToken);
            using var response = await httpClient.GetAsync(_options.SaTrafficUrl, requestCts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(requestCts.Token));
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Success(
                OfficialFeedGeoJson.ParseArcGis(doc, area, ParseEvent));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[OfficialFeed] SA TrafficSATime fetch failed");
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Failure($"SA feed unavailable: {ex.Message}");
        }
    }

    private OfficialTrafficEvent? ParseEvent(
        JsonElement attributes,
        IReadOnlyList<CoveragePoint> shape)
    {
        if (!string.Equals(
                OfficialFeedGeoJson.StringProperty(attributes, "timeframe_type")?.Trim(),
                "CURRENT",
                StringComparison.OrdinalIgnoreCase)
            || !IsActive(OfficialFeedGeoJson.StringProperty(attributes, "active")))
            return null;

        string category = OfficialFeedGeoJson.StringProperty(attributes, "rec_type") ?? "";
        string? detail = OfficialFeedGeoJson.StringProperty(attributes, "description");
        string? road = OfficialFeedGeoJson.StringProperty(attributes, "local_road");
        string? headline = string.IsNullOrWhiteSpace(detail) ? category : detail;
        if (string.IsNullOrWhiteSpace(headline))
            headline = road ?? "South Australia traffic event";
        var trafficFacts = ReadTrafficFacts(
            OfficialFeedGeoJson.StringProperty(attributes, "traffic_dir"));
        int? topLevelLanesClosed = OfficialFeedGeoJson.PositiveIntProperty(attributes, "no_lanes_closed");
        // Direction-specific closures are reduced to the maximum positive value, never summed:
        // two opposing one-lane closures are not a two-lane closure.
        int? lanesClosed = MaxPositive(topLevelLanesClosed, trafficFacts.LanesClosed);

        return new OfficialTrafficEvent(
            SourceId,
            headline!,
            !category.Contains("WORK", StringComparison.OrdinalIgnoreCase),
            shape,
            Id: OfficialFeedGeoJson.StringProperty(attributes, "incident_id")
                ?? OfficialFeedGeoJson.StringProperty(attributes, "job_id")
                ?? OfficialFeedGeoJson.StringProperty(attributes, "roadworks_and_incidents_id"),
            RoadName: road,
            CrossStreet: OfficialFeedGeoJson.StringProperty(attributes, "side_street"),
            Suburb: OfficialFeedGeoJson.StringProperty(attributes, "suburb"),
            Direction: trafficFacts.Direction,
            StartTime: OfficialFeedGeoJson.ArcGisDateProperty(attributes, "start_date"),
            EndTime: OfficialFeedGeoJson.ArcGisDateProperty(attributes, "end_date"),
            Category: category,
            QualitativeDelay: OfficialFeedGeoJson.StringProperty(attributes, "highest_delay"),
            LanesClosed: lanesClosed,
            SpeedLimitKph: OfficialFeedGeoJson.PositiveIntProperty(attributes, "speed_limit"),
            Detail: detail);
    }

    private static bool IsActive(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("Y", StringComparison.OrdinalIgnoreCase)
        || value.Equals("YES", StringComparison.OrdinalIgnoreCase)
        || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
        || value.Equals("1", StringComparison.OrdinalIgnoreCase)
        || value.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

    private readonly record struct TrafficFacts(string? Direction, int? LanesClosed);

    private static TrafficFacts ReadTrafficFacts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new TrafficFacts(null, null);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new TrafficFacts(raw.Trim(), null);

            var directions = new List<string>();
            int? lanesClosed = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("direction", out var direction)
                    && direction.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(direction.GetString()))
                {
                    directions.Add(direction.GetString()!.Trim());
                }

                lanesClosed = MaxPositive(
                    lanesClosed,
                    OfficialFeedGeoJson.PositiveIntProperty(item, "lanes_closed"));
            }

            string? directionText = directions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() is { Length: > 0 } values
                ? string.Join(" and ", values)
                : null;
            return new TrafficFacts(directionText, lanesClosed);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new TrafficFacts(null, null);
        }
    }

    private static int? MaxPositive(int? first, int? second) =>
        first.HasValue && second.HasValue
            ? Math.Max(first.Value, second.Value)
            : first ?? second;
}
