namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Governance posture for configuration bundle imports (the admin export/import surface).
///     <see cref="RequireSecondApprover" /> mirrors the two-gate production deploy approval:
///     when true, an apply request parks the bundle as a pending import that a DIFFERENT
///     admin must approve before it is applied. MUST be true in prod (see deployment
///     bicepparams); dev/test apply directly.
/// </summary>
public sealed class ConfigImportOptions
{
    public const string SectionName = "ConfigImport";

    /// <summary>When true, applying an import requires approval by a second (different) admin.</summary>
    public bool RequireSecondApprover { get; set; }

    /// <summary>Blob container holding pending and resolved import envelopes.</summary>
    public string ContainerName { get; set; } = "config-imports";
    /// <summary>
    ///     Disables the pending-import metadata index and restores body enumeration for emergency
    ///     rollback. Pending envelopes remain the source of truth.
    /// </summary>
    public bool IndexDisabled { get; set; }
}
