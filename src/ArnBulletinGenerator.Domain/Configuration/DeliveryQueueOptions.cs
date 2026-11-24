namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>Operational controls for the asynchronous playout delivery queue.</summary>
public sealed class DeliveryQueueOptions
{
    public const string SectionName = "DeliveryQueue";

    /// <summary>
    ///     Master switch for queue-based delivery. Disabled by default so existing deployments
    ///     retain the synchronous delivery path until the queue worker is explicitly rolled out.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Maximum wall-clock time a worker may spend on one delivery attempt.</summary>
    public int DeadlineSeconds { get; set; } = 30;

    /// <summary>Maximum durable attempts before a pending delivery is failed.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Queue visibility timeout used by the host for a delivery command.</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 90;
}
