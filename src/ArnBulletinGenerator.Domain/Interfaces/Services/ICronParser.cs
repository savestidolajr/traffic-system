using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Abstracts cron expression parsing and scheduling calculations.
/// </summary>
public interface ICronParser
{
    /// <summary>
    ///     Parses a cron expression and returns whether it's valid.
    /// </summary>
    bool TryParseCron(string expression);

    /// <summary>
    ///     Calculates the next run time based on a cron expression and timezone, relative to now.
    /// </summary>
    Option<DateTime> CalculateNextRun(string cronExpression, string timezone);

    /// <summary>
    ///     Calculates the next run time based on a cron expression, timezone, and base time.
    /// </summary>
    Option<DateTime> CalculateNextRun(string cronExpression, string timezone, DateTime baseTimeUtc);

    /// <summary>
    ///     Calculates the earliest occurrence strictly after <paramref name="afterUtc" /> and
    ///     strictly before <paramref name="untilUtc" />.
    /// </summary>
    Option<DateTime> CalculateNextRunWithin(
        string cronExpression,
        string timezone,
        DateTime afterUtc,
        DateTime untilUtc)
    {
        if (untilUtc <= afterUtc)
            return Option<DateTime>.None;
        var next = CalculateNextRun(cronExpression, timezone, afterUtc);
        return next.HasValue && next.Value < untilUtc ? next : Option<DateTime>.None;
    }
}
