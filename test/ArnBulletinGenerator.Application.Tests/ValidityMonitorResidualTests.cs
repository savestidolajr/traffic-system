using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class ValidityMonitorResidualTests
{
    [Fact]
    public async Task RunAsync_CachesSuccessfulResultPerMarket()
    {
        var scheduleRepository = Substitute.For<IScheduleRepository>();
        var traffic = Substitute.For<ITrafficProvider>();
        var feed = Substitute.For<IOfficialTrafficFeed>();
        var metrics = Substitute.For<IBroadcastMetrics>();
        var schedule = Schedule("sydney");
        scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([schedule]));
        traffic.GetIncidentsAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficIncident>>.Success([]));
        feed.SourceId.Returns("official");
        feed.GetEventsAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OfficialTrafficEvent>>.Success([]));
        var monitor = new ValidityMonitorService(scheduleRepository, traffic, [feed], metrics,
            TimeProvider.System, Substitute.For<ILogger<ValidityMonitorService>>());

        (await monitor.RunAsync()).ShouldContainKey("sydney");
        (await monitor.RunAsync()).ShouldContainKey("sydney");

        await traffic.Received(1).GetIncidentsAsync(
            Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>());
        await feed.Received(1).GetEventsAsync(
            Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ConcurrentSameMarketCallsShareOneProbe()
    {
        var scheduleRepository = Substitute.For<IScheduleRepository>();
        var traffic = Substitute.For<ITrafficProvider>();
        var feed = Substitute.For<IOfficialTrafficFeed>();
        var metrics = Substitute.For<IBroadcastMetrics>();
        var schedule = Schedule("sydney");
        scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([schedule]));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        traffic.GetIncidentsAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProbeAfterReleaseAsync(started, release));
        feed.SourceId.Returns("official");
        feed.GetEventsAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OfficialTrafficEvent>>.Success([]));
        var monitor = new ValidityMonitorService(scheduleRepository, traffic, [feed], metrics,
            TimeProvider.System, Substitute.For<ILogger<ValidityMonitorService>>());

        var first = monitor.RunAsync();
        await started.Task;
        var second = monitor.RunAsync();
        await Task.Yield();
        release.SetResult(true);
        await Task.WhenAll(first, second);

        await traffic.Received(1).GetIncidentsAsync(
            Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>());
    }

    private static async Task<Result<IEnumerable<TrafficIncident>>> ProbeAfterReleaseAsync(
        TaskCompletionSource<bool> started, TaskCompletionSource<bool> release)
    {
        started.SetResult(true);
        await release.Task;
        return Result<IEnumerable<TrafficIncident>>.Success([]);
    }

    private static MarketSchedule Schedule(string market)
    {
        return MarketSchedule.Create(
            market, market, "Station", "Australia/Sydney", "0 * * * *", true,
            CoverageArea.Create(-33.86, 151.2, 10).Value,
            "voice", "output", 30, 5, 3, DateTime.UtcNow, DateTime.UtcNow).Value;
    }
}
