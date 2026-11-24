using System.Web;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Standing-guidance management (self-service guidance, Layer 2) plus the candidate
///     promotion queue (Layer 3). Market/station directives are operator-maintainable
///     (market-scoped); global directives and promotions are Admin-only. Roles are enforced
///     by <see cref="RouteRoleMatrix" />; market scoping by <see cref="IMarketAccessService" />.
///     The unscoped PUT /guidance and DELETE /guidance/{id} carry their scope in the
///     payload/query, so they re-check Admin for Global rows and apply the market guard to
///     Market/Station rows in-handler (Station → market via the schedule catalogue).
/// </summary>
public sealed class GuidanceHttpTrigger(
    IGuidanceRepository repository,
    IFutureLessonRepository feedbackRepository,
    IScheduleRepository scheduleRepository,
    IMarketAccessService marketAccess,
    TimeProvider timeProvider,
    ILogger<GuidanceHttpTrigger> logger)
{
    private const int CandidatePageSize = 20;

    // ── Market-scoped management (Operator) ──────────────────────────

    [Function("ListMarketGuidance")]
    public async Task<HttpResponseData> ListMarketGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/guidance")]
        HttpRequestData req, string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");
        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await repository.ListAsync(GuidanceScope.Market, market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        return await HttpResponseHelper.OkJsonAsync(req, result.Value.Select(GuidanceDto.From).ToList());
    }

    [Function("UpsertMarketGuidance")]
    public async Task<HttpResponseData> UpsertMarketGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "markets/{market}/guidance")]
        HttpRequestData req, string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");
        if (StorageKey.RejectIfInvalid(req, "market", market) is { } badKey)
            return await badKey;
        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var scheduleResult = await scheduleRepository.GetScheduleAsync(market, ctx.CancellationToken);
        if (scheduleResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, scheduleResult.Error);

        var parsed = await HttpRequestParser.ParseAsync<UpsertGuidanceRequest>(req, ctx.CancellationToken);
        if (parsed.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);
        if (Validate(parsed.Value) is { } invalid) return await HttpResponseHelper.BadRequestAsync(req, invalid);

        var entry = await BuildEntry(parsed.Value, GuidanceScope.Market, market, UpdatedBy(req), ctx.CancellationToken);
        var result = await repository.UpsertAsync(entry, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        logger.LogInformation("[Guidance] Market {Market} directive {Id} upserted by {User}",
            market, entry.Id, entry.CreatedBy);
        return await HttpResponseHelper.OkJsonAsync(req, GuidanceDto.From(entry));
    }

    [Function("DeleteMarketGuidance")]
    public async Task<HttpResponseData> DeleteMarketGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "markets/{market}/guidance/{id}")]
        HttpRequestData req, string market, string id, FunctionContext ctx)
    {
        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;
        if (StorageKey.RejectIfInvalid(req, "market", market) is { } badKey)
            return await badKey;
        if (StorageKey.RejectIfInvalid(req, "id", id) is { } badId)
            return await badId;

        var result = await repository.DeleteAsync(GuidanceScope.Market, market, id, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        if (!result.Value) return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Guidance.NotFound);
        return await HttpResponseHelper.OkJsonAsync(req, new { deleted = id });
    }

    /// <summary>Composed "rules in effect" preview for an operator (market + optional station + global).</summary>
    [Function("PreviewComposedGuidance")]
    public async Task<HttpResponseData> PreviewComposedGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/guidance/composed")]
        HttpRequestData req, string market, FunctionContext ctx)
    {
        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var station = Query(req, "station");
        var result = await repository.GetForScopesAsync(
            market, string.IsNullOrWhiteSpace(station) ? default : Option<string>.Some(station!),
            ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var composed = GuidanceComposer.Compose(
            result.Value, market, string.IsNullOrWhiteSpace(station) ? default : Option<string>.Some(station!));
        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            inEffect = composed.Entries.Select(GuidanceDto.From).ToList(),
            droppedCount = composed.DroppedCount
        });
    }

    /// <summary>Active explicit future lessons for every station in one market.</summary>
    [Function("ListGuidanceCandidates")]
    public async Task<HttpResponseData> ListGuidanceCandidates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/guidance/candidates")]
        HttpRequestData req, string market, FunctionContext ctx)
    {
        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var schedulesResult = await scheduleRepository.ListSchedulesAsync(ctx.CancellationToken);
        if (schedulesResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, schedulesResult.Error);
        var schedules = schedulesResult.Value.ToArray();

        var result = await GetCandidatesForMarketAsync(schedules, market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var promoted = await PromotedJobIdsAsync(schedules, market, ctx.CancellationToken);
        if (promoted.IsFailure)
            logger.LogWarning("[Guidance] Candidates could not read promoted markers: {Error}", promoted.Error);
        var promotedIds = promoted.IsSuccess ? promoted.Value : new HashSet<string>(StringComparer.Ordinal);

        var candidates = result.Value
            .OrderByDescending(entry => entry.LastSeenAtUtc)
            .Take(CandidatePageSize)
            .Select(entry => GuidanceCandidateDto.From(
                entry, promotedIds.Contains(entry.SourceJobId)))
            .ToList();
        return await HttpResponseHelper.OkJsonAsync(req, candidates);
    }

    [Function("DismissGuidanceCandidate")]
    public async Task<HttpResponseData> DismissGuidanceCandidate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete",
            Route = "stations/{station}/guidance/candidates/{id}")]
        HttpRequestData req, string station, string id, FunctionContext ctx)
    {
        if (StorageKey.RejectIfInvalid(req, "candidate id", id) is { } badId)
            return await badId;

        var marketResult = await ResolveStationMarketAsync(station, ctx.CancellationToken);
        if (marketResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, marketResult.Error);
        if (marketResult.Value is null)
            return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Schedule.NotFound);
        if (await marketAccess.EnsureMarketAccessAsync(
                req, marketResult.Value, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await feedbackRepository.DeleteAsync(station, id, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        return await HttpResponseHelper.OkJsonAsync(req, new { Dismissed = id, Station = station });
    }

    /// <summary>
    ///     Aggregate inventory: global directives (admin only) plus every requested market the
    ///     caller can access, merged in ONE round-trip. Replaces the operator console's
    ///     per-market fan-out (one GET per market) that made the Bulletin-rules page issue
    ///     O(markets) requests. Markets the caller is not assigned to are silently dropped;
    ///     a per-scope store failure drops that scope rather than failing the whole inventory.
    /// </summary>
    [Function("GuidanceInventory")]
    public async Task<HttpResponseData> GuidanceInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "guidance/inventory")]
        HttpRequestData req, FunctionContext ctx)
    {
        var principal = ClientPrincipal.Parse(req);
        bool includeGlobal = principal?.IsAdmin ?? false;

        var allowed = principal is null
            ? Option<IReadOnlyList<string>>.None
            : await marketAccess.GetAllowedMarketsAsync(principal.ObjectId, principal.IsAdmin, ctx.CancellationToken);
        var markets = MarketScopeRequest.FilterToAllowed(
            MarketScopeRequest.ParseMarkets(MarketScopeRequest.Query(req, "markets")), allowed);

        // Station-scoped directives reach bulletins (GuidanceComposer matches ScopeId == station)
        // but are invisible to the console if the inventory stops at Global + Market partitions.
        // Enumerate the Station partitions from the schedule catalogue so every directive that can
        // affect air is listed; a catalogue failure drops the station enrichment (logged) rather
        // than failing the whole inventory.
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(ctx.CancellationToken);
        if (schedulesResult.IsFailure)
            logger.LogWarning(
                "[Guidance] Inventory could not load the schedule catalogue to enumerate station scopes: {Error}",
                schedulesResult.Error);
        var schedules = schedulesResult.IsSuccess ? schedulesResult.Value.ToArray() : [];

        var tasks = new List<Task<Result<IReadOnlyList<GuidanceEntry>>>>(markets.Count + 1);
        if (includeGlobal)
            tasks.Add(repository.ListAsync(GuidanceScope.Global, string.Empty, ctx.CancellationToken));
        foreach (var market in markets)
        {
            tasks.Add(repository.ListAsync(GuidanceScope.Market, market, ctx.CancellationToken));
            foreach (var station in StationsOf(schedules, market))
                tasks.Add(repository.ListAsync(GuidanceScope.Station, station, ctx.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);
        var entries = new List<GuidanceDto>();
        foreach (var r in results)
            if (r.IsSuccess)
                entries.AddRange(r.Value.Select(GuidanceDto.From));

        return await HttpResponseHelper.OkJsonAsync(req, entries);
    }

    // ── Cross-scope management (Admin) ───────────────────────────────

    [Function("ListGuidanceByScope")]
    public async Task<HttpResponseData> ListGuidanceByScope(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "guidance")]
        HttpRequestData req, FunctionContext ctx)
    {
        string? scopeRaw = Query(req, "scope");
        if (!Enum.TryParse<GuidanceScope>(scopeRaw, true, out var scope))
            return await HttpResponseHelper.BadRequestAsync(req, "Query 'scope' must be Global, Market, or Station.");

        string scopeId = scope == GuidanceScope.Global ? string.Empty : Query(req, "scopeId") ?? string.Empty;
        if (scope != GuidanceScope.Global && string.IsNullOrWhiteSpace(scopeId))
            return await HttpResponseHelper.BadRequestAsync(req, "Query 'scopeId' is required for Market/Station scope.");

        var result = await repository.ListAsync(scope, scopeId, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        return await HttpResponseHelper.OkJsonAsync(req, result.Value.Select(GuidanceDto.From).ToList());
    }

    [Function("UpsertGuidance")]
    public async Task<HttpResponseData> UpsertGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "guidance")]
        HttpRequestData req, FunctionContext ctx)
    {
        var parsed = await HttpRequestParser.ParseAsync<UpsertGuidanceRequest>(req, ctx.CancellationToken);
        if (parsed.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);
        if (Validate(parsed.Value) is { } invalid) return await HttpResponseHelper.BadRequestAsync(req, invalid);

        var r = parsed.Value;
        string scopeId = GuidanceEntry.NormalizeScopeId(r.Scope, r.ScopeId);
        if (r.Scope != GuidanceScope.Global && string.IsNullOrWhiteSpace(scopeId))
            return await HttpResponseHelper.BadRequestAsync(req, "ScopeId is required for Market/Station scope.");
        if (r.Scope != GuidanceScope.Global && StorageKey.RejectIfInvalid(req, "scopeId", scopeId) is { } badScope)
            return await badScope;

        // Scope-dependent role enforcement: RouteRoleMatrix admits any Operator to this route
        // because the scope lives in the payload. Global directives are network-wide editorial
        // policy (Admin-only, mirroring the matrix's comment); Market/Station directives are
        // per-market operational config carrying the same market guard as the /markets/{m} route
        // — Station is resolved to its market via the schedule catalogue first. A null principal
        // is the dev auth bypass (the middleware synthesized an Admin identity without a header),
        // so it passes, exactly as MarketAccessGuard treats it.
        if (r.Scope == GuidanceScope.Global)
        {
            if (ClientPrincipal.Parse(req) is { IsAdmin: false })
                return await HttpResponseHelper.ForbiddenAsync(req, "Forbidden: global guidance requires Admin.");
        }
        else if (r.Scope == GuidanceScope.Market)
        {
            if (await marketAccess.EnsureMarketAccessAsync(req, scopeId, logger, ctx.CancellationToken) is { } forbidden)
                return forbidden;
        }
        else
        {
            var marketResult = await ResolveStationMarketAsync(scopeId, ctx.CancellationToken);
            if (marketResult.IsFailure)
                return await HttpResponseHelper.InternalErrorAsync(req, marketResult.Error);
            if (marketResult.Value is null)
                return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Schedule.NotFound);
            if (await marketAccess.EnsureMarketAccessAsync(req, marketResult.Value, logger, ctx.CancellationToken) is { } forbidden)
                return forbidden;
        }

        // Same write-rule as the market route: a directive must target a scope the schedule
        // catalogue owns, or it never composes into a bulletin (GuidanceComposer matches ScopeId
        // against the market/station from the schedule) and becomes invisible dead config.
        if (r.Scope == GuidanceScope.Market)
        {
            var scheduleResult = await scheduleRepository.GetScheduleAsync(scopeId, ctx.CancellationToken);
            if (scheduleResult.IsFailure)
                return await HttpResponseHelper.NotFoundAsync(req, scheduleResult.Error);
        }

        var entry = await BuildEntry(r, r.Scope, scopeId, UpdatedBy(req), ctx.CancellationToken);
        var result = await repository.UpsertAsync(entry, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        logger.LogInformation("[Guidance] {Scope}:{ScopeId} directive {Id} upserted by {User}",
            entry.Scope, entry.ScopeId, entry.Id, entry.CreatedBy);
        return await HttpResponseHelper.OkJsonAsync(req, GuidanceDto.From(entry));
    }

    [Function("DeleteGuidance")]
    public async Task<HttpResponseData> DeleteGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "guidance/{id}")]
        HttpRequestData req, string id, FunctionContext ctx)
    {
        string? scopeRaw = Query(req, "scope");
        if (!Enum.TryParse<GuidanceScope>(scopeRaw, true, out var scope))
            return await HttpResponseHelper.BadRequestAsync(req, "Query 'scope' must be Global, Market, or Station.");
        string scopeId = scope == GuidanceScope.Global ? string.Empty : Query(req, "scopeId") ?? string.Empty;
        if (StorageKey.RejectIfInvalid(req, "id", id) is { } badId)
            return await badId;
        if (scope != GuidanceScope.Global && StorageKey.RejectIfInvalid(req, "scopeId", scopeId) is { } badScope)
            return await badScope;

        // Scope-dependent role enforcement, mirroring UpsertGuidance: RouteRoleMatrix admits any
        // Operator to DELETE /guidance/{id}, so Global deletes are re-checked for Admin here and
        // Market/Station deletes carry the market guard (Station → market via the catalogue).
        if (scope == GuidanceScope.Global)
        {
            if (ClientPrincipal.Parse(req) is { IsAdmin: false })
                return await HttpResponseHelper.ForbiddenAsync(req, "Forbidden: global guidance requires Admin.");
        }
        else if (scope == GuidanceScope.Market)
        {
            if (await marketAccess.EnsureMarketAccessAsync(req, scopeId, logger, ctx.CancellationToken) is { } forbidden)
                return forbidden;
        }
        else
        {
            var marketResult = await ResolveStationMarketAsync(scopeId, ctx.CancellationToken);
            if (marketResult.IsFailure)
                return await HttpResponseHelper.InternalErrorAsync(req, marketResult.Error);
            if (marketResult.Value is null)
                return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Schedule.NotFound);
            if (await marketAccess.EnsureMarketAccessAsync(req, marketResult.Value, logger, ctx.CancellationToken) is { } forbidden)
                return forbidden;
        }

        var result = await repository.DeleteAsync(scope, scopeId, id, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        if (!result.Value)
            return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Guidance.NotFound);
        // Id + ScopeId (not just "deleted"): ActivityLoggingMiddleware's response-body fallback
        // reads these when this route deletes a Market-scope entry (ScopeId wins) or a Global
        // one (empty ScopeId falls through to Id), matching UpsertGuidance's own resolution.
        return await HttpResponseHelper.OkJsonAsync(req, new { Deleted = id, Id = id, ScopeId = scopeId });
    }

    /// <summary>Promotes a candidate operator note into a durable standing directive (Admin gate).</summary>
    [Function("PromoteGuidance")]
    public async Task<HttpResponseData> PromoteGuidance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "guidance/promote")]
        HttpRequestData req, FunctionContext ctx)
    {
        var parsed = await HttpRequestParser.ParseAsync<PromoteGuidanceRequest>(req, ctx.CancellationToken);
        if (parsed.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parsed.Error);

        var r = parsed.Value;
        if (string.IsNullOrWhiteSpace(r.Text))
            return await HttpResponseHelper.BadRequestAsync(req, "Text is required.");
        if (r.Text.Length > ConfigValidationLimits.MaxTextLength)
            return await HttpResponseHelper.BadRequestAsync(req, $"Text must be at most {ConfigValidationLimits.MaxTextLength} characters.");

        // A promotion must reference a real candidate note: the job id is the candidate's key in
        // the feedback corpus, so arbitrary non-candidate text can no longer be minted into a
        // directive (a ghost or absent job id used to produce a silent "promoted" row).
        string jobId = r.JobId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, "JobId is required.");

        string scopeId = GuidanceEntry.NormalizeScopeId(r.Scope, r.ScopeId);
        if (r.Scope != GuidanceScope.Global && string.IsNullOrWhiteSpace(scopeId))
            return await HttpResponseHelper.BadRequestAsync(req, "ScopeId is required for Market/Station scope.");
        if (r.Scope != GuidanceScope.Global && StorageKey.RejectIfInvalid(req, "scopeId", scopeId) is { } badScope)
            return await badScope;

        // The candidate's market is the promote target's market (Market scope), the market owning
        // the station (Station scope, resolved via the schedule catalogue), or discovered by
        // scanning each market's corpus when promoting to Global.
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(ctx.CancellationToken);
        if (schedulesResult.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, schedulesResult.Error);
        var schedules = schedulesResult.Value.ToArray();

        string market;
        if (r.Scope == GuidanceScope.Market)
        {
            market = scopeId;
        }
        else if (r.Scope == GuidanceScope.Station)
        {
            market = schedules.FirstOrDefault(s =>
                string.Equals(s.Station, scopeId, StringComparison.OrdinalIgnoreCase))?.MarketId
                ?? string.Empty;
            if (string.IsNullOrEmpty(market))
                return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Schedule.NotFound);
        }
        else
        {
            var found = await FindCandidateMarketAsync(schedules, jobId, ctx.CancellationToken);
            if (found.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, found.Error);
            if (found.Value is null)
                return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Guidance.CandidateNotFound);
            market = found.Value;
        }

        if (r.Scope != GuidanceScope.Global)
        {
            Result<IReadOnlyList<FutureLessonEntry>> candidates = r.Scope == GuidanceScope.Station
                ? await feedbackRepository.GetRecentAsync(scopeId, CandidatePageSize, ctx.CancellationToken)
                : await GetCandidatesForMarketAsync(schedules, market, ctx.CancellationToken);
            if (candidates.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, candidates.Error);
            bool known = candidates.Value.Any(c =>
                string.Equals(c.SourceJobId, jobId, StringComparison.Ordinal));
            if (!known)
                return await HttpResponseHelper.NotFoundAsync(req, ErrorMessages.Guidance.CandidateNotFound);
        }

        // Idempotent: the first promotion stored Source = "promoted:{jobId}" on the directive, so a
        // second promote of the same candidate returns that directive instead of airing a duplicate.
        // Every scope the candidate could have been promoted to is scanned (Global, the market, its
        // stations); a store failure fails closed rather than risking a duplicate.
        var promoted = await FindPromotedDirectiveAsync(schedules, market, jobId, ctx.CancellationToken);
        if (promoted.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, promoted.Error);
        if (promoted.Value is not null)
        {
            logger.LogInformation(
                "[Guidance] Candidate {JobId} already promoted as {Scope}:{ScopeId} directive {Id}; returning existing",
                jobId, promoted.Value.Scope, promoted.Value.ScopeId, promoted.Value.Id);
            return await HttpResponseHelper.OkJsonAsync(req, GuidanceDto.From(promoted.Value));
        }

        var source = Option<string>.Some(GuidanceEntry.PromotedSource(jobId));
        var entry = GuidanceEntry.Create(r.Scope, scopeId, r.Type, r.Text, true,
            UpdatedBy(req), timeProvider.GetUtcNow().UtcDateTime, source);

        var result = await repository.UpsertAsync(entry, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        logger.LogInformation("[Guidance] Promoted candidate {JobId} into {Scope}:{ScopeId} directive {Id} by {User}",
            jobId, entry.Scope, entry.ScopeId, entry.Id, entry.CreatedBy);
        return await HttpResponseHelper.OkJsonAsync(req, GuidanceDto.From(entry));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string? Validate(UpsertGuidanceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Text))
            return "Text is required.";
        if (r.Text.Length > ConfigValidationLimits.MaxTextLength)
            return $"Text must be at most {ConfigValidationLimits.MaxTextLength} characters.";
        return null;
    }

    private async Task<GuidanceEntry> BuildEntry(
        UpsertGuidanceRequest r, GuidanceScope scope, string scopeId, string user, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var review = r.ReviewAfterUtc.HasValue ? Option<DateTime>.Some(r.ReviewAfterUtc.Value) : default;

        if (string.IsNullOrWhiteSpace(r.Id))
            return GuidanceEntry.Create(scope, scopeId, r.Type, r.Text, r.Enabled, user, now, reviewAfterUtc: review);

        // Update: keep the id, restamp UpdatedAt, preserve manual provenance AND the original
        // CreatedAtUtc — fetched from the existing entry so an edit never re-stamps creation.
        var normalizedScopeId = GuidanceEntry.NormalizeScopeId(scope, scopeId);
        var existing = await repository.ListAsync(scope, normalizedScopeId, ct);
        var createdAtUtc = existing.IsSuccess
            ? existing.Value.FirstOrDefault(e => e.Id == r.Id)?.CreatedAtUtc ?? now
            : now;

        return new GuidanceEntry(
            r.Id, scope, normalizedScopeId, r.Type, r.Text.Trim(),
            r.Enabled, user, createdAtUtc, now, Option<string>.Some(GuidanceEntry.ManualSource), review);
    }

    private static string UpdatedBy(HttpRequestData req)
        => ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";

    private static string? Query(HttpRequestData req, string key)
        => HttpUtility.ParseQueryString(req.Url.Query).Get(key);

    // ── Promotion / candidate helpers ────────────────────────────────

    /// <summary>Station display names a market owns, per the schedule catalogue (the pipeline's station source).</summary>
    private static IEnumerable<string> StationsOf(IEnumerable<MarketSchedule> schedules, string market)
        => schedules
            .Where(s => string.Equals(s.MarketId, market, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Station)
            .Where(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>
    ///     The market owning a Station-scoped directive on the unscoped routes, resolved via the
    ///     schedule catalogue (the pipeline's station source, and the same lookup GuidanceComposer
    ///     uses to match ScopeId against schedule.Station). Returns Success(null) when the station
    ///     is not in the catalogue; a catalogue failure is a failed result so the caller 500s
    ///     rather than misreporting an unknown station.
    /// </summary>
    private async Task<Result<string?>> ResolveStationMarketAsync(string station, CancellationToken ct)
    {
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(ct);
        if (schedulesResult.IsFailure)
            return Result<string?>.Failure(schedulesResult.Error);

        string? market = schedulesResult.Value
            .FirstOrDefault(s => string.Equals(s.Station, station, StringComparison.OrdinalIgnoreCase))
            ?.MarketId;
        return Result<string?>.Success(market);
    }

    /// <summary>
    ///     The set of job ids whose candidates are already promoted, derived from the
    ///     "promoted:{jobId}" source markers on directives in the candidate's scopes (Global,
    ///     the market, its stations). Fed to the candidates queue so the console can mark
    ///     already-promoted notes.
    /// </summary>
    private async Task<Result<HashSet<string>>> PromotedJobIdsAsync(
        IEnumerable<MarketSchedule> schedules, string market, CancellationToken ct)
    {
        var partitions = new List<Task<Result<IReadOnlyList<GuidanceEntry>>>>(3);
        partitions.Add(repository.ListAsync(GuidanceScope.Global, string.Empty, ct));
        partitions.Add(repository.ListAsync(GuidanceScope.Market, market, ct));
        foreach (var station in StationsOf(schedules, market))
            partitions.Add(repository.ListAsync(GuidanceScope.Station, station, ct));

        var results = await Task.WhenAll(partitions);
        var promoted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            if (r.IsFailure)
                return Result<HashSet<string>>.Failure(r.Error);
            foreach (var entry in r.Value)
                if (GuidanceEntry.PromotedJobIdOf(entry.Source) is { } jobId)
                    promoted.Add(jobId);
        }
        return Result<HashSet<string>>.Success(promoted);
    }

    /// <summary>
    ///     The directive that already carries a "promoted:{jobId}" marker in any of the candidate's
    ///     scopes, or null when this candidate has never been promoted. The promote route returns it
    ///     instead of minting a duplicate directive.
    /// </summary>
    private async Task<Result<GuidanceEntry?>> FindPromotedDirectiveAsync(
        IEnumerable<MarketSchedule> schedules, string market, string jobId, CancellationToken ct)
    {
        string marker = GuidanceEntry.PromotedSource(jobId);
        var partitions = new List<Task<Result<IReadOnlyList<GuidanceEntry>>>>(3);
        partitions.Add(repository.ListAsync(GuidanceScope.Global, string.Empty, ct));
        partitions.Add(repository.ListAsync(GuidanceScope.Market, market, ct));
        foreach (var station in StationsOf(schedules, market))
            partitions.Add(repository.ListAsync(GuidanceScope.Station, station, ct));

        var results = await Task.WhenAll(partitions);
        foreach (var r in results)
        {
            if (r.IsFailure)
                return Result<GuidanceEntry?>.Failure(r.Error);
            var hit = r.Value.FirstOrDefault(e => e.Source.Match(s => s == marker, () => false));
            if (hit is not null)
                return Result<GuidanceEntry?>.Success(hit);
        }
        return Result<GuidanceEntry?>.Success(null);
    }

    private async Task<Result<IReadOnlyList<FutureLessonEntry>>> GetCandidatesForMarketAsync(
        IEnumerable<MarketSchedule> schedules, string market, CancellationToken ct)
    {
        var tasks = StationsOf(schedules, market)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(station => feedbackRepository.GetRecentAsync(station, CandidatePageSize, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        var candidates = new List<FutureLessonEntry>();
        foreach (var result in results)
        {
            if (result.IsFailure)
                return Result<IReadOnlyList<FutureLessonEntry>>.Failure(result.Error);
            candidates.AddRange(result.Value);
        }

        return Result<IReadOnlyList<FutureLessonEntry>>.Success(candidates);
    }

    /// <summary>
    ///     Discovers which market's feedback corpus contains the given job id (Global-scope
    ///     promotion, where the candidate's market is not carried by the request). Returns null
    ///     when no market owns that candidate; a store failure is a failed result so the caller
    ///     surfaces a 500 rather than a misleading 404.
    /// </summary>
    private async Task<Result<string?>> FindCandidateMarketAsync(
        IEnumerable<MarketSchedule> schedules, string jobId, CancellationToken ct)
    {
        foreach (var schedule in schedules
                     .DistinctBy(item => item.Station, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = await feedbackRepository.GetRecentAsync(
                schedule.Station, CandidatePageSize, ct);
            if (candidates.IsFailure)
                return Result<string?>.Failure(candidates.Error);
            if (candidates.Value.Any(c =>
                    string.Equals(c.SourceJobId, jobId, StringComparison.Ordinal)))
                return Result<string?>.Success(schedule.MarketId);
        }
        return Result<string?>.Success(null);
    }

}
