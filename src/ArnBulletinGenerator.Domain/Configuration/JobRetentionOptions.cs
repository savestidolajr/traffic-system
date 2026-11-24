namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Safety-first retention policy. Purging is disabled unless <see cref="RetentionDays" /> is
///     positive and <see cref="Disable" /> is explicitly false.
/// </summary>
public sealed record JobRetentionOptions
{
    public const string SectionName = "Jobs:Retention";

    /// <summary>Age after which settled jobs become candidates. Defaults to thirty days.</summary>
    public int RetentionDays { get; init; } = 30;

    /// <summary>Highest-precedence deletion kill switch; defaults to true for migration safety.</summary>
    public bool Disable { get; init; } = true;

    /// <summary>Plan and report candidates without deleting anything.</summary>
    public bool DryRun { get; init; }

    /// <summary>Run the protection audit without deleting anything.</summary>
    public bool AuditOnly { get; init; }

    /// <summary>Rows examined in one timer invocation.</summary>
    public int PageSize { get; init; } = 200;

    /// <summary>Maximum rows examined per invocation; never exceeds one thousand.</summary>
    public int MaxRowsPerRun { get; init; } = 1000;

    /// <summary>Continuation token supplied by the previous checkpoint.</summary>
    public string? Checkpoint { get; init; }

    /// <summary>Settled rows remain protected for this period after the last transition.</summary>
    public int GracePeriodHours { get; init; } = 2;

    public int ContinuityWindowDays { get; init; } = 30;
    public int FallbackWindowDays { get; init; } = 30;
    public int AuditWindowDays { get; init; } = 180;
    public int AudioWindowDays { get; init; } = 30;
    public int OperatorHistoryWindowDays { get; init; } = 180;

    /// <summary>When true, bounded orphan references are reported and reconciled.</summary>
    public bool ReconcileOrphans { get; init; } = true;
}
