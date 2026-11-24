using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Infrastructure.Configuration;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     On-demand run of the data-validity probe, returning the per-market comparison so an engineer
///     can see the rates immediately instead of waiting for the nightly timer and the metrics
///     pipeline. Admin-only via the route-role matrix default (POST /api/metrics/* is not an
///     operator surface). Emits the same <c>data.validity</c> metrics as the nightly run.
/// </summary>
public sealed class ValidityProbeHttpTrigger(
    ValidityMonitorService validityMonitor,
    IOptions<OfficialFeedsOptions> officialFeeds,
    ILogger<ValidityProbeHttpTrigger> logger)
{
    [Function("RunValidityProbe")]
    public async Task<HttpResponseData> RunValidityProbe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "metrics/validity-probe")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        if (!officialFeeds.Value.Enabled)
            return await HttpResponseHelper.BadRequestAsync(req, "OfficialFeeds is disabled.");

        logger.LogInformation("[ValidityMonitor] on-demand probe requested");
        var results = await validityMonitor.RunAsync(ctx.CancellationToken);
        return await HttpResponseHelper.OkJsonAsync(req, results);
    }
}
