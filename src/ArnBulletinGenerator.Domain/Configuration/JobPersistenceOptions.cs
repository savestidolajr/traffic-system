namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Feature flags for the additive job persistence read models. Every flag defaults to OFF: a
///     read model is only correct once its backfill has run for the environment it is enabled in.
///     An index holding rows for recent jobs and none for the history is worse than no index,
///     because a page read from it silently omits the jobs it has never seen. Measured on test
///     2026-09-03, where code carrying `true` defaults met an environment with no backfill and a
///     776-job history read as the 15 jobs written after the deploy. Enable per environment
///     through configuration, after the backfill and comparison, never by changing these defaults.
/// </summary>
public sealed record JobPersistenceOptions
{
    public const string SectionName = "Jobs:Persistence";

    /// <summary>Global kill switch for all additive persistence paths.</summary>
    public bool KillSwitch { get; init; }
    /// <summary>Dual-write the append-only lifecycle timeline during migration.</summary>
    public bool TimelineWriteEnabled { get; init; }
    /// <summary>Read the append-only timeline on bounded detail reads.</summary>
    public bool TimelineReadEnabled { get; init; }
    /// <summary>Dual-write large immutable payloads to the payload read model.</summary>
    public bool PayloadWriteEnabled { get; init; }
    /// <summary>Read payload references on bounded detail reads.</summary>
    public bool PayloadReadEnabled { get; init; }
    public bool ReadIndexWriteEnabled { get; init; }
    public bool ReadIndexReadEnabled { get; init; }
    public bool LastSuccessfulWriteEnabled { get; init; }
    public bool LastSuccessfulReadEnabled { get; init; }
    public bool LedgerWriteEnabled { get; init; }
    public bool LedgerReadEnabled { get; init; }
    public bool CompareEnabled { get; init; }

    /// <summary>Payloads above this size are written to the payload read model.</summary>
    public int PayloadOffloadThresholdChars { get; init; } = 24_000;

    /// <summary>Maximum number of timeline rows read for one aggregate reconstruction.</summary>
    public int TimelinePageSize { get; init; } = 500;

    /// <summary>Maximum number of ETag retries for a targeted status merge.</summary>
    public int StatusMergeRetries { get; init; } = 3;
}
