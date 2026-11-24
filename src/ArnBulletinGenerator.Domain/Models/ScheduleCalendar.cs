using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>A reusable named cron pattern for a station's calendar.</summary>
public sealed record SchedulePattern(string Id, string Name, string CronExpression);

/// <summary>A station-local inclusive date assignment to a calendar pattern or suppression.</summary>
public sealed record ScheduleAssignment(
    DateOnly StartDate,
    DateOnly EndDate,
    string? PatternId,
    bool Suppress,
    string? Note);

/// <summary>
///     Optional calendar-year overlay for a market schedule. A small bounded set of named patterns
///     and date windows keeps the stored model reviewable while allowing public holidays, ratings
///     periods and other station-local exceptions. The base schedule remains authoritative whenever
///     no assignment covers a date, preserving the behaviour of rows written before calendars.
/// </summary>
public sealed record ScheduleCalendar(
    IReadOnlyList<SchedulePattern> Patterns,
    IReadOnlyList<ScheduleAssignment> Assignments)
{
    public const int MaxPatterns = 24;
    public const int MaxAssignments = 400;

    private static readonly Regex Identifier = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Validates a new calendar at the domain admission boundary. Cron syntax deliberately does
    ///     not belong here: the domain project has no Cronos dependency, so application validation
    ///     must run the same parser and interval checks as the base schedule.
    /// </summary>
    public static Result<ScheduleCalendar> Create(
        IReadOnlyList<SchedulePattern>? patterns,
        IReadOnlyList<ScheduleAssignment>? assignments)
    {
        if (patterns is null)
            return Result<ScheduleCalendar>.Failure("Schedule calendar patterns are required.");
        if (assignments is null)
            return Result<ScheduleCalendar>.Failure("Schedule calendar assignments are required.");
        if (patterns.Count > MaxPatterns)
            return Result<ScheduleCalendar>.Failure($"Schedule calendar cannot contain more than {MaxPatterns} patterns.");
        if (assignments.Count > MaxAssignments)
            return Result<ScheduleCalendar>.Failure($"Schedule calendar cannot contain more than {MaxAssignments} assignments.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            if (pattern is null)
                return Result<ScheduleCalendar>.Failure("Schedule calendar patterns must not contain null entries.");
            if (string.IsNullOrWhiteSpace(pattern.Id))
                return Result<ScheduleCalendar>.Failure("Schedule pattern id must not be blank.");
            var id = pattern.Id.Trim();
            if (!Identifier.IsMatch(id))
                return Result<ScheduleCalendar>.Failure(
                    $"Schedule pattern id '{pattern.Id}' is invalid; use letters, numbers, '-' or '_' only.");
            if (!ids.Add(id))
                return Result<ScheduleCalendar>.Failure(
                    $"Schedule pattern id '{pattern.Id}' must be unique (case-insensitive).");
            if (string.IsNullOrWhiteSpace(pattern.Name))
                return Result<ScheduleCalendar>.Failure($"Schedule pattern '{pattern.Id}' name must not be blank.");
            if (string.IsNullOrWhiteSpace(pattern.CronExpression))
                return Result<ScheduleCalendar>.Failure(
                    $"Schedule pattern '{pattern.Id}' cron expression must not be blank.");
        }

        foreach (var assignment in assignments)
        {
            if (assignment is null)
                return Result<ScheduleCalendar>.Failure("Schedule calendar assignments must not contain null entries.");
            if (assignment.EndDate < assignment.StartDate)
                return Result<ScheduleCalendar>.Failure("Schedule assignment end date must not be before its start date.");

            bool hasPattern = !string.IsNullOrWhiteSpace(assignment.PatternId);
            if (hasPattern && assignment.Suppress)
                return Result<ScheduleCalendar>.Failure(
                    "Schedule assignment cannot set both PatternId and Suppress.");
            if (!hasPattern && !assignment.Suppress)
                return Result<ScheduleCalendar>.Failure(
                    "Schedule assignment must set a PatternId or Suppress=true.");
            if (hasPattern && !ids.Contains(assignment.PatternId!.Trim()))
                return Result<ScheduleCalendar>.Failure(
                    $"Schedule assignment references unknown pattern '{assignment.PatternId}'.");
        }

        return Result<ScheduleCalendar>.Success(new ScheduleCalendar(patterns, assignments));
    }

    /// <summary>
    ///     Resolves one station-local date. Overlapping assignments are deterministic: the shortest
    ///     inclusive window wins, and an exact tie is resolved by the later list entry. A date with
    ///     no assignment returns a non-suppressed resolution with no cron, meaning the base cron
    ///     expression must be used by the occurrence resolver.
    /// </summary>
    public ScheduleDayResolution ResolveDay(DateOnly localDate)
    {
        ScheduleAssignment? selected = null;
        int selectedLength = int.MaxValue;
        for (int i = 0; i < Assignments.Count; i++)
        {
            var assignment = Assignments[i];
            if (localDate < assignment.StartDate || localDate > assignment.EndDate)
                continue;

            // Shortest window wins; `<=` lets a LATER entry take an exact tie, so the most
            // recent edit supersedes an earlier one of the same span. Written as one
            // comparison on purpose: `length < x || length == x` reads like a defect.
            int length = assignment.EndDate.DayNumber - assignment.StartDate.DayNumber;
            if (selected is null || length <= selectedLength)
            {
                selected = assignment;
                selectedLength = length;
            }
        }

        if (selected is null)
            return new ScheduleDayResolution(false, null, null, null);
        if (selected.Suppress)
            return new ScheduleDayResolution(true, null, null, null);

        var pattern = Patterns.FirstOrDefault(p =>
            string.Equals(p.Id, selected.PatternId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return pattern is null
            ? new ScheduleDayResolution(false, null, selected.PatternId, null)
            : new ScheduleDayResolution(false, pattern.CronExpression, pattern.Id, pattern.Name);
    }
}

/// <summary>Resolved calendar policy for one station-local date.</summary>
public readonly record struct ScheduleDayResolution(
    bool Suppressed,
    string? CronExpression,
    string? PatternId,
    string? PatternName);
