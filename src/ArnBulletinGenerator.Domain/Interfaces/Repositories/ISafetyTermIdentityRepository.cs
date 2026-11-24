using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Point identity index for normalized, scoped safety terms.</summary>
public interface ISafetyTermIdentityRepository
{
    bool ReadEnabled { get; }
    bool WriteEnabled { get; }
    bool KillSwitch { get; }

    Task<Result<Option<SafetyTermIdentity>>> FindAsync(
        SafetyScope scope, string scopeId, SafetyTermKind kind, string term,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically reserves an identity for a candidate canonical id. A concurrent loser receives
    /// the winner's pointer, so callers never create a second canonical row for one identity.
    /// </summary>
    Task<Result<SafetyTermIdentity>> ReserveAsync(
        SafetyScope scope, string scopeId, SafetyTermKind kind, string term,
        string candidateTermId, string createdBy, DateTime createdAtUtc,
        CancellationToken ct = default);

    Task<Result> UpsertAsync(SafetyTermIdentity identity, CancellationToken ct = default);

    /// <summary>Removes all pointers in one known scope that reference a canonical term id.</summary>
    Task<Result> DeleteForTermAsync(
        SafetyScope scope, string scopeId, string termId, CancellationToken ct = default);

    Task<Result<SafetyTermIdentityBackfillPage>> ReconcileAsync(
        IReadOnlyList<SafetyTerm> canonicalTerms, CancellationToken ct = default);

    Task<Result<Option<SafetyTermIdentity>>> FindByCanonicalIdAsync(
        SafetyScope scope, string scopeId, string termId, CancellationToken ct = default);
}
