using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
/// Protects generated preview blobs while their returned URLs may still be active and performs a
/// bounded, idempotent sweep of expired preview artifacts.
/// </summary>
public interface IPreviewAudioCleanup
{
    Task<Result> PinAsync(
        string audioFileName,
        DateTimeOffset? pinnedUntil = null,
        CancellationToken cancellationToken = default);

    Task<Result<PreviewAudioCleanupReport>> CleanupAsync(
        CancellationToken cancellationToken = default);
}
