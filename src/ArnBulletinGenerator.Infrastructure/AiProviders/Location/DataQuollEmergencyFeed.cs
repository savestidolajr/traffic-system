using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     DataQuoll's GeoJSON emergency-services feed. This is a separate evidence rail from official
///     traffic: DataQuoll supplies no road closure, lane, delay or detour facts, so these records are
///     never converted into traffic corridors.
/// </summary>
public sealed class DataQuollEmergencyFeed(
    HttpClient httpClient,
    IOptions<EmergencyFeedOptions> options,
    ILogger<DataQuollEmergencyFeed> logger,
    IMemoryCache cache) : IEmergencyIncidentFeed
{
    private const string AttributionCacheKey = "dataquoll:attribution";
    private readonly EmergencyFeedOptions _options = options.Value;
    public string SourceId => "dataquoll";
    private readonly IMemoryCache _cache = cache;

    /// <summary>
    ///     Categories that are meaningful emergency-services information for a commercial bulletin.
    ///     Medical dispatches, alarms, planned notices and miscellaneous records remain visible but
    ///     are never eligible for airing.
    /// </summary>
    public static IReadOnlySet<string> AirableCategories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fire", "weather", "hazmat", "earth", "rescue"
    };

    /// <summary>
    ///     Returns whether a source licence can be used for a commercial AI-rewritten broadcast.
    ///     <c>-ND</c> forbids derivatives and the rewritten script is a derivative; <c>-NC</c> forbids
    ///     commercial use and this platform serves commercial radio.
    /// </summary>
    public static bool IsCommerciallyReusableLicence(string? licence)
    {
        if (string.IsNullOrWhiteSpace(licence)
            || !licence.Trim().StartsWith("CC BY", StringComparison.OrdinalIgnoreCase))
            return false;

        string normalised = licence.Trim();
        return normalised.IndexOf("-NC", StringComparison.OrdinalIgnoreCase) < 0
            && normalised.IndexOf("-ND", StringComparison.OrdinalIgnoreCase) < 0;
    }

    /// <summary>
    ///     Returns whether an event category may become an on-air candidate. The allow-list keeps
    ///     patient-level medical data, alarms and planned notices operator-visible but non-airable.
    /// </summary>
    public static bool IsAirableCategory(string? category) =>
        !string.IsNullOrWhiteSpace(category) && AirableCategories.Contains(category.Trim());

    public async Task<Result<IReadOnlyList<EmergencyEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Result<IReadOnlyList<EmergencyEvent>>.Failure("DataQuoll emergency feed not configured (disabled)");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Result<IReadOnlyList<EmergencyEvent>>.Failure("DataQuoll emergency feed not configured (no API key)");

        string url = BuildIncidentsUrl(area);
        try
        {
            string payload = await GetIncidentPayloadAsync(url, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(payload);
            List<ParsedEmergencyEvent> parsed = ParseFeatures(document.RootElement, area);

            IReadOnlyDictionary<string, AttributionInfo> attribution = parsed.Count == 0
                ? new Dictionary<string, AttributionInfo>(StringComparer.OrdinalIgnoreCase)
                : await GetAttributionAsync(cancellationToken);

            var collapsed = new Dictionary<string, ParsedEmergencyEvent>(StringComparer.OrdinalIgnoreCase);
            foreach (ParsedEmergencyEvent item in parsed)
            {
                string key = $"{item.SourceFeedId}\u001f{item.StableId}";
                if (!collapsed.TryGetValue(key, out ParsedEmergencyEvent? current)
                    || IsNewer(item, current))
                    collapsed[key] = item;
            }

            var events = new List<EmergencyEvent>(collapsed.Count);
            foreach (ParsedEmergencyEvent item in collapsed.Values)
            {
                AttributionInfo? source = FindAttribution(attribution, item.State, item.Agency);
                string licence = source?.Licence ?? string.Empty;
                bool eligible = source?.Official == true
                    && IsCommerciallyReusableLicence(licence)
                    && IsAirableCategory(item.EventCategory);

                if (!_options.IncludeIneligible && !eligible)
                    continue;

                events.Add(new EmergencyEvent(
                    Id: item.StableId,
                    SourceFeedId: item.SourceFeedId,
                    Agency: item.Agency,
                    State: item.State,
                    Title: item.Title,
                    WarningLevel: item.WarningLevel,
                    EventType: item.EventType,
                    EventCategory: item.EventCategory,
                    Suburb: item.Suburb,
                    Address: item.Address,
                    Latitude: item.Latitude,
                    Longitude: item.Longitude,
                    DistanceKm: item.Latitude is { } lat && item.Longitude is { } lng
                        ? Math.Round(GreatCircleKm(area.Latitude, area.Longitude, lat, lng), 1)
                        : null,
                    Status: item.Status,
                    ReportedUtc: item.ReportedUtc,
                    UpdatedUtc: item.UpdatedUtc,
                    SourceUrl: item.SourceUrl,
                    Detail: item.Detail,
                    Licence: licence,
                    AirEligible: eligible));
            }

            events.Sort(EmergencyEvent.CompareForOperator);
            if (events.Count > Math.Max(0, _options.MaxEvents))
                events.RemoveRange(Math.Max(0, _options.MaxEvents), events.Count - Math.Max(0, _options.MaxEvents));
            return Result<IReadOnlyList<EmergencyEvent>>.Success(events);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[EmergencyFeed] DataQuoll incidents fetch failed");
            return Result<IReadOnlyList<EmergencyEvent>>.Failure($"DataQuoll emergency feed unavailable: {ex.Message}");
        }
    }

    private async Task<string> GetIncidentPayloadAsync(string url, CancellationToken cancellationToken)
    {
        TimeSpan ttl = TimeSpan.FromSeconds(Math.Max(0, _options.CacheTtlSeconds));
        if (ttl > TimeSpan.Zero && _cache.TryGetValue(url, out string? cachedPayload) && cachedPayload is not null)
            return cachedPayload;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DataQuoll incidents returned {(int)response.StatusCode} ({response.ReasonPhrase})");

        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
            throw new JsonException("DataQuoll incidents response was empty");

        if (ttl > TimeSpan.Zero)
            _cache.Set(url, payload, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        return payload;
    }

    private string BuildIncidentsUrl(CoverageArea area)
    {
        (double west, double south, double east, double north) = GetBoundingBox(area);
        string bbox = string.Join(",", new[] { west, south, east, north }.Select(value =>
            value.ToString("G17", CultureInfo.InvariantCulture)));
        return $"{_options.BaseUrl.TrimEnd('/')}/incidents?bbox={Uri.EscapeDataString(bbox)}&limit={Math.Max(0, _options.MaxResults)}&format=geojson";
    }

    private static (double West, double South, double East, double North) GetBoundingBox(CoverageArea area)
    {
        if (area.West is { } west && area.South is { } south && area.East is { } east && area.North is { } north)
            return (ClampLongitude(west), ClampLatitude(south), ClampLongitude(east), ClampLatitude(north));

        double latDelta = area.RadiusKm / 111.32;
        double longitudeScale = Math.Max(0.01, Math.Abs(Math.Cos(area.Latitude * Math.PI / 180.0)));
        double lngDelta = area.RadiusKm / (111.32 * longitudeScale);
        return (
            ClampLongitude(area.Longitude - lngDelta),
            ClampLatitude(area.Latitude - latDelta),
            ClampLongitude(area.Longitude + lngDelta),
            ClampLatitude(area.Latitude + latDelta));
    }

    private List<ParsedEmergencyEvent> ParseFeatures(JsonElement root, CoverageArea area)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !string.Equals(GetString(root, "type"), "FeatureCollection", StringComparison.OrdinalIgnoreCase)
            || !TryGetProperty(root, "features", out JsonElement features)
            || features.ValueKind != JsonValueKind.Array)
            throw new JsonException("DataQuoll response was not a GeoJSON FeatureCollection");

        var parsed = new List<ParsedEmergencyEvent>();
        var unknownWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (JsonElement feature in features.EnumerateArray())
        {
            if (feature.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            JsonElement properties = TryGetProperty(feature, "properties", out JsonElement p)
                && p.ValueKind == JsonValueKind.Object ? p : default;
            JsonElement source = TryGetObject(properties, "source");
            JsonElement location = TryGetObject(properties, "location");
            JsonElement timestamps = TryGetObject(properties, "timestamps");
            JsonElement details = TryGetObject(properties, "details");
            string rawId = GetString(feature, "id") ?? $"feature-{index}";
            string stableId = StableIdentity(rawId);
            string? warningWire = GetString(properties, "warningLevel");
            EmergencyWarningLevel warning = ParseWarningLevel(warningWire, unknownWarnings);
            (double? latitude, double? longitude) = GetFeaturePoint(feature, location);

            if (latitude is { } pointLat && longitude is { } pointLng && !IsInside(area, pointLat, pointLng))
            {
                index++;
                continue;
            }

            string sourceFeedId = GetString(source, "feedId")?.Trim() ?? string.Empty;
            string state = (GetString(source, "state") ?? GetString(location, "state") ?? string.Empty).Trim().ToLowerInvariant();
            string agency = (GetString(source, "agency") ?? string.Empty).Trim();
            parsed.Add(new ParsedEmergencyEvent(
                rawId,
                stableId,
                sourceFeedId,
                agency,
                state,
                GetString(properties, "title")?.Trim() ?? "Emergency incident",
                warning,
                GetString(properties, "eventType")?.Trim() ?? string.Empty,
                GetString(properties, "eventCategory")?.Trim() ?? string.Empty,
                GetString(location, "suburb")?.Trim(),
                GetString(location, "address")?.Trim(),
                latitude,
                longitude,
                GetString(properties, "status")?.Trim(),
                ParseTimestamp(GetString(timestamps, "reported")),
                ParseTimestamp(GetString(timestamps, "updated")),
                GetString(details, "source_url")?.Trim(),
                GetString(details, "description"),
                index));
            index++;
        }

        return parsed;
    }

    private EmergencyWarningLevel ParseWarningLevel(string? value, ISet<string> unknownValues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return EmergencyWarningLevel.None;

        return value.Trim().ToLowerInvariant() switch
        {
            "emergency_warning" => EmergencyWarningLevel.EmergencyWarning,
            "watch_and_act" => EmergencyWarningLevel.WatchAndAct,
            "advice" => EmergencyWarningLevel.Advice,
            "none" => EmergencyWarningLevel.None,
            _ => LogUnknownWarning(value, unknownValues)
        };
    }
    private EmergencyWarningLevel LogUnknownWarning(string value, ISet<string> unknownValues)
    {
        if (unknownValues.Add(value))
            logger.LogDebug("[EmergencyFeed] Unrecognised DataQuoll warningLevel {WarningLevel}; treating as None", value);
        return EmergencyWarningLevel.None;
    }
    private async Task<IReadOnlyDictionary<string, AttributionInfo>> GetAttributionAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(AttributionCacheKey, out IReadOnlyDictionary<string, AttributionInfo>? cached)
            && cached is not null)
            return cached;

        var empty = new Dictionary<string, AttributionInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string baseUrl = _options.BaseUrl.TrimEnd('/');
            using HttpResponseMessage response = await httpClient.GetAsync(
                $"{baseUrl}/attribution", cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"DataQuoll attribution returned {(int)response.StatusCode} ({response.ReasonPhrase})");

            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (!TryGetProperty(root, "sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
                throw new JsonException("DataQuoll attribution response did not contain sources");

            foreach (JsonElement source in sources.EnumerateArray())
            {
                string state = (GetString(source, "state") ?? string.Empty).Trim();
                string agency = (GetString(source, "agency") ?? string.Empty).Trim();
                if (state.Length == 0 || agency.Length == 0)
                    continue;
                empty[AttributionKey(state, agency)] = new AttributionInfo(
                    GetString(source, "licence")?.Trim() ?? string.Empty,
                    GetBoolean(source, "official"));
            }

            CacheAttribution(empty);
            return empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[EmergencyFeed] DataQuoll attribution unavailable; all events are ineligible");
            CacheAttribution(empty);
            return empty;
        }
    }

    private void CacheAttribution(IReadOnlyDictionary<string, AttributionInfo> attribution)
    {
        TimeSpan ttl = TimeSpan.FromHours(Math.Max(0, _options.AttributionCacheHours));
        if (ttl > TimeSpan.Zero)
            _cache.Set(AttributionCacheKey, attribution,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }

    private static AttributionInfo? FindAttribution(
        IReadOnlyDictionary<string, AttributionInfo> attribution, string state, string agency) =>
        attribution.TryGetValue(AttributionKey(state, agency), out AttributionInfo? value) ? value : null;

    private static string AttributionKey(string state, string agency) =>
        $"{state.Trim()}\u001f{agency.Trim()}";

    private static bool IsNewer(ParsedEmergencyEvent candidate, ParsedEmergencyEvent current)
    {
        DateTimeOffset? candidateTime = candidate.UpdatedUtc ?? candidate.ReportedUtc;
        DateTimeOffset? currentTime = current.UpdatedUtc ?? current.ReportedUtc;
        int byTime = Nullable.Compare(candidateTime, currentTime);
        return byTime != 0 ? byTime > 0 : string.CompareOrdinal(candidate.RawId, current.RawId) > 0;
    }

    private static string StableIdentity(string rawId)
    {
        int separator = rawId.LastIndexOf(':');
        return separator >= 0 && separator < rawId.Length - 1 ? rawId[(separator + 1)..] : rawId;
    }

    private static (double? Latitude, double? Longitude) GetFeaturePoint(JsonElement feature, JsonElement location)
    {
        if (TryGetObject(feature, "geometry") is JsonElement geometry
            && geometry.ValueKind == JsonValueKind.Object
            && string.Equals(GetString(geometry, "type"), "Point", StringComparison.OrdinalIgnoreCase)
            && TryGetProperty(geometry, "coordinates", out JsonElement coordinates)
            && coordinates.ValueKind == JsonValueKind.Array
            && coordinates.GetArrayLength() >= 2
            && TryGetNumber(coordinates[0], out double pointLng)
            && TryGetNumber(coordinates[1], out double pointLat)
            && IsValidPoint(pointLat, pointLng))
            return (pointLat, pointLng);

        string? geometryType = TryGetObject(feature, "geometry") is JsonElement shape
            ? GetString(shape, "type") : null;
        if ((string.Equals(geometryType, "Polygon", StringComparison.OrdinalIgnoreCase)
             || string.Equals(geometryType, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
            && TryGetNumberProperty(location, "latitude", out double lat)
            && TryGetNumberProperty(location, "longitude", out double lng)
            && IsValidPoint(lat, lng))
            return (lat, lng);

        return (null, null);
    }

    private static bool IsInside(CoverageArea area, double lat, double lng)
    {
        if (!area.ContainsPoint(lat, lng))
            return false;

        return area.ShapeType switch
        {
            CoverageShapeType.Circle => GreatCircleKm(area.Latitude, area.Longitude, lat, lng) <= area.RadiusKm,
            CoverageShapeType.BoundingBox when area.West is { } west && area.South is { } south
                && area.East is { } east && area.North is { } north =>
                lat >= south && lat <= north && lng >= west && lng <= east,
            _ => true
        };
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && TryGetProperty(parent, name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static JsonElement TryGetObject(JsonElement parent, string name) =>
        TryGetObject(parent, name, out JsonElement value) ? value : default;

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in parent.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement parent, string name) =>
        TryGetProperty(parent, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool GetBoolean(JsonElement parent, string name) =>
        TryGetProperty(parent, name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static bool TryGetNumberProperty(JsonElement parent, string name, out double value)
    {
        value = default;
        return TryGetProperty(parent, name, out JsonElement item) && TryGetNumber(item, out value);
    }

    private static bool TryGetNumber(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number))
            return true;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return true;
        number = default;
        return false;
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset timestamp)
            ? timestamp : null;

    private static bool IsValidPoint(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static double ClampLatitude(double value) => Math.Clamp(value, -90, 90);
    private static double ClampLongitude(double value) => Math.Clamp(value, -180, 180);

    private static double GreatCircleKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLng = (lng2 - lng1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private sealed record ParsedEmergencyEvent(
        string RawId,
        string StableId,
        string SourceFeedId,
        string Agency,
        string State,
        string Title,
        EmergencyWarningLevel WarningLevel,
        string EventType,
        string EventCategory,
        string? Suburb,
        string? Address,
        double? Latitude,
        double? Longitude,
        string? Status,
        DateTimeOffset? ReportedUtc,
        DateTimeOffset? UpdatedUtc,
        string? SourceUrl,
        string? Detail,
        int Index);

    private sealed record AttributionInfo(string Licence, bool Official);
    private sealed record AttributionCacheEntry(DateTimeOffset FetchedAt, IReadOnlyDictionary<string, AttributionInfo> Sources);
}
