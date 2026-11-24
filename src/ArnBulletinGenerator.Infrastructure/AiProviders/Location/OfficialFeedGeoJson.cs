using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Shared GeoJSON / JSON reduction for state official feeds: flatten geometry to points, keep only
///     features inside the coverage area, extract road metadata, and reduce each to an <see cref="OfficialTrafficEvent" />.
///     Total per feature — a malformed feature is skipped, never thrown, so one bad row cannot blank an authority's whole picture.
/// </summary>
internal static class OfficialFeedGeoJson
{
    /// <summary>
    ///     District values that mark a record as ANOTHER state's — a mirror, not the feed's own
    ///     jurisdiction. Native QLD districts are regions ("Metropolitan", "Far North").
    /// </summary>
    private static readonly HashSet<string> InterstateDistricts =
        new(StringComparer.OrdinalIgnoreCase) { "NSW", "VIC", "SA", "NT", "WA", "TAS", "ACT" };
    internal static List<OfficialTrafficEvent> ParseNsw(
        JsonDocument doc, string sourceId, bool incidentClass, CoverageArea area)
    {
        var events = new List<OfficialTrafficEvent>();
        foreach (var feature in Features(doc))
        {
            try
            {
                if (feature.TryGetProperty("properties", out var props))
                {
                    if (props.TryGetProperty("ended", out var ended) && ended.ValueKind == JsonValueKind.True)
                        continue;

                    var shape = ShapeInside(feature, area);
                    if (shape.Count == 0)
                        continue;

                    // Roadwork records carry an EMPTY headline; displayName ("SCHEDULED ROADWORK")
                    // is the record's own label and keeps the event describable.
                    string headline = props.TryGetProperty("headline", out var h) && h.GetString() is { Length: > 0 } hv
                        ? hv
                        : props.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                    string? roadName = props.TryGetProperty("roads", out var roads) && roads.ValueKind == JsonValueKind.Array && roads.GetArrayLength() > 0
                        && roads[0].TryGetProperty("mainStreet", out var ms) ? ms.GetString() : null;
                    string? crossStreet = props.TryGetProperty("roads", out var rds) && rds.ValueKind == JsonValueKind.Array && rds.GetArrayLength() > 0
                        && rds[0].TryGetProperty("crossStreet", out var cs) ? cs.GetString() : null;
                    string? suburb = props.TryGetProperty("roads", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0
                        && r[0].TryGetProperty("suburb", out var s) ? s.GetString() : null;
                    var laneImpacts = new List<JsonElement>();
                    if (props.TryGetProperty("roads", out var roadsForImpacts)
                        && roadsForImpacts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var road in roadsForImpacts.EnumerateArray())
                        {
                            if (!road.TryGetProperty("impactedLanes", out var impacts)
                                || impacts.ValueKind != JsonValueKind.Array)
                                continue;
                            laneImpacts.AddRange(impacts.EnumerateArray());
                        }
                    }
                    // The PRESENCE of the impactedLanes key means nothing: measured 2026-08-24, 310 of
                    // 322 live records carry it as an EMPTY array. Keying the periods fallback off the
                    // key would have dropped direction on 96% of the feed - 1.2% coverage instead of
                    // the 93% periods[].direction actually supplies. Only real ENTRIES outrank periods.
                    string? direction = ResolveNswDirection(props, laneImpacts, laneImpacts.Count > 0);
                    var laneFacts = ResolveNswLaneFacts(laneImpacts);
                    string? id = SafeId(feature) ?? SafeId(props);
                    string? adviceA = StringProperty(props, "adviceA");
                    string? adviceB = StringProperty(props, "adviceB");
                    string? adviceC = StringProperty(props, "adviceC");
                    string? advice = adviceA;
                    var adviceTags = Deduplicate(adviceA, adviceB, adviceC);
                    string? authorityPriority = props.TryGetProperty("isMajor", out var major)
                        && major.ValueKind == JsonValueKind.True
                        ? "Major"
                        : null;
                    bool? isStateRoad = ParseClassification(StringProperty(props, "isLocalRoad"),
                        "State road", "Local road");
                    bool? isPlanned = ParseClassification(StringProperty(props, "incidentKind"),
                        "Planned", "Unplanned");
                    string? impactType = null;
                    string? impactSubtype = null;
                    if (props.TryGetProperty("periods", out var periods)
                        && periods.ValueKind == JsonValueKind.Array
                        && periods.GetArrayLength() > 0)
                    {
                        var period = periods[0];
                        impactType = StringProperty(period, "closureType");
                        impactSubtype = StringProperty(period, "roadextent");
                    }
                    // The authority's own clock, epoch millis, kept DISTINCT: 'start' is the
                    // event's scheduled start, 'created' when the authority first published the
                    // record. Fusion decides which signal proves age.
                    DateTimeOffset? startTime = EpochMs(props, "start");
                    DateTimeOffset? publishedTime = EpochMs(props, "created");
                    DateTimeOffset? endTime = EpochMs(props, "end");

                    events.Add(new OfficialTrafficEvent(
                        SourceId: sourceId,
                        Headline: headline,
                        IsIncidentClass: incidentClass,
                        Shape: shape,
                        Id: id,
                        RoadName: roadName,
                        CrossStreet: crossStreet,
                        Suburb: suburb,
                        Description: advice ?? headline,
                        Direction: direction,
                        StartTime: startTime,
                        EndTime: endTime,
                        PublishedTime: publishedTime,
                        AuthorityPriority: authorityPriority,
                        Category: StringProperty(props, "mainCategory"),
                        SubCategory: StringProperty(props, "subCategoryA"),
                        IsStateRoad: isStateRoad,
                        IsPlanned: isPlanned,
                        AdviceTags: adviceTags,
                        ImpactType: impactType,
                        ImpactSubtype: impactSubtype,
                        SpeedLimitKph: PositiveIntProperty(props, "speedLimit"),
                        LanesClosed: laneFacts.LanesClosed,
                        LastUpdated: EpochMs(props, "lastUpdated"),
                        Detail: StripHtml(StringProperty(props, "otherAdvice")),
                        LanesTotal: laneFacts.LanesTotal,
                        ExtentDescription: laneFacts.ExtentDescription));
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // Skip the malformed feature; the rest of the feed still counts.
            }
        }

        return events;
    }

    private static string? ResolveNswDirection(
        JsonElement props,
        IReadOnlyList<JsonElement> laneImpacts,
        bool hasImpactedLanes)
    {
        var candidates = new List<string>();
        if (hasImpactedLanes)
        {
            foreach (var impact in laneImpacts)
            {
                if (StringProperty(impact, "affectedDirection") is { } direction
                    && IsVoiceableNswDirection(direction))
                    candidates.Add(direction.Trim());
            }
        }
        else if (props.TryGetProperty("periods", out var periods)
                 && periods.ValueKind == JsonValueKind.Array)
        {
            foreach (var period in periods.EnumerateArray())
            {
                if (StringProperty(period, "direction") is { } direction
                    && IsVoiceableNswDirection(direction))
                    candidates.Add(direction.Trim());
            }
        }

        var distinct = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static bool IsVoiceableNswDirection(string direction) =>
        !string.IsNullOrWhiteSpace(direction)
        && !direction.Equals("Both directions", StringComparison.OrdinalIgnoreCase)
        && !direction.Equals("All directions", StringComparison.OrdinalIgnoreCase);

    private static (int? LanesClosed, int? LanesTotal, string? ExtentDescription) ResolveNswLaneFacts(
        IReadOnlyList<JsonElement> laneImpacts)
    {
        int? maxClosed = null;
        int? selectedTotal = null;
        string? selectedExtent = null;
        bool selected = false;
        int? firstTotal = null;
        string? firstTotalExtent = null;
        string? firstExtent = null;

        foreach (var impact in laneImpacts)
        {
            int? closed = PositiveNswLaneCount(impact, "closedLanes");
            int? total = PositiveNswLaneCount(impact, "numberOfLanes");
            string? extent = StringProperty(impact, "extent") is { } rawExtent
                && !string.IsNullOrWhiteSpace(rawExtent)
                ? rawExtent
                : null;
            firstExtent ??= extent;

            if (closed.HasValue && (!maxClosed.HasValue || closed.Value > maxClosed.Value))
            {
                maxClosed = closed;
                selectedTotal = total;
                selectedExtent = extent;
                selected = true;
            }

            if (!firstTotal.HasValue && total.HasValue)
            {
                firstTotal = total;
                firstTotalExtent = extent;
            }
        }

        if (!selected && firstTotal.HasValue)
        {
            selectedTotal = firstTotal;
            selectedExtent = firstTotalExtent;
            selected = true;
        }

        if (!selected && firstExtent is not null)
            selectedExtent = firstExtent;

        if (maxClosed.HasValue && selectedTotal.HasValue && selectedTotal.Value < maxClosed.Value)
            selectedTotal = null;

        return (maxClosed, selectedTotal, selectedExtent);
    }

    private static int? PositiveNswLaneCount(JsonElement impact, string name)
    {
        if (!impact.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int numeric)
            && numeric > 0)
            return numeric;

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            && parsed > 0)
            return parsed;

        return null;
    }

    /// <summary>Epoch-milliseconds property as a UTC instant, or null when absent/non-numeric.</summary>
    internal static DateTimeOffset? EpochMs(JsonElement props, string name) =>
        props.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    internal static List<OfficialTrafficEvent> ParseQld(JsonDocument doc, string sourceId, CoverageArea area)
    {
        var events = new List<OfficialTrafficEvent>();
        foreach (var feature in Features(doc))
        {
            try
            {
                var shape = ShapeInside(feature, area);
                if (shape.Count == 0)
                    continue;

                string eventType = "", description = "", roadName = "", suburb = "", advice = "";
                string? eventSubtype = null;
                string? authorityPriority = null;
                string? impactType = null;
                string? impactSubtype = null;
                string? qualitativeDelay = null;
                string? direction = null;
                string? detail = null;
                DateTimeOffset? startTime = null;
                DateTimeOffset? endTime = null;
                DateTimeOffset? publishedTime = null;
                DateTimeOffset? lastUpdated = null;
                string? id = SafeId(feature);
                if (feature.TryGetProperty("properties", out var props))
                {
                    id ??= SafeId(props);
                    eventType = StringProperty(props, "event_type") ?? "";
                    eventSubtype = StringProperty(props, "event_subtype");
                    authorityPriority = StringProperty(props, "event_priority");
                    description = StringProperty(props, "description") ?? "";
                    detail = StripHtml(StringProperty(props, "information"));
                    publishedTime = IsoDateProperty(props, "published");
                    lastUpdated = IsoDateProperty(props, "last_updated");
                    if (props.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Object)
                    {
                        startTime = IsoDateProperty(dur, "start");
                        endTime = IsoDateProperty(dur, "end");
                    }
                    if (props.TryGetProperty("impact", out var impact) && impact.ValueKind == JsonValueKind.Object)
                    {
                        impactType = StringProperty(impact, "impact_type");
                        impactSubtype = StringProperty(impact, "impact_subtype");
                        qualitativeDelay = StringProperty(impact, "delay");
                        direction = StringProperty(impact, "direction");
                    }
                    if (props.TryGetProperty("road_summary", out var rs))
                    {
                        // Jurisdiction boundary: QLDTraffic MIRRORS other states' records (73
                        // NSW-district rows observed live 2026-08-18 — West Botany St, Rockdale,
                        // inside the QLD feed). A mirror is second-hand: the home authority is
                        // the source of truth for its own roads, and ingesting the copy produced
                        // 'Live Traffic NSW + QLDTraffic' provenance on Sydney corridors. Native
                        // QLD districts are regions ("Metropolitan", "Far North"); a state code
                        // marks the record as another jurisdiction's.
                        if (rs.TryGetProperty("district", out var dist)
                            && dist.GetString() is { } district
                            && InterstateDistricts.Contains(district))
                            continue;
                        roadName = StringProperty(rs, "road_name") ?? "";
                        suburb = StringProperty(rs, "locality") ?? "";
                    }
                    advice = StringProperty(props, "advice") ?? "";
                }

                bool incidentClass = eventType is not ("Roadworks" or "Special event");
                string headline = string.IsNullOrWhiteSpace(description) ? eventType : $"{eventType}: {description}";
                events.Add(new OfficialTrafficEvent(
                    SourceId: sourceId,
                    Headline: headline,
                    IsIncidentClass: incidentClass,
                    Shape: shape,
                    Id: id,
                    RoadName: string.IsNullOrWhiteSpace(roadName) ? null : roadName,
                    Suburb: string.IsNullOrWhiteSpace(suburb) ? null : suburb,
                    Description: string.IsNullOrWhiteSpace(advice) ? description : advice,
                    Direction: string.IsNullOrWhiteSpace(direction) ? null : direction,
                    StartTime: startTime,
                    EndTime: endTime,
                    PublishedTime: publishedTime,
                    Category: eventType,
                    SubCategory: eventSubtype,
                    AuthorityPriority: authorityPriority,
                    ImpactType: impactType,
                    ImpactSubtype: impactSubtype,
                    QualitativeDelay: qualitativeDelay,
                    LastUpdated: lastUpdated,
                    Detail: detail));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
            }
        }

        return events;
    }

    /// <summary>
    ///     Feed ids are strings in some jurisdictions and NUMBERS in others (QLD). GetString() on a
    ///     Number kind THROWS InvalidOperationException — it never returns null — and the per-feature
    ///     catch then silently dropped every QLD feature, blanking the whole feed (caught by the
    ///     golden corpus at integration, 2026-08-18).
    /// </summary>
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    internal static string? StringProperty(JsonElement props, string name) =>
        props.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    /// <summary>
    ///     Parses a traffic integer only when it is strictly positive. Road-authority feeds use
    ///     zero, negative values, and empty strings as unknown sentinels; treating those as values
    ///     suppresses prose enrichment and can air a fabricated measurement. Use a dedicated
    ///     parser only when a source contract proves zero is a meaningful value.
    /// </summary>
    internal static int? PositiveIntProperty(JsonElement props, string name)
    {
        if (!props.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number > 0)
            return number;
        return value.ValueKind == JsonValueKind.String
               && int.TryParse(value.GetString(), out number)
               && number > 0
            ? number
            : null;
    }

    internal static DateTimeOffset? ArcGisDateProperty(JsonElement props, string name)
    {
        if (!props.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long milliseconds)
            && milliseconds > 0)
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        if (value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            return null;

        string text = value.GetString()!;
        return DateTimeOffset.TryParseExact(
                   text,
                   "dd/MM/yyyy HH:mm:ss",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out var exact)
            ? exact
            : DateTimeOffset.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out var parsed)
                ? parsed
                : null;
    }
    private static DateTimeOffset? IsoDateProperty(JsonElement props, string name) =>
        StringProperty(props, name) is { } value
        && DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<string> Deduplicate(params string?[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool? ParseClassification(string? value, string trueValue, string falseValue) =>
        value?.Trim() switch
        {
            var candidate when candidate is not null
                && candidate.Equals(trueValue, StringComparison.OrdinalIgnoreCase) => true,
            var candidate when candidate is not null
                && candidate.Equals(falseValue, StringComparison.OrdinalIgnoreCase) => false,
            _ => null
        };

    private static string? StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string withoutTags = HtmlTagRegex.Replace(value, " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);
        string compact = string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(compact) ? null : compact;
    }
    private static string? SafeId(JsonElement props) =>
        props.TryGetProperty("id", out var id)
            ? id.ValueKind switch
            {
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Number => id.GetRawText(),
                _ => null
            }
            : null;

    internal static List<OfficialTrafficEvent> ParseVic(JsonDocument doc, string sourceId, CoverageArea area)
    {
        var events = new List<OfficialTrafficEvent>();
        foreach (var feature in Features(doc))
        {
            try
            {
                var shape = ShapeInside(feature, area);
                if (shape.Count == 0)
                    continue;

                string headline = "", roadName = "", suburb = "", desc = "", id = SafeId(feature) ?? "";
                bool isIncident = true;
                if (feature.TryGetProperty("properties", out var props))
                {
                    if (string.IsNullOrWhiteSpace(id))
                        id = SafeId(props) ?? "";
                    if (props.TryGetProperty("title", out var t))
                        headline = t.GetString() ?? "";
                    if (props.TryGetProperty("road_name", out var rn))
                        roadName = rn.GetString() ?? "";
                    if (props.TryGetProperty("suburb", out var s))
                        suburb = s.GetString() ?? "";
                    if (props.TryGetProperty("description", out var d))
                        desc = d.GetString() ?? "";
                    if (props.TryGetProperty("type", out var typeProp))
                    {
                        string type = typeProp.GetString() ?? "";
                        isIncident = !type.Contains("work", StringComparison.OrdinalIgnoreCase)
                                     && !type.Contains("planned", StringComparison.OrdinalIgnoreCase);
                    }
                }

                events.Add(new OfficialTrafficEvent(
                    SourceId: sourceId,
                    Headline: string.IsNullOrWhiteSpace(headline) ? desc : headline,
                    IsIncidentClass: isIncident,
                    Shape: shape,
                    Id: string.IsNullOrWhiteSpace(id) ? null : id,
                    RoadName: string.IsNullOrWhiteSpace(roadName) ? null : roadName,
                    Suburb: string.IsNullOrWhiteSpace(suburb) ? null : suburb,
                    Description: desc));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
            }
        }

        return events;
    }

    internal static List<OfficialTrafficEvent> ParseArcGis(
        JsonDocument doc,
        string sourceId,
        CoverageArea area) =>
        ParseArcGis(doc, area, (attributes, shape) =>
        {
            string eventType = StringProperty(attributes, "EVENT_TYPE") ?? "";
            string description = StringProperty(attributes, "DESCRIPTION") ?? "";
            return new OfficialTrafficEvent(
                sourceId,
                string.IsNullOrWhiteSpace(eventType) ? description : $"{eventType}: {description}",
                !eventType.Contains("work", StringComparison.OrdinalIgnoreCase)
                    && !eventType.Contains("maintenance", StringComparison.OrdinalIgnoreCase),
                shape,
                Id: StringProperty(attributes, "OBJECTID"),
                RoadName: StringProperty(attributes, "ROAD_NAME"),
                Description: description);
        });

    /// <summary>
    ///     Shared ArcGIS FeatureSet reduction. ArcGIS records keep fields in an
    ///     <c>attributes</c> object, and different authorities expose coordinates either as
    ///     geometry or latitude/longitude attributes. Feed-specific mapping is supplied by the
    ///     callback so coordinate parsing and coverage filtering cannot drift between feeds.
    /// </summary>
    internal static List<OfficialTrafficEvent> ParseArcGis(
        JsonDocument doc,
        CoverageArea area,
        Func<JsonElement, IReadOnlyList<CoveragePoint>, OfficialTrafficEvent?> map)
    {
        var events = new List<OfficialTrafficEvent>();
        foreach (var feature in Features(doc))
        {
            try
            {
                if (!feature.TryGetProperty("attributes", out var attributes)
                    || attributes.ValueKind != JsonValueKind.Object)
                    continue;

                var shape = ArcGisShape(feature, attributes);
                if (shape.Count == 0)
                    continue;

                var centre = new List<CoveragePoint> { new(area.Latitude, area.Longitude) };
                if (TrafficFeedComparison.MinDistanceMeters(shape, centre) > area.RadiusKm * 1000
                    || !area.IntersectsPolygon(shape))
                    continue;

                if (map(attributes, shape) is { } parsed)
                    events.Add(parsed);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // A malformed feature must not blank the authority's whole picture.
            }
        }

        return events;
    }

    private static List<CoveragePoint> ArcGisShape(JsonElement feature, JsonElement attributes)
    {
        var points = new List<CoveragePoint>();
        if (feature.TryGetProperty("geometry", out var geometry)
            && geometry.ValueKind == JsonValueKind.Object)
        {
            if (TryNumber(geometry, "x", out double x) && TryNumber(geometry, "y", out double y))
            {
                points.Add(new CoveragePoint(y, x));
            }
            else if (geometry.TryGetProperty("paths", out var paths)
                     && paths.ValueKind == JsonValueKind.Array)
            {
                foreach (var path in paths.EnumerateArray())
                {
                    if (path.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var coordinate in path.EnumerateArray())
                    {
                        if (coordinate.ValueKind != JsonValueKind.Array)
                            continue;
                        var values = coordinate.EnumerateArray().ToList();
                        if (values.Count >= 2
                            && TryNumber(values[0], out double pathX)
                            && TryNumber(values[1], out double pathY))
                            points.Add(new CoveragePoint(pathY, pathX));
                    }
                }
            }
        }

        // SA requests returnGeometry=false and expose the point in these columns.
        if (points.Count == 0
            && TryNumber(attributes, "latitude", out double latitude)
            && TryNumber(attributes, "longitude", out double longitude))
            points.Add(new CoveragePoint(latitude, longitude));

        return points;
    }

    private static bool TryNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var property) && TryNumber(property, out value);
    }

    private static bool TryNumber(JsonElement value, out double number)
    {
        number = 0;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetDouble(out number);
        return value.ValueKind == JsonValueKind.String
               && double.TryParse(
                   value.GetString(),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out number);
    }

    private static IEnumerable<JsonElement> Features(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var f in features.EnumerateArray())
            yield return f;
    }

    /// <summary>The feature's points, or empty when none fall inside the area (or it has no geometry).</summary>
    private static List<CoveragePoint> ShapeInside(JsonElement feature, CoverageArea area)
    {
        var pts = new List<CoveragePoint>();
        if (feature.TryGetProperty("geometry", out var geometry))
            Flatten(geometry, pts);
        if (pts.Count == 0)
            return pts;

        var centre = new List<CoveragePoint> { new(area.Latitude, area.Longitude) };
        if (TrafficFeedComparison.MinDistanceMeters(pts, centre) > area.RadiusKm * 1000)
            return [];

        return area.IntersectsPolygon(pts) ? pts : [];
    }

    /// <summary>GeoJSON coordinates are [lng, lat]; every geometry kind flattens to its points.</summary>
    private static void Flatten(JsonElement geometry, List<CoveragePoint> pts)
    {
        if (geometry.ValueKind != JsonValueKind.Object)
            return;
        if (geometry.TryGetProperty("geometries", out var nested)
            && nested.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in nested.EnumerateArray())
                Flatten(g, pts);
            return;
        }

        if (!geometry.TryGetProperty("coordinates", out var coords))
            return;
        FlattenCoordinates(coords, pts);
    }

    private static void FlattenCoordinates(JsonElement node, List<CoveragePoint> pts)
    {
        if (node.ValueKind != JsonValueKind.Array)
            return;
        // A position is an array whose first element is a number; anything else is a nested list.
        var first = node.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Number)
        {
            var values = node.EnumerateArray().ToList();
            if (values.Count >= 2)
                pts.Add(new CoveragePoint(values[1].GetDouble(), values[0].GetDouble()));
            return;
        }

        foreach (var child in node.EnumerateArray())
            FlattenCoordinates(child, pts);
    }
}
