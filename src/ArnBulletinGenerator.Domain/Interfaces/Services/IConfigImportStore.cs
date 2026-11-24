using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Persistence for pending configuration imports awaiting a second approver (the prod
///     two-person posture — mirrors the two-gate production deploy approval). Payloads are
///     opaque JSON strings so the store stays free of HTTP-layer contract types; resolution
///     moves the envelope to an applied/rejected archive rather than deleting it, preserving
///     the approval evidence trail (who requested, who resolved, what was in the bundle).
/// </summary>
public interface IConfigImportStore
{
    Task<Result> SavePendingAsync(PendingConfigImport import, CancellationToken cancellationToken = default);

    /// <summary>All pending imports, oldest first.</summary>
    Task<Result<IReadOnlyList<PendingConfigImport>>> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<Result<Option<PendingConfigImport>>> GetPendingAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Archives a pending import as resolved (<paramref name="resolution" /> is
    ///     "applied" or "rejected") and removes it from the pending set.
    /// </summary>
    Task<Result> ResolveAsync(
        string id, string resolution, string resolvedBy, string resolvedByObjectId,
        CancellationToken cancellationToken = default);
}
