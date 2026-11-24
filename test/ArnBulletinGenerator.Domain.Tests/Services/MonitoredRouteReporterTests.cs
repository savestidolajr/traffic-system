using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class MonitoredRouteReporterTests
{
    [Fact]
    public async Task GetReportsAsync_ReturnsConfiguredRoutesSortedByDelay()
    {
        var routing = new FakeRoutingProvider([
            new DriveTimeResult(30, 20, 18.0),
            new DriveTimeResult(25, 10, 12.0)
        ]);
        var options = OptionsFor("sydney", [Route("M4"), Route("M5")]);

        var result = await MonitoredRouteReporter.GetReportsAsync(routing, "sydney", options);

        result.Select(r => r.Name).ShouldBe(["M5", "M4"]);
        result[0].DelayMinutes.ShouldBe(15);
        result[1].DelayMinutes.ShouldBe(10);
    }

    [Fact]
    public async Task GetReportsAsync_SkipsFailedAndDisabledRoutes()
    {
        var routing = new FakeRoutingProvider([new DriveTimeResult(30, 20, 18.0)]);
        var options = OptionsFor("sydney", [Route("off", enabled: false), Route("on")]);

        var result = await MonitoredRouteReporter.GetReportsAsync(routing, "Sydney", options);

        result.ShouldHaveSingleItem();
        result[0].Name.ShouldBe("on");
    }

    [Fact]
    public async Task GetReportsAsync_ReturnsEmptyWhenMarketUnconfigured()
    {
        var result = await MonitoredRouteReporter.GetReportsAsync(
            new FakeRoutingProvider([]), "brisbane", OptionsFor("sydney", [Route("M4")]));

        result.ShouldBeEmpty();
    }

    private static MonitoredRoutesOptions OptionsFor(string market, List<MonitoredRouteDefinition> routes)
    {
        return new MonitoredRoutesOptions
        {
            Markets = new Dictionary<string, List<MonitoredRouteDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                [market] = routes
            }
        };
    }

    private static MonitoredRouteDefinition Route(string name, bool enabled = true)
    {
        return new MonitoredRouteDefinition
        {
            Name = name,
            Origin = new CoveragePoint(-33.8, 151.0),
            Destination = new CoveragePoint(-33.9, 151.2),
            Enabled = enabled
        };
    }

    private sealed class FakeRoutingProvider(Queue<DriveTimeResult> results) : IRoutingProvider
    {
        public FakeRoutingProvider(IEnumerable<DriveTimeResult> results) : this(new Queue<DriveTimeResult>(results))
        {
        }

        public Task<Result<DriveTimeResult>> GetDriveTimeAsync(
            (double Lat, double Lng) origin,
            (double Lat, double Lng) destination,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(results.Count == 0
                ? Result<DriveTimeResult>.Failure("no route")
                : Result<DriveTimeResult>.Success(results.Dequeue()));
        }
    }
}
