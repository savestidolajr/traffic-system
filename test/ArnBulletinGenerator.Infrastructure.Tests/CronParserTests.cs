using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests;

public class CronParserTests
{
    private readonly CronParser _sut = new();

    // ─── TryParseCron ────────────────────────────────────────────

    [Fact]
    public void TryParseCron_ValidExpression_ReturnsTrue()
    {
        _sut.TryParseCron("0 8 * * 1,2,3").ShouldBeTrue();
    }

    [Fact]
    public void TryParseCron_EveryMinute_ReturnsTrue()
    {
        _sut.TryParseCron("* * * * *").ShouldBeTrue();
    }

    [Fact]
    public void TryParseCron_Invalid_ReturnsFalse()
    {
        _sut.TryParseCron("not a cron").ShouldBeFalse();
    }

    [Fact]
    public void TryParseCron_Empty_ReturnsFalse()
    {
        _sut.TryParseCron("").ShouldBeFalse();
    }

    [Fact]
    public void TryParseCron_WhitespaceOnly_ReturnsFalse()
    {
        _sut.TryParseCron("   ").ShouldBeFalse();
    }

    [Fact]
    public void TryParseCron_CompoundValid_ReturnsTrue()
    {
        _sut.TryParseCron("0 8 * * *\n30 10 * * *").ShouldBeTrue();
    }

    [Fact]
    public void TryParseCron_CompoundWithOneInvalidLine_ReturnsFalse()
    {
        _sut.TryParseCron("0 8 * * *\nbad line").ShouldBeFalse();
    }

    [Fact]
    public void TryParseCron_CompoundWithBlankLines_ReturnsTrue()
    {
        // Blank lines are stripped by RemoveEmptyEntries
        _sut.TryParseCron("0 8 * * *\n\n30 10 * * *").ShouldBeTrue();
    }

    // ─── CalculateNextRun ────────────────────────────────────────

    [Fact]
    public void CalculateNextRun_ValidCronAndTimezone_ReturnsSome()
    {
        var baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc); // Sunday
        // "0 8 * * 1" = Monday 08:00
        var result = _sut.CalculateNextRun("0 8 * * 1", "UTC", baseTime);

        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBeGreaterThan(baseTime);
    }

    [Fact]
    public void CalculateNextRun_CompoundCron_ReturnsEarliestOccurrence()
    {
        // Monday 2025-06-02 00:00 UTC
        var baseTime = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        // 08:00 is earlier than 10:30 on the same day
        var result = _sut.CalculateNextRun("0 8 * * *\n30 10 * * *", "UTC", baseTime);

        result.HasValue.ShouldBeTrue();
        result.Value.Hour.ShouldBe(8);
        result.Value.Minute.ShouldBe(0);
    }

    [Fact]
    public void CalculateNextRun_CompoundCron_SecondLineEarlier_ReturnsThat()
    {
        // baseTime is already past 08:00 today, so "30 10" today is earlier than "0 8" tomorrow
        var baseTime = new DateTime(2025, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var result = _sut.CalculateNextRun("0 8 * * *\n30 10 * * *", "UTC", baseTime);

        result.HasValue.ShouldBeTrue();
        result.Value.Hour.ShouldBe(10);
        result.Value.Minute.ShouldBe(30);
    }

    [Fact]
    public void CalculateNextRun_InvalidTimezone_ReturnsNone()
    {
        var result = _sut.CalculateNextRun("0 8 * * *", "Invalid/Timezone");

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void CalculateNextRun_InvalidCron_ReturnsNone()
    {
        var result = _sut.CalculateNextRun("bad cron", "UTC");

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void CalculateNextRun_WithRealTimezone_ReturnsSome()
    {
        var baseTime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = _sut.CalculateNextRun("0 8 * * *", "Australia/Sydney", baseTime);

        result.HasValue.ShouldBeTrue();
    }
}
