using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Persistence for content-safety terms (banned words + allowed exceptions) used by the
///     deterministic <see cref="IContentSafetyFilter" />. Composition is additive union
///     (global ∪ market); there is no precedence/override (a market may only add, never
///     weaken). Admin-only at the API boundary (a compliance control), enforced by the
///     route role matrix — this type is pure persistence.
/// </summary>
public interface ISafetyTermRepository
{
    /// <summary>
    ///     Every term in the scopes a bulletin enforces: all Global plus the market's.
    ///     A null/blank market returns Global only (ad-hoc/global bulletins).
    /// </summary>
    Task<Result<IReadOnlyList<SafetyTerm>>> GetForMarketAsync(string? market, CancellationToken ct = default);

    /// <summary>Every term across all scopes — for the Admin management UI.</summary>
    Task<Result<IReadOnlyList<SafetyTerm>>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Creates or replaces a term (caller sends the full term).</summary>
    Task<Result> UpsertAsync(SafetyTerm term, CancellationToken ct = default);

    /// <summary>Removes a term by its scope coordinates and id.</summary>
    Task<Result> DeleteAsync(SafetyScope scope, string scopeId, string id, CancellationToken ct = default);
}
