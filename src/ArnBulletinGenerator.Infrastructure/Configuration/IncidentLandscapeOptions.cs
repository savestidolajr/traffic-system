namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
/// Controls the additive incident-landscape top-N read model and bounded inventory fan-out.
/// Legacy rows remain the source of truth and are used for explicit complete views or when the
/// read model is unavailable.
/// </summary>
public sealed class IncidentLandscapeOptions
{
    public const string SectionName = "IncidentLandscape";

    /// <summary>Reads the bounded top-N projection when true; false is an explicit rollback.</summary>
    public bool ReadModelEnabled { get; init; } = true;

    /// <summary>Dual-writes the top-N projection alongside the legacy aggregate rows.</summary>
    public bool WriteReadModelEnabled { get; init; } = true;

    /// <summary>Enables bounded omitted-market inventory reads.</summary>
    public bool BoundedReadsEnabled { get; init; } = true;

    /// <summary>Optional configured market set for trusted operator-wide inventory views.</summary>
    public List<string> BoundedMarkets { get; init; } = [];


    /// <summary>Maximum entries retained by one market's top-N projection.</summary>
    public int TopN { get; init; } = 50;

    /// <summary>Maximum markets an inventory request may fan out to.</summary>
    public int MaxInventoryMarkets { get; init; } = 13;

    /// <summary>Default page size for explicit complete landscape views.</summary>
    public int CompletePageSize { get; init; } = 100;

    /// <summary>Maximum legacy rows read by a bounded fallback page when the projection is unavailable.</summary>
    public int LegacyFallbackRows { get; init; } = 1_000;
}
