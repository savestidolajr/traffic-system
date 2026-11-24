using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Read surface for the playout drop tray: whether a market's delivery file is still
///     sitting in the drop folder awaiting pickup (or has been collected, or delivery is
///     disabled). The on-air board shows this so the operator knows a same-name overwrite for
///     the next slot is safe — present = not yet picked up = the swap replaces it; absent =
///     collected = writing again queues a NEW file that may air in the wrong break.
/// </summary>
public sealed class PlayoutTrayHttpTrigger(
    IFileDeliveryService fileDeliveryService,
    IMarketAccessService marketAccess,
    ILogger<PlayoutTrayHttpTrigger> logger)
{
    [Function("GetPlayoutTrayStatus")]
    public async Task<HttpResponseData> GetPlayoutTrayStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "markets/{marketId}/playout-tray")]
        HttpRequestData req,
        string marketId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            return await HttpResponseHelper.BadRequestAsync(req, "Market is required.");

        if (await marketAccess.EnsureMarketAccessAsync(req, marketId, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var result = await fileDeliveryService.GetTrayStatusAsync(marketId, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.NotFoundAsync);

        return await HttpResponseHelper.OkJsonAsync(req, result.Value);
    }
}
