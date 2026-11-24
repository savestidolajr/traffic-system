namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Outcome of one bounded preview artifact sweep.</summary>
public sealed record PreviewAudioCleanupReport(
    int Examined,
    int Deleted,
    int Pinned,
    int Skipped,
    bool ReachedLimit);
