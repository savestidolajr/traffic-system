namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Independent rollout switches for normalized versioned-library storage and bounded list
///     reads. Defaults are dual-read/dual-write and preserve the historical complete response.
///     The composition root binds this type to the <c>Libraries</c> section.
/// </summary>
public sealed class VersionedLibraryOptions
{
    public const string SectionName = "Libraries";

    /// <summary>Enables current/scalar summary reads for explicitly opted-in list queries.</summary>
    public bool SummaryListsEnabled { get; set; } = true;

    /// <summary>Enables opt-in page/cursor handling; false returns the complete legacy result.</summary>
    public bool HistoryPagingEnabled { get; set; } = true;

    /// <summary>Enables the per-library partition/current-row read model.</summary>
    public bool NormalizedStorageEnabled { get; set; } = true;

    /// <summary>Allows fallback to pre-normalization aggregate rows on misses or stale pointers.</summary>
    public bool LegacyReadEnabled { get; set; } = true;

    /// <summary>Writes the legacy aggregate row while normalized storage is being rolled out.</summary>
    public bool LegacyWriteEnabled { get; set; } = true;

    /// <summary>Allows full-history aggregate reads. Disabling is a deliberate emergency switch.</summary>
    public bool FullAggregateReadEnabled { get; set; } = true;

    /// <summary>Maximum concurrent authoritative keyed reads for bulk library lookups.</summary>
    public int BulkReadConcurrency { get; set; } = 8;

    /// <summary>Worker-local lifetime for current-summary list cache entries.</summary>
    public TimeSpan SummaryCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}
