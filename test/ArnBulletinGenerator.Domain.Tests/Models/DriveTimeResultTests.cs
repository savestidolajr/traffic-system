using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class DriveTimeResultTests
{
    [Fact]
    public void DelayMinutes_PositiveDelay_ReturnsDifference()
    {
        var result = new DriveTimeResult(25, 15, 10.0);
        result.DelayMinutes.ShouldBe(10);
    }

    [Fact]
    public void DelayMinutes_ZeroDelay_ReturnsZero()
    {
        var result = new DriveTimeResult(15, 15, 10.0);
        result.DelayMinutes.ShouldBe(0);
    }

    [Fact]
    public void DelayMinutes_FasterThanTypical_ReturnsNegative()
    {
        var result = new DriveTimeResult(10, 15, 10.0);
        result.DelayMinutes.ShouldBe(-5);
    }
}
