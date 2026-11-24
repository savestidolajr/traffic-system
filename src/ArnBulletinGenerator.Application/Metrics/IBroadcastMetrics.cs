namespace ArnBulletinGenerator.Application.Metrics;

/// <summary>
///     Custom-metric sink for the broadcast pipeline. Implementations forward to
///     Application Insights (production) or a no-op (tests / local dev).
///     All methods must be allocation-light and never throw — metric emission
///     is opportunistic instrumentation, not a critical-path dependency.
/// </summary>
public interface IBroadcastMetrics
{
    void TrackPipelineStage(string stage, double durationSeconds, string market, bool success);
    void TrackRefineCount(string market, int count);
    void TrackTtsCharacters(string market, int characters);
    void TrackTtsCostEstimateUsd(string market, double estimatedUsd);
    void TrackFallbackActivated(string market, string reason);
    void TrackQueueDepth(string queueName, int depth);
    void TrackQueueReadyDepth(string queueName, int depth);
    void TrackQueueOldestAge(string queueName, double ageSeconds);
    void TrackQueueInFlight(string queueName, string phase);
    void TrackQueueNoExecution(string queueName, string reason);
    void TrackGenerationScaleInEligible();
    void TrackBulletinEmitted(string market, bool success);
    void TrackTrafficStalenessDropped(string market, int droppedCount);
    void TrackZettaDeliverySuccess(string market);
    void TrackZettaDeliveryFailure(string market, string reason);
    void TrackLessonPoison(string reason);

    /// <summary>
    ///     One measure from the data-validity probe (custom metric <c>data.validity</c>, dimensions
    ///     market + measure): the HERE-vs-official-feed comparison rates first measured in the
    ///     2026-08-17 staleness landscape study.
    /// </summary>
    void TrackDataValidity(string market, string measure, double value);
}

/// <summary>
///     Default no-op implementation used when telemetry is not configured (e.g. local dev,
///     unit tests). Registered in the Application layer so consumers never receive null.
/// </summary>
public sealed class NoOpBroadcastMetrics : IBroadcastMetrics
{
    public void TrackPipelineStage(string stage, double durationSeconds, string market, bool success) { }
    public void TrackRefineCount(string market, int count) { }
    public void TrackTtsCharacters(string market, int characters) { }
    public void TrackTtsCostEstimateUsd(string market, double estimatedUsd) { }
    public void TrackFallbackActivated(string market, string reason) { }
    public void TrackQueueDepth(string queueName, int depth) { }
    public void TrackQueueReadyDepth(string queueName, int depth) { }
    public void TrackQueueOldestAge(string queueName, double ageSeconds) { }
    public void TrackQueueInFlight(string queueName, string phase) { }
    public void TrackQueueNoExecution(string queueName, string reason) { }
    public void TrackGenerationScaleInEligible() { }
    public void TrackBulletinEmitted(string market, bool success) { }
    public void TrackTrafficStalenessDropped(string market, int droppedCount) { }
    public void TrackZettaDeliverySuccess(string market) { }
    public void TrackZettaDeliveryFailure(string market, string reason) { }
    public void TrackLessonPoison(string reason) { }

    public void TrackDataValidity(string market, string measure, double value) { }
}
