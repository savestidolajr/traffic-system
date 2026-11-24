using System.Text.Json;
using ArnBulletinGenerator.Application.Access;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Functions.Middleware;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Static Web Apps custom-auth roles bridge. SWA receives the AAD app-role claim
///     in the sign-in token, but only forwards <c>userRoles</c> to linked backends.
///     This rolesSource function copies known Arn app roles from token claims into
///     SWA custom roles at sign-in time.
///
///     The endpoint is anonymous by design — Easy Auth <c>excludedPaths</c> exempts
///     <c>/api/GetRoles</c> in every environment and SWA's route table lets <c>/api/*</c> through,
///     because the rolesSource callback must work before a session exists. That makes the
///     payload UNAUTHENTICATED: anyone can POST any oid/name/roles, and the path is also served
///     directly on the Function App hostname (verified live), which bypasses SWA's route table
///     entirely.
///
///     The payload stays untrusted. Its <c>claims</c> are still used to build the roles response
///     that SWA's own sign-in flow consumes, but nothing from the payload is ever persisted: the
///     only writes this endpoint performs come from a cryptographically validated sign-in.
///     <c>accessToken</c> is checked by <see cref="SwaRolesSourceTokenValidator" /> and, only on
///     success, the token's <c>oid</c>, name, and roles register the operator, and the payload's
///     <c>userId</c> (harmless on its own; it is the key SWA will present on every later
///     <c>/api/*</c> call) is aliased to that <c>oid</c> so proxied API calls can resolve market
///     scope. An unverifiable sign-in records nothing — the roles response is returned unchanged.
///
///     This honours bug-bash 2026-08-05 finding 10 rather than reverting it: an attacker still
///     cannot forge staff names, Admin badges, or sign-in rows, because they cannot mint an
///     access token that validates against AAD. The remaining sign-in-row sources — the Entra
///     read (<c>IOperatorDirectory</c>), the admin's assignment PUT, and <c>/operators/me</c>
///     (whose principal IS authenticated by Easy Auth / the SWA proxy) — are unchanged.
/// </summary>
public sealed class RolesSourceHttpTrigger(
    SwaRolesSourceTokenValidator validator,
    OperatorRegistrar registrar,
    IOperatorAssignmentRepository repository,
    TimeProvider timeProvider,
    ILogger<RolesSourceHttpTrigger> logger,
    IMarketAccessCacheInvalidator? cacheInvalidator = null)
{
    private const string RolesClaim = "roles";
    private const string LegacyRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    private static readonly IReadOnlyDictionary<string, string> AllowedRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [AuthorizationMiddleware.RoleOperator] = AuthorizationMiddleware.RoleOperator,
        [AuthorizationMiddleware.RoleAdmin] = AuthorizationMiddleware.RoleAdmin,
    };

    [Function("GetRoles")]
    public async Task<HttpResponseData> GetRolesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "GetRoles")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        string body = await new StreamReader(req.Body).ReadToEndAsync(ctx.CancellationToken);
        using JsonDocument? doc = ParseBody(body);
        IReadOnlyList<string> roles = doc is null ? [] : ExtractAllowedRoles(doc.RootElement);

        logger.LogInformation("Mapped {RoleCount} AAD app role(s) into SWA userRoles", roles.Count);

        // Registration is a side effect of a real sign-in, not of the roles response: SWA's
        // sign-in flow consumes the response below, which must keep working even when every new
        // step fails. What gets recorded comes only from the validated token (see
        // RecordVerifiedSignInAsync), never from the payload.
        if (doc is not null)
            await RecordVerifiedSignInAsync(doc.RootElement, ctx.CancellationToken);

        return await HttpResponseHelper.OkJsonAsync(req, new RolesSourceResponse(roles));
    }

    /// <summary>
    ///     Persists a verified sign-in: the operator row keyed by the token's <c>oid</c> and a
    ///     <c>userId → oid</c> alias so later proxied API calls resolve market scope. Fail-soft
    ///     by contract: any validation or storage fault is logged at Warning and swallowed, so
    ///     the roles response (and therefore SWA sign-in) is never affected.
    /// </summary>
    private async Task RecordVerifiedSignInAsync(JsonElement root, CancellationToken cancellationToken)
    {
        try
        {
            string? accessToken = root.TryGetProperty("accessToken", out var accessTokenEl) && accessTokenEl.ValueKind == JsonValueKind.String
                ? accessTokenEl.GetString()
                : null;
            string? userId = root.TryGetProperty("userId", out var userIdEl) && userIdEl.ValueKind == JsonValueKind.String
                ? userIdEl.GetString()
                : null;

            var verified = await validator.ValidateAsync(accessToken, cancellationToken);
            if (verified.IsNone)
            {
                logger.LogInformation(
                    "SWA sign-in could not be verified from the rolesSource access token; nothing was recorded");
                return;
            }

            var signIn = verified.Value;
            DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;

            // The oid and roles persisted here come from verified.ObjectId / verified.Roles —
            // the cryptographically validated token — NEVER from the payload. The payload is
            // attacker-forgeable (the endpoint is anonymous and serves directly on the Function
            // App hostname, verified live), so trusting its claims would resurrect bug-bash
            // 2026-08-05 finding 10. LastSignInUtc marks this as the sign-in path, distinguishing
            // it from /operators/me, which records no sign-in.
            await registrar.RegisterAsync(
                new OperatorIdentity(
                    signIn.ObjectId,
                    signIn.DisplayName,
                    signIn.UserPrincipalName,
                    signIn.Roles,
                    LastSignInUtc: nowUtc),
                nowUtc,
                cancellationToken);

            // The payload's userId is the one tolerated exception to "payload is untrusted": on
            // its own it is useless to an attacker, and it is exactly the key SWA will present on
            // every later /api/* call. The repository's first-writer-wins rule bounds alias abuse.
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var aliasResult = await repository.UpsertDirectoryAliasAsync(
                    userId, signIn.ObjectId, cancellationToken);
                if (aliasResult.IsSuccess)
                    cacheInvalidator?.InvalidateAlias(userId);
                else
                    logger.LogWarning(
                        "SWA sign-in alias write failed for {SwaUserId}; roles response remains unaffected: {Error}",
                        userId, aliasResult.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SWA rolesSource sign-in recording failed; returning the roles response unchanged");
        }
    }

    private JsonDocument? ParseBody(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "SWA rolesSource payload was not valid JSON; returning no custom roles");
            return null;
        }
    }

    internal static IReadOnlyList<string> ExtractAllowedRoles(JsonElement root)
    {
        var roles = new List<string>(capacity: 2);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("claims", out var claims) ||
            claims.ValueKind != JsonValueKind.Array)
            return roles;

        foreach (var claim in claims.EnumerateArray())
        {
            if (!claim.TryGetProperty("typ", out var typEl) || typEl.ValueKind != JsonValueKind.String ||
                !claim.TryGetProperty("val", out var valEl) || valEl.ValueKind != JsonValueKind.String)
                continue;

            string? typ = typEl.GetString();
            if (!string.Equals(typ, RolesClaim, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(typ, LegacyRoleClaim, StringComparison.OrdinalIgnoreCase))
                continue;

            string? value = valEl.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (AllowedRoles.TryGetValue(value.Trim(), out string? canonicalRole) &&
                canonicalRole is not null &&
                !roles.Contains(canonicalRole, StringComparer.OrdinalIgnoreCase))
                roles.Add(canonicalRole);
        }

        return roles;
    }

    private sealed record RolesSourceResponse(IReadOnlyList<string> Roles);
}
