using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Per-market filler-statement management (BFN-002). Short statements used to fill remaining
///     slot time. Both routes are Operator + market-scoped: the operator is who hears a filler
///     read badly on air, so it sits with priority roads and verified pronunciations rather than
///     with network-wide editorial policy.
/// </summary>
public sealed class FillerStatementsHttpTrigger(
    IFillerStatementsRepository repository,
    IFillerAudioCacheService fillerAudioCacheService,
    IScheduleRepository scheduleRepository,
    IMarketAccessService marketAccess,
    ILogger<FillerStatementsHttpTrigger> logger)
{
    [Function("GetFillerStatements")]
    public async Task<HttpResponseData> GetFillerStatements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/filler-statements")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await repository.GetStatementsAsync(market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, new FillerStatementsDto(market, ToDtos(result.Value)));
    }

    [Function("SetFillerStatements")]
    public async Task<HttpResponseData> SetFillerStatements(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "markets/{market}/filler-statements")]
        HttpRequestData req,
        string market, FunctionContext ctx)
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

        var parseResult = await HttpRequestParser.ParseAsync<UpsertFillerStatementsRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        if (parseResult.Value.Statements is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Body must contain a 'statements' array.");

        // Validate the payload the client actually sent, THEN build. The previous shape filtered
        // blanks and de-duplicated before checking, so the cap described the survivors (a
        // 51-item payload with 2 blanks stored 49 under a 200) and blanks vanished silently.
        // Mirrors the priority-roads route: count first, then reject blanks.
        var incoming = parseResult.Value.Statements;

        if (incoming.Count > ConfigValidationLimits.MaxStatements)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"At most {ConfigValidationLimits.MaxStatements} filler statements per market (received {incoming.Count}).");

        if (incoming.Any(s => s is null || string.IsNullOrWhiteSpace(s.Text)))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Every filler statement needs text.");

        // Two statements sharing an explicit id would previously collapse to the first (silent
        // first-wins dedupe under a 200) — the config-import planner rejects the identical payload
        // with "Each filler statement id may appear only once", so the interactive PUT must too.
        // A copied-then-tweaked statement that keeps the source id is a caller error, not a
        // silently-dropped edit.
        var duplicatedStatementIds = incoming
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicatedStatementIds.Count > 0)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Each filler statement id may appear only once. Duplicated: {string.Join(", ", duplicatedStatementIds)}.");

        var built = new List<FillerStatement>(incoming.Count);
        foreach (var s in incoming)
        {
            // Availability windows are validated HERE at the write boundary (dates, times, day
            // names, end>=start, both-or-neither window bounds) so a malformed window is a
            // clear 400 instead of a silently-stored rule that never matches.
            var availability = FillerAvailabilityMapping.ToDomain(s.Availability);
            if (availability.IsFailure)
                return await HttpResponseHelper.BadRequestAsync(req, availability.Error);
            built.Add(new FillerStatement(
                string.IsNullOrWhiteSpace(s.Id) ? Guid.NewGuid().ToString("N") : s.Id.Trim(),
                s.Text.Trim(),
                s.Enabled,
                availability.Value));
        }

        var statements = built;

        var invalid = statements.FirstOrDefault(s => s.Text.Length > ConfigValidationLimits.MaxStatementLength);
        if (invalid is not null)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Statement text must be at most {ConfigValidationLimits.MaxStatementLength} characters (found {invalid.Text.Length}).");

        // Filler is appended VERBATIM by the pipeline — no token substitution exists for it — so
        // bookend-style {token} syntax would be read aloud as literal braces on air. Reject it at
        // the write boundary so the bad content never gets stored.
        var tokenStatement = statements.FirstOrDefault(s => BookendTokenResolver.ExtractTokens(s.Text).Count > 0);
        if (tokenStatement is not null)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Filler text cannot use bookend token syntax ("
                + $"{string.Join(", ", BookendTokenResolver.ExtractTokens(tokenStatement.Text))}). "
                + "Tokens are only substituted in bookends — remove the braces from this statement.");

        var oldStatementsResult = await repository.GetStatementsAsync(market, ctx.CancellationToken);
        if (oldStatementsResult.IsSuccess)
        {
            var failures = await FillerAudioCacheInvalidator.InvalidateChangedAsync(
                fillerAudioCacheService, oldStatementsResult.Value, statements, ctx.CancellationToken);
            foreach (var (id, error) in failures)
                logger.LogWarning(
                    "[FillerAudioCache] Failed to invalidate cached audio for statement {StatementId}: {Error}",
                    id, error);
        }

        string updatedBy = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var result = await repository.SetStatementsAsync(market, statements, updatedBy, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        logger.LogInformation("[FillerStatements] {Market} updated by {User}: {Count} statement(s)",
            market, updatedBy, statements.Count);

        return await HttpResponseHelper.OkJsonAsync(req, new FillerStatementsDto(market, ToDtos(statements)));
    }

    /// <summary>
    ///     Aggregate inventory: filler statements for every requested market the caller can access,
    ///     in ONE round-trip. Markets the caller is not assigned to are dropped; a per-market store
    ///     failure yields an empty statement list for that market rather than failing the whole inventory.
    /// </summary>
    [Function("FillerStatementsInventory")]
    public async Task<HttpResponseData> FillerStatementsInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "filler-statements/inventory")]
        HttpRequestData req, FunctionContext ctx)
    {
        var principal = ClientPrincipal.Parse(req);
        var allowed = principal is null
            ? Option<IReadOnlyList<string>>.None
            : await marketAccess.GetAllowedMarketsAsync(principal.ObjectId, principal.IsAdmin, ctx.CancellationToken);
        var markets = MarketScopeRequest.FilterToAllowed(
            MarketScopeRequest.ParseMarkets(MarketScopeRequest.Query(req, "markets")), allowed);

        var results = await Task.WhenAll(
            markets.Select(m => repository.GetStatementsAsync(m, ctx.CancellationToken)));

        var inventory = new List<FillerStatementsDto>(markets.Count);
        for (int i = 0; i < markets.Count; i++)
            inventory.Add(new FillerStatementsDto(markets[i], results[i].IsSuccess ? ToDtos(results[i].Value) : []));

        return await HttpResponseHelper.OkJsonAsync(req, inventory);
    }

    internal static FillerStatementDto ToDto(FillerStatement s) =>
        new(s.Id, s.Text, s.Enabled, FillerAvailabilityMapping.ToDto(s.Availability));

    internal static IReadOnlyList<FillerStatementDto> ToDtos(IReadOnlyList<FillerStatement> statements) =>
        statements.Select(ToDto).ToList();

}

