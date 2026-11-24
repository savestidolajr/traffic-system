using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Operator → market assignments (Workshop 2 / SDD §4.2): a non-empty market list
///     narrows an operator to those markets. Admins and operators without a row, or with an
///     empty market list, remain unrestricted; Entra app roles are the access-control boundary.
/// </summary>
public interface IOperatorAssignmentRepository
{
    Task<Result<Option<OperatorAssignment>>> GetAsync(string objectId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OperatorAssignment>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result> UpsertAsync(OperatorAssignment assignment, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string objectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records that a Static Web Apps per-app <paramref name="swaUserId" /> belongs to the Entra
    ///     directory object <paramref name="objectId" />. Captured at sign-in, the only moment both ids
    ///     are present together. First writer wins: an existing alias pointing at a DIFFERENT object id
    ///     is left untouched.
    /// </summary>
    Task<Result> UpsertDirectoryAliasAsync(string swaUserId, string objectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Resolves a Static Web Apps <paramref name="swaUserId" /> to the Entra object id captured at
    ///     sign-in. <see cref="Option{T}.None" /> when the alias was never recorded.
    /// </summary>
    Task<Result<Option<string>>> GetObjectIdByAliasAsync(string swaUserId, CancellationToken cancellationToken = default);
}
