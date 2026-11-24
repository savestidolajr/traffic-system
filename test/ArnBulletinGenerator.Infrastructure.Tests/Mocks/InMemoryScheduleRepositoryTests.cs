using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Mocks;

public class InMemoryScheduleRepositoryTests
{
    private readonly ICronParser _cronParser = Substitute.For<ICronParser>();
    private readonly InMemoryScheduleRepository _sut;

    public InMemoryScheduleRepositoryTests()
    {
        _cronParser.CalculateNextRun(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Option<DateTime>.Some(DateTime.UtcNow.AddHours(1)));
        _sut = new InMemoryScheduleRepository(_cronParser);
    }

    private static MarketSchedule CreateSchedule(
        string marketId = "Sydney",
        bool enabled = true,
        DateTime? nextRunUtc = null,
        string cron = "0 * * * *")
    {
        var schedule = MarketSchedule.Create(
            marketId,
            $"{marketId} Schedule",
            "2GB",
            "Australia/Sydney",
            cron,
            enabled,
            CoverageArea.Create(-33.87, 151.21, 25).Value,
            "voice-1",
            "/output",
            90,
            5,
            2,
            DateTime.UtcNow,
            DateTime.UtcNow).Value;
        schedule.NextRunUtc = nextRunUtc.HasValue
            ? Option<DateTime>.Some(nextRunUtc.Value)
            : Option<DateTime>.None;
        return schedule;
    }

    // ── SaveScheduleAsync / GetScheduleAsync round-trip ──

    [Fact]
    public async Task SaveAndGet_RoundTrip_ReturnsSchedule()
    {
        var schedule = CreateSchedule();
        await _sut.SaveScheduleAsync(schedule);

        var result = await _sut.GetScheduleAsync("Sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.MarketId.ShouldBe("Sydney");
        result.Value.Station.ShouldBe("2GB");
    }

    [Fact]
    public async Task GetScheduleAsync_Missing_ReturnsFailure()
    {
        var result = await _sut.GetScheduleAsync("nonexistent");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveScheduleAsync_EnabledWithoutNextRun_CalculatesNextRun()
    {
        var schedule = CreateSchedule();

        await _sut.SaveScheduleAsync(schedule);

        var result = await _sut.GetScheduleAsync("Sydney");
        result.Value.NextRunUtc.HasValue.ShouldBeTrue();
    }

    // ── ListSchedulesAsync ──

    [Fact]
    public async Task ListSchedulesAsync_ReturnsAllSchedulesOrderedByName()
    {
        await _sut.SaveScheduleAsync(CreateSchedule("Melbourne"));
        await _sut.SaveScheduleAsync(CreateSchedule("Adelaide"));
        await _sut.SaveScheduleAsync(CreateSchedule());

        var result = await _sut.ListSchedulesAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(3);
        result.Value.First().MarketId.ShouldBe("Adelaide");
    }

    // ── DeleteScheduleAsync ──

    [Fact]
    public async Task DeleteScheduleAsync_RemovesExistingSchedule()
    {
        await _sut.SaveScheduleAsync(CreateSchedule());

        var deleteResult = await _sut.DeleteScheduleAsync("Sydney");
        var getResult = await _sut.GetScheduleAsync("Sydney");

        deleteResult.IsSuccess.ShouldBeTrue();
        getResult.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteScheduleAsync_Missing_ReturnsFailure()
    {
        var result = await _sut.DeleteScheduleAsync("nonexistent");

        result.IsFailure.ShouldBeTrue();
    }

    // ── GetDueSchedulesAsync ──

    [Fact]
    public async Task GetDueSchedulesAsync_ReturnsDueEnabledSchedules()
    {
        var now = DateTime.UtcNow;
        await _sut.SaveScheduleAsync(CreateSchedule("Due", true, now.AddMinutes(-5)));
        await _sut.SaveScheduleAsync(CreateSchedule("NotYet", true, now.AddHours(1)));
        await _sut.SaveScheduleAsync(CreateSchedule("Disabled", false, now.AddMinutes(-5)));

        var result = await _sut.GetDueSchedulesAsync(now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(s => s.MarketId == "Due");
    }

    [Fact]
    public async Task GetDueSchedulesAsync_ExcludesSchedulesWithCurrentJob()
    {
        var now = DateTime.UtcNow;
        var schedule = CreateSchedule("Running", true, now.AddMinutes(-5));
        await _sut.SaveScheduleAsync(schedule);
        await _sut.TryStartJobAsync("Running", "job-1");

        var result = await _sut.GetDueSchedulesAsync(now);

        result.Value.ShouldBeEmpty();
    }

    // ── TryStartJobAsync ──

    [Fact]
    public async Task TryStartJobAsync_SetsCurrentJobId()
    {
        await _sut.SaveScheduleAsync(CreateSchedule());

        var result = await _sut.TryStartJobAsync("Sydney", "job-1");

        result.IsSuccess.ShouldBeTrue();
        var schedule = await _sut.GetScheduleAsync("Sydney");
        schedule.Value.CurrentJobId.HasValue.ShouldBeTrue();
        schedule.Value.CurrentJobId.Value.ShouldBe("job-1");
    }

    [Fact]
    public async Task TryStartJobAsync_AlreadyRunning_ReturnsFailure()
    {
        await _sut.SaveScheduleAsync(CreateSchedule());
        await _sut.TryStartJobAsync("Sydney", "job-1");

        var result = await _sut.TryStartJobAsync("Sydney", "job-2");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task TryStartJobAsync_Missing_ReturnsFailure()
    {
        var result = await _sut.TryStartJobAsync("nonexistent", "job-1");

        result.IsFailure.ShouldBeTrue();
    }

    // ── CompleteJobAsync ──

    [Fact]
    public async Task CompleteJobAsync_ClearsCurrentJobAndSetsLastRun()
    {
        await _sut.SaveScheduleAsync(CreateSchedule());
        await _sut.TryStartJobAsync("Sydney", "job-1");
        var nextRun = Option<DateTime>.Some(DateTime.UtcNow.AddHours(2));

        var result = await _sut.CompleteJobAsync("Sydney", nextRun);

        result.IsSuccess.ShouldBeTrue();
        var schedule = await _sut.GetScheduleAsync("Sydney");
        schedule.Value.CurrentJobId.IsNone.ShouldBeTrue();
        schedule.Value.LastRunUtc.HasValue.ShouldBeTrue();
        schedule.Value.NextRunUtc.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteJobAsync_NoNextRunProvided_UseCronParser()
    {
        var cronNext = DateTime.UtcNow.AddHours(3);
        _cronParser.CalculateNextRun(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Option<DateTime>.Some(cronNext));
        await _sut.SaveScheduleAsync(CreateSchedule());
        await _sut.TryStartJobAsync("Sydney", "job-1");

        await _sut.CompleteJobAsync("Sydney", Option<DateTime>.None);

        var schedule = await _sut.GetScheduleAsync("Sydney");
        schedule.Value.NextRunUtc.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteJobAsync_Missing_ReturnsFailure()
    {
        var result = await _sut.CompleteJobAsync("nonexistent", Option<DateTime>.None);

        result.IsFailure.ShouldBeTrue();
    }

    // ── Deep copy isolation ──

    [Fact]
    public async Task SaveScheduleAsync_StoresDeepCopy_MutationsDoNotAffectRepo()
    {
        var schedule = CreateSchedule();
        await _sut.SaveScheduleAsync(schedule);

        schedule.Enabled = !schedule.Enabled;

        var fetched = await _sut.GetScheduleAsync("Sydney");
        fetched.Value.Enabled.ShouldBe(!schedule.Enabled);
    }
}
