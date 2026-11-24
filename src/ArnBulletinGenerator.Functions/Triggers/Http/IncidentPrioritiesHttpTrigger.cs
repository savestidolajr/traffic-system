using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Per-market incident priorities and dynamic landscape API triggers.
/// </summary>
public sealed class IncidentPrioritiesHttpTrigger(
    IIncidentPriorityRepository repository,
    IScheduleRepository scheduleRepository,
    IMarketAccessService marketAccess,
    ILogger<IncidentPrioritiesHttpTrigger> logger,
    IOptions<IncidentLandscapeOptions>? landscapeOptions = null)
{
    private IncidentLandscapeOptions LandscapeOptions =>
        landscapeOptions?.Value ?? new IncidentLandscapeOptions();

    [Function("GetIncidentPriorities")]
    public async Task<HttpResponseData> GetIncidentPriorities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/incident-priorities")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await repository.GetRulesAsync(market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, new IncidentPrioritiesDto(market, ToDtos(result.Value)));
    }

    [Function("SetIncidentPriorities")]
    public async Task<HttpResponseData> SetIncidentPriorities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "markets/{market}/incident-priorities")]
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

        var parseResult = await HttpRequestParser.ParseAsync<UpsertIncidentPrioritiesRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        if (parseResult.Value.Rules is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Body must contain a 'rules' array.");

        var incoming = parseResult.Value.Rules;
        var rules = incoming
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.IncidentClass))
            .Select(r => new IncidentPriorityRule(
                r.IncidentClass.Trim(),
                Math.Clamp(r.ScoreBonus, -100, 100),
                r.Enabled))
            .ToList();

        string updatedBy = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var result = await repository.SetRulesAsync(market, rules, updatedBy, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        logger.LogInformation("[IncidentPriorities] {Market} updated by {User}: {Count} rule(s)",
            market, updatedBy, rules.Count);

        return await HttpResponseHelper.OkJsonAsync(req, new IncidentPrioritiesDto(market, ToDtos(rules)));
    }

    [Function("IncidentPrioritiesInventory")]
    public async Task<HttpResponseData> IncidentPrioritiesInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incident-priorities/inventory")]
        HttpRequestData req, FunctionContext ctx)
    {
        var scope = await ResolveInventoryMarketsAsync(req, ctx.CancellationToken);
        if (scope.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, scope.Error);

        var results = await ReadBoundedInventoryAsync(
            scope.Value,
            market => repository.GetRulesAsync(market, ctx.CancellationToken),
            ctx.CancellationToken);

        var inventory = new List<IncidentPrioritiesDto>(scope.Value.Count);
        for (int i = 0; i < scope.Value.Count; i++)
            inventory.Add(new IncidentPrioritiesDto(
                scope.Value[i], results[i].IsSuccess ? ToDtos(results[i].Value) : []));

        return await HttpResponseHelper.OkJsonAsync(req, inventory);
    }

    [Function("GetIncidentLandscape")]
    public async Task<HttpResponseData> GetIncidentLandscape(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/incident-landscape")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        bool complete = ParseBoolean(MarketScopeRequest.Query(req, "complete"));
        string? cursor = MarketScopeRequest.Query(req, "cursor")
            ?? MarketScopeRequest.Query(req, "continuation");
        if (!complete && !string.IsNullOrWhiteSpace(cursor))
            return await HttpResponseHelper.BadRequestAsync(req, "cursor requires complete=true");

        IncidentLandscapeOptions settings = LandscapeOptions;
        int defaultLimit = complete
            ? Math.Max(1, settings.CompletePageSize)
            : Math.Max(1, settings.TopN);
        int maxLimit = complete ? Math.Max(defaultLimit, 500) : Math.Max(1, settings.TopN);
        int limit = ParseLimit(MarketScopeRequest.Query(req, "limit"), defaultLimit, maxLimit);

        var result = await repository.GetLandscapePageAsync(
            market, limit, cursor, complete, ctx.CancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Contains("continuation", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.BadRequestAsync(req, result.Error);
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);
        }

        var page = result.Value;
        var dtos = ToLandscapeDtos(page.Entries);
        return await HttpResponseHelper.OkJsonAsync(req, new IncidentLandscapeResponse(
            market, dtos, page.TotalCount, page.ContinuationToken));
    }

    [Function("IncidentLandscapeInventory")]
    public async Task<HttpResponseData> IncidentLandscapeInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "incident-landscape/inventory")]
        HttpRequestData req, FunctionContext ctx)
    {
        var scope = await ResolveInventoryMarketsAsync(req, ctx.CancellationToken);
        if (scope.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, scope.Error);

        bool complete = ParseBoolean(MarketScopeRequest.Query(req, "complete"));
        string? cursor = MarketScopeRequest.Query(req, "cursor")
            ?? MarketScopeRequest.Query(req, "continuation");
        if (!complete && !string.IsNullOrWhiteSpace(cursor))
            return await HttpResponseHelper.BadRequestAsync(req, "cursor requires complete=true");

        IncidentLandscapeOptions settings = LandscapeOptions;
        int defaultLimit = complete
            ? Math.Max(1, settings.CompletePageSize)
            : Math.Max(1, settings.TopN);
        int maxLimit = complete ? Math.Max(defaultLimit, 500) : Math.Max(1, settings.TopN);
        int limit = ParseLimit(MarketScopeRequest.Query(req, "limit"), defaultLimit, maxLimit);

        var results = await ReadBoundedInventoryAsync(
            scope.Value,
            market => repository.GetLandscapePageAsync(
                market, limit, cursor, complete, ctx.CancellationToken),
            ctx.CancellationToken);

        var inventory = new List<IncidentLandscapeResponse>(scope.Value.Count);
        for (int i = 0; i < scope.Value.Count; i++)
        {
            var page = results[i].IsSuccess
                ? results[i].Value
                : new IncidentLandscapePage([], 0);
            inventory.Add(new IncidentLandscapeResponse(
                scope.Value[i],
                ToLandscapeDtos(page.Entries),
                page.TotalCount,
                page.ContinuationToken));
        }

        return await HttpResponseHelper.OkJsonAsync(req, inventory);
    }

    private async Task<Result<IReadOnlyList<string>>> ResolveInventoryMarketsAsync(
        HttpRequestData req, CancellationToken cancellationToken)
    {
        var requested = MarketScopeRequest.ParseMarkets(
            MarketScopeRequest.Query(req, "markets")).ToList();
        var principal = ClientPrincipal.Parse(req);
        Option<IReadOnlyList<string>> allowed = Option<IReadOnlyList<string>>.None;
        if (principal is not null)
            allowed = await marketAccess.GetAllowedMarketsAsync(
                principal.ObjectId, principal.IsAdmin, cancellationToken);

        if (requested.Count == 0)
        {
            if (allowed.IsSome)
                requested.AddRange(allowed.Value);
            else if (LandscapeOptions.BoundedReadsEnabled)
                requested.AddRange(LandscapeOptions.BoundedMarkets);
        }
        requested = requested
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string market in requested)
        {
            if (!ArnBulletinGenerator.Domain.Models.StorageKey.IsValid(market))
                return Result<IReadOnlyList<string>>.Failure(
                    $"market '{market}' contains characters that are not allowed.");
        }

        if (allowed.IsSome)
        {
            var allowedSet = new HashSet<string>(
                allowed.Value.Where(market => !string.IsNullOrWhiteSpace(market)),
                StringComparer.OrdinalIgnoreCase);
            requested = requested.Where(allowedSet.Contains).ToList();
        }

        if (requested.Count == 0)
            return Result<IReadOnlyList<string>>.Failure(
                "markets is required, or the caller must have an authorized bounded market set.");

        int maxMarkets = Math.Max(1, LandscapeOptions.MaxInventoryMarkets);
        if (requested.Count > maxMarkets)
            return Result<IReadOnlyList<string>>.Failure(
                $"At most {maxMarkets} markets may be requested at once.");

        return Result<IReadOnlyList<string>>.Success(requested);
    }

    private async Task<Result<T>[]> ReadBoundedInventoryAsync<T>(
        IReadOnlyList<string> markets,
        Func<string, Task<Result<T>>> read,
        CancellationToken cancellationToken)
    {
        var results = new Result<T>[markets.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, Math.Min(markets.Count,
            LandscapeOptions.MaxInventoryMarkets)));
        var tasks = markets.Select((market, index) => ReadOneAsync(market, index));
        await Task.WhenAll(tasks);
        return results;

        async Task ReadOneAsync(string market, int index)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[index] = await read(market);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private static bool ParseBoolean(string? raw) =>
        bool.TryParse(raw, out bool value) && value;

    private static int ParseLimit(string? raw, int fallback, int maximum) =>
        !int.TryParse(raw, out int parsed)
            ? fallback
            : Math.Clamp(parsed, 1, Math.Max(1, maximum));

    private static IReadOnlyList<IncidentLandscapeEntryDto> ToLandscapeDtos(
        IReadOnlyList<IncidentLandscapeEntry> entries) =>
        entries.Select(e => new IncidentLandscapeEntryDto(
            e.Provider,
            e.CanonicalClass,
            e.EventType,
            e.Details,
            e.Impact,
            e.Codes,
            e.Count,
            e.FirstSeenUtc,
            e.LastSeenUtc)).ToList();

    private static IReadOnlyList<IncidentPriorityRuleDto> ToDtos(IReadOnlyList<IncidentPriorityRule> rules) =>
        rules.Select(r => new IncidentPriorityRuleDto(r.IncidentClass, r.ScoreBonus, r.Enabled)).ToList();
}

public sealed record IncidentPriorityRuleDto(string IncidentClass, int ScoreBonus, bool Enabled = true);
public sealed record IncidentPrioritiesDto(string Market, IReadOnlyList<IncidentPriorityRuleDto> Rules);
public sealed record UpsertIncidentPrioritiesRequest(IReadOnlyList<IncidentPriorityRuleDto>? Rules = null);

public sealed record IncidentLandscapeEntryDto(
    string Provider,
    string CanonicalClass,
    string EventType,
    string Details,
    string Impact,
    string? Codes,
    int Count,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record IncidentLandscapeResponse(
    string Market,
    IReadOnlyList<IncidentLandscapeEntryDto> Entries,
    int TotalCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContinuationToken = null);
