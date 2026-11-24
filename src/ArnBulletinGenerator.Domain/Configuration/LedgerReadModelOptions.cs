namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Controls the incremental ledger bucket and predecessor read models. Both default to OFF for
///     the same reason as the job read models: buckets covering only the jobs written since a
///     deploy answer a ledger read with a partial history. Enable after the backfill has compared.
/// </summary>
public sealed record LedgerReadModelOptions
{
    public const string SectionName = "Jobs:Ledger";
    public bool WriteEnabled { get; init; }
    public bool ReadEnabled { get; init; }
    public bool KillSwitch { get; init; }
    public bool CompareEnabled { get; init; }
    /// <summary>Maximum scalar bucket rows returned by one cursor page.</summary>
    public int PageSize { get; init; } = 500;
    public int FoldWindowDays { get; init; } = 30;
}
