using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Live Traffic NSW open-data hazards (Transport for NSW, CC-BY, keyless). Fetches the statewide
///     incident and roadwork feeds and returns events inside the coverage area. The parse is shared
///     GeoJSON plumbing in <see cref="OfficialFeedGeoJson" />; only the class split is NSW-specific:
///     the incident feed is incident-class, the roadwork feed is not.
///     Measured against 322 open live records on 2026-08-24, <c>impactedLanes</c> supplies lane facts
///     structurally on 3% of records and <c>periods[].direction</c> supplies direction on 93%.
/// </summary>
public sealed class LiveTrafficNswFeed(
    HttpClient httpClient,
    IOptions<OfficialFeedsOptions> options,
    ILogger<LiveTrafficNswFeed> logger) : IOfficialTrafficFeed
{
    private readonly OfficialFeedsOptions _options = options.Value;

    public string SourceId => "livetraffic-nsw";
    /// <summary>
    ///     NSW supplies road/category/timing metadata structurally. On 2026-08-24, measured against
    ///     322 open live records, <c>impactedLanes</c> supplies lane facts structurally on 3% and
    ///     <c>periods[].direction</c> supplies direction on 93%.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        "livetraffic-nsw",
        "live-traffic-open-v1",
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.RoadName,
            ParsedField.Category,
            ParsedField.SubCategory,
            ParsedField.Direction,
            ParsedField.LanesClosed,
            ParsedField.LanesTotal,
            ParsedField.SpeedLimitKph,
            ParsedField.StartTime,
            ParsedField.EndTime,
            ParsedField.AuthorityPriority
        },
        new HashSet<string>(StringComparer.Ordinal)
        {
            ParsedField.OtherAssets
        });

    public async Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        using var feedBudget = OfficialFeedCallBudget.Begin(OfficialFeedCallBudget.MaxFeedTimeout);
        var events = new List<OfficialTrafficEvent>();
        foreach ((string kind, bool incidentClass) in new[] { ("incident", true), ("roadwork", false) })
        {
            string url = $"{_options.NswHazardsBaseUrl}/{kind}-open.json";
            try
            {
                using var requestCts = OfficialFeedCallBudget.Current!
                    .CreateRequestCancellation(cancellationToken);
                using var response = await httpClient.GetAsync(url, requestCts.Token);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(requestCts.Token));
                events.AddRange(OfficialFeedGeoJson.ParseNsw(doc, SourceId, incidentClass, area));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One feed kind failing must not blank the other: a partial official picture still
                // corroborates; only a total failure is reported as one.
                logger.LogWarning(ex, "[OfficialFeed] NSW {Kind} fetch failed", kind);
                if (kind == "incident" && events.Count == 0)
                    return Result<IReadOnlyList<OfficialTrafficEvent>>.Failure($"NSW feed unavailable: {ex.Message}");
            }
        }

        return Result<IReadOnlyList<OfficialTrafficEvent>>.Success(events);
    }
}
