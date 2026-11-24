using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Drops long-running roadworks that stopped being news days ago.
///     <para>
///         Measured on dev job 25fa6114 (cairns, 27 Aug): all 14 incidents behind the board were
///         entered on 19 August — SEVEN days old — and 16 of 17 corridors were construction. The
///         bulletin was competing for a 22 second slot between sixteen week-old work zones, and
///         whichever four scored highest went to air as though something had happened.
///     </para>
///     <para>
///         Only WORKS age out. A crash or a closure reported two days ago and still live is still
///         the most important thing on the road, and congestion has no report time at all because it
///         is a measurement rather than an event.
///     </para>
///     <para>
///     HERE resets <c>entryTime</c> on every revision, so a five-week-old Sydney work zone re-issued
///     on the morning of job 9a66a239 (2026-09-03) looked fresh when age was measured from entry time
///     alone. When both timestamps exist, the earlier of <c>startTime</c> and <c>entryTime</c> is
///     authoritative; an absent entry time remains old as it was before.
///     </para>
/// </summary>
public static class StaleRoadworksFilter
{
    /// <summary>Incident types that are planned work rather than something that just happened.</summary>
    private static readonly string[] WorksTypes = ["construction", "roadwork", "plannedevent", "maintenance"];

    /// <summary>
    ///     Corridors worth airing, given how old their works are.
    ///     <paramref name="maxAge" /> of zero or less disables the filter and returns the input.
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> Fresh(
        IReadOnlyList<TrafficCorridor> corridors, DateTimeOffset now, TimeSpan maxAge) =>
        maxAge <= TimeSpan.Zero
            ? corridors
            : [.. corridors.Where(corridor => !IsStaleWorks(corridor, now, maxAge))];

    /// <summary>How many corridors <see cref="Fresh" /> would remove, for the operator log.</summary>
    public static int CountStale(
        IReadOnlyList<TrafficCorridor> corridors, DateTimeOffset now, TimeSpan maxAge) =>
        maxAge <= TimeSpan.Zero ? 0 : corridors.Count(corridor => IsStaleWorks(corridor, now, maxAge));

    private static bool IsStaleWorks(TrafficCorridor corridor, DateTimeOffset now, TimeSpan maxAge)
    {
        // No incidents means a flow-derived corridor: a live speed reading, never stale.
        if (corridor.Incidents.Count == 0)
        {
            return false;
        }

        // Every incident must be works AND old. One fresh item, or one that is not works, keeps the
        // whole corridor: the road still has something happening on it.
        return corridor.Incidents.All(incident => IsWorks(incident.Type) && IsOld(incident, now, maxAge));
    }

    private static bool IsWorks(string? type) =>
        type is not null
        && WorksTypes.Any(works => type.Replace("-", "").Replace("_", "")
            .Contains(works, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     A works incident is old when its earliest known start or entry time is older than the
    ///     allowed age. HERE resets <c>entryTime</c> on every revision, so using the earlier timestamp
    ///     catches a long-running work zone re-issued this morning, as in Sydney job 9a66a239
    ///     (2026-09-03). An absent entry time still counts as old, preserving the fail-closed rule.
    /// </summary>
    private static bool IsOld(TrafficIncident incident, DateTimeOffset now, TimeSpan maxAge)
    {
        if (incident.EntryTimeUtc is not { } entered)
        {
            return true;
        }

        DateTimeOffset earliest = incident.StartTimeUtc is { } started && started < entered
            ? started
            : entered;
        return now - earliest > maxAge;
    }
}
