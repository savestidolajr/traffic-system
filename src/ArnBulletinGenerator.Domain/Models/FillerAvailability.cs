namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The optional station-local eligibility window of a filler statement: a calendar-date
///     range, a day-of-week set, and a time-of-day window, each independently optional. Any
///     part omitted is unconstrained, so <c>null</c> on <see cref="FillerStatement.Availability" />
///     (no window at all) means the statement is ALWAYS eligible — the exact behaviour of every
///     record persisted before this type existed.
///     <para>
///         All bounds are evaluated in the STATION's local timezone, resolved at the load
///         boundaries (MarketContentFetcher, FallbackAudioPadder) — this type is pure and
///         clock-free by construction, taking the local date/time it is asked about.
///     </para>
///     <para>
///         MIDNIGHT-WRAP INTERACTION: a time window with <c>EndLocal &lt;= StartLocal</c> wraps
///         past midnight (e.g. 22:00–06:00) and reuses <see cref="TimeWindowOverlap" />'s
///         half-open <c>[Start, End)</c> semantics. The window is matched against the local
///         date on which the bulletin AIRS: a broadcast at 23:00 Wednesday matches the wrap
///         window and is checked against Wednesday in <see cref="Days" />, while a broadcast at
///         02:00 Thursday is also inside the same window but is checked against Thursday — so a
///         weeknight wrap needs Thursday in the day set to cover its post-midnight hours.
///     </para>
/// </summary>
public sealed record FillerAvailability(
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<DayOfWeek> Days,      // empty = every day
    TimeOnly? StartLocal,
    TimeOnly? EndLocal)
{
    /// <summary>
    ///     True when the window covers the given station-local date/time. Date bounds are
    ///     inclusive; <see cref="Days" /> is matched against <paramref name="localDate" />'s
    ///     day-of-week (see the type doc for the midnight-wrap interaction); the time window is
    ///     half-open <c>[StartLocal, EndLocal)</c> via <see cref="TimeWindowOverlap" /> so a
    ///     wrap is handled by the one shared primitive every per-station time policy uses.
    ///     A window with no time bounds is active at any time of day.
    /// </summary>
    public bool IsActiveAt(DateOnly localDate, TimeOnly localTime)
    {
        if (StartDate is { } start && localDate < start)
            return false;
        if (EndDate is { } end && localDate > end)
            return false;
        if (Days is { Count: > 0 } && !Days.Contains(localDate.DayOfWeek))
            return false;
        if (StartLocal is { } startTime && EndLocal is { } endTime)
            return TimeWindowOverlap.Contains(startTime, endTime, localTime);
        return true;
    }

    /// <summary>
    ///     Validates and normalises an availability window. The only construction path used by
    ///     the write boundaries (the filler-statements PUT route and the config-bundle import)
    ///     so malformed windows are rejected with a clear message instead of silently stored.
    ///     Rejects an end date before its start date, non-day-of-week values in
    ///     <paramref name="days" /> (possible only via a rogue cast — the wire layer already
    ///     rejects unknown day NAMES), a half-set time window, and a zero-length window where
    ///     start equals end. Normalises <paramref name="days" /> to a deduplicated,
    ///     Sunday-first ordered set. <c>null</c> <paramref name="days" /> is an empty set
    ///     (every day).
    /// </summary>
    public static Result<FillerAvailability> Create(
        DateOnly? startDate,
        DateOnly? endDate,
        IReadOnlyList<DayOfWeek>? days,
        TimeOnly? startLocal,
        TimeOnly? endLocal)
    {
        if (endDate is { } end && startDate is { } start && end < start)
            return Result<FillerAvailability>.Failure(
                "Availability end date must not be before its start date.");

        if (days is not null)
        {
            foreach (var day in days)
                if (day < DayOfWeek.Sunday || day > DayOfWeek.Saturday)
                    return Result<FillerAvailability>.Failure(
                        $"Unknown day-of-week value '{day}'. Use Sunday through Saturday.");
        }

        if (startLocal is null != endLocal is null)
            return Result<FillerAvailability>.Failure(
                "Availability time window needs both a start and end time — set both, or neither.");
        if (startLocal is { } s && endLocal is { } e && s == e)
            return Result<FillerAvailability>.Failure(
                "Availability time window must not start and end at the same time.");

        IReadOnlyList<DayOfWeek> normalized = days is null
            ? []
            : days.Distinct().OrderBy(d => d).ToList();

        return Result<FillerAvailability>.Success(
            new FillerAvailability(startDate, endDate, normalized, startLocal, endLocal));
    }
}
