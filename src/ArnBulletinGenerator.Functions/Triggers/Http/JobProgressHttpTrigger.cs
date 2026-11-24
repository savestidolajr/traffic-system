using System.Net;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using ArnBulletinGenerator.Functions.Auth;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Read-side endpoint for the live generation progress document. It authorizes from the cheap
///     job index and never materializes the full aggregate, keeping the console's two-second poll
///     inexpensive while a bulletin is running.
/// </summary>
public sealed class JobProgressHttpTrigger(
    IJobRepository jobRepository,
    IJobProgressRepository progressRepository,
    IMarketAccessService marketAccess,
    ILogger<JobProgressHttpTrigger> logger)
{
    [Function("GetJobProgress")]
    public async Task<HttpResponseData> GetJobProgress(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}/progress")]
        HttpRequestData req,
        string jobId,
        FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, "Job id is required.");

        var indexResult = await jobRepository.GetJobIndexAsync(jobId, ctx.CancellationToken);
        if (indexResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, indexResult.Error);

        if (await marketAccess.EnsureMarketAccessAsync(
                req, indexResult.Value.Market, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var progressResult = await progressRepository.GetAsync(jobId, ctx.CancellationToken);
        if (progressResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, progressResult.Error);
        if (progressResult.Value.IsNone)
            return await HttpResponseHelper.NotFoundAsync(req, "Job progress was not found.");

        var document = progressResult.Value.Value.Document;
        IReadOnlyDictionary<string, double>? typicalSeconds = null;
        try
        {
            var typicalResult = await progressRepository.GetTypicalAsync(
                indexResult.Value.Market, ctx.CancellationToken);
            if (typicalResult.IsSuccess && typicalResult.Value.HasValue)
                typicalSeconds = typicalResult.Value.Value;
            else if (typicalResult.IsFailure)
                logger.LogWarning(
                    "[JobProgress] Typical duration read failed for market {Market}: {Error}",
                    indexResult.Value.Market, typicalResult.Error);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "[JobProgress] Typical duration read threw for market {Market}",
                indexResult.Value.Market);
        }

        // GetAsync may have loaded a previous typical row. Replace it with this request's point read
        // so a missing history row is represented as null rather than stale data.
        document.SetTypicalSeconds(typicalSeconds);
        var plan = JobProgressResponsePlanner.Plan(document, Header(req, "If-None-Match"));

        if (plan.Kind == JobProgressResponseKind.NotModified)
        {
            var notModified = req.CreateResponse(HttpStatusCode.NotModified);
            AddCacheHeaders(notModified, plan.ETag!);
            return notModified;
        }

        var response = await HttpResponseHelper.OkJsonAsync(req, document.ToProgressDto());
        AddCacheHeaders(response, plan.ETag!);
        return response;
    }

    private static string? Header(HttpRequestData req, string name) =>
        req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static void AddCacheHeaders(HttpResponseData response, string etag)
    {
        response.Headers.Add("ETag", etag);
        response.Headers.Add("Cache-Control", "no-cache");
    }
}
