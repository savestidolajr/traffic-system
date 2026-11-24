using Microsoft.Extensions.Logging;
using Shouldly;
using ArnBulletinGenerator.Functions.Metrics;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class E2EFakeBroadcastMetricsTests
{
    private readonly RecordingLogger<E2EFakeBroadcastMetrics> _logger = new();
    private readonly E2EFakeBroadcastMetrics _sut;

    public E2EFakeBroadcastMetricsTests()
    {
        _sut = new E2EFakeBroadcastMetrics(_logger);
    }

    [Fact]
    public void TrackPipelineStage_EmitsE2EMetricMarkerWithStageMarketSuccessValue()
    {
        _sut.TrackPipelineStage("PoisonQueue.Broadcast", 0.0, "system", false);

        _logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("[E2E-Metric]", StringComparison.Ordinal) &&
            e.Message.Contains("PipelineStage", StringComparison.Ordinal) &&
            e.Message.Contains("stage=PoisonQueue.Broadcast", StringComparison.Ordinal) &&
            e.Message.Contains("market=system", StringComparison.Ordinal) &&
            e.Message.Contains("success=false", StringComparison.Ordinal) &&
            e.Message.Contains("value=0", StringComparison.Ordinal));
    }

    [Fact]
    public void TrackPipelineStage_SuccessTrue_EmitsSuccessTrue()
    {
        _sut.TrackPipelineStage("Draft", 1.5, "SYD", true);

        _logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("[E2E-Metric]", StringComparison.Ordinal) &&
            e.Message.Contains("success=true", StringComparison.Ordinal) &&
            e.Message.Contains("market=SYD", StringComparison.Ordinal));
    }

    [Fact]
    public void TrackFallbackActivated_EmitsWarningLevelMarker()
    {
        _sut.TrackFallbackActivated("MEL", "timeout");

        _logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("[E2E-Metric]", StringComparison.Ordinal) &&
            e.Message.Contains("FallbackActivated", StringComparison.Ordinal) &&
            e.Message.Contains("market=MEL", StringComparison.Ordinal) &&
            e.Message.Contains("reason=timeout", StringComparison.Ordinal));
    }

    [Fact]
    public void TrackZettaDeliveryFailure_EmitsWarningLevelMarker()
    {
        _sut.TrackZettaDeliveryFailure("BNE", "file-not-found");

        _logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("[E2E-Metric]", StringComparison.Ordinal) &&
            e.Message.Contains("ZettaDeliveryFailure", StringComparison.Ordinal) &&
            e.Message.Contains("reason=file-not-found", StringComparison.Ordinal));
    }

    [Fact]
    public void AllMethods_EmitAtLeastOneMarker()
    {
        _sut.TrackRefineCount("SYD", 3);
        _sut.TrackTtsCharacters("SYD", 500);
        _sut.TrackTtsCostEstimateUsd("SYD", 0.12);
        _sut.TrackQueueDepth("broadcast-queue", 5);
        _sut.TrackBulletinEmitted("SYD", true);
        _sut.TrackTrafficStalenessDropped("SYD", 2);
        _sut.TrackZettaDeliverySuccess("SYD");

        _logger.Entries.Count.ShouldBe(7);
        _logger.Entries.ShouldAllBe(e => e.Message.Contains("[E2E-Metric]", StringComparison.Ordinal));
    }

    [Fact]
    public void NeverThrows_EvenOnExtremeValues()
    {
        var act = () =>
        {
            _sut.TrackPipelineStage("stage", double.NaN, "", false);
            _sut.TrackTtsCostEstimateUsd("", double.MaxValue);
            _sut.TrackRefineCount("", int.MinValue);
            _sut.TrackFallbackActivated("", "");
        };

        act.ShouldNotThrow();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
