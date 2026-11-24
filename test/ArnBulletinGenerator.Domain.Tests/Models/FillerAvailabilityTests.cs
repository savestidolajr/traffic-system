using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

/// <summary>
///     Filler availability windows: inclusive date bounds, a day-of-week set and a half-open
///     station-local time window that wraps past midnight. The eligibility semantics here are
///     the STRICT contract the pipeline relies on — a statement outside its window must never
///     air, in the generation path or the fallback padder — and the midnight-wrap × day-of-week
///     interaction is the part that is easy to get subtly wrong, so it gets its own tests.
/// </summary>
public class FillerAvailabilityTests
{
    private static FillerAvailability Window(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        IReadOnlyList<DayOfWeek>? days = null,
        TimeOnly? startLocal = null,
        TimeOnly? endLocal = null) =>
        FillerAvailability.Create(startDate, endDate, days, startLocal, endLocal).Value;

    // ── IsActiveAt: date bounds ─────────────────────────────────────────

    [Fact]
    public void DateBounds_AreInclusive()
    {
        var w = Window(startDate: new DateOnly(2026, 8, 1), endDate: new DateOnly(2026, 11, 30));

        w.IsActiveAt(new DateOnly(2026, 8, 1), new TimeOnly(0, 0)).ShouldBeTrue();
        w.IsActiveAt(new DateOnly(2026, 11, 30), new TimeOnly(23, 59)).ShouldBeTrue();
        w.IsActiveAt(new DateOnly(2026, 7, 31), new TimeOnly(12, 0)).ShouldBeFalse();
        w.IsActiveAt(new DateOnly(2026, 12, 1), new TimeOnly(12, 0)).ShouldBeFalse();
    }

    // ── IsActiveAt: day of week ─────────────────────────────────────────

    [Fact]
    public void DaySet_IsMatchedAgainstTheLocalDate()
    {
        var w = Window(days: [DayOfWeek.Thursday]);

        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(8, 0)).ShouldBeTrue(); // Thursday
        w.IsActiveAt(new DateOnly(2026, 8, 7), new TimeOnly(8, 0)).ShouldBeFalse(); // Friday
    }

    [Fact]
    public void EmptyDaySet_MeansEveryDay()
    {
        var w = Window(days: []);

        w.IsActiveAt(new DateOnly(2026, 8, 1), new TimeOnly(3, 0)).ShouldBeTrue(); // Saturday
        w.IsActiveAt(new DateOnly(2026, 8, 3), new TimeOnly(3, 0)).ShouldBeTrue(); // Monday
    }

    // ── IsActiveAt: time window ─────────────────────────────────────────

    [Fact]
    public void TimeWindow_IsHalfOpen()
    {
        var w = Window(startLocal: new TimeOnly(8, 0), endLocal: new TimeOnly(9, 0));

        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(8, 0)).ShouldBeTrue();
        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(8, 59)).ShouldBeTrue();
        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(9, 0)).ShouldBeFalse();
        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(7, 59)).ShouldBeFalse();
    }

    [Fact]
    public void MidnightWrappingWindow_IsActiveOnEitherSideOfMidnight()
    {
        var w = Window(startLocal: new TimeOnly(22, 0), endLocal: new TimeOnly(6, 0));

        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(23, 0)).ShouldBeTrue();
        w.IsActiveAt(new DateOnly(2026, 8, 7), new TimeOnly(2, 0)).ShouldBeTrue();
        w.IsActiveAt(new DateOnly(2026, 8, 7), new TimeOnly(6, 0)).ShouldBeFalse(); // half-open end
        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(10, 0)).ShouldBeFalse();
    }

    [Fact]
    public void WrappingWindow_DayOfWeekUsesTheAirDateNotTheWindowStartDate()
    {
        // 02:00 Friday is inside the window that STARTED Thursday 22:00, but the day set is
        // evaluated against the air date (Friday), so Thursday-only excludes the 02:00 read.
        var w = Window(
            days: [DayOfWeek.Thursday],
            startLocal: new TimeOnly(22, 0), endLocal: new TimeOnly(6, 0));

        w.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(23, 0)).ShouldBeTrue(); // Thu 23:00
        w.IsActiveAt(new DateOnly(2026, 8, 7), new TimeOnly(2, 0)).ShouldBeFalse(); // Fri 02:00
    }

    // ── Create validation ───────────────────────────────────────────────

    [Fact]
    public void Create_RejectsEndDateBeforeStartDate()
    {
        var result = FillerAvailability.Create(
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 1), null, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("before");
    }

    [Fact]
    public void Create_RejectsNonDayOfWeekValues()
    {
        var result = FillerAvailability.Create(
            null, null, [(DayOfWeek)99], null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("day-of-week");
    }

    [Fact]
    public void Create_RejectsZeroLengthTimeWindow()
    {
        var result = FillerAvailability.Create(
            null, null, null, new TimeOnly(6, 0), new TimeOnly(6, 0));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("same time");
    }

    [Fact]
    public void Create_RejectsHalfSetTimeWindow()
    {
        var startOnly = FillerAvailability.Create(
            null, null, null, new TimeOnly(6, 0), null);
        var endOnly = FillerAvailability.Create(
            null, null, null, null, new TimeOnly(6, 0));

        startOnly.IsFailure.ShouldBeTrue();
        endOnly.IsFailure.ShouldBeTrue();
        startOnly.Error.ShouldContain("both");
    }

    [Fact]
    public void Create_NormalizesDuplicateAndUnsortedDays()
    {
        var result = FillerAvailability.Create(
            null, null, [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday], null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Days.ShouldBe([DayOfWeek.Monday, DayOfWeek.Friday]);
    }

    [Fact]
    public void Create_AcceptsAnAllNullWindow()
    {
        var result = FillerAvailability.Create(null, null, null, null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsActiveAt(new DateOnly(2026, 8, 6), new TimeOnly(13, 0)).ShouldBeTrue();
    }
}
