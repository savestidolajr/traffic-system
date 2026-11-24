namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
/// Rollout settings for the per-broadcast work budget. Cancellation and byte release are always
/// enforced; the legacy switch only restores the previous attempt counts while the bounded policy
/// is measured in each environment.
/// </summary>
public sealed record BroadcastWorkBudgetOptions
{
    public bool UseLegacyAttemptPolicy { get; init; }

    /// <summary>Bounded total generation attempts for one broadcast operation.</summary>
    public int MaxGenerationAttempts { get; init; } = 2;

    /// <summary>Bounded total synthesis attempts for one broadcast operation.</summary>
    public int MaxSynthesisAttempts { get; init; } = 2;

    /// <summary>Previous generation-attempt count (one initial attempt plus configured retries).</summary>
    public int LegacyGenerationAttempts { get; init; } = 4;

    /// <summary>Previous synthesis-attempt count.</summary>
    public int LegacySynthesisAttempts { get; init; } = 3;

    /// <summary>Hard wall-clock ceiling for one broadcast operation.</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(240);
}
