namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Markets an operator may act on. <see cref="ObjectId" /> is the AAD object id (oid)
///     of the operator's principal — stable across token renewals and name changes.
/// </summary>
/// <param name="UserPrincipalName">
///     Sign-in name as presented in the token at the person's last sign-in. Null when the row
///     predates sign-in capture or was created by an admin or a config import, which carry no
///     token. Never a substitute for a directory read: it records what we were told, not what
///     Entra currently says.
/// </param>
/// <param name="RolesAtSignIn">
///     App roles the token carried at the last sign-in. Null (not empty) when no sign-in has been
///     captured for this row, so a caller can tell "no roles" from "never seen".
/// </param>
/// <param name="LastSignInUtc">When the sign-in above was observed; null when none has been.</param>
public sealed record OperatorAssignment(
    string ObjectId,
    string DisplayName,
    IReadOnlyList<string> Markets,
    DateTime UpdatedAtUtc,
    string UpdatedBy,
    string? UserPrincipalName = null,
    IReadOnlyList<string>? RolesAtSignIn = null,
    DateTime? LastSignInUtc = null);
