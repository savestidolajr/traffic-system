using ArnBulletinGenerator.Infrastructure;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Startup;

public sealed class StartupSafetyTests
{
    [Theory]
    [InlineData(true, "Admin", true)]
    [InlineData(true, "Operator", true)]
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "Admin", false)]
    [InlineData(false, "", false)]
    [InlineData(false, null, false)]
    [InlineData(true, "  ", false)]
    [InlineData(false, "  ", false)]
    public void IsUnsafeDeliveryConfig_ReturnsExpected(bool zettaEnabled, string? devFallbackRole, bool expected)
    {
        var result = StartupSafety.IsUnsafeDeliveryConfig(zettaEnabled, devFallbackRole);
        result.ShouldBe(expected);
    }
}
