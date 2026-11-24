namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Controls durable activity-audit queue publishing and consumption. Disabled by default so
///     existing deployments continue to write audit records inline.
/// </summary>
public sealed class AuditQueueOptions
{
    public const string SectionName = "Audit:Queue";

    /// <summary>When false, middleware records successful mutations directly in Table Storage.</summary>
    public bool Enabled { get; set; } = false;
}
