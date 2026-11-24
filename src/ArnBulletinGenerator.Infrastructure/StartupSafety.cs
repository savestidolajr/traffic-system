namespace ArnBulletinGenerator.Infrastructure;

/// <summary>
///     Startup safety validations that must fail fast rather than silently deliver.
///     Extracted as a testable predicate so environment integrity checks are
///     verifiable without booting the full Functions host.
/// </summary>
public static class StartupSafety
{
    /// <summary>
    ///     Returns <c>true</c> when the deployment configuration is unsafe:
    ///     delivery to playout (Zetta enabled) combined with a blanket dev auth bypass
    ///     (<paramref name="devFallbackRole" /> non-empty). These settings must never
    ///     coexist outside a developer's local sandbox.
    /// </summary>
    public static bool IsUnsafeDeliveryConfig(bool zettaEnabled, string? devFallbackRole)
        => zettaEnabled && !string.IsNullOrWhiteSpace(devFallbackRole);

    /// <summary>
    ///     Azure name segments that positively identify a non-development deployment.
    ///     Derived from the naming convention in <c>deployment/main.bicep</c>
    ///     (<c>IBP-{region}-{DEV|TST|PRD|SBX}-FNA-APP01</c>).
    /// </summary>
    private static readonly string[] NonDevSiteSegments = ["-TST-", "-PRD-"];

    /// <summary>
    ///     Returns <c>true</c> when the blanket dev authorization bypass is active on a site whose
    ///     name identifies it as test or production.
    ///     <para>
    ///         The bypass synthesises an Admin principal for any caller arriving without an
    ///         <c>X-MS-CLIENT-PRINCIPAL</c> header, which is exactly what an anonymous internet
    ///         request looks like through the Static Web App. Production carried the setting
    ///         (empty, therefore inert) until 2026-07-28; it was one non-empty value away from
    ///         becoming an anonymous admin API, with nothing to fail the deploy.
    ///     </para>
    ///     <para>
    ///         <paramref name="siteName" /> is <c>WEBSITE_SITE_NAME</c>, which Azure always sets and
    ///         which no app setting can remove — deliberately a stronger signal than a configuration
    ///         value the same mistake could delete. An ABSENT site name means a developer's machine
    ///         or the E2E host, where the bypass is the point, so absence stays permissive: this
    ///         only ever refuses a positively-identified test or production deployment.
    ///     </para>
    /// </summary>
    public static bool IsUnsafeAuthBypass(string? siteName, string? devFallbackRole)
    {
        if (string.IsNullOrWhiteSpace(devFallbackRole) || string.IsNullOrWhiteSpace(siteName))
            return false;

        foreach (string segment in NonDevSiteSegments)
        {
            if (siteName.Contains(segment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
