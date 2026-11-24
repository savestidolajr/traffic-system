using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>One real fetch's validity picture, counted from the records themselves.</summary>
/// <param name="Records">Incidents the fetch selected for the bulletin.</param>
/// <param name="Fresh">Window class <see cref="WindowClass.Fresh" />.</param>
/// <param name="Ongoing">Window class <see cref="WindowClass.Ongoing" />.</param>
/// <param name="Scheduled">Window class <see cref="WindowClass.Scheduled" />.</param>
/// <param name="Closures">Records still carrying a live closure claim after eligibility.</param>
/// <param name="DemotedClosures">Closure claims the boundary reduced ("demoted:…").</param>
/// <param name="Flagged">Records flowing for ranking with an unresolved identity/condition ("flagged:…").</param>
/// <param name="EntryOnlyFreshClosures">
///     Closures whose only freshness signal is a recent <c>entryTime</c> — an old
///     <c>startTime</c> inside a long window, re-issued into the feed recently. Since the
///     2026-08-18 enforcement (<see cref="IncidentWindow" /> suppresses entry freshness for
///     re-issued worksites) these classify Scheduled and their closure claims demote, so this
///     counts re-issues CAUGHT per fetch rather than escapes (Mitchell Rd aired via this
///     loophole on pilot day 2 before enforcement).
/// </param>
public sealed record FetchValidityCounts(
    int Records,
    int Fresh,
    int Ongoing,
    int Scheduled,
    int Closures,
    int DemotedClosures,
    int Flagged,
    int EntryOnlyFreshClosures);

/// <summary>
///     Per-fetch validity accounting, emitted from the REAL generation path (no scheduled probe):
///     every bulletin's own fetch reports the data quality it was built from, so the trend series is
///     driven by broadcasts, sampled exactly when the data mattered, at zero extra API cost.
/// </summary>
public static class FetchValidityAccounting
{
    public static FetchValidityCounts Count(IReadOnlyList<TrafficIncident> incidents, DateTimeOffset nowUtc)
    {
        int fresh = 0, ongoing = 0, scheduled = 0, closures = 0, demoted = 0, flagged = 0, entryOnly = 0;
        foreach (var incident in incidents)
        {
            var window = IncidentWindow.Classify(
                incident.StartTimeUtc, incident.EndTimeUtc, incident.EntryTimeUtc, nowUtc);
            switch (window)
            {
                case WindowClass.Fresh: fresh++; break;
                case WindowClass.Ongoing: ongoing++; break;
                case WindowClass.Scheduled: scheduled++; break;
            }

            if (incident.WithheldReason.HasValue)
            {
                if (incident.WithheldReason.Value.StartsWith("demoted:", StringComparison.Ordinal))
                    demoted++;
                else if (incident.WithheldReason.Value.StartsWith("flagged:", StringComparison.Ordinal))
                    flagged++;
            }

            if (!incident.RoadClosed)
                continue;
            closures++;

            // The re-issue class: fresh entry, old start, long window. IncidentWindow now
            // classifies these Scheduled (never Fresh), so this counts the catches, not escapes.
            bool entryFresh = incident.EntryTimeUtc is { } entry
                              && nowUtc >= entry && nowUtc - entry <= TimeSpan.FromHours(3);
            bool startedRecently = incident.StartTimeUtc is { } start
                                   && nowUtc >= start && nowUtc - start <= TimeSpan.FromHours(24);
            bool longWindow = incident is { StartTimeUtc: { } s, EndTimeUtc: { } e }
                              && e - s > TimeSpan.FromDays(IncidentCurrencyPolicy.LongTermWindowDays);
            if (entryFresh && !startedRecently && longWindow)
                entryOnly++;
        }

        return new FetchValidityCounts(
            incidents.Count, fresh, ongoing, scheduled, closures, demoted, flagged, entryOnly);
    }
}
