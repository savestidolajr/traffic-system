using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     The dev authorization bypass synthesises an Admin principal for any caller arriving
///     without an <c>X-MS-CLIENT-PRINCIPAL</c> header — exactly what an anonymous internet
///     request looks like through the Static Web App. Production carried the setting (empty,
///     therefore inert) until 2026-07-28 with nothing to fail a deploy if it acquired a value.
/// </summary>
public sealed class StartupSafetyTests
{
    [Theory]
    [InlineData("IBP-AUE-PRD-FNA-APP01")]
    [InlineData("IBP-AUE-TST-FNA-APP01")]
    [InlineData("ibp-aue-prd-fna-app01")]
    public void AuthBypass_OnTestOrProdSite_IsUnsafe(string siteName)
    {
        StartupSafety.IsUnsafeAuthBypass(siteName, "Admin").ShouldBeTrue();
    }

    [Theory]
    [InlineData("IBP-AUE-DEV-FNA-APP01")]
    [InlineData("IBP-AUE-SBX-FNA-APP01")]
    public void AuthBypass_OnNonProductionSite_IsAllowed(string siteName)
    {
        // The operator console on dev depends on this. Closing it here was explicitly declined.
        StartupSafety.IsUnsafeAuthBypass(siteName, "Admin").ShouldBeFalse();
    }

    [Fact]
    public void AuthBypass_WithNoSiteName_IsAllowed()
    {
        // A developer's machine and the E2E host set no WEBSITE_SITE_NAME. Absence must stay
        // permissive, so the guard only ever refuses a POSITIVELY identified test/prod site
        // rather than failing closed on an environment it cannot recognise.
        StartupSafety.IsUnsafeAuthBypass(null, "Admin").ShouldBeFalse();
        StartupSafety.IsUnsafeAuthBypass("", "Admin").ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoBypassConfigured_IsAlwaysSafe(string? role)
    {
        // The setting being PRESENT but empty is how prod ships; that must not block startup.
        StartupSafety.IsUnsafeAuthBypass("IBP-AUE-PRD-FNA-APP01", role).ShouldBeFalse();
    }

    [Fact]
    public void ZettaDelivery_WithBypass_RemainsUnsafe()
    {
        StartupSafety.IsUnsafeDeliveryConfig(zettaEnabled: true, devFallbackRole: "Admin").ShouldBeTrue();
        StartupSafety.IsUnsafeDeliveryConfig(zettaEnabled: true, devFallbackRole: "").ShouldBeFalse();
        StartupSafety.IsUnsafeDeliveryConfig(zettaEnabled: false, devFallbackRole: "Admin").ShouldBeFalse();
    }
}
