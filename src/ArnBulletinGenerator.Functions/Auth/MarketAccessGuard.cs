using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Auth;

/// <summary>
///     Shared market-scoping guard for HTTP triggers keyed by a market id. Returns a 403
///     response when the calling non-admin is not assigned the given market; null when the
///     action may proceed. Admins are unrestricted, and missing or empty assignments are
///     also unrestricted because station scoping is an opt-in wrong-station guard.
///     <para>
///         An EMPTY allowed list is the one exception: <see cref="MarketAccessService" /> uses it
///         to signal that the caller's scope could not be established (lookup threw or failed),
///         which denies every market rather than silently widening access.
///     </para>
/// </summary>
internal static class MarketAccessGuard
{
    public static async Task<HttpResponseData?> EnsureMarketAccessAsync(
        this IMarketAccessService service, HttpRequestData req, string marketId, ILogger logger,
        CancellationToken ct)
    {
        // A null principal means no X-MS-CLIENT-PRINCIPAL header. In test/prod that is
        // unreachable: AuthorizationMiddleware already rejected the request with a 401 before
        // any trigger ran. It is reachable only under the dev auth bypass, which synthesizes an
        // Admin identity without adding the header — so treating it as unrestricted here is the
        // dev console's load-bearing path, not a production hole. Failing closed would break dev
        // and change nothing anywhere else.
        var principal = ClientPrincipal.Parse(req);
        if (principal is null)
            return null;

        var allowed = await service.GetAllowedMarketsAsync(principal.ObjectId, principal.IsAdmin, ct);
        if (allowed.IsNone)
            return null;

        if (allowed.Value.Count == 0)
        {
            logger.LogError(
                "[MarketAccess] Scope unavailable for operator {ObjectId} — denying market '{MarketId}'",
                principal.ObjectId, marketId);
            return await HttpResponseHelper.ForbiddenAsync(req, MarketAccessService.UnverifiableAccessMessage);
        }

        if (allowed.Value.Any(m => string.Equals(m.Trim(), marketId.Trim(), StringComparison.OrdinalIgnoreCase)))
            return null;

        logger.LogWarning(
            "[MarketAccess] Operator {ObjectId} blocked from market '{MarketId}' — not in assigned [{Assigned}]",
            principal.ObjectId, marketId, string.Join(", ", allowed.Value));
        return await HttpResponseHelper.ForbiddenAsync(req, $"Forbidden: you are not assigned to market '{marketId}'.");
    }

    /// <summary>
    ///     Job-scoped variant of the guard above: returns a 403 response when the calling
    ///     operator is not assigned this job's market; null when the action may proceed.
    ///     Admins are unrestricted, while missing and empty assignments are also unrestricted.
    ///     Shared by <see cref="JobsHttpTrigger"/> (read side) and <see cref="JobLifecycleHttpTrigger"/>
    ///     (write side) so the guard logic lives in exactly one place.
    /// </summary>
    public static async Task<HttpResponseData?> EnsureJobAccessAsync(
        this IMarketAccessService service, HttpRequestData req, string jobId, CancellationToken ct)
    {
        var principal = ClientPrincipal.Parse(req);
        if (principal is null)
            return null;

        var access = await service.EnsureCanActOnJobAsync(principal.ObjectId, principal.IsAdmin, jobId, ct);
        return access.IsFailure ? await HttpResponseHelper.ForbiddenAsync(req, access.Error) : null;
    }
}
