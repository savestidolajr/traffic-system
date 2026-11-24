using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Persistence for standing-guidance directives (Layer 2 of the self-service guidance
///     model). Candidate operator memories (Layer 3) reuse the existing
///     <see cref="IFutureLessonRepository" /> as their store; promotion is an admin action
///     that writes a new <see cref="GuidanceEntry" /> with a "promoted:" source. Composition
///     (precedence, cap, sanitisation) lives in <see cref="Services.GuidanceComposer" />, not
///     here, so it stays pure and testable.
/// </summary>
public interface IGuidanceRepository
{
    /// <summary>
    ///     Fetches every directive in the scopes applicable to a bulletin: all Global, the
    ///     market's, and (when present) the station's. The caller composes/ranks/caps via
    ///     <see cref="Services.GuidanceComposer" />.
    /// </summary>
    Task<Result<IReadOnlyList<GuidanceEntry>>> GetForScopesAsync(
        string market, Option<string> station, CancellationToken ct = default);

    /// <summary>Lists every directive in one scope (admin/operator management view).</summary>
    Task<Result<IReadOnlyList<GuidanceEntry>>> ListAsync(
        GuidanceScope scope, string scopeId, CancellationToken ct = default);

    /// <summary>Creates or replaces a directive (also serves enable/disable — caller sends the full entry).</summary>
    Task<Result> UpsertAsync(GuidanceEntry entry, CancellationToken ct = default);

    /// <summary>
    ///     Removes a directive by its scope coordinates and id, reporting whether a row was actually
    ///     removed. Azure Table no-ops an unconditional delete of a missing row, so a void result
    ///     would let a wrong-scope or stale delete look like success; returning <c>false</c> lets the
    ///     HTTP layer answer 404 when nothing matched.
    /// </summary>
    Task<Result<bool>> DeleteAsync(GuidanceScope scope, string scopeId, string id, CancellationToken ct = default);
}
