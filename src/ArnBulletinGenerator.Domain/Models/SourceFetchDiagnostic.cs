namespace ArnBulletinGenerator.Domain.Models;

/// <summary>What a bulletin actually got from one upstream data source.</summary>
public enum SourceFetchOutcome
{
    /// <summary>Live fetch succeeded.</summary>
    Ok,
    /// <summary>Live fetch failed; a cached payload within the stale ceiling was served instead.</summary>
    ServedStale,
    /// <summary>No usable data at all — fetch failed and no cached payload was within the ceiling.</summary>
    Failed,
    /// <summary>Not consulted: the source's jurisdiction does not cover this market.</summary>
    Skipped
}

/// <summary>
///     One data source's outcome for a single bulletin. Persisted on the job so an operator can see
///     that a bulletin is thinner than it looks; a feed failure was previously visible only in
///     Application Insights.
/// </summary>
/// <param name="SourceId">Stable source id: "here", "here-weather", "here-fuel", "livetraffic-nsw", "qldtraffic", "victraffic", "mainroads-wa", "trafficsa", "tasalert".</param>
/// <param name="Outcome">What the bulletin actually got.</param>
/// <param name="Detail">Operator-facing reason, e.g. "HTTP 429 (rate limited)". Null when Ok.</param>
/// <param name="DataAsOfUtc">When the SERVED payload was actually fetched upstream. Null when Failed/Skipped.</param>
/// <param name="StaleSeconds">Age of the served payload. Non-null only when Outcome is ServedStale.</param>
public sealed record SourceFetchDiagnostic(
    string SourceId,
    SourceFetchOutcome Outcome,
    string? Detail,
    DateTimeOffset? DataAsOfUtc,
    int? StaleSeconds);
