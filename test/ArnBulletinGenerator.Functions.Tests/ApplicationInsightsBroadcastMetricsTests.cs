using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ArnBulletinGenerator.Functions.Metrics;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Guards the metric-dimension contract.
///
///     Application Insights records a dimension on a custom METRIC only when its name is declared
///     up front on <c>GetMetric</c>. A bare <c>GetMetric(name)</c> still emits the value, so nothing
///     looks broken — but every dimension value is silently discarded, and any alert or workbook
///     that splits <c>customMetrics</c> by <c>customDimensions</c> goes permanently quiet.
///
///     That is not hypothetical: two deployed alert rules (poison queue, synthesis failures) could
///     never fire, and the Queue Status and Cost Tracking workbooks could never label a series,
///     because the dimension names were missing here. These tests fail if that regresses.
/// </summary>
public sealed class ApplicationInsightsBroadcastMetricsTests
{
    private readonly RecordingChannel _channel = new();
    private readonly TelemetryClient _client;
    private readonly ApplicationInsightsBroadcastMetrics _sut;

    public ApplicationInsightsBroadcastMetricsTests()
    {
        var configuration = new TelemetryConfiguration
        {
            TelemetryChannel = _channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000001"
        };
        _client = new TelemetryClient(configuration);
        _sut = new ApplicationInsightsBroadcastMetrics(
            _client, NullLogger<ApplicationInsightsBroadcastMetrics>.Instance);
    }

    [Fact]
    public void TrackQueueDepth_MetricCarriesQueueDimension()
    {
        // The -poison-queue alert filters customMetrics on customDimensions.queue.
        _sut.TrackQueueDepth("broadcast-queue-poison", 3);

        MetricOf("broadcast.queue.depth")
            .Properties.ShouldContainKeyAndValue("queue", "broadcast-queue-poison");
    }

    [Fact]
    public void TrackPipelineStage_MetricCarriesMarketAndSuccessDimensions()
    {
        // The -synthesis-failures alert filters customMetrics on customDimensions.success.
        _sut.TrackPipelineStage("tts_synthesize", 12.5, "sydney", success: false);

        var metric = MetricOf("broadcast.stage.tts_synthesize.duration_seconds");
        metric.Properties.ShouldContainKeyAndValue("market", "sydney");
        metric.Properties.ShouldContainKeyAndValue("success", "false");
    }

    [Fact]
    public void TrackTtsCostEstimateUsd_MetricCarriesMarketDimension()
    {
        // Cost Tracking splits estimated spend by customDimensions.market.
        _sut.TrackTtsCostEstimateUsd("brisbane", 0.42);

        MetricOf("broadcast.tts.estimated_cost_usd")
            .Properties.ShouldContainKeyAndValue("market", "brisbane");
    }

    [Fact]
    public void TrackBulletinEmitted_EmitsBothStreamsWithSuccessDimension()
    {
        // SLO 1 reads the event stream; Broadcast Health reads customEvents too.
        _sut.TrackBulletinEmitted("melbourne", success: true);

        MetricOf("broadcast.bulletin.emitted")
            .Properties.ShouldContainKeyAndValue("success", "true");

        var emitted = EventOf("broadcast.bulletin.emitted");
        emitted.Properties.ShouldContainKeyAndValue("market", "melbourne");
        emitted.Properties.ShouldContainKeyAndValue("success", "true");
    }

    [Fact]
    public void PipelineStageMetric_AggregatesSumAndCount_SoSumOverCountIsThePerBulletinMean()
    {
        // SLO 3 and the latency workbooks divide the aggregate sum by its count. Percentiling the
        // raw sum instead reports the per-minute total, which overstates latency under load.
        _sut.TrackPipelineStage("orchestration_total", 100, "sydney", success: true);
        _sut.TrackPipelineStage("orchestration_total", 200, "sydney", success: true);

        var metric = MetricOf("broadcast.stage.orchestration_total.duration_seconds");
        metric.Count.ShouldBe(2);
        metric.Sum.ShouldBe(300);
        (metric.Sum / metric.Count!.Value).ShouldBe(150);
    }

    [Fact]
    public void DistinctDimensionValues_ProduceSeparateSeries()
    {
        _sut.TrackQueueDepth("broadcast-queue", 1);
        _sut.TrackQueueDepth("broadcast-queue-poison", 9);

        _client.Flush();
        var queues = _channel.Items.OfType<MetricTelemetry>()
            .Where(m => m.Name == "broadcast.queue.depth")
            .Select(m => m.Properties["queue"])
            .ToList();

        queues.ShouldBe(["broadcast-queue", "broadcast-queue-poison"], ignoreOrder: true);
    }
    [Fact]
    public void TtsMetrics_AggregateSumAndCount_ForCharactersAndCost()
    {
        _sut.TrackTtsCharacters(" Sydney ", 100);
        _sut.TrackTtsCharacters("sydney", 50);
        _sut.TrackTtsCostEstimateUsd(" Sydney ", 0.30);
        _sut.TrackTtsCostEstimateUsd("sydney", 0.15);

        var characters = MetricOf("broadcast.tts.characters");
        characters.Count.ShouldBe(2);
        characters.Sum.ShouldBe(150);

        var cost = MetricOf("broadcast.tts.estimated_cost_usd");
        cost.Count.ShouldBe(2);
        Math.Round(cost.Sum, 2, MidpointRounding.AwayFromZero).ShouldBe(0.45);
    }

    [Fact]
    public void MarketDimensions_UseCanonicalValue_AndRetainBoundedRawDiagnostic()
    {
        _sut.TrackTtsCharacters("  Sydney / NSW  ", 42);

        MetricOf("broadcast.tts.characters")
            .Properties.ShouldContainKeyAndValue("market", "sydney / nsw");
        EventOf("broadcast.tts.characters")
            .Properties.ShouldContainKeyAndValue("market.raw", "Sydney / NSW");
    }

    [Fact]
    public void EventTelemetry_UsesCurrentActivityTraceAsOperationId()
    {
        using var activity = new Activity("broadcast");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        _sut.TrackTtsCostEstimateUsd("sydney", 0.42);

        activity.Stop();
        var eventTelemetry = EventOf("broadcast.tts.estimated_cost_usd");
        eventTelemetry.Context.Operation.Id.ShouldBe(activity.TraceId.ToString());
    }

    [Fact]
    public void QueueCapacityMetrics_ExposeReadyAgeInFlightAndNoExecutionSignals()
    {
        _sut.TrackQueueReadyDepth("broadcast-queue", 5);
        _sut.TrackQueueOldestAge("broadcast-queue", 61);
        _sut.TrackQueueInFlight("broadcast-queue", "begin");
        _sut.TrackQueueNoExecution("broadcast-queue", "lease_active");
        _sut.TrackGenerationScaleInEligible();

        MetricOf("broadcast.queue.ready.depth")
            .Properties.ShouldContainKeyAndValue("queue", "broadcast-queue");
        MetricOf("broadcast.queue.oldest_age_seconds")
            .Properties.ShouldContainKeyAndValue("queue", "broadcast-queue");
        MetricOf("broadcast.queue.in_flight")
            .Properties.ShouldContainKeyAndValue("phase", "begin");
        MetricOf("broadcast.queue.no_execution")
            .Properties.ShouldContainKeyAndValue("reason", "lease_active");
        MetricOf("broadcast.queue.scale_in.eligible").Count.ShouldBe(1);
    }


    private MetricTelemetry MetricOf(string name)
    {
        _client.Flush();
        return _channel.Items.OfType<MetricTelemetry>().SingleOrDefault(m => m.Name == name)
               ?? throw new Xunit.Sdk.XunitException(
                   $"No MetricTelemetry named '{name}' was flushed. Flushed: " +
                   string.Join(", ", _channel.Items.Select(i => i.GetType().Name)));
    }

    private EventTelemetry EventOf(string name)
    {
        _client.Flush();
        return _channel.Items.OfType<EventTelemetry>().SingleOrDefault(e => e.Name == name)
               ?? throw new Xunit.Sdk.XunitException($"No EventTelemetry named '{name}' was flushed.");
    }

    private sealed class RecordingChannel : ITelemetryChannel
    {
        private readonly List<ITelemetry> _items = [];

        public IReadOnlyList<ITelemetry> Items
        {
            get { lock (_items) return [.. _items]; }
        }

        public bool? DeveloperMode { get; set; }
        public string? EndpointAddress { get; set; }

        public void Send(ITelemetry item)
        {
            lock (_items) _items.Add(item);
        }

        public void Flush() { }
        public void Dispose() { }
    }
}
