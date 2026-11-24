using ArnBulletinGenerator.Domain.Configuration;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Operator → market assignment management (Workshop 2 / SDD §4.2). Admin manages
///     assignments; any authenticated caller can read their own scope via /operators/me.
///     Route roles are enforced by <see cref="RouteRoleMatrix" />.
/// </summary>
public sealed class OperatorsHttpTrigger(
    IOperatorAssignmentRepository repository,
    IOperatorDirectory operatorDirectory,
    IMarketAccessService marketAccess,
    IScheduleRepository scheduleRepository,
    OperatorRegistrar registrar,
    TimeProvider timeProvider,
    ILogger<OperatorsHttpTrigger> logger,
    IOptions<OperatorOptions>? options = null,
    IMarketAccessCacheInvalidator? cacheInvalidator = null)
{
    private readonly OperatorOptions _options = options?.Value ?? new();
    private const string Iso = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    [Function("GetMyOperatorScope")]
    public async Task<HttpResponseData> GetMyOperatorScope(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "operators/me")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var principal = ClientPrincipal.Parse(req);
        if (principal is null)
            return await HttpResponseHelper.OkJsonAsync(req, new OperatorScopeDto(false, null));

        // No LastSignInUtc: this runs on every page load, and a write per load would turn a read
        // into a hot path. The sign-in itself is recorded by the SWA rolesSource bridge. When the
        // registrar has already read the assignment, reuse that authoritative row rather than
        // immediately reading it again through MarketAccess.
        var registrationResult = await registrar.RegisterAsync(
            new OperatorIdentity(
                principal.HasDirectoryObjectId ? principal.ObjectId : string.Empty,
                principal.Name,
                principal.UserPrincipalName,
                principal.Roles),
            timeProvider.GetUtcNow().UtcDateTime,
            ctx.CancellationToken);

        Option<IReadOnlyList<string>> allowed;
        if (!_options.RequestScopeReuseDisabled &&
            registrationResult.IsSuccess &&
            registrationResult.Value.IsSome)
        {
            allowed = ScopeFromAssignment(registrationResult.Value.Value, principal.IsAdmin);
        }
        else
        {
            allowed = await marketAccess.GetAllowedMarketsAsync(
                principal.ObjectId, principal.IsAdmin, ctx.CancellationToken);
        }
        return await HttpResponseHelper.OkJsonAsync(req, allowed.Match(
            markets => new OperatorScopeDto(true, markets),
            () => new OperatorScopeDto(false, null)));
    }

    [Function("ListOperatorAssignments")]
    public async Task<HttpResponseData> ListOperatorAssignments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "operators")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var assignmentsResult = await repository.ListAsync(ctx.CancellationToken);
        if (assignmentsResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, assignmentsResult.Error);

        var directoryResult = await operatorDirectory.ListRoleHoldersAsync(ctx.CancellationToken);
        if (directoryResult.IsFailure)
        {
            logger.LogWarning(
                "[Operators] Entra directory lookup failed; returning persisted assignments only: {Error}",
                directoryResult.Error);
            var persistedOnly = assignmentsResult.Value.Select(ToDirectoryEntry).ToList();
            return await HttpResponseHelper.OkJsonAsync(req,
                new OperatorDirectoryDto(persistedOnly, "Unavailable", directoryResult.Error));
        }

        var operators = MergeDirectoryEntries(directoryResult.Value, assignmentsResult.Value);
        return await HttpResponseHelper.OkJsonAsync(req,
            new OperatorDirectoryDto(operators, "Ok", null));
    }

    [Function("UpsertOperatorAssignment")]
    public async Task<HttpResponseData> UpsertOperatorAssignment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "operators/{objectId}")]
        HttpRequestData req,
        string objectId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return await HttpResponseHelper.BadRequestAsync(req, "Operator object id is required.");
        if (StorageKey.RejectIfInvalid(req, "operator object id", objectId) is { } badKey)
            return await badKey;

        var parseResult =
            await HttpRequestParser.ParseAsync<UpsertOperatorAssignmentRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        if (parseResult.Value.Markets is null)
            return await HttpResponseHelper.BadRequestAsync(req,
                "Body must contain a 'markets' array (empty array = unrestricted).");

        var markets = parseResult.Value.Markets
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Referential integrity: a scoped operator's market list is a set of schedule market ids.
        // A typo'd id saves cleanly but then 403s the operator on every route with no signal at
        // save time, so reject unknown ids up front — the same standard the schedule route
        // applies to voice/style/dictionary assignment. Empty list = unrestricted, no catalogue
        // needed.
        if (markets.Count > 0)
        {
            var schedules = await scheduleRepository.ListSchedulesAsync(ctx.CancellationToken);
            if (schedules.IsFailure)
                return await HttpResponseHelper.InternalErrorAsync(req,
                    "Could not verify markets against the schedule catalogue: " + schedules.Error);

            var knownMarkets = new HashSet<string>(
                schedules.Value.Select(s => s.MarketId), StringComparer.OrdinalIgnoreCase);
            var unknown = markets.Where(m => !knownMarkets.Contains(m)).ToList();
            if (unknown.Count > 0)
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Unknown market id(s): {string.Join(", ", unknown)}. "
                    + "Market ids must match a configured schedule (see /api/schedules).");
        }

        string updatedBy = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var assignment = new OperatorAssignment(
            objectId.Trim(),
            parseResult.Value.DisplayName?.Trim() ?? string.Empty,
            markets,
            timeProvider.GetUtcNow().UtcDateTime,
            updatedBy);

        var result = await repository.UpsertAsync(assignment, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));
        cacheInvalidator?.InvalidateScope(objectId);

        // Respond with what was actually persisted, not the request: a blank DisplayName means
        // "not supplied" to the repository (the scoping dialog sends markets only), so echoing
        // the request would report a name that was never written.
        var persisted = await repository.GetAsync(objectId.Trim(), ctx.CancellationToken);
        if (persisted.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, persisted.Error);
        if (persisted.Value.IsNone)
            return await HttpResponseHelper.InternalErrorAsync(req,
                $"Assignment for '{objectId}' was not found immediately after save.");

        logger.LogInformation("[Operators] Assignment for {ObjectId} set by {User}: [{Markets}]",
            assignment.ObjectId, updatedBy, string.Join(", ", markets));

        return await HttpResponseHelper.OkJsonAsync(req, ToDto(persisted.Value.Value));
    }

    [Function("DeleteOperatorAssignment")]
    public async Task<HttpResponseData> DeleteOperatorAssignment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "operators/{objectId}")]
        HttpRequestData req,
        string objectId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return await HttpResponseHelper.BadRequestAsync(req, "Operator object id is required.");
        if (StorageKey.RejectIfInvalid(req, "operator object id", objectId) is { } badKey)
            return await badKey;

        var result = await repository.DeleteAsync(objectId.Trim(), ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        cacheInvalidator?.InvalidateScope(objectId);
        logger.LogInformation("[Operators] Assignment removed for {ObjectId} — operator is now unrestricted",
            objectId);

        return await HttpResponseHelper.OkJsonAsync(req,
            new OperatorActionResponseDto("deleted", objectId,
                "Operator assignment removed; operator is now unrestricted."));
    }

    private static Option<IReadOnlyList<string>> ScopeFromAssignment(
        OperatorAssignment assignment,
        bool isAdmin)
    {
        if (isAdmin || assignment.Markets.Count == 0)
            return Option<IReadOnlyList<string>>.None;
        return Option<IReadOnlyList<string>>.Some(assignment.Markets);
    }

    private static IReadOnlyList<OperatorDirectoryEntryDto> MergeDirectoryEntries(
        IReadOnlyList<DirectoryPrincipal> principals,
        IReadOnlyList<OperatorAssignment> assignments)
    {
        var assignmentsByObjectId = new Dictionary<string, OperatorAssignment>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
            assignmentsByObjectId[assignment.ObjectId] = assignment;

        var directoryObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<OperatorDirectoryEntryDto>(principals.Count + assignments.Count);
        foreach (var principal in principals)
        {
            assignmentsByObjectId.TryGetValue(principal.ObjectId, out var assignment);
            directoryObjectIds.Add(principal.ObjectId);
            entries.Add(ToDirectoryEntry(principal, assignment));
        }

        foreach (var assignment in assignments)
        {
            if (!directoryObjectIds.Contains(assignment.ObjectId))
                entries.Add(ToDirectoryEntry(assignment));
        }

        return entries;
    }

    private static OperatorDirectoryEntryDto ToDirectoryEntry(
        DirectoryPrincipal principal, OperatorAssignment? assignment)
    {
        bool isAdmin = principal.Roles.Any(
            role => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase));
        return new OperatorDirectoryEntryDto(
            principal.ObjectId,
            principal.DisplayName,
            principal.UserPrincipalName,
            principal.Roles,
            principal.Source,
            assignment?.Markets ?? Array.Empty<string>(),
            isAdmin,
            assignment?.UpdatedAtUtc.ToString(Iso),
            assignment?.UpdatedBy);
    }

    private static OperatorDirectoryEntryDto ToDirectoryEntry(OperatorAssignment assignment)
    {
        // Roles here are what the person's own token presented at their last sign-in, not a
        // directory read. Source stays "Assignment" so the console can say which it is.
        var roles = assignment.RolesAtSignIn ?? [];
        bool isAdmin = roles.Any(role => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase));
        return new OperatorDirectoryEntryDto(
            assignment.ObjectId,
            assignment.DisplayName,
            assignment.UserPrincipalName,
            roles,
            "Assignment",
            assignment.Markets,
            isAdmin,
            assignment.UpdatedAtUtc.ToString(Iso),
            assignment.UpdatedBy);
    }

    private static OperatorAssignmentDto ToDto(OperatorAssignment a)
    {
        return new OperatorAssignmentDto(
            a.ObjectId, a.DisplayName, a.Markets, a.UpdatedAtUtc.ToString(Iso), a.UpdatedBy);
    }
}
