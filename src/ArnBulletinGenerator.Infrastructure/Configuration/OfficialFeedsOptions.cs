namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     The authoritative road-authority feeds used to corroborate HERE. Endpoint defaults are
///     public, keyless feeds; the QLDTraffic credential is supplied per environment at runtime.
///     Each source is optional at startup: a missing or malformed value disables only that feed.
/// </summary>
public sealed class OfficialFeedsOptions
{
    public const string SectionName = "OfficialFeeds";

    /// <summary>
    ///     Published QLDTraffic demo credential retained only as a local-development marker. It is
    ///     never used as the deployed default; deployed environments must load
    ///     <c>OfficialFeeds--QldApiKey</c> from Key Vault.
    /// </summary>
    public const string PublishedDemoApiKey = "3e83add325cbb69ac4d8e5bf433d770b";

    /// <summary>Master switch for everything consuming the official feeds.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to corroborate HERE incidents against official feeds (e.g. demote ghost closures, enrich lane details).</summary>
    public bool CorroborateIncidents { get; set; } = true;

    /// <summary>Whether to inject official emergency incidents (crashes, hazards) missing from HERE.</summary>
    public bool InjectMissingOfficialIncidents { get; set; } = true;

    /// <summary>Maximum distance in metres between official event and HERE incident to consider a spatial match.</summary>
    public double MaxCorroborationDistanceMeters { get; set; } = 500.0;

    /// <summary>
    ///     Live Traffic NSW open-data hazard feeds root. Feed kind + "-open.json" is appended
    ///     ("incident-open.json", "roadwork-open.json"). No key required.
    /// </summary>
    public string NswHazardsBaseUrl { get; set; } = "https://www.livetraffic.com/traffic/hazards";

    /// <summary>QLDTraffic GeoJSON v2 events endpoint.</summary>
    public string QldEventsUrl { get; set; } = "https://api.qldtraffic.qld.gov.au/v2/events";

    /// <summary>
    ///     QLDTraffic API key. Deployed environments load the per-environment secret
    ///     <c>OfficialFeeds--QldApiKey</c> from Key Vault at runtime. The local appsettings file
    ///     may provide the published demo key for an offline development loop; it is never an
    ///     infrastructure default and must not be used by a deployed environment.
    /// </summary>
    public string QldApiKey { get; set; } = string.Empty;

    /// <summary>Victoria DataVic unplanned disruptions endpoint (keyless open JSON).</summary>
    public string VicDisruptionsUrl { get; set; } = "https://opendata.transport.vic.gov.au/dataset/unplanned-disruptions-road/resource/e49bbb1b-4764-4736-a629-02c339ebaab1/download/unplanned_disruptions.json";

    /// <summary>Main Roads Western Australia TravelInformation ArcGIS MapServer base.</summary>
    public string WaTravelMapUrl { get; set; } = "https://gisservices.mainroads.wa.gov.au/arcgis/rest/services/TravelInformation/MapServer";

    /// <summary>South Australia TrafficSATime ArcGIS layer-1 query endpoint.</summary>
    public string SaTrafficUrl { get; set; } = "https://dit-cms-prod.geohub.sa.gov.au/arcgis/rest/services/TrafficSATime/TrafficSATime/MapServer/1/query?where=1%3D1&outFields=*&returnGeometry=false&f=pjson";

    /// <summary>Tasmania Police TasALERT road alerts RSS/GeoJSON endpoint.</summary>
    public string TasAlertUrl { get; set; } = "https://www.police.tas.gov.au/news-events/media-releases/feed/";
}
