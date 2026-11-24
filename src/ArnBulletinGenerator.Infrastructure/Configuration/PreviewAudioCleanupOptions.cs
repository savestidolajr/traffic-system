namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
/// Bounds the prospective cleanup sweep for synchronous preview audio. The minimum retention is
/// deliberately seven days; a bad setting cannot turn this into an immediate delete policy.
/// </summary>
public sealed class PreviewAudioCleanupOptions
{
    public const string SectionName = "PreviewAudioCleanup";

    /// <summary>Whether the timer sweep and URL pin writes are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum age before a preview blob is eligible for deletion.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Maximum blobs inspected by one timer invocation.</summary>
    public int MaxBlobsPerRun { get; set; } = 200;

    /// <summary>How long a generated URL remains protected from a cleanup sweep.</summary>
    public int ActiveUrlPinHours { get; set; } = 1;

    internal int EffectiveRetentionDays => Math.Max(7, RetentionDays);
    internal int EffectiveMaxBlobsPerRun => Math.Max(1, MaxBlobsPerRun);
    internal TimeSpan EffectiveActiveUrlPinDuration =>
        TimeSpan.FromHours(Math.Max(1, ActiveUrlPinHours));
}
