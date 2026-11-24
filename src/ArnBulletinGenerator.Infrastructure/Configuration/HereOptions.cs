using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration options for HERE Maps API integration.
/// </summary>
public sealed class HereOptions
{
    public const string SectionName = "Here";

    [Required] public required string ApiKey { get; set; }

    /// <summary>
    ///     Browser-safe HERE Maps JS API key for the operator console's evidence map.
    ///     MUST be a SEPARATE key from <see cref="ApiKey"/> (the server-side paid REST key)
    ///     and SHOULD be domain-restricted in the HERE console to the console's own hostnames.
    ///     Served only to authenticated operators via <c>GET /api/map-config</c>; never baked
    ///     into the static bundle. Empty (default) ⇒ every console map shows "Map unavailable".
    /// </summary>
    public string MapApiKey { get; set; } = string.Empty;

    public string TrafficBaseUrl { get; set; } = "https://data.traffic.hereapi.com/v7";
    public string GeocodingBaseUrl { get; set; } = "https://geocode.search.hereapi.com/v1";
    public string RoutingBaseUrl { get; set; } = "https://router.hereapi.com/v8";

    /// <summary>
    ///     HERE Map Attributes API — the map's own record of a road segment, used to settle an
    ///     incident's road identity from the segments the incident itself references rather than from
    ///     whatever is nearest its coordinates. Verified reachable on the current key (2026-08-13).
    /// </summary>
    public string MapAttributesBaseUrl { get; set; } = "https://smap.hereapi.com/v8/maps/attributes";

    public string IncidentsPath { get; set; } = "incidents";
    public string FlowPath { get; set; } = "flow";
    public string GeocodePath { get; set; } = "geocode";
    public string ReverseGeocodePath { get; set; } = "revgeocode";

    public string BatchReverseGeocodeUrl { get; set; } =
        "https://multi-revgeocode.search.hereapi.com/v1/multi-revgeocode";

    /// <summary>
    ///     Per-attempt HTTP timeout (seconds) for HERE traffic, routing, and geocoding calls.
    ///     Applied by Polly PER retry attempt (not across the whole retry chain), so a transient
    ///     failure's backoff can never cancel a healthy retry. Weather and fuel use their own
    ///     <c>TimeoutSeconds</c> with the same per-attempt semantics.
    /// </summary>
    [Range(5, 120)]
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    ///     When true, uses the HERE batch reverse geocode API (requires premium plan).
    ///     When false, falls back to sequential individual reverse geocode calls.
    /// </summary>
    public bool UseBatchGeocoding { get; set; }

    /// <summary>
    ///     Maximum degree of parallelism for sequential reverse geocoding when batch is disabled.
    /// </summary>
    public int MaxParallelReverseGeocode { get; set; } = 5;

    /// <summary>
    ///     Minimum HERE flow <c>jamFactor</c> (0–10) for an incident-free road to become a
    ///     standalone congestion corridor. Low bar so ranking decides air-time (generalised-bulletin
    ///     spec §3); 0 disables congestion corridors entirely.
    /// </summary>
    public double CongestionJamFactorThreshold { get; set; } = 5.0;

    /// <summary>
    ///     Allowed incident types from HERE Traffic API.
    ///     Leave empty to allow all types.
    /// </summary>
    public List<string> AllowedIncidentTypes { get; set; } = new()
    {
        "accident",
        "congestion",
        "construction",
        "disabledVehicle",
        "laneRestriction",
        "roadClosure",
        "roadHazard",
        "plannedEvent"
    };
}
