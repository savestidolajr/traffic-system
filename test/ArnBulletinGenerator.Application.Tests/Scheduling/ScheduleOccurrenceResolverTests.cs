using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Scheduling;

public sealed class ScheduleOccurrenceResolverTests
{
    private static MarketSchedule Schedule(string cron = "base", string timezone = "UTC") =>
        MarketSchedule.Create(
            "market", "Market", "Station", timezone, cron, true,
            CoverageArea.Create(0, 0, 1).Value, "voice", "/output", 60, 15, 5,
            DateTime.UnixEpoch, DateTime.UnixEpoch).Value;

    private static ScheduleCalendar Calendar(params ScheduleAssignment[] assignments) =>
        ScheduleCalendar.Create(
            [
                new SchedulePattern("holiday", "Holiday", "holiday"),
                new SchedulePattern("base", "Base", "base")
            ], assignments).Value;

    [Fact]
    public void NoCalendar_DelegatesToLegacyParserUnchanged()
    {
        var parser = Substitute.For<ICronParser>();
        var after = new DateTime(2026, 1, 1, 1, 2, 3, DateTimeKind.Utc);
        var expected = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        parser.CalculateNextRun("base", "UTC", after).Returns(expected);
        var schedule = Schedule();

        var actual = new ScheduleOccurrenceResolver(parser, NullLogger.Instance).NextRunUtc(schedule, after);

        actual.Value.ShouldBe(expected);
        parser.Received(1).CalculateNextRun("base", "UTC", after);
        parser.DidNotReceiveWithAnyArgs().CalculateNextRunWithin(default!, default!, default, default);
    }

    [Fact]
    public void HolidayDate_UsesHolidayPattern()
    {
        var parser = new WindowParser(new Dictionary<string, DateTime>
        {
            ["holiday"] = new(2026, 12, 25, 10, 0, 0, DateTimeKind.Utc)
        });
        var schedule = Schedule();
        schedule.Calendar = Calendar(new ScheduleAssignment(
            new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 25), "holiday", false, null));

        var actual = new ScheduleOccurrenceResolver(parser, NullLogger.Instance).NextRunUtc(
            schedule, new DateTime(2026, 12, 24, 0, 0, 0, DateTimeKind.Utc));

        actual.Value.ShouldBe(new DateTime(2026, 12, 25, 10, 0, 0, DateTimeKind.Utc));
        parser.Expressions.ShouldContain("holiday");
    }

    [Fact]
    public void SuppressedDate_IsSkippedAndNextDayIsReturned()
    {
        var parser = new WindowParser(new Dictionary<string, DateTime>
        {
            ["base"] = new(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc)
        });
        var schedule = Schedule();
        schedule.Calendar = Calendar(new ScheduleAssignment(
            new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), null, true, null));

        var actual = new ScheduleOccurrenceResolver(parser, NullLogger.Instance).NextRunUtc(
            schedule, new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc));

        actual.Value.ShouldBe(new DateTime(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc));
        parser.Expressions.ShouldNotContain("holiday");
    }

    [Fact]
    public void AllSuppressedCalendar_TerminatesWithNone()
    {
        var parser = new WindowParser(new Dictionary<string, DateTime>());
        var schedule = Schedule();
        schedule.Calendar = Calendar(new ScheduleAssignment(
            new DateOnly(2026, 1, 1), new DateOnly(2027, 2, 4), null, true, null));

        var actual = new ScheduleOccurrenceResolver(parser, NullLogger.Instance).NextRunUtc(
            schedule, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        actual.IsNone.ShouldBeTrue();
        parser.Expressions.ShouldBeEmpty();
    }

    [Fact]
    public void DstTransitionDay_UsesLocalDayWindow()
    {
        var expected = new DateTime(2026, 10, 3, 21, 0, 0, DateTimeKind.Utc);
        var parser = new WindowParser(new Dictionary<string, DateTime> { ["base"] = expected });
        var schedule = Schedule("base", "Australia/Sydney");
        schedule.Calendar = Calendar();

        var actual = new ScheduleOccurrenceResolver(parser, NullLogger.Instance).NextRunUtc(
            schedule, new DateTime(2026, 10, 3, 13, 0, 0, DateTimeKind.Utc));

        actual.Value.ShouldBe(expected);
        parser.Windows[0].Start.ShouldBe(new DateTime(2026, 10, 3, 13, 0, 0, DateTimeKind.Utc));
        parser.Windows[0].End.ShouldBe(new DateTime(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    ///     Cronos treats its `from` as EXCLUSIVE, so walking to a later day with that day's first
    ///     instant as the lower bound silently skipped a slot sitting exactly on local midnight.
    ///     Here day 0 is suppressed and the only slot on day 1 is 00:00 local: it must be returned.
    /// </summary>
    [Fact]
    public void MidnightSlotOnAWalkedDay_IsNotSkipped()
    {
        var midnight = new DateTime(2027, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var parser = new WindowParser(new Dictionary<string, DateTime> { ["base"] = midnight });
        var schedule = Schedule("base");
        schedule.Calendar = Calendar(new ScheduleAssignment(
            new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 1), null, true, "Suppressed"));

        var actual = new ScheduleOccurrenceResolver(parser, NullLogger.Instance).NextRunUtc(
            schedule, new DateTime(2027, 3, 1, 12, 0, 0, DateTimeKind.Utc));

        actual.Value.ShouldBe(midnight);
        // The day-1 probe's lower bound sits one tick BEFORE local midnight, never on it.
        parser.Windows[0].Start.ShouldBe(midnight.AddTicks(-1));
    }

    private sealed class WindowParser(IReadOnlyDictionary<string, DateTime> candidates) : ICronParser
    {
        public List<string> Expressions { get; } = [];
        public List<(DateTime Start, DateTime End)> Windows { get; } = [];

        public bool TryParseCron(string expression) => true;
        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone) => Option<DateTime>.None;
        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone, DateTime baseTimeUtc) =>
            Option<DateTime>.None;

        public Option<DateTime> CalculateNextRunWithin(string cronExpression, string timezone,
            DateTime afterUtc, DateTime untilUtc)
        {
            Expressions.Add(cronExpression);
            Windows.Add((afterUtc, untilUtc));
            return candidates.TryGetValue(cronExpression, out var candidate)
                && candidate > afterUtc && candidate < untilUtc
                ? candidate
                : Option<DateTime>.None;
        }
    }
}
