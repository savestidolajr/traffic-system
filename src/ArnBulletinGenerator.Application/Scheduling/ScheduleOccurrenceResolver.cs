using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Resolves the next schedule occurrence, applying an optional station-local calendar while
///     leaving schedules without one on the exact legacy ICronParser path.
/// </summary>
public sealed class ScheduleOccurrenceResolver(
    ICronParser cronParser,
    ILogger logger) : IScheduleOccurrenceResolver
{
    private const int MaxCalendarDays = 400;

    public Option<DateTime> NextRunUtc(MarketSchedule schedule, DateTime afterUtc)
    {
        if (schedule.Calendar.IsNone)
            return cronParser.CalculateNextRun(schedule.CronExpression, schedule.Timezone, afterUtc);

        var calendar = schedule.Calendar.Value;
        var (localDate, _) = TimeZoneHelper.GetLocalDateAndTime(
            afterUtc, Option<string>.Some(schedule.Timezone));

        for (int dayOffset = 0; dayOffset < MaxCalendarDays; dayOffset++)
        {
            DateOnly day;
            try
            {
                day = localDate.AddDays(dayOffset);
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }

            var resolution = calendar.ResolveDay(day);
            if (resolution.Suppressed)
                continue;

            string cronExpression = resolution.CronExpression ?? schedule.CronExpression;
            if (resolution.PatternId is not null && resolution.CronExpression is null)
            {
                logger.LogWarning(
                    "Schedule {MarketId} calendar assignment references missing pattern {PatternId}; "
                    + "falling back to the base cron expression.",
                    schedule.MarketId, resolution.PatternId);
            }

            var (dayStartUtc, dayEndUtc) = TimeZoneHelper.GetLocalDayWindowUtc(day, schedule.Timezone);
            // Cronos treats its `from` as EXCLUSIVE, so passing the day's first instant would
            // skip a slot sitting exactly on local midnight. Step back one tick when the day
            // boundary is the binding bound; `afterUtc` keeps its exclusive meaning, which is
            // what stops the poller re-firing the occurrence it just dispatched.
            var lowerBound = afterUtc > dayStartUtc ? afterUtc : dayStartUtc.AddTicks(-1);
            var occurrence = cronParser.CalculateNextRunWithin(
                cronExpression, schedule.Timezone, lowerBound, dayEndUtc);
            if (occurrence.HasValue)
                return occurrence;
        }

        return Option<DateTime>.None;
    }
}
