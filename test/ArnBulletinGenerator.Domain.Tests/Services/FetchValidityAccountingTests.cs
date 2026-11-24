using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class FetchValidityAccountingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 6, 0, 0, TimeSpan.Zero);

    private static TrafficIncident Incident(
        string id, bool closed = false, int windowDays = 1,
        double startedHoursAgo = 200, double enteredHoursAgo = 100,
        string? withheldReason = null) =>
        new(id, "Closed", 1, "Test Road", closed ? "roadClosure" : "construction",
            RoadClosed: closed,
            StartTimeUtc: Now.AddHours(-startedHoursAgo),
            EndTimeUtc: Now.AddHours(-startedHoursAgo).AddDays(windowDays),
            EntryTimeUtc: Now.AddHours(-enteredHoursAgo),
            WithheldReason: withheldReason is null ? Option<string>.None : Option<string>.Some(withheldReason));

    [Fact]
    public void EntryOnlyFreshClosure_IsTheReentryLoopholeTripwire()
    {
        // A closure with an old start inside a long window, re-issued into the feed an hour ago.
        // Enforcement (2026-08-18): IncidentWindow refuses the entry-freshness claim for this
        // shape, so it classifies Scheduled — the tripwire still counts it, as a CATCH.
        var reentry = Incident("re", closed: true, windowDays: 90, startedHoursAgo: 500, enteredHoursAgo: 1);
        // A closure that genuinely started this morning is Fresh on its own merits.
        var genuine = Incident("new", closed: true, windowDays: 90, startedHoursAgo: 2, enteredHoursAgo: 1);

        var counts = FetchValidityAccounting.Count([reentry, genuine], Now);

        counts.Closures.ShouldBe(2);
        counts.EntryOnlyFreshClosures.ShouldBe(1);
        counts.Fresh.ShouldBe(1);
        counts.Scheduled.ShouldBe(1);
    }

    [Fact]
    public void DemotedAndFlagged_AreCountedFromTheBoundaryLedger()
    {
        var counts = FetchValidityAccounting.Count(
            [
                Incident("d", withheldReason: "demoted:scheduled-window"),
                Incident("f", withheldReason: "flagged:identity-unnamed"),
                Incident("k")
            ], Now);

        counts.Records.ShouldBe(3);
        counts.DemotedClosures.ShouldBe(1);
        counts.Flagged.ShouldBe(1);
    }

    [Fact]
    public void WindowClasses_AreCountedWithTheGateRules()
    {
        var counts = FetchValidityAccounting.Count(
            [
                Incident("s", windowDays: 90),                       // long window, old entry -> Scheduled
                Incident("o", windowDays: 2),                        // short window -> Ongoing
                Incident("f", windowDays: 2, startedHoursAgo: 3)     // started today -> Fresh
            ], Now);

        counts.Scheduled.ShouldBe(1);
        counts.Ongoing.ShouldBe(1);
        counts.Fresh.ShouldBe(1);
    }
}
