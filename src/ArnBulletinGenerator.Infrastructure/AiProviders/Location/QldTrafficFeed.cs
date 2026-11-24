using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     QLDTraffic GeoJSON v2 (TMR, CC-BY, public API key). One statewide events call; the class split
///     is per-feature: <c>event_type</c> "Roadworks"/"Special event" are standing conditions, all
///     other types (Crash, Hazard, Congestion, Flooding) are incident-class.
/// </summary>
public sealed class QldTrafficFeed(
    HttpClient httpClient,
    IOptions<OfficialFeedsOptions> options,
    ILogger<QldTrafficFeed> logger) : IOfficialTrafficFeed
{
    private readonly OfficialFeedsOptions _options = options.Value;

    public string SourceId => "qldtraffic";
    /// <summary>
    ///     QLD's event type, subtype, authority priority, and road summary are structured.
    ///     The description/information pair remains source prose for lane, direction, and
    ///     secondary-asset facts.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        "qldtraffic",
        "qldtraffic-v2",
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.RoadName,
            ParsedField.Category,
            ParsedField.SubCategory,
            ParsedField.AuthorityPriority,
            ParsedField.StartTime,
            ParsedField.EndTime,
            ParsedField.Direction
        },
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.LanesClosed,
            ParsedField.LanesTotal,
            ParsedField.SpeedLimitKph,
            ParsedField.OtherAssets
        });

    public async Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        using var feedBudget = OfficialFeedCallBudget.Begin(OfficialFeedCallBudget.MaxFeedTimeout);
        string url = $"{_options.QldEventsUrl}?apikey={Uri.EscapeDataString(_options.QldApiKey)}";
        try
        {
            using var requestCts = OfficialFeedCallBudget.Current!
                .CreateRequestCancellation(cancellationToken);
            using var response = await httpClient.GetAsync(url, requestCts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(requestCts.Token));
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Success(
                OfficialFeedGeoJson.ParseQld(doc, SourceId, area));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[OfficialFeed] QLD events fetch failed");
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Failure($"QLD feed unavailable: {ex.Message}");
        }
    }
}
