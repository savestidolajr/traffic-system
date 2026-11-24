using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Shared timezone utilities for calculating local-day boundaries in UTC.
/// </summary>
public static class TimeZoneHelper
{
    /// <summary>
    ///     Returns the UTC instant corresponding to midnight (start of day) in the given timezone.
    /// </summary>
    public static DateTime GetLocalDayStartUtc(DateTime nowUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var startLocal = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone);
    }

    /// <summary>
    ///     Returns the [start, end) UTC window for the local calendar day that contains
    ///     <paramref name="referenceUtc" /> in <paramref name="timezoneId" />. An unresolvable
    ///     timezone falls back to the UTC calendar day. Use this — never a UTC-midnight window
    ///     built from a local date — when filtering "same local day" rows by a UTC timestamp.
    /// </summary>
    public static (DateTime DayStartUtc, DateTime DayEndUtc) GetLocalDayWindowUtc(
        DateTime referenceUtc, string timezoneId)
    {
        return TryResolve(timezoneId).Match(
            tz =>
            {
                var start = GetLocalDayStartUtc(referenceUtc, tz);
                return (start, start.AddDays(1));
            },
            _ => (referenceUtc.Date, referenceUtc.Date.AddDays(1)));
    }

    /// <summary>
    ///     Returns the UTC [start, end) window for a specific station-local date. Converting both
    ///     local midnights independently is required on DST transition days, whose UTC duration is
    ///     23 or 25 hours rather than a fixed 24-hour <c>AddDays(1)</c>.
    /// </summary>
    public static (DateTime DayStartUtc, DateTime DayEndUtc) GetLocalDayWindowUtc(
        DateOnly localDate, string timezoneId)
    {
        return TryResolve(timezoneId).Match(
            tz =>
            {
                var startLocal = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                var endLocal = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                return (TimeZoneInfo.ConvertTimeToUtc(startLocal, tz),
                    TimeZoneInfo.ConvertTimeToUtc(endLocal, tz));
            },
            _ =>
            {
                var start = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                return (start, start.AddDays(1));
            });
    }

    /// <summary>
    ///     Returns the station-local calendar date and clock time for a UTC instant, resolving
    ///     <paramref name="timezoneId" /> with the SAME fallback as
    ///     <see cref="GetLocalDayWindowUtc" />: an absent or unresolvable timezone degrades to
    ///     UTC. Used at the load boundaries where station-local eligibility is evaluated (filler
    ///     availability windows) — callers with a never-fail-closed contract rely on this never
    ///     throwing for any input, so resolution failures must route to the UTC branch, never
    ///     escape.
    /// </summary>
    public static (DateOnly LocalDate, TimeOnly LocalTime) GetLocalDateAndTime(
        DateTime utcNow, Option<string> timezoneId)
    {
        var local = timezoneId.Match(
            id => TryResolve(id).Match(
                tz => TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz),
                _ => utcNow),
            () => utcNow);
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    /// <summary>
    ///     Safely resolves a timezone by ID, returning a Result failure on invalid or unknown IDs.
    /// </summary>
    internal static Result<TimeZoneInfo> TryResolve(string timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return Result<TimeZoneInfo>.Failure("Timezone ID is null or empty");

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            return Result<TimeZoneInfo>.Failure($"Timezone '{timezoneId}' not found: {ex.Message}");
        }
        catch (InvalidTimeZoneException ex)
        {
            return Result<TimeZoneInfo>.Failure($"Timezone '{timezoneId}' is invalid: {ex.Message}");
        }
    }
}
