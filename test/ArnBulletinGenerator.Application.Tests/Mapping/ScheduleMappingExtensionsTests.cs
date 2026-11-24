using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public class ScheduleMappingExtensionsTests
{
    private static MarketSchedule CreateSchedule()
    {
        return MarketSchedule.Create(
            "mkt-1",
            "Sydney AM",
            "2GB",
            "Australia/Sydney",
            "0 8 * * 1-5",
            true,
            CoverageArea.Create(-33.8, 151.2, 25).Value,
            "voice-abc",
            "/out",
            30,
            5,
            3,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc)).Value;
    }

    [Fact]
    public void ToDto_MapsAllScalarFields()
    {
        var schedule = CreateSchedule();

        var dto = schedule.ToDto();

        dto.MarketId.ShouldBe("mkt-1");
        dto.Name.ShouldBe("Sydney AM");
        dto.Station.ShouldBe("2GB");
        dto.Timezone.ShouldBe("Australia/Sydney");
        dto.CronExpression.ShouldBe("0 8 * * 1-5");
        dto.Enabled.ShouldBeTrue();
        dto.TargetDurationSeconds.ShouldBe(30);
        dto.NotifyBeforePlayoutMinutes.ShouldBe(5);
        dto.EscalationBeforePlayoutMinutes.ShouldBe(3);
        dto.VoiceProfileId.ShouldBe("voice-abc");
        dto.CreatedAt.ShouldBe(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        dto.UpdatedAt.ShouldBe(new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc));
    }


    [Fact]
    public void ToDto_MapsNextRunAt_WhenPresent()
    {
        var schedule = CreateSchedule();
        var nextRun = new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        schedule.NextRunUtc = Option<DateTime>.Some(nextRun);

        var dto = schedule.ToDto();

        dto.NextRunAt.ShouldBe(nextRun);
    }

    [Fact]
    public void ToDto_MapsNextRunAtAsNull_WhenNone()
    {
        var schedule = CreateSchedule();
        schedule.NextRunUtc = Option<DateTime>.None;

        var dto = schedule.ToDto();

        dto.NextRunAt.ShouldBeNull();
    }

    [Fact]
    public void ToDto_MapsCoverage()
    {
        var schedule = CreateSchedule();

        var dto = schedule.ToDto();

        dto.Coverage.ShouldNotBeNull();
        dto.Coverage!.Latitude.ShouldBe(-33.8);
        dto.Coverage.Longitude.ShouldBe(151.2);
        dto.Coverage.RadiusKm.ShouldBe(25);
    }

    [Fact]
    public void ToDto_MapsCurrentJobId_WhenPresent()
    {
        var schedule = CreateSchedule();
        schedule.CurrentJobId = Option<string>.Some("job-42");

        var dto = schedule.ToDto();

        dto.CurrentJobId.ShouldBe("job-42");
    }

    [Fact]
    public void ToDto_MapsCurrentJobIdAsNull_WhenNone()
    {
        var schedule = CreateSchedule();
        schedule.CurrentJobId = Option<string>.None;

        var dto = schedule.ToDto();

        dto.CurrentJobId.ShouldBeNull();
    }

    [Fact]
    public void ToDto_MapsBookendFields()
    {
        var schedule = CreateSchedule();
        schedule.BookendId = "bk-1";
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value);
        schedule.BookendTokens = new Dictionary<string, string> { ["brand"] = "2GB" };

        var dto = schedule.ToDto();

        dto.BookendId.ShouldBe("bk-1");
        dto.BookendRotation.ShouldNotBeNull();
        dto.BookendRotation!.Mode.ShouldBe("roundRobin");
        dto.BookendRotation.BookendIds.ShouldBe(["bk-1", "bk-2"]);
        dto.BookendTokens.ShouldNotBeNull();
        dto.BookendTokens!["brand"].ShouldBe("2GB");
    }

    [Fact]
    public void ToDto_UnassignedBookends_AreNull()
    {
        var schedule = CreateSchedule();

        var dto = schedule.ToDto();

        dto.BookendId.ShouldBeNull();
        dto.BookendRotation.ShouldBeNull();
        dto.BookendTokens.ShouldBeNull();
    }
}
