using System.Net;
using System.Text.Json;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

/// <summary>
///     End-to-end proof of the year calendar over the REAL stack: HTTP PUT through the
///     Functions host, into Azure Table Storage (Azurite) as a serialised JSON column, back
///     out through GET, with <c>NextRunAt</c> computed server-side by the calendar-aware
///     <c>ScheduleOccurrenceResolver</c> using the real Cronos parser.
///     <para>
///     The unit and integration suites each cover one side of this: the domain tests build
///     aggregates in memory, the resolver tests use a fake parser, and the wire round-trip
///     test stops at the storage-entity JSON. Only this test proves the whole chain the
///     operator console actually drives, including the promotion path through
///     <c>GET /api/config/export</c> and <c>POST /api/config/import</c> - the seam where the
///     calendar was silently dropped before it carried <c>Calendar</c>.
///     </para>
///     <para>
///     Dates are anchored well into the future and asserted in the STATION's timezone, so the
///     assertions do not depend on when the suite runs. Brisbane is chosen deliberately: it
///     has no DST, so a local wall-clock time maps to exactly one UTC instant and a failure
///     here means a real defect rather than an hour of ambiguity.
///     </para>
/// </summary>
[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class CalendarSchedulingE2ETests(E2EHttpClientFixture fixture)
{
    private const string Timezone = "Australia/Brisbane";

    /// <summary>Base clock: 06:00 every day, so an unsuppressed day always has a slot.</summary>
    private const string BaseCron = "0 6 * * *";

    /// <summary>A day type that runs late, so it cannot be confused with the base clock.</summary>
    private const string LatePatternCron = "0 23 * * *";

    [Fact(Timeout = 150_000)]
    public async Task Calendar_SurvivesHttpAndStorage_AndDrivesTheServerComputedNextRun()
    {
        var market = $"e2e-cal-{Guid.NewGuid():N}"[..24];
        var voiceProfileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, $"cal-{Guid.NewGuid():N}"[..12]);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Timezone);

        // Anchor on the station's own date, because that is what the calendar is written in.
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var suppressFrom = today.AddDays(10);
        var suppressTo = today.AddDays(40);
        var lateDay = suppressTo.AddDays(1);

        try
        {
            // --- 1. Create WITH a calendar in the same PUT the console sends ------------
            using (var create = await fixture.Client.PutAsync($"schedules/{market}",
                       E2EHttpClientFixture.JsonContent(new
                       {
                           Name = "E2E Calendar",
                           Station = "E2E FM",
                           Timezone,
                           CronExpression = BaseCron,
                           Enabled = true,
                           TargetDurationSeconds = 30.0,
                           NotifyBeforePlayoutMinutes = 5,
                           EscalationBeforePlayoutMinutes = 2,
                           Coverage = new { Latitude = -27.4698, Longitude = 153.0251, RadiusKm = 35.0 },
                           Content = new { Traffic = true, Weather = false, Fuel = false },
                           VoiceProfileId = voiceProfileId,
                           Calendar = new
                           {
                               Patterns = new[]
                               {
                                   new { Id = "late", Name = "Late clock", CronExpression = LatePatternCron }
                               },
                               Assignments = new object[]
                               {
                                   new
                                   {
                                       StartDate = Iso(suppressFrom),
                                       EndDate = Iso(suppressTo),
                                       Suppress = true,
                                       Note = "Station off air"
                                   },
                                   new
                                   {
                                       StartDate = Iso(lateDay),
                                       EndDate = Iso(lateDay),
                                       PatternId = "late",
                                       Note = "Late clock day"
                                   }
                               }
                           }
                       })))
            {
                create.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            // --- 2. It came back off real storage, not out of memory --------------------
            var stored = await GetScheduleAsync(market);
            var calendar = Required(stored, "Calendar");

            calendar.ValueKind.ShouldBe(JsonValueKind.Object,
                "the calendar must survive the Table Storage JSON column");
            var patterns = Required(calendar, "Patterns");
            patterns.GetArrayLength().ShouldBe(1);
            patterns[0].StringProp("CronExpression", "cronExpression").ShouldBe(LatePatternCron);

            var assignments = Required(calendar, "Assignments");
            assignments.GetArrayLength().ShouldBe(2);
            assignments[0].Prop("Suppress", "suppress")!.Value.GetBoolean().ShouldBeTrue();
            assignments[0].StringProp("StartDate", "startDate").ShouldBe(Iso(suppressFrom));
            assignments[1].StringProp("PatternId", "patternId").ShouldBe("late");

            // --- 3. The SERVER's next run honours the calendar -------------------------
            // Everything from suppressFrom to suppressTo is off air, so the first slot after
            // the window is the late day at 23:00 - never that day's 06:00 base slot.
            var nextRun = NextRunLocal(stored, tz);

            nextRun.ShouldNotBeNull("an enabled schedule must have a next run");
            var nextDate = DateOnly.FromDateTime(nextRun!.Value);

            if (nextDate < suppressFrom)
            {
                // Today is before the window: the base clock legitimately runs first.
                nextRun.Value.Hour.ShouldBe(6);
            }
            else
            {
                nextDate.ShouldBe(lateDay,
                    "the suppressed window must be skipped entirely");
                nextRun.Value.Hour.ShouldBe(23,
                    "the day type must beat the base clock on its own date");
            }

            // --- 4. No hour inside the window resolves ---------------------------------
            // Narrow the window to start today so the very next slot has to clear it, which
            // is the assertion that would fail if the resolver ignored suppression.
            await PatchCalendarAsync(market, new
            {
                Patterns = new[]
                {
                    new { Id = "late", Name = "Late clock", CronExpression = LatePatternCron }
                },
                Assignments = new object[]
                {
                    new
                    {
                        StartDate = Iso(today),
                        EndDate = Iso(suppressTo),
                        Suppress = true,
                        Note = "Off air from today"
                    },
                    new
                    {
                        StartDate = Iso(lateDay),
                        EndDate = Iso(lateDay),
                        PatternId = "late",
                        Note = "Late clock day"
                    }
                }
            });

            var afterSuppression = NextRunLocal(await GetScheduleAsync(market), tz);

            afterSuppression.ShouldNotBeNull();
            DateOnly.FromDateTime(afterSuppression!.Value).ShouldBe(lateDay,
                "with today suppressed the next run must jump the whole window");
            afterSuppression.Value.Hour.ShouldBe(23);

            // --- 5. Clearing the calendar restores the plain weekly clock -------------
            await PatchCalendarAsync(market, new { Patterns = Array.Empty<object>(), Assignments = Array.Empty<object>() });

            var cleared = await GetScheduleAsync(market);
            var clearedNext = NextRunLocal(cleared, tz);

            clearedNext.ShouldNotBeNull();
            clearedNext!.Value.Hour.ShouldBe(6,
                "an empty calendar must fall back to the base clock, not stay suppressed");
        }
        finally
        {
            using var cleanup = await fixture.Client.DeleteAsync($"schedules/{market}");
        }
    }

    /// <summary>
    ///     The promotion path: export a station's config and import it back. The calendar was
    ///     missing from the bundle DTO and dropped by the import plan builder, so a promoted
    ///     station arrived with every day type gone under a green "Update". This drives the
    ///     real endpoints, so a regression fails here rather than in production.
    /// </summary>
    [Fact(Timeout = 150_000)]
    public async Task ConfigExportImport_CarriesTheCalendar_ThroughTheRealEndpoints()
    {
        var market = $"e2e-cal-x-{Guid.NewGuid():N}"[..24];
        var voiceProfileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(fixture, $"calx-{Guid.NewGuid():N}"[..13]);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var holiday = today.AddDays(20);

        try
        {
            using (var create = await fixture.Client.PutAsync($"schedules/{market}",
                       E2EHttpClientFixture.JsonContent(new
                       {
                           Name = "E2E Calendar Export",
                           Station = "E2E FM",
                           Timezone,
                           CronExpression = BaseCron,
                           Enabled = true,
                           TargetDurationSeconds = 30.0,
                           NotifyBeforePlayoutMinutes = 5,
                           EscalationBeforePlayoutMinutes = 2,
                           Coverage = new { Latitude = -27.4698, Longitude = 153.0251, RadiusKm = 35.0 },
                           Content = new { Traffic = true, Weather = false, Fuel = false },
                           VoiceProfileId = voiceProfileId,
                           Calendar = new
                           {
                               Patterns = new[]
                               {
                                   new { Id = "public-holiday", Name = "Public holiday", CronExpression = LatePatternCron }
                               },
                               Assignments = new[]
                               {
                                   new
                                   {
                                       StartDate = Iso(holiday),
                                       EndDate = Iso(holiday),
                                       PatternId = "public-holiday",
                                       Note = "Exported holiday"
                                   }
                               }
                           }
                       })))
            {
                create.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            // --- Export: the bundle must actually contain the calendar ------------------
            JsonElement exportedSchedule;
            using (var export = await fixture.Client.GetAsync("config/export"))
            {
                export.StatusCode.ShouldBe(HttpStatusCode.OK);
                var bundle = await BackendE2ETestSupport.ReadJsonAsync(export);

                exportedSchedule = Required(bundle, "Schedules").EnumerateArray()
                    .Single(s => string.Equals(s.StringProp("MarketId", "marketId"), market,
                        StringComparison.OrdinalIgnoreCase));

                var exportedCalendar = Required(exportedSchedule, "Calendar");
                exportedCalendar.ValueKind.ShouldBe(JsonValueKind.Object,
                    "an exported schedule must carry its calendar or promotion loses it");
                Required(exportedCalendar, "Patterns")[0].StringProp("Id", "id")
                    .ShouldBe("public-holiday");
                Required(exportedCalendar, "Assignments")[0].StringProp("StartDate", "startDate")
                    .ShouldBe(Iso(holiday));
            }

            // --- Wipe the calendar, then re-import the exported bundle -----------------
            await PatchCalendarAsync(market, new { Patterns = Array.Empty<object>(), Assignments = Array.Empty<object>() });
            var wiped = await GetScheduleAsync(market);
            Required(Required(wiped, "Calendar"), "Assignments").GetArrayLength().ShouldBe(0);

            var importBundle = JsonSerializer.Deserialize<JsonElement>($$"""
                {"schemaVersion":1,"schedules":[{{exportedSchedule.GetRawText()}}]}
                """);

            using (var import = await fixture.Client.PostAsync("config/import?dryRun=false",
                       E2EHttpClientFixture.JsonContent(importBundle)))
            {
                import.StatusCode.ShouldBe(HttpStatusCode.OK);
                var result = await BackendE2ETestSupport.ReadJsonAsync(import);
                result.GetRawText().ShouldNotContain("\"error\"",
                    Case.Insensitive, "the import must not report errors");
            }

            // --- The calendar is back, and it drives the resolver ---------------------
            var reimported = await GetScheduleAsync(market);
            var calendar = Required(reimported, "Calendar");

            calendar.ValueKind.ShouldBe(JsonValueKind.Object,
                "the import must restore the calendar, not silently drop it");
            Required(calendar, "Patterns")[0].StringProp("CronExpression", "cronExpression")
                .ShouldBe(LatePatternCron);
            Required(calendar, "Assignments")[0].StringProp("PatternId", "patternId")
                .ShouldBe("public-holiday");
        }
        finally
        {
            using var cleanup = await fixture.Client.DeleteAsync($"schedules/{market}");
        }
    }

    /// <summary>
    ///     The one that matters operationally: the POLLER must not put a bulletin to air on a
    ///     suppressed date. Everything else here proves the calendar is stored and that
    ///     <c>NextRunAt</c> reflects it; this proves the timer-driven dispatch path agrees.
    ///     <para>
    ///     Asserting "no job appeared" alone would pass even if the poller were dead, so this
    ///     runs a CONTROL schedule with an identical clock and no calendar. The control
    ///     producing a job is the evidence that the poller ran at all; only then is the
    ///     suppressed market's silence meaningful.
    ///     </para>
    /// </summary>
    [Fact(Timeout = 300_000)]
    public async Task Poller_DoesNotDispatchOnASuppressedDate_WhileTheControlStationAirs()
    {
        var suppressed = $"e2e-cal-off-{Guid.NewGuid():N}"[..24];
        var control = $"e2e-cal-on-{Guid.NewGuid():N}"[..24];
        var voiceProfileId = await BackendE2ETestSupport.CreateVoiceProfileAsync(
            fixture, $"calp-{Guid.NewGuid():N}"[..13]);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);

        // Every five minutes is the enforced floor, and the 15 minute lead time makes it due
        // on the next tick. Suppression spans today through +2 days so a run that straddles
        // station-local midnight cannot slip onto an uncovered date.
        object Body(object? calendar) => new
        {
            Name = "E2E Calendar Poller",
            Station = "E2E FM",
            Timezone,
            CronExpression = "*/5 * * * *",
            Enabled = true,
            TargetDurationSeconds = 30.0,
            NotifyBeforePlayoutMinutes = 5,
            EscalationBeforePlayoutMinutes = 2,
            Coverage = new { Latitude = -27.4698, Longitude = 153.0251, RadiusKm = 35.0 },
            Content = new { Traffic = true, Weather = false, Fuel = false },
            VoiceProfileId = voiceProfileId,
            Calendar = calendar
        };

        try
        {
            using (var offAir = await fixture.Client.PutAsync($"schedules/{suppressed}",
                       E2EHttpClientFixture.JsonContent(Body(new
                       {
                           Patterns = Array.Empty<object>(),
                           Assignments = new[]
                           {
                               new
                               {
                                   StartDate = Iso(today),
                                   EndDate = Iso(today.AddDays(2)),
                                   Suppress = true,
                                   Note = "Off air"
                               }
                           }
                       }))))
            {
                offAir.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using (var onAir = await fixture.Client.PutAsync($"schedules/{control}",
                       E2EHttpClientFixture.JsonContent(Body(null))))
            {
                onAir.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            // A suppressed schedule must not even be given a next run inside the window.
            var suppressedNext = NextRunLocal(await GetScheduleAsync(suppressed), tz);
            suppressedNext.ShouldNotBeNull("an enabled schedule still needs a next run");
            DateOnly.FromDateTime(suppressedNext!.Value).ShouldBeGreaterThan(today.AddDays(2),
                "the suppressed window must push the next run past itself");

            // The poller ticks every minute; the control proves it is alive.
            await Eventually.UntilAsync(async () => await HasJobAsync(control),
                TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(5),
                "The control schedule never produced a job, so the poller was not observed " +
                "running and the suppression assertion below would be vacuous.");

            (await HasJobAsync(suppressed)).ShouldBeFalse(
                "the poller dispatched a bulletin on a suppressed date");
        }
        finally
        {
            using var cleanupOff = await fixture.Client.DeleteAsync($"schedules/{suppressed}");
            using var cleanupOn = await fixture.Client.DeleteAsync($"schedules/{control}");
        }
    }

    /// <summary>True when the jobs feed carries a job for this market.</summary>
    private async Task<bool> HasJobAsync(string market)
    {
        using var response = await fixture.Client.GetAsync("jobs");
        if (response.StatusCode != HttpStatusCode.OK)
            return false;

        var payload = await BackendE2ETestSupport.ReadJsonAsync(response);
        var items = payload.ValueKind == JsonValueKind.Array
            ? payload
            : payload.Prop("Items", "items") ?? default;

        return items.ValueKind == JsonValueKind.Array
               && items.EnumerateArray().Any(job => string.Equals(
                   job.StringProp("Market", "market"), market, StringComparison.OrdinalIgnoreCase));
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <summary>
    ///     Fetches a property that MUST be present, accepting either casing. A missing key is
    ///     the exact failure mode this suite exists to catch (the calendar being dropped), so
    ///     it fails with the field name rather than a bare KeyNotFoundException.
    /// </summary>
    private static JsonElement Required(JsonElement element, string name)
    {
        var value = element.Prop(name, JsonNamingPolicy.CamelCase.ConvertName(name));
        value.ShouldNotBeNull($"the response is missing '{name}': {Clip(element)}");
        return value!.Value;
    }

    private static string Clip(JsonElement element)
    {
        var raw = element.GetRawText();
        return raw.Length <= 400 ? raw : raw[..400] + "...";
    }

    private async Task<JsonElement> GetScheduleAsync(string market)
    {
        using var response = await fixture.Client.GetAsync($"schedules/{market}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await BackendE2ETestSupport.ReadJsonAsync(response);
    }

    /// <summary>A partial PUT carrying only the calendar, exactly as the studio's save does.</summary>
    private async Task PatchCalendarAsync(string market, object calendar)
    {
        using var response = await fixture.Client.PutAsync($"schedules/{market}",
            E2EHttpClientFixture.JsonContent(new { Calendar = calendar }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The server-computed next run, expressed in the station's local wall clock.</summary>
    private static DateTime? NextRunLocal(JsonElement schedule, TimeZoneInfo tz)
    {
        var next = schedule.Prop("NextRunAt", "nextRunAt");
        if (next is null || next.Value.ValueKind is JsonValueKind.Null)
            return null;

        var utc = DateTime.SpecifyKind(next.Value.GetDateTime(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }
}
