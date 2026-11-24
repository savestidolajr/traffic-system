using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Reads the current Entra principals holding the application's Operator or Admin roles.
///     Implementations must query the directory live; no directory data is persisted.
/// </summary>
public interface IOperatorDirectory
{
    Task<Result<IReadOnlyList<DirectoryPrincipal>>> ListRoleHoldersAsync(
        CancellationToken cancellationToken = default);
}
