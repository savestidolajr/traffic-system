using System.Diagnostics;
using ArnBulletinGenerator.Application.Metrics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Metrics;

/// <summary>
///     Forwards broadcast-pipeline custom metrics to Application Insights.
///     All methods are best-effort: telemetry failures must never break the pipeline.
/// </summary>
public sealed class ApplicationInsightsBroadcastMetrics(
    TelemetryClient telemetryClient,
    ILogger<ApplicationInsightsBroadcastMetrics> logger) : IBroadcastMetrics
{
    private const int MaxDimensionLength = 128;

    public void TrackPipelineStage(string stage, double durationSeconds, string market, bool success) =>
        SafeTrack($"broadcast.stage.{stage}.duration_seconds", durationSeconds,
            "market", Canonical(market), "success", success ? "true" : "false",
            "market.raw", Raw(market));

    public void TrackRefineCount(string market, int count) =>
        SafeTrack("broadcast.refine.count", count, "market", Canonical(market),
            "market.raw", Raw(market));

    public void TrackTtsCharacters(string market, int characters) =>
        SafeTrack("broadcast.tts.characters", characters, "market", Canonical(market),
            "market.raw", Raw(market));

    public void TrackTtsCostEstimateUsd(string market, double estimatedUsd) =>
        // TTS providers bill to cents; normalize at the telemetry boundary so aggregate
        // sums are stable at the precision consumers can act on.
        SafeTrack("broadcast.tts.estimated_cost_usd", Math.Round(estimatedUsd, 2, MidpointRounding.AwayFromZero),
            "market", Canonical(market), "market.raw", Raw(market));

    public void TrackFallbackActivated(string market, string reason) =>
        SafeTrack("broadcast.fallback.activated", 1, "market", Canonical(market),
            "reason", Canonical(reason), "market.raw", Raw(market));

    public void TrackQueueDepth(string queueName, int depth) =>
        SafeTrack("broadcast.queue.depth", depth, "queue", Canonical(queueName),
            "queue.raw", Raw(queueName));

    public void TrackQueueReadyDepth(string queueName, int depth) =>
        SafeTrack("broadcast.queue.ready.depth", depth, "queue", Canonical(queueName),
            "queue.raw", Raw(queueName));

    public void TrackQueueOldestAge(string queueName, double ageSeconds) =>
        SafeTrack("broadcast.queue.oldest_age_seconds", ageSeconds, "queue", Canonical(queueName),
            "queue.raw", Raw(queueName));

    public void TrackQueueInFlight(string queueName, string phase) =>
        SafeTrack("broadcast.queue.in_flight", 1, "queue", Canonical(queueName),
            "phase", Canonical(phase), "queue.raw", Raw(queueName));

    public void TrackQueueNoExecution(string queueName, string reason) =>
        SafeTrack("broadcast.queue.no_execution", 1, "queue", Canonical(queueName),
            "reason", Canonical(reason), "queue.raw", Raw(queueName));
    public void TrackGenerationScaleInEligible() =>
        SafeTrack("broadcast.queue.scale_in.eligible", 1);

    public void TrackBulletinEmitted(string market, bool success) =>
        SafeTrack("broadcast.bulletin.emitted", 1, "market", Canonical(market),
            "success", success ? "true" : "false", "market.raw", Raw(market));

    public void TrackTrafficStalenessDropped(string market, int droppedCount) =>
        SafeTrack("traffic.staleness.dropped_count", droppedCount, "market", Canonical(market),
            "market.raw", Raw(market));

    public void TrackZettaDeliverySuccess(string market) =>
        SafeTrack("zetta.delivery.success", 1, "market", Canonical(market),
            "market.raw", Raw(market));

    public void TrackZettaDeliveryFailure(string market, string reason) =>
        SafeTrack("zetta.delivery.failure", 1, "market", Canonical(market),
            "reason", Canonical(reason), "market.raw", Raw(market));

    public void TrackLessonPoison(string reason) =>
        SafeTrack("lesson.poison", 1, "reason", Canonical(reason));

    public void TrackDataValidity(string market, string measure, double value) =>
        SafeTrack("data.validity", value, "market", Canonical(market),
            "measure", Canonical(measure), "market.raw", Raw(market));

    private void SafeTrack(string name, double value)
    {
        try
        {
            telemetryClient.GetMetric(name).TrackValue(value);
            TrackEvent(name, new Dictionary<string, string>(), value);
        }
        catch (Exception ex)
        {
            Drop(name, ex);
        }
    }

    private void SafeTrack(
        string name,
        double value,
        string dimName,
        string dimValue,
        string? rawDimName = null,
        string? rawDimValue = null)
    {
        try
        {
            if (!telemetryClient.GetMetric(name, dimName).TrackValue(value, dimValue))
                WarnSeriesCap(name);

            var properties = new Dictionary<string, string>(rawDimName is null ? 1 : 2)
            {
                [dimName] = dimValue
            };
            if (rawDimName is not null && rawDimValue is not null)
                properties[rawDimName] = rawDimValue;
            TrackEvent(name, properties, value);
        }
        catch (Exception ex)
        {
            Drop(name, ex);
        }
    }

    private void SafeTrack(
        string name,
        double value,
        string dim1Name,
        string dim1Value,
        string dim2Name,
        string dim2Value,
        string? rawDimName = null,
        string? rawDimValue = null)
    {
        try
        {
            if (!telemetryClient.GetMetric(name, dim1Name, dim2Name)
                .TrackValue(value, dim1Value, dim2Value))
                WarnSeriesCap(name);

            var properties = new Dictionary<string, string>(rawDimName is null ? 2 : 3)
            {
                [dim1Name] = dim1Value,
                [dim2Name] = dim2Value
            };
            if (rawDimName is not null && rawDimValue is not null)
                properties[rawDimName] = rawDimValue;
            TrackEvent(name, properties, value);
        }
        catch (Exception ex)
        {
            Drop(name, ex);
        }
    }

    private void TrackEvent(
        string name,
        IReadOnlyDictionary<string, string> properties,
        double value)
    {
        var telemetry = new EventTelemetry(name);
        foreach (var property in properties)
            telemetry.Properties[property.Key] = property.Value;
        telemetry.Metrics["value"] = value;
        ApplyOperationContext(telemetry);
        telemetryClient.TrackEvent(telemetry);
    }

    private static void ApplyOperationContext(ITelemetry telemetry)
    {
        var activity = Activity.Current;
        if (activity is null || activity.TraceId == default)
            return;

        telemetry.Context.Operation.Id = activity.TraceId.ToString();
        if (activity.ParentSpanId != default)
            telemetry.Context.Operation.ParentId = activity.ParentSpanId.ToString();
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

    private static string Raw(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        string trimmed = value.Trim();
        return trimmed.Length <= MaxDimensionLength
            ? trimmed
            : trimmed[..MaxDimensionLength];
    }

    // TrackValue returns false only when the metric's dimension-series cap is reached, in which
    // case the value is discarded outright. Bounded dimensions (market, queue, success, reason)
    // keep us far below the cap, so this is a canary for an unbounded value slipping in.
    private void WarnSeriesCap(string name) =>
        logger.LogWarning(
            "Metric {Name} hit its dimension-series cap; the value was discarded. " +
            "Check for an unbounded dimension value.", name);

    // Never let telemetry break the pipeline.
    private void Drop(string name, Exception ex) =>
        logger.LogDebug(ex, "Metric {Name} dropped: {Message}", name, ex.Message);
}
