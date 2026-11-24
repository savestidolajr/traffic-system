using Shouldly;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class TimeZoneHelperTests
{
    [Fact]
    public void GetLocalDayStartUtc_ReturnsLocalMidnightConvertedToUtc()
    {
        // 2025-06-15 10:00 UTC → Melbourne is UTC+10 (AEST, no DST in June)
        // Local = 2025-06-15 20:00 → midnight local = 2025-06-15 00:00 AEST = 2025-06-14 14:00 UTC
        var nowUtc = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");

        var result = TimeZoneHelper.GetLocalDayStartUtc(nowUtc, tz);

        result.ShouldBe(new DateTime(2025, 6, 14, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetLocalDayStartUtc_Utc_ReturnsMidnightSameDay()
    {
        var nowUtc = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);

        var result = TimeZoneHelper.GetLocalDayStartUtc(nowUtc, TimeZoneInfo.Utc);

        result.ShouldBe(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetLocalDayStartUtc_AustraliaMelbourne_HandlesOffset()
    {
        // During AEDT (DST, UTC+11): 2025-01-15 02:00 UTC → 2025-01-15 13:00 AEDT
        // Midnight local = 2025-01-15 00:00 AEDT = 2025-01-14 13:00 UTC
        var nowUtc = new DateTime(2025, 1, 15, 2, 0, 0, DateTimeKind.Utc);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");

        var result = TimeZoneHelper.GetLocalDayStartUtc(nowUtc, tz);

        result.ShouldBe(new DateTime(2025, 1, 14, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetLocalDayWindowUtc_AustralianMorning_SpansPreviousUtcDay()
    {
        // The exact failing scenario: a bulletin generated at 2026-06-30 00:08 UTC is, in Sydney
        // (AEST, UTC+10), 2026-06-30 10:08 — so "today" runs from 2026-06-29 14:00 UTC to
        // 2026-06-30 14:00 UTC. A prior same-day bulletin created at 2026-06-29 23:56 UTC
        // (09:56 Sydney) MUST fall inside that window; the old UTC-midnight-of-local-date window
        // ([2026-06-30 00:00, 2026-07-01 00:00) UTC) wrongly excluded it.
        var referenceUtc = new DateTime(2026, 6, 30, 0, 8, 0, DateTimeKind.Utc);

        var (start, end) = TimeZoneHelper.GetLocalDayWindowUtc(referenceUtc, "Australia/Sydney");

        start.ShouldBe(new DateTime(2026, 6, 29, 14, 0, 0, DateTimeKind.Utc));
        end.ShouldBe(new DateTime(2026, 6, 30, 14, 0, 0, DateTimeKind.Utc));

        var priorBulletinCreatedAt = new DateTime(2026, 6, 29, 23, 56, 54, DateTimeKind.Utc);
        (priorBulletinCreatedAt >= start && priorBulletinCreatedAt < end).ShouldBeTrue();
    }

    [Fact]
    public void GetLocalDayWindowUtc_UnresolvableTimezone_FallsBackToUtcDay()
    {
        var referenceUtc = new DateTime(2026, 6, 30, 0, 8, 0, DateTimeKind.Utc);

        var (start, end) = TimeZoneHelper.GetLocalDayWindowUtc(referenceUtc, "Invalid/Timezone");

        start.ShouldBe(new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc));
        end.ShouldBe(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryResolve_ValidTimezone_ReturnsSuccess()
    {
        var result = TimeZoneHelper.TryResolve("Australia/Melbourne");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe("Australia/Melbourne");
    }

    [Fact]
    public void TryResolve_Null_ReturnsFailure()
    {
        var result = TimeZoneHelper.TryResolve(null!);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("null or empty");
    }

    [Fact]
    public void TryResolve_EmptyString_ReturnsFailure()
    {
        var result = TimeZoneHelper.TryResolve("");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("null or empty");
    }

    [Fact]
    public void TryResolve_InvalidTimezone_ReturnsFailure()
    {
        var result = TimeZoneHelper.TryResolve("Invalid/Timezone");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }
}
