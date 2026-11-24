using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The window classifier is the whole freshness contract: Scheduled means a closure claim
///     demotes to roadworks wording, Fresh means it may air as news. The re-issue arm exists
///     because Mitchell Rd (prod, pilot day 2, 2026-08-18 06:04) re-entered HERE's feed with a
///     months-old start and aired as closure news.
/// </summary>
public sealed class IncidentWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 6, 30, 0, TimeSpan.Zero);

    private static WindowClass Classify(
        double startedHoursAgo, double windowDays, double? enteredHoursAgo) =>
        IncidentWindow.Classify(
            Now.AddHours(-startedHoursAgo),
            Now.AddHours(-startedHoursAgo).AddDays(windowDays),
            enteredHoursAgo is { } e ? Now.AddHours(-e) : null,
            Now);

    [Fact]
    public void ReissuedWorksiteClosure_FreshEntryOldStartLongWindow_IsScheduledNotFresh()
    {
        // The Mitchell Rd shape: worksite started months ago, window runs for months more,
        // HERE re-published the record 26 minutes ago. The road did not change; the feed did.
        Classify(startedHoursAgo: 2000, windowDays: 90, enteredHoursAgo: 0.44)
            .ShouldBe(WindowClass.Scheduled);
    }

    [Fact]
    public void NewSubEventInsideLongWorksite_RecentStart_IsStillFresh()
    {
        // The documented counter-case the entry arm was built for: a lane genuinely closed an
        // hour ago inside a year-long project carries a NEW record with a recent start.
        Classify(startedHoursAgo: 1, windowDays: 365, enteredHoursAgo: 0.5)
            .ShouldBe(WindowClass.Fresh);
    }

    [Fact]
    public void LatePublishedShortClosure_OldStartShortWindow_KeepsEntryFreshness()
    {
        // A 3-day closure that started two days ago but only just reached the feed IS news to
        // listeners — entry freshness stands when the record is not a long-planned worksite.
        Classify(startedHoursAgo: 48, windowDays: 3, enteredHoursAgo: 1)
            .ShouldBe(WindowClass.Fresh);
    }

    [Fact]
    public void OldShortWindowRecord_NoRecentEntry_IsOngoing()
    {
        Classify(startedHoursAgo: 48, windowDays: 5, enteredHoursAgo: 40)
            .ShouldBe(WindowClass.Ongoing);
    }

    [Fact]
    public void LongPlannedRecord_NoRecentEntry_IsScheduled()
    {
        Classify(startedHoursAgo: 500, windowDays: 90, enteredHoursAgo: 400)
            .ShouldBe(WindowClass.Scheduled);
    }

    [Fact]
    public void MissingWindow_IsUnknown_NeverGuessed()
    {
        IncidentWindow.Classify(null, Now.AddDays(1), Now.AddHours(-1), Now)
            .ShouldBe(WindowClass.Unknown);
        IncidentWindow.Classify(Now.AddHours(-1), null, Now.AddHours(-1), Now)
            .ShouldBe(WindowClass.Unknown);
    }
}
