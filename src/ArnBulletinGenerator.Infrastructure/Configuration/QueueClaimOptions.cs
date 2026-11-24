namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>Feature flag for atomic queue-job claims and lease renewal.</summary>
public sealed class QueueClaimOptions
{
    public const string SectionName = "Queue:Claims";

    /// <summary>
    ///     Enables the radioJobClaims lease. The default is false to preserve the existing
    ///     LastUpdatedAt-based queue deduplication behaviour until the real-table guardrails pass.
    /// </summary>
    public bool Enabled { get; set; }
}
