using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Ledger;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The Event Ledger API: the Desk's triage feed (fold + verdicts + trust + feed health),
///     verdict writes, per-bulletin diff strips, unvetted counts for the on-air board, and
///     spot-check sign-offs. All operator-facing; market routes enforce market access.
/// </summary>
public sealed class LedgerHttpTrigger(
    IEventLedgerService ledgerService,
    IMarketAccessService marketAccess,
    ArnBulletinGenerator.Domain.Interfaces.IJobRepository jobRepository,
    ILogger<LedgerHttpTrigger> logger,
    IConfiguration? configuration = null)
{
    private const int DefaultSpotCheckLimit = 100;
    private const int MinSpotCheckLimit = 1;
    private const int MaxSpotCheckLimit = 500;

    private bool SpotCheckPagingDisabled =>
        !bool.TryParse(configuration?["SpotChecks:PagingDisabled"], out var disabled) || disabled;
    [Function("GetLedger")]
    public async Task<HttpResponseData> GetLedger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ledger/{market}")]
        HttpRequestData req,
        string market, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await ledgerService.GetLedgerAsync(market, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, result.Value);
    }

    [Function("SetEventVerdict")]
    public async Task<HttpResponseData> SetEventVerdict(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ledger/{market}/events/{eventKey}/verdict")]
        HttpRequestData req,
        string market, string eventKey, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(market) || string.IsNullOrWhiteSpace(eventKey))
            return await HttpResponseHelper.BadRequestAsync(req, "Market and event key are required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var parseResult = await HttpRequestParser.ParseAsync<SetVerdictRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        string by = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var result = await ledgerService.SetVerdictAsync(
            market, Uri.UnescapeDataString(eventKey),
            parseResult.Value.Verdict, parseResult.Value.Notes, by, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, result.Error);

        return HttpResponseHelper.NoContent(req);
    }

    [Function("GetJobDelta")]
    public async Task<HttpResponseData> GetJobDelta(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}/delta")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, "Job id is required.");

        var result = await ledgerService.GetJobDeltaAsync(jobId, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.NotFoundAsync(req, result.Error);

        return await HttpResponseHelper.OkJsonAsync(req, result.Value);
    }

    [Function("GetSpotChecks")]
    public async Task<HttpResponseData> GetSpotChecks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "spot-checks")]
        HttpRequestData req, FunctionContext ctx)
    {
        // The complete-list contract remains the default and is also the kill-switch path. A
        // caller must explicitly request cursor/limit pagination while the flag is enabled.
        string? cursor = MarketScopeRequest.Query(req, "cursor");
        string? limitRaw = MarketScopeRequest.Query(req, "limit");
        if (SpotCheckPagingDisabled || (string.IsNullOrWhiteSpace(cursor) && string.IsNullOrWhiteSpace(limitRaw)))
        {
            var legacy = await ledgerService.GetSpotChecksAsync(ctx.CancellationToken);
            if (legacy.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, legacy.Error);
            return await HttpResponseHelper.OkJsonAsync(req, new { Items = legacy.Value });
        }

        int limit = int.TryParse(limitRaw, out int parsedLimit)
            ? Math.Clamp(parsedLimit, MinSpotCheckLimit, MaxSpotCheckLimit)
            : DefaultSpotCheckLimit;
        var page = await ledgerService.GetSpotChecksPageAsync(
            new VersionedLibraryListQuery(PageSize: limit, Cursor: cursor), ctx.CancellationToken);
        if (page.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, page.Error);

        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            Items = page.Value.Items,
            Total = page.Value.Total,
            HasMore = page.Value.NextCursor is not null,
            page.Value.NextCursor
        });
    }

    [Function("MarkSpotCheck")]
    public async Task<HttpResponseData> MarkSpotCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "spot-checks/{jobId}")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, "Job id is required.");

        // Scoped like every job write: sign-off is only meaningful from an operator
        // assigned to the bulletin's own market.
        var jobResult = await jobRepository.GetJobAsync(jobId, ctx.CancellationToken);
        if (jobResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, jobResult.Error);
        if (await marketAccess.EnsureMarketAccessAsync(
                req, jobResult.Value.Market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        string by = ClientPrincipal.Parse(req)?.Name is { Length: > 0 } name ? name : "unknown";
        var result = await ledgerService.MarkSpotCheckAsync(jobId, by, ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        return HttpResponseHelper.NoContent(req);
    }
}
