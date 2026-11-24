namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
/// Controls the bounded read path for the parser coverage ledger. Explicit market scopes are
/// preferred because an operator-wide Table Storage query cannot be indexed by a single partition.
/// </summary>
public sealed class CoverageOptions
{
    public const string SectionName = "Coverage";

    /// <summary>
    /// Enables the range-bounded coverage read path. Disabling this is an explicit rollback switch;
    /// callers still have to provide a market or a configured bounded market set.
    /// </summary>
    public bool BoundedReadsEnabled { get; init; } = true;

    /// <summary>Optional bounded market set for trusted operator-wide coverage views.</summary>
    public List<string> BoundedMarkets { get; init; } = [];

    /// <summary>Maximum markets accepted in one coverage aggregation request.</summary>
    public int MaxMarkets { get; init; } = 32;
}
