namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Controls the liveness and dependency-health probe cadence. Liveness uses the cached
///     dependency snapshot by default; the legacy inline mode is retained as an emergency rollback.
/// </summary>
public sealed class HealthOptions
{
    public const string SectionName = "Health";

    public HealthDependenciesOptions Dependencies { get; set; } = new();

    /// <summary>
    ///     When true, <c>/api/health</c> performs the original sequential provider probes. The
    ///     default false makes liveness constant-time and provider-independent.
    /// </summary>
    public bool LegacyInlineProbes { get; set; } = false;
}

/// <summary>Probe cadence and timeout settings for the dependency diagnostic endpoint.</summary>
public sealed class HealthDependenciesOptions
{
    /// <summary>Maximum age of a dependency snapshot before a diagnostic request refreshes it.</summary>
    public int RefreshSeconds { get; set; } = 30;

    /// <summary>Per-provider timeout used by the dependency diagnostic probes.</summary>
    public int ProbeTimeoutSeconds { get; set; } = 5;
}
