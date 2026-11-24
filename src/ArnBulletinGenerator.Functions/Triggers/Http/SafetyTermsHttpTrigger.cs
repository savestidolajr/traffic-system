using System.Web;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Content-safety term management (Admin-only): the banned-word list and the place-name
///     exception allowlist for the deterministic <c>IContentSafetyFilter</c>. Unlike guidance,
///     terms compose ADDITIVELY (global ∪ market) and are a compliance control, so EVERY route
///     is Admin (enforced by <see cref="RouteRoleMatrix" />). The engineered
///     <c>ContentSafety:BannedWords</c> config baseline is always merged in by the filter and
///     is not editable here.
/// </summary>
public sealed class SafetyTermsHttpTrigger(
    ISafetyTermRepository repository,
    IScheduleRepository scheduleRepository,
    TimeProvider timeProvider,
    ILogger<SafetyTermsHttpTrigger> logger)
{
    [Function("ListSafetyTerms")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "safety/terms")]
        HttpRequestData req, FunctionContext ctx)
    {
        var result = await repository.ListAllAsync(ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        return await HttpResponseHelper.OkJsonAsync(req, result.Value.Select(SafetyTermDto.From).ToList());
    }

    [Function("UpsertSafetyTerm")]
    public async Task<HttpResponseData> Upsert(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "safety/terms")]
        HttpRequestData req, FunctionContext ctx)
    {
        var parsed = await HttpRequestParser.ParseAsync<UpsertSafetyTermRequest>(req, ctx.CancellationToken);
        if (parsed.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        var r = parsed.Value;
        if (string.IsNullOrWhiteSpace(r.Term))
            return await HttpResponseHelper.BadRequestAsync(req, "Term is required.");
        if (r.Term.Length > ConfigValidationLimits.MaxTermLength)
            return await HttpResponseHelper.BadRequestAsync(req, $"Term must be at most {ConfigValidationLimits.MaxTermLength} characters.");

        string scopeId = SafetyTerm.NormalizeScopeId(r.Scope, r.ScopeId);
        if (r.Scope != SafetyScope.Global && string.IsNullOrWhiteSpace(scopeId))
            return await HttpResponseHelper.BadRequestAsync(req, "ScopeId (market) is required for Market scope.");
        if (r.Scope != SafetyScope.Global && StorageKey.RejectIfInvalid(req, "scopeId", scopeId) is { } badScope)
            return await badScope;

        if (r.Scope == SafetyScope.Market)
        {
            var scheduleResult = await scheduleRepository.GetScheduleAsync(scopeId, ctx.CancellationToken);
            if (scheduleResult.IsFailure)
                return await HttpResponseHelper.NotFoundAsync(req, scheduleResult.Error);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = UpdatedBy(req);

        // Dedup: an identical (kind/scope/scopeId/term) "create" reuses the existing row instead
        // of minting a duplicate banned/allowed entry — an upsert of the same term is idempotent.
        // The full list also lets an update/reuse preserve the original CreatedAtUtc below.
        var existingResult = await repository.ListAllAsync(ctx.CancellationToken);
        var existingTerms = existingResult.IsSuccess ? existingResult.Value : [];

        string? targetId = r.Id;
        if (string.IsNullOrWhiteSpace(targetId))
            targetId = existingTerms.FirstOrDefault(t =>
                t.Kind == r.Kind && t.Scope == r.Scope
                && string.Equals(t.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Term, r.Term.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

        var existingTerm = string.IsNullOrWhiteSpace(targetId)
            ? null
            : existingTerms.FirstOrDefault(t => t.Id == targetId);

        SafetyTerm term = string.IsNullOrWhiteSpace(targetId)
            ? SafetyTerm.Create(r.Kind, r.Scope, scopeId, r.Term, user, now, r.Note, r.Enabled ?? true)
            : new SafetyTerm(targetId!, r.Kind, r.Scope, scopeId, r.Term.Trim(), user,
                existingTerm?.CreatedAtUtc ?? now, now, r.Enabled ?? true, Option.OfWhitespace(r.Note));

        var result = await repository.UpsertAsync(term, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        logger.LogInformation("[Safety] {Kind} term {Id} ({Scope}:{ScopeId}) upserted by {User}",
            term.Kind, term.Id, term.Scope, term.ScopeId, term.CreatedBy);
        return await HttpResponseHelper.OkJsonAsync(req, SafetyTermDto.From(term));
    }

    [Function("DeleteSafetyTerm")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "safety/terms/{id}")]
        HttpRequestData req, string id, FunctionContext ctx)
    {
        string? scopeRaw = Query(req, "scope");
        if (!Enum.TryParse<SafetyScope>(scopeRaw, true, out var scope))
            return await HttpResponseHelper.BadRequestAsync(req, "Query 'scope' must be Global or Market.");
        string scopeId = scope == SafetyScope.Global ? string.Empty : Query(req, "scopeId") ?? string.Empty;
        if (StorageKey.RejectIfInvalid(req, "id", id) is { } badId)
            return await badId;
        if (scope != SafetyScope.Global && StorageKey.RejectIfInvalid(req, "scopeId", scopeId) is { } badScope)
            return await badScope;

        var result = await repository.DeleteAsync(scope, scopeId, id, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        // Id + ScopeId (not just "deleted"): ActivityLoggingMiddleware's response-body fallback
        // reads these to resolve an entity id for this route (no id/market segment other than
        // the term's own id is in the URL) -- ScopeId (market) wins when non-empty, else Id,
        // matching how the PUT upsert below already resolves.
        return await HttpResponseHelper.OkJsonAsync(req, new { Deleted = id, Id = id, ScopeId = scopeId });
    }

    private static string UpdatedBy(HttpRequestData req)
        => ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";

    private static string? Query(HttpRequestData req, string key)
        => HttpUtility.ParseQueryString(req.Url.Query).Get(key);
}
