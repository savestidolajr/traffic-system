using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Cronos;

namespace ArnBulletinGenerator.Infrastructure;

/// <summary>
///     Implementation of ICronParser using Cronos library.
///     Supports compound cron expressions (newline-separated) for mixed-minute schedules.
/// </summary>
public class CronParser(TimeProvider? timeProvider = null) : ICronParser
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public bool TryParseCron(string expression)
    {
        string[] lines = SplitLines(expression);
        if (lines.Length == 0) return false;

        foreach (string line in lines)
            try
            {
                CronExpression.Parse(line);
            }
            catch (CronFormatException)
            {
                return false;
            }

        return true;
    }

    /// <inheritdoc />
    public Option<DateTime> CalculateNextRun(string cronExpression, string timezone)
    {
        return CalculateNextRun(cronExpression, timezone, _timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <inheritdoc />
    public Option<DateTime> CalculateNextRun(string cronExpression, string timezone, DateTime baseTimeUtc)
    {
        return CalculateNextRunInternal(cronExpression, timezone, baseTimeUtc, null);
    }

    /// <inheritdoc />
    public Option<DateTime> CalculateNextRunWithin(
        string cronExpression,
        string timezone,
        DateTime afterUtc,
        DateTime untilUtc)
    {
        if (untilUtc <= afterUtc)
            return Option<DateTime>.None;
        return CalculateNextRunInternal(cronExpression, timezone, afterUtc, untilUtc);
    }

    private static Option<DateTime> CalculateNextRunInternal(
        string cronExpression,
        string timezone,
        DateTime afterUtc,
        DateTime? untilUtc)
    {
        try
        {
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            DateTime? earliest = null;

            foreach (string line in SplitLines(cronExpression))
            {
                var cron = CronExpression.Parse(line);
                var next = cron.GetNextOccurrence(afterUtc, timeZoneInfo);
                if (next.HasValue
                    && (untilUtc is null || next.Value < untilUtc.Value)
                    && (earliest is null || next.Value < earliest.Value))
                    earliest = next.Value;
            }

            return Option.OfNullable(earliest);
        }
        catch (Exception ex) when (ex is CronFormatException or TimeZoneNotFoundException)
        {
            return Option<DateTime>.None;
        }
    }

    private static string[] SplitLines(string expression)
    {
        return expression.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
