using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Validation;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Per-market priority-roads management (Workshop 2 / SDD §4.2). Operators maintain the
///     list (experts pre-populate, operators refine), so both routes require Operator only.
/// </summary>
public sealed class PriorityRoadsHttpTrigger(
    IPriorityRoadsRepository repository,
    IScheduleRepository scheduleRepository,
    IMarketAccessService marketAccess,
    ILogger<PriorityRoadsHttpTrigger> logger)
{
    [Function("GetPriorityRoads")]
    public async Task<HttpResponseData> GetPriorityRoads(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{market}/priority-roads")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await repository.GetRoadsAsync(market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, new PriorityRoadsDto(market, ToDtos(result.Value)));
    }

    [Function("SetPriorityRoads")]
    public async Task<HttpResponseData> SetPriorityRoads(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "markets/{market}/priority-roads")]
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

        var parseResult = await HttpRequestParser.ParseAsync<UpsertPriorityRoadsRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        if (parseResult.Value.Roads is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Body must contain a 'roads' array.");

        // Validate what the caller actually sent, then build. The previous shape filtered blanks
        // and de-duplicated BEFORE checking, so an operator's row could vanish under a 200 and the
        // count limit described the survivors rather than the payload.
        var incoming = parseResult.Value.Roads;

        if (incoming.Count > ConfigValidationLimits.MaxRoads)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"At most {ConfigValidationLimits.MaxRoads} priority roads per market (received {incoming.Count}).");

        if (incoming.Any(r => r is null || string.IsNullOrWhiteSpace(r.Name)))
            return await HttpResponseHelper.BadRequestAsync(req,
                "Every priority road needs a name.");

        if (incoming.Any(r => r!.Name.Trim().Length > ConfigValidationLimits.MaxRoadLength))
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Road names must be at most {ConfigValidationLimits.MaxRoadLength} characters.");

        if (incoming.Any(r => OperatorTextGuard.HasControlOrFormatCharacters(r!.Name)))
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Every priority road name {OperatorTextGuard.RejectionMessage}.");

        var invalidFlowTier = incoming.FirstOrDefault(r =>
            !PriorityRoadMapping.TryWireToTier(r!.FlowTier, out _));
        if (invalidFlowTier is not null)
            return await HttpResponseHelper.BadRequestAsync(req,
                PriorityRoadMapping.InvalidFlowTierMessage(invalidFlowTier.FlowTier));

        var duplicates = incoming
            .GroupBy(r => r!.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Each road may appear once. Duplicated: {string.Join(", ", duplicates)}.");

        var roads = incoming
            .Select(r => new PriorityRoad(
                r!.Name.Trim(), r.BoostIncidents, PriorityRoadMapping.WireToTier(r.FlowTier), r.Enabled))
            .ToList();

        string updatedBy = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var result = await repository.SetRoadsAsync(market, roads, updatedBy, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        logger.LogInformation("[PriorityRoads] {Market} updated by {User}: {Count} road(s)",
            market, updatedBy, roads.Count);

        return await HttpResponseHelper.OkJsonAsync(req, new PriorityRoadsDto(market, ToDtos(roads)));
    }

    /// <summary>
    ///     Aggregate inventory: priority roads for every requested market the caller can access,
    ///     in ONE round-trip. Replaces the operator console's per-market fan-out. Markets the
    ///     caller is not assigned to are dropped; a per-market store failure yields an empty
    ///     road list for that market rather than failing the whole inventory.
    /// </summary>
    [Function("PriorityRoadsInventory")]
    public async Task<HttpResponseData> PriorityRoadsInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "priority-roads/inventory")]
        HttpRequestData req, FunctionContext ctx)
    {
        var principal = ClientPrincipal.Parse(req);
        var allowed = principal is null
            ? Option<IReadOnlyList<string>>.None
            : await marketAccess.GetAllowedMarketsAsync(principal.ObjectId, principal.IsAdmin, ctx.CancellationToken);
        var markets = MarketScopeRequest.FilterToAllowed(
            MarketScopeRequest.ParseMarkets(MarketScopeRequest.Query(req, "markets")), allowed);

        var results = await Task.WhenAll(
            markets.Select(m => repository.GetRoadsAsync(m, ctx.CancellationToken)));

        var inventory = new List<PriorityRoadsDto>(markets.Count);
        for (int i = 0; i < markets.Count; i++)
            inventory.Add(new PriorityRoadsDto(markets[i], results[i].IsSuccess ? ToDtos(results[i].Value) : []));

        return await HttpResponseHelper.OkJsonAsync(req, inventory);
    }

    internal static PriorityRoadDto ToDto(PriorityRoad r) => new(r.Name, r.BoostIncidents, TierToWire(r.FlowTier), r.Enabled);

    internal static string TierToWire(FlowPriorityTier t) => t switch
    {
        FlowPriorityTier.Normal => "normal",
        FlowPriorityTier.High => "high",
        FlowPriorityTier.Critical => "critical",
        _ => "none"
    };

    internal static IReadOnlyList<PriorityRoadDto> ToDtos(IReadOnlyList<PriorityRoad> roads) =>
        roads.Select(ToDto).ToList();

}
