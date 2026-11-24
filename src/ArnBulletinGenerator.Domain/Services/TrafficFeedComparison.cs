using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>The validity picture of one market's HERE fetch against the official feeds, one probe run.</summary>
/// <param name="HereRecords">HERE records fetched for the coverage area.</param>
/// <param name="Fresh">Records whose window classified <see cref="WindowClass.Fresh" />.</param>
/// <param name="Ongoing">Records classified <see cref="WindowClass.Ongoing" />.</param>
/// <param name="Scheduled">Records classified <see cref="WindowClass.Scheduled" /> — the worksite register.</param>
/// <param name="Closures">Records with <c>roadClosed</c>.</param>
/// <param name="ScheduledClosures">Closures that are long-planned worksites (the demoted class).</param>
/// <param name="UncorroboratedClosures">
///     Closures with no official event of ANY class within <see cref="TrafficFeedComparison.MatchRadiusMeters" />.
///     Meaningful only when <paramref name="OfficialEvents" /> &gt; 0 — an empty official picture
///     corroborates nothing and indicts nothing.
/// </param>
/// <param name="OfficialEvents">Official events found in the area, all classes.</param>
/// <param name="OfficialIncidentClass">Official incident-class events (crashes, hazards, breakdowns).</param>
/// <param name="MissingFromHere">Official incident-class events with no HERE record within radius.</param>
/// <param name="MedianEntryAgeHours">Median hours since <c>entryTime</c> across the HERE records.</param>
public sealed record FeedComparisonResult(
    int HereRecords,
    int Fresh,
    int Ongoing,
    int Scheduled,
    int Closures,
    int ScheduledClosures,
    int UncorroboratedClosures,
    int OfficialEvents,
    int OfficialIncidentClass,
    int MissingFromHere,
    double MedianEntryAgeHours);

/// <summary>
///     Pure comparison of one market's HERE picture against the official road-authority picture — the
///     2026-08-17 landscape study (docs/reference/here-staleness-landscape-…) productionised. Every
///     number it emits was first measured by hand there: Sydney ran 76% scheduled records, 76%
///     uncorroborated closures, and 71% of official incident-class events absent from HERE.
/// </summary>
public static class TrafficFeedComparison
{
    /// <summary>
    ///     Geometry match distance. 500 m absorbs the two sources anchoring the same event at
    ///     different points of the same works zone without matching across parallel corridors.
    /// </summary>
    public const double MatchRadiusMeters = 500;

    public static FeedComparisonResult Compare(
        IReadOnlyList<TrafficIncident> hereIncidents,
        IReadOnlyList<OfficialTrafficEvent> officialEvents,
        DateTimeOffset nowUtc)
    {
        int fresh = 0, ongoing = 0, scheduled = 0, closures = 0, scheduledClosures = 0;
        var ages = new List<double>();

        foreach (var incident in hereIncidents)
        {
            var window = IncidentWindow.Classify(
                incident.StartTimeUtc, incident.EndTimeUtc, incident.EntryTimeUtc, nowUtc);
            switch (window)
            {
                case WindowClass.Fresh: fresh++; break;
                case WindowClass.Ongoing: ongoing++; break;
                case WindowClass.Scheduled: scheduled++; break;
            }

            if (incident.EntryTimeUtc is { } entry && nowUtc >= entry)
                ages.Add((nowUtc - entry).TotalHours);

            if (!incident.RoadClosed)
                continue;
            closures++;
            if (window == WindowClass.Scheduled)
                scheduledClosures++;
        }

        int uncorroborated = 0;
        if (officialEvents.Count > 0)
        {
            foreach (var incident in hereIncidents)
            {
                if (!incident.RoadClosed)
                    continue;
                var shape = incident.Shape ?? [];
                if (shape.Count == 0 || officialEvents.All(o => MinDistanceMeters(shape, o.Shape) > MatchRadiusMeters))
                    uncorroborated++;
            }
        }

        int officialIncidentClass = 0, missing = 0;
        foreach (var official in officialEvents)
        {
            if (!official.IsIncidentClass)
                continue;
            officialIncidentClass++;
            if (official.Shape.Count == 0)
                continue;
            bool found = hereIncidents.Any(i =>
                i.Shape is { Count: > 0 } shape && MinDistanceMeters(shape, official.Shape) <= MatchRadiusMeters);
            if (!found)
                missing++;
        }

        ages.Sort();
        double medianAge = ages.Count == 0 ? 0 : ages[ages.Count / 2];

        return new FeedComparisonResult(
            hereIncidents.Count, fresh, ongoing, scheduled, closures, scheduledClosures,
            uncorroborated, officialEvents.Count, officialIncidentClass, missing, medianAge);
    }

    /// <summary>
    ///     Smallest geodesic distance between two point sets, capped at 25 points per side: shapes are
    ///     ordered polylines, so a coarse sample bounds the work without losing the match decision.
    /// </summary>
    public static double MinDistanceMeters(
        IReadOnlyList<CoveragePoint> a, IReadOnlyList<CoveragePoint> b)
    {
        double best = double.MaxValue;
        int stepA = Math.Max(1, a.Count / 25), stepB = Math.Max(1, b.Count / 25);
        for (int i = 0; i < a.Count; i += stepA)
            for (int j = 0; j < b.Count; j += stepB)
            {
                double d = HaversineMeters(a[i].Lat, a[i].Lng, b[j].Lat, b[j].Lng);
                if (d < best) best = d;
            }

        return best;
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double p1 = lat1 * Math.PI / 180.0, p2 = lat2 * Math.PI / 180.0;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(h));
    }
}
