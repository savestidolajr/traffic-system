using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Metrics;

/// <summary>
/// Publishes bounded-cardinality cache, coalescing, wait, and execution telemetry for
/// interactive LLM/TTS work. Telemetry failures never affect the caller's request.
/// </summary>
public sealed class ApplicationInsightsInteractiveWorkMetrics(
    TelemetryClient telemetryClient,
    ILogger<ApplicationInsightsInteractiveWorkMetrics> logger) : IInteractiveWorkMetrics
{
    private const int MaxDimensionLength = 128;

    public void TrackCacheHit(InteractiveWorkPool pool, string operation) =>
        Track("interactive.cache.hit.count", 1, pool, operation, "hit");

    public void TrackSingleFlight(InteractiveWorkPool pool, string operation) =>
        Track("interactive.single_flight.reuse.count", 1, pool, operation, "reused");

    public void TrackWait(
        InteractiveWorkPool pool,
        string operation,
        double durationMilliseconds,
        bool acquired,
        bool cancelled) =>
        Track("interactive.wait.duration_ms", durationMilliseconds, pool, operation,
            cancelled ? "cancelled" : acquired ? "acquired" : "timed_out");

    public void TrackExecution(
        InteractiveWorkPool pool,
        string operation,
        double durationMilliseconds,
        bool success,
        bool cancelled,
        bool timedOut) =>
        Track("interactive.execution.duration_ms", durationMilliseconds, pool, operation,
            timedOut ? "timed_out" : cancelled ? "cancelled" : success ? "succeeded" : "failed");

    private void Track(
        string name,
        double value,
        InteractiveWorkPool pool,
        string operation,
        string outcome)
    {
        try
        {
            bool accepted = telemetryClient.GetMetric(name, "pool", "operation", "outcome")
                .TrackValue(value, Canonical(pool.ToString()), Canonical(operation), outcome);
            if (!accepted)
                logger.LogWarning("Interactive metric {MetricName} reached its dimension-series cap", name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Interactive metric {MetricName} was dropped", name);
        }
    }

    private static string Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        string normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= MaxDimensionLength
            ? normalized
            : normalized[..MaxDimensionLength];
    }
}
