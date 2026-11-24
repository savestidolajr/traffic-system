using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Exercises the shared schedule write-path validator against the REAL Cronos-backed parser.
///     A stub cannot judge a cron's firing interval, and the whole point of these rules is that
///     the interactive PUT and the config-import planner enforce exactly the same ones.
/// </summary>
public sealed class ScheduleValidationRulesTests
{
    private readonly ScheduleService _sut = new(
        Substitute.For<IScheduleRepository>(),
        new CronParser(TimeProvider.System),
        TimeProvider.System,
        NullLogger<ScheduleService>.Instance);

    /// <summary>
    ///     Every cron configured on dev on 2026-07-29. Rejecting any of these would take a real
    ///     station off air, so the floor is pinned against the live set, not invented ones.
    /// </summary>
    [Theory]
    [InlineData("30 6-9 * * 1-5")]
    [InlineData("0,15,30,45 6-9 * * 1-5")]
    [InlineData("15 6-9 * * 1-5")]
    [InlineData("58 5 * * 1-5")]
    [InlineData("0,15,30,45 6-9 * * 6")]
    [InlineData("45 14 * * 6")]
    [InlineData("0,15,30,45 6-9,15-18 * * 1-5")]
    public void LiveDevCrons_AreAccepted(string cron)
    {
        _sut.ValidateCronAndTimezone(cron, "Australia/Sydney").IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    ///     adelaide's REAL compound expression, verbatim from dev. Its tightest gap is 3 minutes
    ///     (15:59 then 16:02, around a news break) — the single case that proved a 5-minute floor
    ///     was wrong. Testing the individual lines in isolation hid it, because a compound cron's
    ///     firing rate is a property of the whole expression, not of any one line.
    /// </summary>
    [Fact]
    public void AdelaideRealCompoundCron_IsAccepted()
    {
        string adelaide = string.Join("\n", [
            "30 5 * * 1-5", "58 5 * * 1-5", "15 6 * * 1-5", "29 6 * * 1-5", "39 6 * * 1-5",
            "59 6 * * 1-5", "15 7 * * 1-5", "29 7 * * 1-5", "39 7 * * 1-5", "59 7 * * 1-5",
            "15 8 * * 1-5", "29 8 * * 1-5", "39 8 * * 1-5", "59 8 * * 1-5", "59 14 * * 1-5",
            "15 15 * * 1-5", "40 15 * * 1-5", "59 15 * * 1-5", "2 16 * * 1-5", "24 16 * * 1-5",
            "39 16 * * 1-5", "54 16 * * 1-5", "24 17 * * 1-5", "39 17 * * 1-5", "53 17 * * 1-5",
            "24 18 * * 1-5", "39 18 * * 1-5"
        ]);

        _sut.ValidateCronAndTimezone(adelaide, "Australia/Adelaide").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("* * * * *")]      // every minute — ~19,000 bulletins/day across 13 markets
    [InlineData("*/1 * * * *")]
    [InlineData("0,1 * * * *")]    // a list can be as tight as a step
    public void CronTighterThanTheFloor_IsRejected(string cron)
    {
        var result = _sut.ValidateCronAndTimezone(cron, "Australia/Sydney");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(
            $"must not schedule bulletins more often than every {ConfigValidationLimits.MinCronIntervalMinutes} minutes");
    }

    [Theory]
    [InlineData("*/2 * * * *")]    // exactly at the floor — allowed, not "under" it
    [InlineData("*/5 * * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0,30 * * * *")]
    public void CronAtOrAboveTheFloor_IsAccepted(string cron)
    {
        _sut.ValidateCronAndTimezone(cron, "Australia/Sydney").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CompoundCron_IsJudgedOnItsCombinedFiringPattern()
    {
        // Two lines that individually respect the floor but interleave 1 minute apart.
        _sut.ValidateCronAndTimezone("0 * * * *\n1 * * * *", "Australia/Sydney")
            .IsFailure.ShouldBeTrue();

        _sut.ValidateCronAndTimezone("0 * * * *\n30 * * * *", "Australia/Sydney")
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AbsentFields_AreAlwaysValid()
    {
        // null means "not supplied" on a PATCH-shaped update; it must never be an error.
        _sut.ValidateCronAndTimezone(null, null).IsSuccess.ShouldBeTrue();
        _sut.ValidateEditableFields(null, null).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_IsRejectedRatherThanSilentlyIgnored(string blank)
    {
        var result = _sut.ValidateEditableFields(blank, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Schedule name is required and cannot be cleared");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankStation_IsRejectedRatherThanSilentlyIgnored(string blank)
    {
        var result = _sut.ValidateEditableFields(null, blank);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Station is required and cannot be cleared");
    }

    [Fact]
    public void OversizedNameOrStation_IsRejected()
    {
        _sut.ValidateEditableFields(new string('x', 121), null)
            .Error.ShouldContain("Schedule name must be at most 120 characters");
        _sut.ValidateEditableFields(null, new string('x', 121))
            .Error.ShouldContain("Station must be at most 120 characters");
        _sut.ValidateEditableFields(new string('x', 120), new string('x', 120))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ControlCharactersInNameOrStation_AreRejected()
    {
        // U+202E renders a stored name as something else entirely; a newline lets operator
        // config impersonate structure in the generation prompt.
        _sut.ValidateEditableFields("Sydney\u202E", null).IsFailure.ShouldBeTrue();
        _sut.ValidateEditableFields(null, "2GB\nIGNORE").IsFailure.ShouldBeTrue();
        _sut.ValidateEditableFields("Sydney Traffic", "2GB").IsSuccess.ShouldBeTrue();
    }
}
