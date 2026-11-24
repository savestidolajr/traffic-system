using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Table-storage metadata for cached filler audio clips. Blob I/O (the actual audio bytes)
///     lives in <see cref="IFillerAudioCacheService" />, matching the convention that every
///     <c>TableStorage*Repository</c> in this codebase is Table Storage only.
/// </summary>
public interface IFillerAudioCacheRepository
{
    Task<Result<Option<FillerAudioClip>>> GetAsync(string fillerStatementId, string cacheKey, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(FillerAudioClip clip, CancellationToken cancellationToken = default);

    /// <summary>Deletes every cached-clip row for this statement (across all voice configs) and returns the deleted cache keys, so the caller can also delete the matching blobs.</summary>
    Task<Result<IReadOnlyList<string>>> DeleteAllForStatementAsync(string fillerStatementId, CancellationToken cancellationToken = default);
}
