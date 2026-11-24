using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ExponentialMovingAverageTests
{
    [Fact]
    public void FirstObservation_ReturnsMeasurement()
    {
        ExponentialMovingAverage.Apply(100, 50, true).ShouldBe(50);
    }

    [Fact]
    public void SubsequentObservation_BlendsByAlpha()
    {
        // 0.3 * 50 + 0.7 * 100 = 85
        double result = ExponentialMovingAverage.Apply(100, 50, false);
        result.ShouldBe(85);
    }

    [Fact]
    public void NoChange_ReturnsCurrent()
    {
        ExponentialMovingAverage.Apply(100, 100, false).ShouldBe(100);
    }

    [Fact]
    public void AlphaZero_IgnoresMeasurement()
    {
        ExponentialMovingAverage.Apply(100, 50, false, 0).ShouldBe(100);
    }

    [Fact]
    public void AlphaOne_FullyAdoptsMeasurement()
    {
        ExponentialMovingAverage.Apply(100, 50, false, 1).ShouldBe(50);
    }

    [Fact]
    public void FirstObservation_IgnoresAlpha()
    {
        ExponentialMovingAverage.Apply(999, 42, true, 0.1).ShouldBe(42);
    }

    [Fact]
    public void DefaultAlpha_IsPointThree()
    {
        // Default alpha=0.3: 0.3*200 + 0.7*100 = 60 + 70 = 130
        double result = ExponentialMovingAverage.Apply(100, 200, false);
        result.ShouldBe(130);
    }
}
