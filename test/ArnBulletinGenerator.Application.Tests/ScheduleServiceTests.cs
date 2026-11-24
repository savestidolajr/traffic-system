using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class ScheduleServiceTests
{
    private static readonly DateTime FixedNow = new(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CoverageArea DefaultCoverage = CoverageArea.Create(-37.8136, 144.9631, 5.0).Value;
    private readonly ICronParser _cronParser = Substitute.For<ICronParser>();
    private readonly IScheduleRepository _repo = Substitute.For<IScheduleRepository>();
    private readonly ScheduleService _sut;
    private readonly FakeTimeProvider _timeProvider = new();

    public ScheduleServiceTests()
    {
        _timeProvider.SetUtcNow(FixedNow);
        _cronParser.TryParseCron(Arg.Any<string>()).Returns(true);
        // Must ADVANCE from the supplied base time, not return a constant: ScheduleService walks
        // successive occurrences to enforce the minimum cron interval, and a stub that answers the
        // same instant every call looks like a schedule firing twice at once.
        _cronParser.CalculateNextRun(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(ci => Option<DateTime>.Some(ci.ArgAt<DateTime>(2).AddHours(1)));
        _repo.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new ScheduleService(
            _repo,
            _cronParser,
            _timeProvider,
            Substitute.For<ILogger<ScheduleService>>());
    }

    private CreateScheduleCommand MakeCreateCommand(string marketId = "mel")
    {
        return new CreateScheduleCommand(
            marketId,
            "Melbourne AM",
            "3AW",
            "Australia/Melbourne",
            "0 8 * * 1-5",
            true,
            DefaultCoverage,
            "voice-1",
            "/out",
            45);
    }

    private MarketSchedule MakeExistingSchedule(string marketId = "mel")
    {
        return MarketSchedule.Create(
            marketId,
            "Old Name",
            "OLD",
            "Australia/Melbourne",
            "0 6 * * *",
            false,
            DefaultCoverage,
            "old-voice",
            "/old",
            30,
            10,
            3,
            FixedNow.AddDays(-7),
            FixedNow.AddDays(-1)).Value;
    }

    [Fact]
    public void CreateSchedule_ValidCommand_ReturnsSuccessWithCorrectProperties()
    {
        var cmd = MakeCreateCommand();

        var result = _sut.CreateSchedule(cmd);

        result.IsSuccess.ShouldBeTrue();
        var s = result.Value;
        s.MarketId.ShouldBe("mel");
        s.Name.ShouldBe("Melbourne AM");
        s.Station.ShouldBe("3AW");
        s.Timezone.ShouldBe("Australia/Melbourne");
        s.CronExpression.ShouldBe("0 8 * * 1-5");
        s.Enabled.ShouldBeTrue();
        s.VoiceProfileId.ShouldBe("voice-1");
        s.OutputPath.ShouldBe("/out");
        s.TargetDurationSeconds.ShouldBe(45);
        s.CreatedAt.ShouldBe(FixedNow);
        s.UpdatedAt.ShouldBe(FixedNow);
    }

    [Fact]
    public void UpdateSchedule_AppliesOverridesAndPreservesUnchanged()
    {
        var existing = MakeExistingSchedule();
        var cmd = new UpdateScheduleCommand(
            "mel",
            "New Name",
            Option<string>.None,
            Enabled: true);

        var result = _sut.UpdateSchedule(existing, cmd);

        result.IsSuccess.ShouldBeTrue();
        var s = result.Value;
        s.Name.ShouldBe("New Name");
        s.Station.ShouldBe("OLD"); // unchanged
        s.Enabled.ShouldBeTrue();
        s.UpdatedAt.ShouldBe(FixedNow);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_CreatesWhenNotExists()
    {
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));

        var update = new UpdateScheduleCommand("mel");
        var create = Option<CreateScheduleCommand>.Some(MakeCreateCommand());

        var result = await _sut.CreateOrUpdateAsync(update, create);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsUpdate.ShouldBeFalse();
        result.Value.Schedule.Name.ShouldBe("Melbourne AM");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_UpdatesWhenExists()
    {
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeExistingSchedule()));

        var update = new UpdateScheduleCommand("mel", "Updated");

        var result = await _sut.CreateOrUpdateAsync(update, Option<CreateScheduleCommand>.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsUpdate.ShouldBeTrue();
        result.Value.Schedule.Name.ShouldBe("Updated");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_InvalidCron_ReturnsFailure()
    {
        _cronParser.TryParseCron("bad-cron").Returns(false);

        var update = new UpdateScheduleCommand("mel", CronExpression: "bad-cron");

        var result = await _sut.CreateOrUpdateAsync(update, Option<CreateScheduleCommand>.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("cron");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_InvalidTimezone_ReturnsFailure()
    {
        var update = new UpdateScheduleCommand("mel", Timezone: "Invalid/Timezone");

        var result = await _sut.CreateOrUpdateAsync(update, Option<CreateScheduleCommand>.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Invalid timezone");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_EnabledScheduleGetsNextRun()
    {
        var expectedNext = FixedNow.AddHours(1);
        _cronParser.CalculateNextRun(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(ci => Option<DateTime>.Some(ci.ArgAt<DateTime>(2).AddHours(1)));
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));

        var create = MakeCreateCommand(); // Enabled = true
        var update = new UpdateScheduleCommand("mel");

        var result = await _sut.CreateOrUpdateAsync(update, Option<CreateScheduleCommand>.Some(create));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Schedule.NextRunUtc.HasValue.ShouldBeTrue();
        result.Value.Schedule.NextRunUtc.Value.ShouldBe(expectedNext);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_DisabledScheduleGetsNone()
    {
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));

        var create = MakeCreateCommand() with { Enabled = false };
        var update = new UpdateScheduleCommand("mel");

        var result = await _sut.CreateOrUpdateAsync(update, Option<CreateScheduleCommand>.Some(create));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Schedule.NextRunUtc.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateOrUpdateAsync_MissingCreateCommand_ReturnsFailure()
    {
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));

        var update = new UpdateScheduleCommand("mel");

        var result = await _sut.CreateOrUpdateAsync(update, Option<CreateScheduleCommand>.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("create data is missing");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_SaveFails_ReturnsFailure()
    {
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));
        _repo.SaveScheduleAsync(Arg.Any<MarketSchedule>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Table Storage unavailable"));

        var update = new UpdateScheduleCommand("mel");
        var create = Option<CreateScheduleCommand>.Some(MakeCreateCommand());

        var result = await _sut.CreateOrUpdateAsync(update, create);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Table Storage unavailable");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_InvalidCronOnCreateCommand_ReturnsFailure()
    {
        _cronParser.TryParseCron("bad-cron-create").Returns(false);
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));

        var update = new UpdateScheduleCommand("mel");
        var create =
            Option<CreateScheduleCommand>.Some(MakeCreateCommand() with { CronExpression = "bad-cron-create" });

        var result = await _sut.CreateOrUpdateAsync(update, create);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("cron");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_InvalidTimezoneOnCreateCommand_ReturnsFailure()
    {
        _repo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("Not found"));

        var update = new UpdateScheduleCommand("mel");
        var create = Option<CreateScheduleCommand>.Some(MakeCreateCommand() with { Timezone = "Bad/Zone" });

        var result = await _sut.CreateOrUpdateAsync(update, create);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Invalid timezone");
    }

    // ── Bookend assignment fields ──

    [Fact]
    public void CreateSchedule_WithBookendFields_SetsThem()
    {
        var cmd = MakeCreateCommand() with
        {
            BookendId = "bk-1",
            BookendRotation = Option<BookendRotationPolicy>.Some(
                BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value),
            BookendTokens = Option<IReadOnlyDictionary<string, string>>.Some(
                new Dictionary<string, string> { ["brand"] = "3AW" })
        };

        var result = _sut.CreateSchedule(cmd);

        result.IsSuccess.ShouldBeTrue();
        var s = result.Value;
        s.BookendId.ShouldBe("bk-1");
        s.BookendRotation.HasValue.ShouldBeTrue();
        s.BookendRotation.Value.Mode.ShouldBe(BookendSelectionMode.RoundRobin);
        s.BookendRotation.Value.BookendIds.ShouldBe(["bk-1", "bk-2"]);
        s.BookendTokens["brand"].ShouldBe("3AW");
    }

    [Fact]
    public void UpdateSchedule_AppliesBookendOverrides()
    {
        var existing = MakeExistingSchedule();
        var cmd = new UpdateScheduleCommand(
            "mel",
            BookendId: "bk-1",
            BookendRotation: Option<BookendRotationPolicy>.Some(
                BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1"], null).Value),
            BookendTokens: Option<IReadOnlyDictionary<string, string>>.Some(
                new Dictionary<string, string> { ["brand"] = "3AW" }));

        var result = _sut.UpdateSchedule(existing, cmd);

        result.IsSuccess.ShouldBeTrue();
        var s = result.Value;
        s.BookendId.ShouldBe("bk-1");
        s.BookendRotation.HasValue.ShouldBeTrue();
        s.BookendRotation.Value.Mode.ShouldBe(BookendSelectionMode.RoundRobin);
        s.BookendTokens["brand"].ShouldBe("3AW");
    }

    [Fact]
    public void UpdateSchedule_BookendFieldsOmitted_LeavesUnchanged()
    {
        var existing = MakeExistingSchedule();
        existing.BookendId = "bk-keep";
        existing.BookendTokens = new Dictionary<string, string> { ["brand"] = "3AW" };

        var result = _sut.UpdateSchedule(existing, new UpdateScheduleCommand("mel", "New Name"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.BookendId.ShouldBe("bk-keep");
        result.Value.BookendTokens["brand"].ShouldBe("3AW");
    }
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public void SetUtcNow(DateTime utc)
    {
        _now = new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }
}
