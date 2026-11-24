using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Settings for the daily audit-evidence export. The platform is the system of
///     record for scripts and approval evidence (CC-002 / ARN technology lead design review,
///     10 Jun 2026): the job table is snapshotted to blob storage, which carries
///     soft delete, versioning, and point-in-time restore — protections Azure
///     Tables do not have.
/// </summary>
public sealed class AuditExportOptions
{
    public const string SectionName = "AuditExport";

    /// <summary>Disable to skip the export tick entirely (e.g. local development).</summary>
    public bool Enabled { get; set; } = true;

    [Required] public string ContainerName { get; set; } = "audit-exports";
}
