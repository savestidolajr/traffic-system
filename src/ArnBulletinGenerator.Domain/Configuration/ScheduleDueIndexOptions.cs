namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>Controls the additive schedule due-index read/write path.</summary>
public sealed record ScheduleDueIndexOptions
{
    public const string SectionName = "Schedules:DueIndex";

    /// <summary>Maintains a due-index row whenever a schedule's persisted next run changes.</summary>
    public bool WriteEnabled { get; init; }

    /// <summary>Reads due candidates from the due index before falling back to the schedule table.</summary>
    public bool ReadEnabled { get; init; }

    /// <summary>Disables both due-index reads and writes regardless of the other flags.</summary>
    public bool KillSwitch { get; init; }

    /// <summary>Reads the legacy schedule query alongside the index and logs candidate differences.</summary>
    public bool CompareEnabled { get; init; }

    /// <summary>
    /// How far behind the threshold an indexed sweep looks for overdue buckets. A stale or
    /// malformed indexed candidate still forces the bounded legacy fallback, so this is not a
    /// correctness cap on a valid schedule's persisted due time.
    /// </summary>
    public int LookbackMinutes { get; init; } = 15;

    /// <summary>Maximum indexed entities materialized in one due sweep.</summary>
    public int PageSize { get; init; } = 500;
}
