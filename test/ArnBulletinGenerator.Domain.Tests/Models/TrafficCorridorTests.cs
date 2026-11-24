using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class TrafficCorridorTests
{
    private static TrafficCorridor CreateCorridor(
        Option<int> driveTime = default,
        Option<int> normalDriveTime = default)
    {
        return new TrafficCorridor(
            "M4",
            1,
            1,
            5000,
            100,
            "congestion",
            "Heavy traffic",
            [],
            DriveTimeMinutes: driveTime,
            NormalDriveTimeMinutes: normalDriveTime);
    }

    [Fact]
    public void DelayMinutes_BothPresent_ReturnsDifference()
    {
        var corridor = CreateCorridor(
            Option<int>.Some(25),
            Option<int>.Some(15));

        corridor.DelayMinutes.HasValue.ShouldBeTrue();
        corridor.DelayMinutes.Value.ShouldBe(10);
    }

    [Fact]
    public void DelayMinutes_OnlyCurrentPresent_ReturnsNone()
    {
        var corridor = CreateCorridor(Option<int>.Some(25));

        corridor.DelayMinutes.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void DelayMinutes_NeitherPresent_ReturnsNone()
    {
        var corridor = CreateCorridor();

        corridor.DelayMinutes.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void DelayMinutes_ZeroDelay_ReturnsSomeZero()
    {
        var corridor = CreateCorridor(
            Option<int>.Some(15),
            Option<int>.Some(15));

        corridor.DelayMinutes.HasValue.ShouldBeTrue();
        corridor.DelayMinutes.Value.ShouldBe(0);
    }
}
