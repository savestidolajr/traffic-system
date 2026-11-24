using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>
///     Holds the last dependency-health result and refreshes it at most once at a time. Provider
///     calls are deliberately kept out of the liveness request path.
/// </summary>
internal sealed class DependencyHealthSnapshot(
    IHttpClientFactory httpClientFactory,
    IOptions<HereOptions> hereOptions,
    IOptions<ElevenLabsOptions> elevenLabsOptions,
    IOptions<AzureAIOptions> azureAiOptions,
    IOptions<ZettaOptions> zettaOptions,
    IScheduleRepository scheduleRepository,
    IOptions<HealthOptions> healthOptions,
    TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Snapshot? _snapshot;

    internal Snapshot? Current => Volatile.Read(ref _snapshot);

    internal async Task<Snapshot> GetOrRefreshAsync(CancellationToken cancellationToken)
    {
        var current = Current;
        if (current is not null && !IsStale(current))
            return current;

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            current = Current;
            if (current is not null && !IsStale(current))
                return current;

            var refreshed = await RefreshAsync(cancellationToken);
            Interlocked.Exchange(ref _snapshot, refreshed);
            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose() => _refreshGate.Dispose();

    private bool IsStale(Snapshot snapshot)
    {
        var age = timeProvider.GetUtcNow() - snapshot.RefreshedAt;
        return age.TotalSeconds >= Math.Max(0, healthOptions.Value.Dependencies.RefreshSeconds);
    }

    private async Task<Snapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        // WhenAll starts all four operations before awaiting any result. This is intentional: a
        // slow provider must not hold up the other three, and each operation owns its own timeout.
        var results = await Task.WhenAll(
            ProbeHereAsync(cancellationToken),
            ProbeElevenLabsAsync(cancellationToken),
            ProbeAzureOpenAiAsync(cancellationToken),
            ProbePlayoutCodesAsync(cancellationToken));

        return new Snapshot(timeProvider.GetUtcNow(), results.Where(r => r is not null)
            .Select(r => r!)
            .ToArray());
    }

    private Task<DependencyCheck?> ProbeHereAsync(CancellationToken cancellationToken)
    {
        string baseUrl = hereOptions.Value.RoutingBaseUrl ?? "https://router.hereapi.com/v8";
        return PingAsync("hereApi", baseUrl.TrimEnd('/') + "/routes?", acceptAnyHttpResponse: true,
            useHead: true, authHeader: null, cancellationToken);
    }

    private Task<DependencyCheck?> ProbeElevenLabsAsync(CancellationToken cancellationToken)
    {
        string baseUrl = elevenLabsOptions.Value.ApiBaseUrl ?? "https://api.elevenlabs.io/v1";
        string apiKey = elevenLabsOptions.Value.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult<DependencyCheck?>(new DependencyCheck("elevenLabs", false,
                "no API key configured — every synthesis will fail"));
        }

        return PingAsync("elevenLabs", baseUrl.TrimEnd('/') + "/user", acceptAnyHttpResponse: true,
            useHead: false, authHeader: ("xi-api-key", apiKey), cancellationToken);
    }

    private Task<DependencyCheck?> ProbeAzureOpenAiAsync(CancellationToken cancellationToken)
    {
        string? endpoint = azureAiOptions.Value.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Task.FromResult<DependencyCheck?>(
                new DependencyCheck("azureOpenAI", false, "endpoint not configured"));
        }

        return PingAsync("azureOpenAI", endpoint.TrimEnd('/') + "/openai/models?api-version=2024-02-01",
            acceptAnyHttpResponse: true, useHead: false, authHeader: null, cancellationToken);
    }

    private async Task<DependencyCheck?> ProbePlayoutCodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var zetta = zettaOptions.Value;
            if (!zetta.Enabled)
                return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetProbeTimeoutMilliseconds());
            var schedules = await scheduleRepository.ListSchedulesAsync(timeoutCts.Token);
            if (schedules.IsFailure)
                return new DependencyCheck("playoutCodes", true, "schedule lookup unavailable");

            var missing = schedules.Value
                .Where(s => s.Enabled && zetta.ResolveMarketCode(s.MarketId) is null)
                .Select(s => s.MarketId)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList();

            return missing.Count == 0
                ? new DependencyCheck("playoutCodes", true, "every enabled market has a Zetta code")
                : new DependencyCheck("playoutCodes", false,
                    $"no Zetta market code for: {string.Join(", ", missing)} — delivery will refuse; "
                    + "add the market to Zetta__MarketCodesJson (e.g. {\"sydney\":\"[SYD]KIIS90s\"}) once ARN confirms the code — a market absent from that map fails rather than guessing a filename");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new DependencyCheck("playoutCodes", true, "timeout (degraded)");
        }
        catch (Exception ex)
        {
            return new DependencyCheck("playoutCodes", false, ex.GetType().Name);
        }
    }

    private int GetProbeTimeoutMilliseconds() =>
        Math.Max(1, healthOptions.Value.Dependencies.ProbeTimeoutSeconds) * 1000;

    private async Task<DependencyCheck?> PingAsync(
        string name,
        string url,
        bool acceptAnyHttpResponse,
        bool useHead,
        (string Name, string Value)? authHeader,
        CancellationToken outerCancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellationToken);
            timeoutCts.CancelAfter(GetProbeTimeoutMilliseconds());

            using var request = new HttpRequestMessage(useHead ? HttpMethod.Head : HttpMethod.Get, url);
            if (authHeader is { } auth)
                request.Headers.TryAddWithoutValidation(auth.Name, auth.Value);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            var (healthy, detail) = HealthHttpTrigger.EvaluateProbe(
                (int)response.StatusCode, acceptAnyHttpResponse, credentialSent: authHeader is not null);
            return new DependencyCheck(name, healthy, detail);
        }
        catch (OperationCanceledException) when (outerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Preserve the existing health semantics: a provider timeout is reachable but
            // degraded, while transport/errors are classified unhealthy.
            return new DependencyCheck(name, true, "timeout (degraded)");
        }
        catch (Exception ex)
        {
            return new DependencyCheck(name, false, ex.GetType().Name);
        }
    }

    internal sealed record Snapshot(DateTimeOffset RefreshedAt, IReadOnlyList<DependencyCheck> Checks);

    internal sealed record DependencyCheck(string Name, bool Healthy, string Detail);
}
