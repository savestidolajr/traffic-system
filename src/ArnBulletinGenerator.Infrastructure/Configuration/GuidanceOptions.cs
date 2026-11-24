namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>Feature switches and bounds for guidance management reads.</summary>
public sealed class GuidanceOptions
{
    public const string SectionName = "Guidance";

    /// <summary>
    ///     Reverts aggregate guidance reads to the legacy sequential path while a bounded batch
    ///     rollout is investigated. The default is false so the bounded path is active.
    /// </summary>
    public bool BatchReadDisabled { get; init; }

    /// <summary>Maximum concurrent guidance and candidate reads per request.</summary>
    public int MaxParallelReads { get; init; } = 4;
}
