using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The single, shared diff-and-evict routine for filler-statement cache invalidation. Both
///     write paths (the filler-statements HTTP trigger and config import) call this so
///     invalidation cannot be bypassed by a caller that forgets it. Pure orchestration beyond the
///     injected <see cref="IFillerAudioCacheService" /> — no direct table/blob I/O.
///     Returns a list of failed invalidation attempts (empty = all succeeded).
/// </summary>
public static partial class FillerAudioCacheInvalidator
{
    /// <summary>
    ///     Diffs old vs new statements and invalidates the cache for any removed or changed
    ///     statements. Returns a list of (StatementId, Error) pairs for failures;
    ///     an empty list means all invalidations succeeded.
    /// </summary>
    public static async Task<IReadOnlyList<(string StatementId, string Error)>> InvalidateChangedAsync(
        IFillerAudioCacheService cacheService,
        IReadOnlyList<FillerStatement> oldStatements,
        IReadOnlyList<FillerStatement> newStatements,
        CancellationToken cancellationToken)
    {
        var newById = newStatements.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var failures = new List<(string, string)>();

        foreach (var old in oldStatements)
        {
            bool removedOrChanged = !newById.TryGetValue(old.Id, out var updated)
                || !string.Equals(updated.Text, old.Text, StringComparison.Ordinal);
            if (!removedOrChanged) continue;

            var result = await cacheService.InvalidateAsync(old.Id, cancellationToken);
            if (result.IsFailure)
                failures.Add((old.Id, result.Error));
        }

        return failures;
    }
}
