using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>
///     The SINGLE market-resolution path for free-text ad-hoc broadcast locations. Every caller
///     (the broadcast trigger and job creation) derives a job's market id through this one
///     implementation so the job record, the queue message and the orchestrator can never
///     disagree about which market a bulletin serves (observed 2026-08-02: the trigger's
///     resolver and <c>JobService</c>'s own copy disagreed, so one job's record claimed a real
///     market while its log insisted the location matched no configured station).
/// </summary>
public static class MarketLocationResolver
{
    /// <summary>
    ///     Matches <paramref name="location" /> against a schedule's <see cref="MarketSchedule.MarketId" />,
    ///     <see cref="MarketSchedule.Name" /> or <see cref="MarketSchedule.Station" /> — the
    ///     operator-visible console display name (e.g. "DCOAST 101.7"), which is a legitimate
    ///     value to fire through <c>POST /broadcast</c> — case- and whitespace-insensitively, and
    ///     returns the canonical market id of the first match.
    ///     <para>
    ///         A location that matches NO schedule resolves to the raw location text itself,
    ///         deliberately NOT a slugified variant: the slug of an unmatched location can
    ///         coincide with a real market id (observed: "Gold Coast" → "goldcoast"), which makes
    ///         the job record claim a configured market the bulletin was generated without. The
    ///         raw text can never collide with a canonical id, so the record stays truthful and
    ///         every per-market lookup correctly reports "nothing configured".
    ///     </para>
    /// </summary>
    /// <param name="schedules">The configured schedules to match against (already fetched).</param>
    /// <param name="location">The caller's free-text location.</param>
    /// <returns>
    ///     The canonical market id, and whether a configured schedule actually backed it.
    ///     A false <see cref="MarketResolution.HasSchedule" /> means every per-market lookup will
    ///     find nothing, so the caller can warn instead of letting the gap pass unremarked.
    /// </returns>
    public static MarketResolution Resolve(IEnumerable<MarketSchedule> schedules, string location)
    {
        var match = schedules.FirstOrDefault(s =>
            string.Equals(Normalize(s.MarketId), Normalize(location), StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(s.Name), Normalize(location), StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(s.Station), Normalize(location), StringComparison.OrdinalIgnoreCase));
        return new MarketResolution(match?.MarketId ?? location, match is not null, match?.Station);
    }

    /// <summary>Trims and collapses internal whitespace runs so "  gold   coast " matches "Gold Coast".</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
///     The outcome of resolving a free-text location to a market id. <see cref="Station" /> is
///     the matched schedule's operator-visible station name, or null when no schedule backed the
///     location — station-keyed corpora (future lessons) must key on the SAME name the schedule
///     poller writes, or lessons taught on ad-hoc bulletins become invisible to station lookups.
/// </summary>
public sealed record MarketResolution(string MarketId, bool HasSchedule, string? Station = null);
