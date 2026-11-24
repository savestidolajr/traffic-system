using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using ArnBulletinGenerator.Workflow.Contracts;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Conversational;

public sealed class ConversationalBuildRequestTests
{
    private sealed class FixedOracle : IDurationOracle
    {
        public double SecondsForWords(int words) => words / 2.5;
        public int WordsForSeconds(double seconds) => (int)(seconds * 2.5);
        public double PredictSeconds(string script) => script.Split(' ').Length / 2.5;
    }

    [Fact]
    public void BuildRequest_carries_segment_order_from_content()
    {
        var content = new BulletinContentSelection(
            Traffic: true, Weather: true, Fuel: false, WeatherRequired: true,
            SegmentOrder: [BulletinSegment.Weather, BulletinSegment.Traffic]);

        var context = new BroadcastWithTraffic
        {
            JobId = "probe",
            Traffic = "quiet",
            Incidents = [],
            Location = "sydney",
            TargetDurationSeconds = 22,
            Content = content,
            TargetWordCountOverride = Option<int>.Some(47),
        };

        WorkflowRequest request = ConversationalScriptGenerator.BuildRequest(context, new FixedOracle());

        request.SegmentOrder.ShouldBe(["Weather", "Traffic"]);
        request.TrafficOrder.ShouldBe(TrafficRunningOrder.WorstFirst);
        request.SlotWords.ShouldBe(47);
        request.TargetSeconds.ShouldBe(22, 0.001);
    }
    [Fact]
    public void BuildRequest_preserves_broadcast_slot_seconds_even_when_word_override_is_set()
    {
        // Reproduction of Dev Job dabcc1e2:
        // A 30s broadcast slot must remain a 30s slot in WorkflowRequest.TargetSeconds.
        // If an uncalibrated oracle prices 79 words at 0.55s/word (43.5s), TargetSeconds must NOT
        // be corrupted to 43.5s, because that leads the workflow to tell the composer:
        // "This read is 101 words and prices at about 34.4s against a 43.4-second slot. Add about 26 words!"
        var uncalibratedOracle = new SlowProbeOracle();
        var context = new BroadcastWithTraffic
        {
            JobId = "dabcc1e2",
            Traffic = "busy",
            Incidents = [],
            Location = "brisbane",
            TargetDurationSeconds = 30.0,
            Content = BulletinContentSelection.Default,
            TargetWordCountOverride = Option<int>.Some(79)
        };

        WorkflowRequest request = ConversationalScriptGenerator.BuildRequest(context, uncalibratedOracle);

        request.SlotWords.ShouldBe(79);
        request.TargetSeconds.ShouldBe(30.0,
            "TargetSeconds should be the real 30.0s broadcast slot, NOT the converted 43.5s oracle probe duration!");
    }

    private sealed class SlowProbeOracle : IDurationOracle
    {
        public double SecondsForWords(int words) => words * 0.55; // 79 words -> 43.45s!
        public int WordsForSeconds(double seconds) => (int)(seconds / 0.55);
        public double PredictSeconds(string script) => script.Split(' ').Length * 0.55;
    }
}
