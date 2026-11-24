using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Health-check endpoint pinging blob storage, Key Vault, HERE, ElevenLabs, and Azure
///     OpenAI, plus a configuration check for playout market codes, reporting
///     critical-vs-degraded status per dependency.
/// </summary>
public static class HealthHttpTrigger
{
    private static readonly TimeSpan DownstreamPingTimeout = TimeSpan.FromSeconds(3);

    [Function("Health")]
    public static async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        FunctionContext context)
    {
        var logger = context.GetLogger("Health");
        var checks = new List<HealthCheckResult>();
        var stopwatch = Stopwatch.StartNew();
        var ct = context.CancellationToken;

        await AddStorageCheckAsync(context, checks);
        AddKeyVaultCheck(context, checks);
        await AddHereCheckAsync(context, checks, ct);
        await AddElevenLabsCheckAsync(context, checks, ct);
        await AddAzureOpenAiCheckAsync(context, checks, ct);
        await AddPlayoutCodeCheckAsync(context, checks, ct);

        stopwatch.Stop();
        bool allCriticalHealthy = checks.Where(c => c.Critical).All(c => c.Healthy);

        var response = req.CreateResponse(
            allCriticalHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        response.Headers.Add("Content-Type", "application/json");

        var body = new
        {
            status = allCriticalHealthy ? "healthy" : "degraded",
            timestamp = DateTimeOffset.UtcNow,
            duration = stopwatch.ElapsedMilliseconds + "ms",
            checks
        };

        await response.WriteStringAsync(
            JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true }));

        // Warn on ANY unhealthy dependency, not only a critical one. A non-critical failure still
        // stops the product working — an ElevenLabs credential the provider refuses means no
        // bulletin can be voiced — and logging only critical failures is why the 2026-08-05 TTS
        // outage ran for hours with nothing in telemetry to find.
        var unhealthy = checks.Where(c => !c.Healthy).ToList();
        if (unhealthy.Count > 0)
            logger.LogWarning("Health check dependencies unhealthy ({Status}): {Failed}",
                allCriticalHealthy ? "degraded" : "critical",
                string.Join(", ", unhealthy.Select(c => $"{c.Name} [{c.Detail}]")));

        return response;
    }

    private static async Task AddStorageCheckAsync(FunctionContext context, List<HealthCheckResult> checks)
    {
        try
        {
            if (context.InstanceServices?.GetService(typeof(BlobServiceClient)) is BlobServiceClient blobServiceClient)
            {
                // Use a data-plane call (not GetPropertiesAsync which is management-plane
                // and requires Storage Account Contributor, not just data-plane roles).
                await foreach (var _ in blobServiceClient.GetBlobContainersAsync(prefix: "broadcasts").AsPages(pageSizeHint: 1))
                {
                    break;
                }
                checks.Add(new HealthCheckResult("storage", true, "reachable", Critical: true));
            }
            else
            {
                checks.Add(new HealthCheckResult("storage", false, "BlobServiceClient not registered", Critical: true));
            }
        }
        catch
        {
            checks.Add(new HealthCheckResult("storage", false, "unreachable", Critical: true));
        }
    }

    private static void AddKeyVaultCheck(FunctionContext context, List<HealthCheckResult> checks)
    {
        try
        {
            string? kvUri = context.InstanceServices?.GetService(typeof(IConfiguration)) is IConfiguration config
                ? config["KeyVaultUri"]
                : null;
            checks.Add(new HealthCheckResult(
                "keyVault",
                !string.IsNullOrEmpty(kvUri),
                string.IsNullOrEmpty(kvUri) ? "KeyVaultUri not configured" : "configured",
                Critical: true));
        }
        catch
        {
            checks.Add(new HealthCheckResult("keyVault", false, "unreachable", Critical: true));
        }
    }

    private static async Task AddHereCheckAsync(FunctionContext context, List<HealthCheckResult> checks, CancellationToken ct)
    {
        // Non-critical: a HERE outage does not fail the health endpoint, but the check
        // still tracks reachability for ops dashboards.
        if (context.InstanceServices?.GetService(typeof(IHttpClientFactory)) is not IHttpClientFactory factory)
        {
            checks.Add(new HealthCheckResult("hereApi", false, "IHttpClientFactory not registered"));
            return;
        }

        var hereOpts = (context.InstanceServices.GetService(typeof(IOptions<HereOptions>)) as IOptions<HereOptions>)?.Value;
        string baseUrl = hereOpts?.RoutingBaseUrl ?? "https://router.hereapi.com/v8";
        await PingAsync(factory, "hereApi", baseUrl + "/routes?", checks, ct,
            // 401/400 still proves the endpoint is reachable; we only care about transport reachability.
            acceptAnyHttpResponse: true,
            useHead: true);
    }

    private static async Task AddElevenLabsCheckAsync(FunctionContext context, List<HealthCheckResult> checks, CancellationToken ct)
    {
        if (context.InstanceServices?.GetService(typeof(IHttpClientFactory)) is not IHttpClientFactory factory)
        {
            checks.Add(new HealthCheckResult("elevenLabs", false, "IHttpClientFactory not registered"));
            return;
        }

        var elOpts = (context.InstanceServices.GetService(typeof(IOptions<ElevenLabsOptions>)) as IOptions<ElevenLabsOptions>)?.Value;
        string baseUrl = elOpts?.ApiBaseUrl ?? "https://api.elevenlabs.io/v1";
        string apiKey = elOpts?.ApiKey ?? string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            checks.Add(new HealthCheckResult("elevenLabs", false,
                "no API key configured — every synthesis will fail"));
            return;
        }

        // Send the REAL credential. Probing /user unauthenticated only proves the host is
        // reachable, which is why an invalid key read as "healthy" for the whole of the
        // 2026-08-05 outage: ElevenLabs began rejecting the legacy 64-char key at ~22:20Z and
        // /api/health still said elevenLabs=True (detail "HTTP 401") while every bulletin fell
        // back. A credential the provider refuses is not a healthy dependency.
        await PingAsync(factory, "elevenLabs", baseUrl.TrimEnd('/') + "/user", checks, ct,
            acceptAnyHttpResponse: true,
            authHeader: ("xi-api-key", apiKey));
    }

    private static async Task AddAzureOpenAiCheckAsync(FunctionContext context, List<HealthCheckResult> checks, CancellationToken ct)
    {
        if (context.InstanceServices?.GetService(typeof(IHttpClientFactory)) is not IHttpClientFactory factory)
        {
            checks.Add(new HealthCheckResult("azureOpenAI", false, "IHttpClientFactory not registered"));
            return;
        }

        var aiOpts = (context.InstanceServices.GetService(typeof(IOptions<AzureAIOptions>)) as IOptions<AzureAIOptions>)?.Value;
        string? endpoint = aiOpts?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            checks.Add(new HealthCheckResult("azureOpenAI", false, "endpoint not configured"));
            return;
        }

        await PingAsync(factory, "azureOpenAI", endpoint.TrimEnd('/') + "/openai/models?api-version=2024-02-01",
            checks, ct, acceptAnyHttpResponse: true);
    }

    /// <summary>
    ///     Reports any market that is scheduled to air but has no Zetta market code, so the gap is
    ///     visible on every health poll instead of only when an operator hits Publish. The code is
    ///     ARN's playout-side naming convention and cannot be derived, so delivery refuses rather
    ///     than guessing (<see cref="Infrastructure.Configuration.ZettaOptions.ResolveMarketCode" />)
    ///     — but refusing at the end of the pipeline, after generation, review and approval, is a
    ///     terrible place to learn about a config gap. Non-critical: it must not flip the endpoint
    ///     to degraded, because an uncoded market is a configuration to-do, not an outage. Skipped
    ///     entirely when Zetta delivery is off, since no market needs a code then.
    ///     <para>
    ///         Adding a code needs no deployment: it is an app setting
    ///         (<c>Zetta__MarketCodes__&lt;marketId&gt;</c>) and takes effect on the next restart.
    ///     </para>
    /// </summary>
    private static async Task AddPlayoutCodeCheckAsync(
        FunctionContext context, List<HealthCheckResult> checks, CancellationToken ct)
    {
        try
        {
            var zetta = (context.InstanceServices?.GetService(typeof(IOptions<ZettaOptions>))
                as IOptions<ZettaOptions>)?.Value;
            if (zetta is null || !zetta.Enabled)
                return;

            if (context.InstanceServices?.GetService(typeof(IScheduleRepository)) is not IScheduleRepository repo)
            {
                checks.Add(new HealthCheckResult("playoutCodes", false, "IScheduleRepository not registered"));
                return;
            }

            var schedules = await repo.ListSchedulesAsync(ct);
            if (schedules.IsFailure)
            {
                checks.Add(new HealthCheckResult("playoutCodes", true, "schedule lookup unavailable"));
                return;
            }

            var missing = schedules.Value
                .Where(s => s.Enabled && zetta.ResolveMarketCode(s.MarketId) is null)
                .Select(s => s.MarketId)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList();

            checks.Add(missing.Count == 0
                ? new HealthCheckResult("playoutCodes", true, "every enabled market has a Zetta code")
                : new HealthCheckResult("playoutCodes", false,
                    $"no Zetta market code for: {string.Join(", ", missing)} — delivery will refuse; "
                    + "set Zetta__MarketCodes__<marketId> once ARN confirms the code"));
        }
        catch (Exception ex)
        {
            checks.Add(new HealthCheckResult("playoutCodes", false, ex.GetType().Name));
        }
    }

    private static async Task PingAsync(
        IHttpClientFactory factory,
        string name,
        string url,
        List<HealthCheckResult> checks,
        CancellationToken outerCt,
        bool acceptAnyHttpResponse,
        bool useHead = false,
        (string Name, string Value)? authHeader = null)
    {
        try
        {
            var client = factory.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            timeoutCts.CancelAfter(DownstreamPingTimeout);

            using var req = new HttpRequestMessage(useHead ? HttpMethod.Head : HttpMethod.Get, url);
            if (authHeader is { } auth)
                req.Headers.TryAddWithoutValidation(auth.Name, auth.Value);

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            var (ok, detail) = EvaluateProbe(
                (int)resp.StatusCode, acceptAnyHttpResponse, credentialSent: authHeader is not null);

            checks.Add(new HealthCheckResult(name, ok, detail, Critical: false));
        }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
        {
            checks.Add(new HealthCheckResult(name, true, "timeout (degraded)"));
        }

        catch (Exception ex)
        {
            checks.Add(new HealthCheckResult(name, false, ex.GetType().Name));
        }
    }

    /// <summary>
    ///     Turns a probe's status code into a health verdict. Extracted so the credential rule is
    ///     directly testable: when the probe carried a credential, an auth rejection (401/403, and
    ///     400 — which is what ElevenLabs returns for a malformed key) is a REAL failure no matter
    ///     how lenient <paramref name="acceptAnyHttpResponse" /> is, because the dependency is
    ///     reachable but unusable. Without a credential the probe only measures transport
    ///     reachability, so a 401 remains acceptable.
    ///     <para>
    ///         This rule exists because of the 2026-08-05 dev outage: ElevenLabs began refusing the
    ///         legacy 64-character key at ~22:20Z, every bulletin fell back or failed for hours, and
    ///         <c>/api/health</c> reported <c>elevenLabs=True</c> with detail "HTTP 401" throughout,
    ///         because the probe sent no key and accepted any response.
    ///     </para>
    /// </summary>
    internal static (bool Ok, string Detail) EvaluateProbe(
        int statusCode, bool acceptAnyHttpResponse, bool credentialSent)
    {
        bool credentialRejected = credentialSent && statusCode is 400 or 401 or 403;

        bool ok = !credentialRejected
                  && (acceptAnyHttpResponse || statusCode is >= 200 and <= 299);

        string detail = credentialRejected
            ? $"HTTP {statusCode} — credential REJECTED; the configured key is invalid or no longer "
              + "accepted, so every call to this provider will fail"
            : $"HTTP {statusCode}";

        return (ok, detail);
    }

    private sealed record HealthCheckResult(string Name, bool Healthy, string Detail, bool Critical = false);
}
