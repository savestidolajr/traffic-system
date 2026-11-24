using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Access;

/// <summary>
///     Provides opt-in operator → market scoping (Workshop 2 / SDD §4.2). Semantics:
///     - Admins are always unrestricted and receive <see cref="Option{T}.None" />.
///     - An operator with a non-empty assignment market list receives
///       <see cref="Option{T}.Some(T)" /> containing those markets.
///     - An operator with no assignment record, or with an empty market list, is unrestricted
///       and receives <see cref="Option{T}.None" />. This is the deliberate opt-in policy:
///       scoping only applies once someone has been given a market list.
///     - A caller whose scope cannot be ESTABLISHED - the assignment lookup threw, returned a
///       failure, or a non-admin arrived with no object id - receives
///       <see cref="Option{T}.Some(T)" /> over an EMPTY list, which denies every market.
///     <para>
///         That last rule changed on 2026-07-29. These three paths previously returned
///         <see cref="Option{T}.None" />, so a transient Azure Table fault silently granted every
///         operator access to every market, logged only as a warning. Not knowing someone's scope
///         is not the same as them having none, and widening access is the wrong way to resolve
///         the difference. The opt-in policy above is unchanged: an operator with no assignment
///         row is still unrestricted, so no working caller loses access.
///     </para>
/// </summary>
public interface IMarketAccessService
{
    /// <summary>
    ///     Allowed markets for the caller. <see cref="Option{T}.None" /> means unrestricted.
    ///     <see cref="Option{T}.Some(T)" /> is returned only for a non-empty assigned market list.
    /// </summary>
    Task<Option<IReadOnlyList<string>>> GetAllowedMarketsAsync(
        string objectId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>Fails with a "Forbidden:" message when the caller may not act on the job's market.</summary>
    Task<Result> EnsureCanActOnJobAsync(
        string objectId, bool isAdmin, string jobId, CancellationToken cancellationToken = default);
}

/// <summary>
///     Evicts authorization cache entries after assignment or SWA-alias writes. Narrowing an
///     operator's scope is a security change and must take effect immediately rather than waiting
///     for the short read-cache lifetime.
/// </summary>
public interface IMarketAccessCacheInvalidator
{
    void InvalidateScope(string objectId);
    void InvalidateAlias(string swaUserId);
}

public sealed class MarketAccessService(
    IOperatorAssignmentRepository assignmentRepository,
    IJobRepository jobRepository,
    ILogger<MarketAccessService> logger,
    IMemoryCache? cache = null,
    IOptions<MarketAccessOptions>? options = null) : IMarketAccessService, IMarketAccessCacheInvalidator
{
    /// <summary>
    ///     Returned when the caller's market scope could not be read at all. Deliberately distinct
    ///     from "you are not assigned to market X", which is a statement about permissions; this is
    ///     an infrastructure condition the operator should retry.
    /// </summary>
    public const string UnverifiableAccessMessage =
        "Forbidden: your market access could not be verified right now. Please retry.";

    public Task<Option<IReadOnlyList<string>>> GetAllowedMarketsAsync(
        string objectId, bool isAdmin, CancellationToken cancellationToken = default) =>
        ResolveAllowedMarketsAsync(objectId, isAdmin, cancellationToken);

    public void InvalidateScope(string objectId)
    {
        if (!string.IsNullOrWhiteSpace(objectId))
            cache?.Remove(MarketAccessOptions.ScopeCacheKey(objectId));
    }

    public void InvalidateAlias(string swaUserId)
    {
        if (string.IsNullOrWhiteSpace(swaUserId))
            return;

        cache?.Remove(MarketAccessOptions.AliasCacheKey(swaUserId));
        // Before an alias exists, resolution caches the absent direct assignment under the caller id.
        // Drop that negative scope too or a freshly-recorded alias remains denied until its TTL expires.
        cache?.Remove(MarketAccessOptions.ScopeCacheKey(swaUserId));
    }

    public async Task<Result> EnsureCanActOnJobAsync(
        string objectId, bool isAdmin, string jobId, CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedMarketsAsync(objectId, isAdmin, cancellationToken);
        if (allowed.IsNone)
            return Result.Success();

        // Empty means "scope could not be established", never "assigned to nothing" — the opt-in
        // policy maps a genuinely empty assignment to None above. Answer honestly: this is a
        // retryable infrastructure condition, not a statement about the operator's permissions.
        if (allowed.Value.Count == 0)
            return Result.Failure(UnverifiableAccessMessage);

        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result.Success(); // Job-not-found surfaces from the action itself with a proper 404.

        string market = jobResult.Value.Market ?? jobResult.Value.Station;
        if (allowed.Value.Any(m => string.Equals(m.Trim(), market.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Result.Success();

        logger.LogWarning(
            "[MarketAccess] Operator {ObjectId} blocked from job {JobId} — market '{Market}' not in assigned [{Assigned}]",
            objectId, jobId, market, string.Join(", ", allowed.Value));
        return Result.Failure($"Forbidden: you are not assigned to market '{market}'.");
    }

    /// <summary>Denies every market. Distinct from an absent assignment, which stays unrestricted.</summary>
    private static readonly Option<IReadOnlyList<string>> ScopeUnavailable =
        Option<IReadOnlyList<string>>.Some([]);

    private async Task<Option<IReadOnlyList<string>>> ResolveAllowedMarketsAsync(
        string objectId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (isAdmin)
            return Option<IReadOnlyList<string>>.None;

        if (string.IsNullOrWhiteSpace(objectId))
        {
            logger.LogError(
                "[MarketAccess] Non-admin request has no object id — denying every market. "
                + "The caller cannot be identified, so no scope can be established.");
            return ScopeUnavailable;
        }

        string requestedId = objectId.Trim();
        if (TryGetCachedScope(requestedId, out var cachedScope))
            return cachedScope;

        // Once an SWA id has been resolved, skip the known-empty assignment lookup on every
        // request. The alias mapping and the stable oid's scope have separate cache entries:
        // changing the oid's assignment therefore needs to evict only the scope, while changing
        // the alias explicitly evicts the mapping.
        if (TryGetCachedAlias(requestedId, out var cachedObjectId))
            return await ResolveStableScopeAsync(cachedObjectId, cancellationToken);

        var direct = await LookupAssignmentScopeAsync(requestedId, cancellationToken);
        if (!direct.RowAbsent)
        {
            // A present row with no markets means unrestricted. Do not cache that privilege:
            // narrowing the assignment must take effect even if a writer bypasses our eviction path.
            if (direct.Scope.IsSome)
                CacheScope(requestedId, direct.Scope);
            return direct.Scope;
        }

        // The row was absent. The caller may be a console user whose id here is a Static Web Apps
        // per-app userId rather than an Entra oid: SWA never forwards the `claims` array (which
        // carries the real objectidentifier) to a linked backend, so ClientPrincipal falls back to
        // the SWA-generated userId. Assignment rows are keyed by the Entra oid, so resolve the
        // sign-in alias exactly one hop and fail closed if the alias itself cannot be read.
        Result<Option<string>> alias;
        try
        {
            alias = await assignmentRepository.GetObjectIdByAliasAsync(requestedId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "[MarketAccess] SWA→Entra alias lookup threw for {ObjectId} — denying every market until it can be read",
                requestedId);
            return ScopeUnavailable;
        }

        if (alias.IsFailure)
        {
            logger.LogError(
                "[MarketAccess] SWA→Entra alias lookup failed for {ObjectId} — denying every market until it can be read: {Error}",
                requestedId, alias.Error);
            return ScopeUnavailable;
        }

        if (alias.Value.IsNone
            || string.Equals(alias.Value.Value, requestedId, StringComparison.OrdinalIgnoreCase))
        {
            CacheScope(requestedId, Option<IReadOnlyList<string>>.None);
            return Option<IReadOnlyList<string>>.None;
        }

        string stableObjectId = alias.Value.Value.Trim();
        CacheAlias(requestedId, stableObjectId);
        return await ResolveStableScopeAsync(stableObjectId, cancellationToken);
    }

    private async Task<Option<IReadOnlyList<string>>> ResolveStableScopeAsync(
        string objectId, CancellationToken cancellationToken)
    {
        if (TryGetCachedScope(objectId, out var cached))
            return cached;

        var lookup = await LookupAssignmentScopeAsync(objectId, cancellationToken);
        // RowAbsent is a short negative-cache candidate. A present unrestricted row is not:
        // stale unrestricted access is materially different from a brief stale denial.
        if (lookup.RowAbsent || lookup.Scope.IsSome)
            CacheScope(objectId, lookup.Scope);
        return lookup.Scope;
    }

    private bool TryGetCachedScope(
        string objectId, out Option<IReadOnlyList<string>> scope)
    {
        scope = default;
        return cache is not null
            && options?.Value.CacheDisabled is not true
            && cache.TryGetValue(MarketAccessOptions.ScopeCacheKey(objectId), out scope);
    }

    private bool TryGetCachedAlias(string swaUserId, out string objectId)
    {
        objectId = string.Empty;
        if (cache is null || options?.Value.CacheDisabled is true
            || !cache.TryGetValue(
                MarketAccessOptions.AliasCacheKey(swaUserId), out string? cachedObjectId)
            || string.IsNullOrWhiteSpace(cachedObjectId))
            return false;

        objectId = cachedObjectId;
        return true;
    }

    private void CacheScope(string objectId, Option<IReadOnlyList<string>> scope)
    {
        if (cache is null || options?.Value.CacheDisabled is true
            || (scope.IsSome && scope.Value.Count == 0))
            return;

        int seconds = scope.IsNone
            ? options?.Value.EmptyScopeCacheTtlSeconds ?? 5
            : options?.Value.CacheTtlSeconds ?? 45;
        cache.Set(MarketAccessOptions.ScopeCacheKey(objectId), scope, TimeSpan.FromSeconds(seconds));
    }

    private void CacheAlias(string swaUserId, string objectId)
    {
        if (cache is null || options?.Value.CacheDisabled is true)
            return;

        cache.Set(
            MarketAccessOptions.AliasCacheKey(swaUserId),
            objectId,
            TimeSpan.FromSeconds(options?.Value.CacheTtlSeconds ?? 45));
    }

    /// <summary>
    ///     Outcome of one assignment-table lookup for a single id, normalised so the direct lookup
    ///     and the alias-resolved lookup can never drift apart: failure or throw →
    ///     <see cref="ScopeUnavailable" /> (deny every market); row absent or empty market list →
    ///     unrestricted <see cref="Option{T}.None" />; otherwise the assigned markets.
    ///     <see cref="RowAbsent" /> distinguishes "no row" from "row with no markets" so the direct
    ///     path can decide whether an SWA → Entra alias hop is worth taking.
    /// </summary>
    private readonly record struct AssignmentScopeLookup(bool RowAbsent, Option<IReadOnlyList<string>> Scope);

    private async Task<AssignmentScopeLookup> LookupAssignmentScopeAsync(
        string objectId, CancellationToken cancellationToken)
    {
        Result<Option<OperatorAssignment>> result;
        try
        {
            result = await assignmentRepository.GetAsync(objectId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "[MarketAccess] Assignment lookup threw for {ObjectId} — denying every market until it can be read",
                objectId);
            return new AssignmentScopeLookup(RowAbsent: false, ScopeUnavailable);
        }

        if (result.IsFailure)
        {
            logger.LogError(
                "[MarketAccess] Assignment lookup failed for {ObjectId} — denying every market until it can be read: {Error}",
                objectId, result.Error);
            return new AssignmentScopeLookup(RowAbsent: false, ScopeUnavailable);
        }

        if (result.Value.IsNone)
            return new AssignmentScopeLookup(RowAbsent: true, Option<IReadOnlyList<string>>.None);

        if (result.Value.Value.Markets.Count == 0)
            return new AssignmentScopeLookup(RowAbsent: false, Option<IReadOnlyList<string>>.None);

        IReadOnlyList<string> markets = result.Value.Value.Markets
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Select(market => market.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return markets.Count == 0
            ? new AssignmentScopeLookup(RowAbsent: false, Option<IReadOnlyList<string>>.None)
            : new AssignmentScopeLookup(
                RowAbsent: false, Option<IReadOnlyList<string>>.Some(markets));
    }
}
