using ArnBulletinGenerator.Application;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Metrics;

namespace ArnBulletinGenerator.Functions.Metrics;

/// <summary>
///     Fake-mode broadcast metrics sink for deterministic E2E testing.
///     Every call emits a stable structured <c>[E2E-Metric]</c> log marker so the
///     E2E host log can be grepped for pipeline-stage completion, fallback, delivery, etc.
///     Never throws — mirrors the no-throw contract of <see cref="IBroadcastMetrics"/>.
/// </summary>
public sealed class E2EFakeBroadcastMetrics(ILogger<E2EFakeBroadcastMetrics> logger) : IBroadcastMetrics
{
    public void TrackPipelineStage(string stage, double durationSeconds, string market, bool success)
    {
        logger.LogInformation(
            "[E2E-Metric] PipelineStage stage={Stage} market={Market} success={Success} value={Value}",
            stage, market, success ? "true" : "false", durationSeconds);
    }

    public void TrackRefineCount(string market, int count) =>
        logger.LogInformation(
            "[E2E-Metric] RefineCount market={Market} value={Value}", market, count);

    public void TrackTtsCharacters(string market, int characters) =>
        logger.LogInformation(
            "[E2E-Metric] TtsCharacters market={Market} value={Value}", market, characters);

    public void TrackTtsCostEstimateUsd(string market, double estimatedUsd) =>
        logger.LogInformation(
            "[E2E-Metric] TtsCostEstimateUsd market={Market} value={Value}", market, estimatedUsd);

    public void TrackFallbackActivated(string market, string reason) =>
        logger.LogWarning(
            "[E2E-Metric] FallbackActivated market={Market} reason={Reason}", market, reason);

    public void TrackQueueDepth(string queueName, int depth) =>
        logger.LogInformation(
            "[E2E-Metric] QueueDepth queue={Queue} value={Value}", queueName, depth);

    public void TrackQueueReadyDepth(string queueName, int depth) =>
        logger.LogInformation(
            "[E2E-Metric] QueueReadyDepth queue={Queue} value={Value}", queueName, depth);

    public void TrackQueueOldestAge(string queueName, double ageSeconds) =>
        logger.LogInformation(
            "[E2E-Metric] QueueOldestAge queue={Queue} value={Value}", queueName, ageSeconds);

    public void TrackQueueInFlight(string queueName, string phase) =>
        logger.LogInformation(
            "[E2E-Metric] QueueInFlight queue={Queue} phase={Phase}", queueName, phase);

    public void TrackQueueNoExecution(string queueName, string reason) =>
        logger.LogWarning(
            "[E2E-Metric] QueueNoExecution queue={Queue} reason={Reason}", queueName, reason);

    public void TrackGenerationScaleInEligible() =>
        logger.LogInformation("[E2E-Metric] GenerationScaleInEligible value=1");

    public void TrackBulletinEmitted(string market, bool success) =>
        logger.LogInformation(
            "[E2E-Metric] BulletinEmitted market={Market} success={Success}",
            market, success ? "true" : "false");

    public void TrackTrafficStalenessDropped(string market, int droppedCount) =>
        logger.LogWarning(
            "[E2E-Metric] TrafficStalenessDropped market={Market} value={Value}",
            market, droppedCount);

    public void TrackZettaDeliverySuccess(string market) =>
        logger.LogInformation(
            "[E2E-Metric] ZettaDeliverySuccess market={Market}", market);

    public void TrackZettaDeliveryFailure(string market, string reason) =>
        logger.LogWarning(
            "[E2E-Metric] ZettaDeliveryFailure market={Market} reason={Reason}", market, reason);

    public void TrackLessonPoison(string reason) =>
        logger.LogError(
            "[E2E-Metric] LessonPoison reason={Reason}", reason);

    public void TrackDataValidity(string market, string measure, double value) =>
        logger.LogInformation(
            "[E2E-Metric] DataValidity market={Market} measure={Measure} value={Value}", market, measure, value);
}
