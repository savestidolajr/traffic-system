using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>
///     Segment running order is station configuration, enforced deterministically on the read.
///     Measured live 2026-08-27 (job e858b062): a weather-first SegmentOrder reached the stitch,
///     the briefs were ordered correctly, and the read still closed with weather because the
///     composer followed convention and no check objected.
/// </summary>
public sealed class SegmentRunningOrderCheckTests
{
    // Briefs cite events; the supplied values come from the source, exactly as the composer and
    // this check now read them. EventId is the evidence unit's SOURCE KEY, so the story ids here
    // must be "weather" and "corridor:Richmond Road" — not the per-fact unit ids.
    private static BulletinBriefs WeatherFirstBriefs() => new(
    [
        new StoryBrief(1, "weather", "Weather", ["weather"], 0, "Always carried.", Required: true),
        new StoryBrief(2, "western-delays", "Western delays", ["corridor:Richmond Road"], 254,
            "Strongest measured delays.")
    ]);

    private static NormalizedInput Input()
    {
        var request = new WorkflowRequest(
            "job", "sydney", "KIIS 90s", "Sydney", 30,
            [
                new EvidenceUnit("w1", "weather", "condition", "Low clouds"),
                new EvidenceUnit("w2", "weather", "temperature", "15 degrees"),
                new EvidenceUnit("r1", "corridor:Richmond Road", "road", "Richmond Road")
            ],
            []);
        return Normalizer.Normalize(request, NewTrace());
    }

    private static CheckResult RunCheck(string read) =>
        ConversationalChecks.Run(Input(), read, WeatherFirstBriefs(), NewTrace())
            .Single(check => check.Name == "SegmentRunningOrder");

    [Fact]
    public void A_read_following_the_configured_order_passes()
    {
        RunCheck("Low clouds, 15 degrees. Richmond Road is backed up.")
            .Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_read_transposing_the_segments_fails_with_the_required_order()
    {
        var result = RunCheck("Richmond Road is backed up. Low clouds, 15 degrees.");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("weather then traffic");
    }

    [Fact]
    public void A_read_that_never_voices_a_segment_is_not_penalised_for_order()
    {
        // Presence is RequiredStoryVoiced's concern; this check only orders what aired.
        RunCheck("Richmond Road is backed up.").Passed.ShouldBeTrue();
    }

    private static TraceRecorder NewTrace() =>
        new("run", "wf", "desc", "job", "market", "station", 30.0);
}
