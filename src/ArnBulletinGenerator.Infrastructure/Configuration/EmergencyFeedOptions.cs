namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration for the optional DataQuoll emergency-services evidence feed. The master gate
///     is deliberately off by default because this is a new external dependency and phase 1 only
///     persists and displays its records; it must not influence bulletin generation yet.
/// </summary>
public sealed class EmergencyFeedOptions
{
    public const string SectionName = "EmergencyFeeds";

    /// <summary>
    ///     Master gate for DataQuoll. Off by default so an environment opts into the new external
    ///     dependency explicitly and a missing key cannot change an existing bulletin path.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>DataQuoll API root; the v1 path is the stable public API surface.</summary>
    public string BaseUrl { get; set; } = "https://dataquoll.io/api/v1";

    /// <summary>
    ///     Bearer credential supplied per environment from Key Vault. Blank by default so no secret
    ///     is committed to source control.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    ///     Per-attempt request timeout in seconds. Eight seconds keeps this supplementary, fail-soft
    ///     feed from delaying a scheduled bulletin when the upstream is slow.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>
    ///     Incident response cache lifetime. Five minutes fits DataQuoll's 30-second-to-15-minute
    ///     agency publication cadence while keeping request volume within the Starter tier.
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>
    ///     Maximum records persisted for a job. Forty gives operators useful situational awareness
    ///     without allowing a noisy statewide response to inflate the job payload.
    /// </summary>
    public int MaxEvents { get; set; } = 40;

    /// <summary>
    ///     Maximum records requested from DataQuoll. Five hundred is the upstream limit ceiling and
    ///     ensures local filtering sees the complete bounded response rather than an arbitrary page.
    /// </summary>
    public int MaxResults { get; set; } = 500;

    /// <summary>
    ///     Keeps records rejected by the licence or category allow-lists visible in the operator
    ///     console. True by default makes suppression explicit rather than silently hiding evidence.
    /// </summary>
    public bool IncludeIneligible { get; set; } = true;

    /// <summary>
    ///     Attribution responses are reference data, so a 24-hour cache is sufficient while still
    ///     allowing licence changes to take effect daily without an app restart.
    /// </summary>
    public int AttributionCacheHours { get; set; } = 24;
}
