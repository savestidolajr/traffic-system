using System.Text.Json;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests.Scheduling;

/// <summary>
///     Proves the calendar survives the WHOLE path the console actually drives, with the
///     real Cronos-backed <see cref="CronParser" /> rather than a stand-in:
///     <para>
///         console JSON → <see cref="UpsertScheduleRequest" /> → <see cref="ScheduleCalendar" />
///         → stored entity JSON → back → <see cref="ScheduleOccurrenceResolver" />.
///     </para>
///     <para>
///         Every other calendar test sits on one side of that path. The domain tests prove
///         <c>ResolveDay</c>'s precedence and the resolver tests prove the day walk, but both
///         construct their aggregates in memory, so nothing caught a wire-shape or
///         serialisation disagreement between the console and the poller. That seam is the one
///         that fails silently: a station would simply keep airing on a date an operator had
///         taken off air, with a green save and no error anywhere.
///     </para>
/// </summary>
public sealed class CalendarWireRoundTripTests
{
    /// <summary>
    ///     Byte-for-byte the shape the console PUTs: PascalCase, `Calendar` last, a range with
    ///     single dates nested inside it, and a suppression. Taken from the Cairns fixture.
    /// </summary>
    private const string ConsolePayload = """
    {
      "Name": "Cairns 4CA Traffic",
      "Station": "4CA 846 AM",
      "Timezone": "Australia/Brisbane",
      "CronExpression": "0,15,30,45 6-9,15-18 * * 1-5",
      "Enabled": true,
      "TargetDurationSeconds": 30,
      "Calendar": {
        "Patterns": [
          { "Id": "public-holiday", "Name": "Public holiday", "CronExpression": "0,30 7-9,15-17 * * 0,1,2,3,4,5,6" },
          { "Id": "christmas-eve", "Name": "Christmas Eve", "CronExpression": "0,30 6-11 * * 0,1,2,3,4,5,6" },
          { "Id": "reduced", "Name": "Reduced clock", "CronExpression": "0 7-9,15-17 * * 0,1,2,3,4,5,6" }
        ],
        "Assignments": [
          { "StartDate": "2026-12-21", "EndDate": "2026-12-31", "PatternId": "reduced", "Suppress": false, "Note": "Christmas to New Year" },
          { "StartDate": "2026-12-24", "EndDate": "2026-12-24", "PatternId": "christmas-eve", "Suppress": false, "Note": "Christmas Eve" },
          { "StartDate": "2026-12-25", "EndDate": "2026-12-25", "PatternId": null, "Suppress": true, "Note": "Christmas Day, station off air" },
          { "StartDate": "2026-12-26", "EndDate": "2026-12-26", "PatternId": "public-holiday", "Suppress": false, "Note": "Boxing Day" }
        ]
      }
    }
    """;

    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Cairns sits on Australia/Brisbane, which never observes daylight saving.</summary>
    private const string Timezone = "Australia/Brisbane";

    private static MarketSchedule RoundTrip()
    {
        var request = JsonSerializer.Deserialize<UpsertScheduleRequest>(ConsolePayload, Wire);
        request.ShouldNotBeNull();
        request.Calendar.ShouldNotBeNull();

        var calendar = request.Calendar!.ToDomain();
        calendar.IsSuccess.ShouldBeTrue(calendar.Error);

        var schedule = MarketSchedule.Create(
            "cairns-4ca", request.Name!, request.Station!, request.Timezone!,
            request.CronExpression!, true, CoverageArea.Create(-16.92, 145.77, 25).Value,
            "vp-1", "cairns/traffic/", request.TargetDurationSeconds!.Value, 15, 5,
            DateTime.UnixEpoch, DateTime.UnixEpoch).Value;
        schedule.Calendar = Option<ScheduleCalendar>.Some(calendar.Value);

        // The storage hop, with the SAME options the Table repository uses.
        string stored = JsonSerializer.Serialize(schedule.Calendar.Value, StorageOptions);
        var reloaded = JsonSerializer.Deserialize<ScheduleCalendar>(stored, StorageOptions);
        reloaded.ShouldNotBeNull();
        schedule.Calendar = Option<ScheduleCalendar>.Some(reloaded!);

        return schedule;
    }

    /// <summary>Mirrors `JsonDefaults.Storage`, which is internal to the Infrastructure assembly.</summary>
    private static readonly JsonSerializerOptions StorageOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static ScheduleOccurrenceResolver Resolver() =>
        new(new CronParser(), NullLogger.Instance);

    /// <summary>Local Brisbane wall time as UTC. Brisbane is a flat UTC+10.</summary>
    private static DateTime BrisbaneUtc(int year, int month, int day, int hour, int minute = 0) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-10);

    [Fact]
    public void ConsolePayloadSurvivesTheWireAndTheStore()
    {
        var schedule = RoundTrip();

        schedule.Calendar.HasValue.ShouldBeTrue();
        var calendar = schedule.Calendar.Value;
        calendar.Patterns.Count.ShouldBe(3);
        calendar.Assignments.Count.ShouldBe(4);
        calendar.Assignments[2].Suppress.ShouldBeTrue();
        calendar.Assignments[2].StartDate.ShouldBe(new DateOnly(2026, 12, 25));
        calendar.Assignments[0].EndDate.ShouldBe(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void AnOrdinaryWeekdayStillRunsTheBaseClock()
    {
        // 2026-12-15 is a Tuesday outside every assignment: first base slot is 06:00.
        var next = Resolver().NextRunUtc(RoundTrip(), BrisbaneUtc(2026, 12, 15, 5, 30));

        next.HasValue.ShouldBeTrue();
        next.Value.ShouldBe(BrisbaneUtc(2026, 12, 15, 6));
    }

    [Fact]
    public void ADateInsideTheRangeRunsTheRangesPattern()
    {
        // 2026-12-22 falls in the reduced range: hourly at the shoulders, so 07:00.
        var next = Resolver().NextRunUtc(RoundTrip(), BrisbaneUtc(2026, 12, 22, 5, 30));

        next.HasValue.ShouldBeTrue();
        next.Value.ShouldBe(BrisbaneUtc(2026, 12, 22, 7));
    }

    [Fact]
    public void ASingleDateBeatsTheRangeItSitsInside()
    {
        // 2026-12-24 is inside the 21-31 reduced range AND has its own assignment.
        // Shortest window wins, so the Christmas Eve clock runs: first slot 06:00,
        // which the reduced clock does not have.
        var next = Resolver().NextRunUtc(RoundTrip(), BrisbaneUtc(2026, 12, 24, 5, 30));

        next.HasValue.ShouldBeTrue();
        next.Value.ShouldBe(BrisbaneUtc(2026, 12, 24, 6));
    }

    [Fact]
    public void ASuppressedDateIsSkippedAndTheNextDayIsScheduled()
    {
        // THE test. Standing at 05:30 on Christmas Day, the poller must not schedule
        // anything that day and must land on Boxing Day's holiday clock instead: 07:00.
        var next = Resolver().NextRunUtc(RoundTrip(), BrisbaneUtc(2026, 12, 25, 5, 30));

        next.HasValue.ShouldBeTrue();
        next.Value.ShouldBe(BrisbaneUtc(2026, 12, 26, 7));
    }

    [Fact]
    public void NothingIsScheduledAtAnyPointDuringASuppressedDay()
    {
        // Walk Christmas Day hour by hour: not one of them may resolve inside the day.
        var resolver = Resolver();
        var schedule = RoundTrip();

        for (int hour = 0; hour < 24; hour++)
        {
            var next = resolver.NextRunUtc(schedule, BrisbaneUtc(2026, 12, 25, hour));

            next.HasValue.ShouldBeTrue();
            next.Value.ShouldBeGreaterThanOrEqualTo(BrisbaneUtc(2026, 12, 26, 7));
        }
    }

    [Fact]
    public void AHolidayOnASaturdayAirsEvenThoughTheBaseClockIsWeekdaysOnly()
    {
        // 2026-12-26 is a SATURDAY and the base clock is Mon-Fri, so without the
        // calendar this day would air nothing. Day types cover all seven days, which is
        // what makes a weekend public holiday airable at all.
        var next = Resolver().NextRunUtc(RoundTrip(), BrisbaneUtc(2026, 12, 26, 0));

        next.HasValue.ShouldBeTrue();
        next.Value.ShouldBe(BrisbaneUtc(2026, 12, 26, 7));
    }

    [Fact]
    public void AScheduleWithNoCalendarIsUnaffected()
    {
        var schedule = RoundTrip();
        schedule.Calendar = Option<ScheduleCalendar>.None;

        // Christmas Day is a Friday, so the untouched base clock runs it as normal.
        var next = Resolver().NextRunUtc(schedule, BrisbaneUtc(2026, 12, 25, 5, 30));

        next.HasValue.ShouldBeTrue();
        next.Value.ShouldBe(BrisbaneUtc(2026, 12, 25, 6));
    }

    /// <summary>
    ///     Config transfer is how a station is promoted dev to test to prod. The calendar was
    ///     missing from <see cref="ConfigScheduleDto" />, so an export dropped it and the
    ///     import replayed a schedule with no day types and no dated exceptions, reporting a
    ///     clean Update. Silent config loss at promotion time, which is the worst place for it.
    /// </summary>
    [Fact]
    public void ExportAndImportAreLosslessForTheCalendar()
    {
        var schedule = RoundTrip();

        var exported = schedule.ToConfigScheduleDto();
        exported.Calendar.ShouldNotBeNull();
        exported.Calendar!.Patterns.Count.ShouldBe(3);
        exported.Calendar.Assignments.Count.ShouldBe(4);

        // Through a real serialisation hop, as a bundle file would take.
        string bundle = JsonSerializer.Serialize(exported, Wire);
        var reimported = JsonSerializer.Deserialize<ConfigScheduleDto>(bundle, Wire);
        reimported.ShouldNotBeNull();

        // The import replays the bundle through the ordinary upsert request.
        var request = reimported!.ToUpsertRequest();
        request.Calendar.ShouldNotBeNull();

        var domain = request.Calendar!.ToDomain();
        domain.IsSuccess.ShouldBeTrue(domain.Error);
        domain.Value.Patterns.Select(p => p.Id)
            .ShouldBe(["public-holiday", "christmas-eve", "reduced"]);

        // The imported copy suppresses Christmas Day exactly like the original.
        var imported = RoundTrip();
        imported.Calendar = Option<ScheduleCalendar>.Some(domain.Value);
        Resolver()
            .NextRunUtc(imported, BrisbaneUtc(2026, 12, 25, 5, 30))
            .Value.ShouldBe(BrisbaneUtc(2026, 12, 26, 7));
    }

    /// <summary>
    ///     A bundle exported before calendars existed has no `Calendar` key at all. It must
    ///     still import, and must mean "leave the calendar alone" rather than "clear it",
    ///     which is why the schema version was deliberately NOT bumped for this field.
    /// </summary>
    [Fact]
    public void ABundleWithNoCalendarKeyStillImports()
    {
        const string legacyBundle = """
        { "MarketId": "cairns-4ca", "Name": "Cairns", "Station": "4CA 846 AM",
          "Timezone": "Australia/Brisbane", "CronExpression": "0 6 * * 1-5",
          "Enabled": true, "TargetDurationSeconds": 30 }
        """;

        var dto = JsonSerializer.Deserialize<ConfigScheduleDto>(legacyBundle, Wire);

        dto.ShouldNotBeNull();
        dto!.Calendar.ShouldBeNull();
        dto.ToUpsertRequest().Calendar.ShouldBeNull();
    }
}
