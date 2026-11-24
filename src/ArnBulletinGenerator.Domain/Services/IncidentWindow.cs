namespace ArnBulletinGenerator.Domain.Services;

/// <summary>How current a HERE incident is, from its own <c>startTime</c>/<c>endTime</c> window.</summary>
public enum WindowClass
{
    /// <summary>HERE stated no window. Nothing may be inferred about the record's age.</summary>
    Unknown,

    /// <summary>Started within a day, or entered the feed within three hours — this is news.</summary>
    Fresh,

    /// <summary>A window of a week or less: a real, current disruption that is not new.</summary>
    Ongoing,

    /// <summary>A long-term planned condition — a worksite, not a traffic event.</summary>
    Scheduled
}

/// <summary>
///     Classifies HERE's incident validity window.
///     <para>
///         Both <c>startTime</c> and <c>endTime</c> are present on 100% of measured incidents and
///         neither was read before this: a closure scheduled 2025-02-04 → 2026-12-30 (693 days) was
///         presented as this morning's news and collected "still" continuity every cycle. The 7-day
///         boundary is where the measured corpus splits — of 793 records, 143 are under a day, 222 are
///         1–7 days, and 196 are over 90 days with a longest window of 969 days.
///     </para>
/// </summary>
public static class IncidentWindow
{
    /// <summary>Started within this long → <see cref="WindowClass.Fresh" />.</summary>
    private static readonly TimeSpan FreshStart = TimeSpan.FromHours(24);

    /// <summary>Entered HERE's feed within this long → <see cref="WindowClass.Fresh" /> — a lane
    ///  closed an hour ago IS news. Suppressed for re-issues: a record whose own start is already
    ///  more than <see cref="FreshStart" /> old inside a long-planned window gained its recent
    ///  entry from HERE re-publishing an old record, not from anything changing on the road
    ///  (Mitchell Rd, prod 2026-08-18: a months-old worksite closure re-entered the feed at
    ///  06:04 and aired as closure news).</summary>
    private static readonly TimeSpan FreshEntry = TimeSpan.FromHours(3);

    /// <summary>A window at or below this length is a current disruption rather than a worksite.</summary>
    private static readonly TimeSpan OngoingWindow = TimeSpan.FromDays(IncidentCurrencyPolicy.LongTermWindowDays);

    /// <summary>
    ///     Classifies the record's window. Total: a missing <c>startTime</c> or <c>endTime</c> yields
    ///     <see cref="WindowClass.Unknown" /> rather than a guess.
    ///     <para>
    ///         Entry freshness is folded in HERE rather than left to the caller, so
    ///         <see cref="WindowClass.Scheduled" /> means exactly "long-planned AND not newly reported"
    ///         — a lane HERE published three hours ago inside a year-long worksite is still news
    ///         PROVIDED the record itself is new (recent start). A re-issued old record is not:
    ///         its entry time says the feed changed, its start time says the road did not.
    ///     </para>
    /// </summary>
    public static WindowClass Classify(
        DateTimeOffset? startTime, DateTimeOffset? endTime, DateTimeOffset? entryTime, DateTimeOffset now)
    {
        if (startTime is null || endTime is null)
            return WindowClass.Unknown;

        bool startedToday = now >= startTime.Value && now - startTime.Value <= FreshStart;
        bool justEntered = entryTime.HasValue && now >= entryTime.Value && now - entryTime.Value <= FreshEntry;
        bool longPlanned = endTime.Value - startTime.Value > OngoingWindow;

        // The re-entry loophole, closed: entry freshness may not overrule the record's own
        // evidence. An old start inside a long-planned window is a worksite whether or not HERE
        // re-issued the record five minutes ago. A genuinely new sub-event carries a new record
        // with a recent start and is caught by startedToday.
        bool reissuedWorksite = longPlanned && !startedToday;
        if (startedToday || (justEntered && !reissuedWorksite))
            return WindowClass.Fresh;

        return endTime.Value - startTime.Value <= OngoingWindow
            ? WindowClass.Ongoing
            : WindowClass.Scheduled;
    }
}
