using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public sealed class ScheduleCalendarTests
{
    private static SchedulePattern Pattern(string id = "weekday", string cron = "0 8 * * *") =>
        new(id, "Weekday", cron);

    private static ScheduleAssignment Assignment(
        string start = "2026-12-25", string end = "2026-12-25", string? patternId = "weekday",
        bool suppress = false) =>
        new(DateOnly.Parse(start), DateOnly.Parse(end), patternId, suppress, null);

    [Fact]
    public void Create_BlankPatternId_Fails() =>
        ScheduleCalendar.Create([Pattern(" ")], []).Error.ShouldContain("id must not be blank");

    [Fact]
    public void Create_InvalidPatternId_Fails() =>
        ScheduleCalendar.Create([Pattern("bad id")], []).Error.ShouldContain("invalid");

    [Fact]
    public void Create_DuplicatePatternIdsAreCaseInsensitive_Fails() =>
        ScheduleCalendar.Create([Pattern("Holiday"), Pattern("holiday")], []).Error.ShouldContain("unique");

    [Fact]
    public void Create_BlankPatternName_Fails() =>
        ScheduleCalendar.Create([new SchedulePattern("holiday", " ", "0 8 * * *")], []).Error
            .ShouldContain("name must not be blank");

    [Fact]
    public void Create_BlankPatternCron_Fails() =>
        ScheduleCalendar.Create([Pattern("holiday", " ")], []).Error.ShouldContain("cron expression");

    [Fact]
    public void Create_BackwardsAssignment_Fails() =>
        ScheduleCalendar.Create([Pattern()], [Assignment("2026-12-26", "2026-12-25")]).Error
            .ShouldContain("before");

    [Fact]
    public void Create_AssignmentWithoutPatternOrSuppression_Fails() =>
        ScheduleCalendar.Create([Pattern()], [Assignment(patternId: null)]).Error.ShouldContain("PatternId");

    [Fact]
    public void Create_AssignmentWithPatternAndSuppression_Fails() =>
        ScheduleCalendar.Create([Pattern()], [Assignment(suppress: true)]).Error.ShouldContain("both");

    [Fact]
    public void Create_UnknownPatternReference_Fails() =>
        ScheduleCalendar.Create([Pattern()], [Assignment(patternId: "missing")]).Error.ShouldContain("unknown");

    [Fact]
    public void Create_ExceedsPatternCap_Fails() =>
        ScheduleCalendar.Create(Enumerable.Range(0, ScheduleCalendar.MaxPatterns + 1)
            .Select(i => Pattern($"p{i}")).ToList(), []).Error.ShouldContain("patterns");

    [Fact]
    public void Create_ExceedsAssignmentCap_Fails() =>
        ScheduleCalendar.Create([Pattern()], Enumerable.Range(0, ScheduleCalendar.MaxAssignments + 1)
            .Select(_ => Assignment("2026-01-01", "2026-12-31")).ToList()).Error.ShouldContain("assignments");

    [Fact]
    public void ResolveDay_NoMatch_UsesBaseExpression()
    {
        var calendar = ScheduleCalendar.Create([Pattern()], []).Value;

        var resolution = calendar.ResolveDay(new DateOnly(2026, 1, 1));

        resolution.Suppressed.ShouldBeFalse();
        resolution.CronExpression.ShouldBeNull();
        resolution.PatternId.ShouldBeNull();
    }

    [Fact]
    public void ResolveDay_SingleMatch_UsesPattern()
    {
        var calendar = ScheduleCalendar.Create(
            [Pattern("holiday", "0 10 * * *")],
            [Assignment("2026-12-25", patternId: "holiday")]).Value;

        var resolution = calendar.ResolveDay(new DateOnly(2026, 12, 25));

        resolution.CronExpression.ShouldBe("0 10 * * *");
        resolution.PatternId.ShouldBe("holiday");
        resolution.PatternName.ShouldBe("Weekday");
    }

    [Fact]
    public void ResolveDay_OverlappingWindows_ShortestWins()
    {
        var calendar = ScheduleCalendar.Create(
            [Pattern("long", "0 9 * * *"), Pattern("short", "0 10 * * *")],
            [
                Assignment("2026-12-01", "2026-12-31", "long"),
                Assignment("2026-12-24", "2026-12-26", "short")
            ]).Value;

        calendar.ResolveDay(new DateOnly(2026, 12, 25)).PatternId.ShouldBe("short");
    }

    [Fact]
    public void ResolveDay_EqualLengthTieLaterAssignmentWins()
    {
        var calendar = ScheduleCalendar.Create(
            [Pattern("first", "0 9 * * *"), Pattern("second", "0 10 * * *")],
            [
                Assignment("2026-12-24", "2026-12-26", "first"),
                Assignment("2026-12-24", "2026-12-26", "second")
            ]).Value;

        calendar.ResolveDay(new DateOnly(2026, 12, 25)).PatternId.ShouldBe("second");
    }

    [Fact]
    public void ResolveDay_SuppressionWinsWhenSelected()
    {
        var calendar = ScheduleCalendar.Create(
            [Pattern()],
            [Assignment(patternId: null, suppress: true)]).Value;

        calendar.ResolveDay(new DateOnly(2026, 12, 25)).Suppressed.ShouldBeTrue();
    }
}
