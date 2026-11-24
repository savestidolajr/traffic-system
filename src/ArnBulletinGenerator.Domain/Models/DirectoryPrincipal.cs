namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A user principal that holds one or more application roles in the Entra tenant.
/// </summary>
public sealed record DirectoryPrincipal(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    IReadOnlyList<string> Roles,
    string Source);
