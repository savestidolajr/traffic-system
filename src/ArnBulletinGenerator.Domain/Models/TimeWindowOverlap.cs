namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Overlap detection for half-open station-local time windows that may wrap past midnight.
///     Shared by every per-station time-of-day selection policy (voice rotation, bookend rotation)
///     so the wrap-around arithmetic — the one part of window validation that is easy to get
///     subtly wrong — exists exactly once.
/// </summary>
public static class TimeWindowOverlap
{
    /// <summary>Minutes in a day; the circle window ranges are projected onto.</summary>
    private const int MinutesPerDay = 1440;

    /// <summary>
    ///     True when any two distinct windows intersect. Each window is expanded to half-open
    ///     minute ranges on a 1440-minute circle (a midnight wrap becomes two ranges), then every
    ///     cross-window range pair is checked for intersection.
    /// </summary>
    public static bool HasOverlap(IReadOnlyList<(TimeOnly Start, TimeOnly End)> windows)
    {
        var ranges = new List<(int Lo, int Hi, int Window)>();
        for (int i = 0; i < windows.Count; i++)
            foreach (var r in ToMinuteRanges(windows[i].Start, windows[i].End))
                ranges.Add((r.Lo, r.Hi, i));

        for (int a = 0; a < ranges.Count; a++)
            for (int b = a + 1; b < ranges.Count; b++)
                if (ranges[a].Window != ranges[b].Window &&
                    ranges[a].Lo < ranges[b].Hi && ranges[b].Lo < ranges[a].Hi)
                    return true;
        return false;
    }

    /// <summary>
    ///     True when the half-open window <c>[start, end)</c> covers the given station-local time.
    ///     <c>end &lt;= start</c> wraps past midnight (e.g. 22:00–06:00).
    /// </summary>
    public static bool Contains(TimeOnly start, TimeOnly end, TimeOnly localNow) =>
        start <= end
            ? localNow >= start && localNow < end // same-day window
            : localNow >= start || localNow < end; // wraps midnight

    private static IEnumerable<(int Lo, int Hi)> ToMinuteRanges(TimeOnly start, TimeOnly end)
    {
        int lo = start.Hour * 60 + start.Minute;
        int hi = end.Hour * 60 + end.Minute;
        if (lo < hi)
        {
            yield return (lo, hi);
        }
        else
        {
            yield return (lo, MinutesPerDay); // start .. midnight
            yield return (0, hi); // midnight .. end
        }
    }
}
