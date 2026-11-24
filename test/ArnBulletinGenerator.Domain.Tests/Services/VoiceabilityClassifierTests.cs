using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class VoiceabilityClassifierTests
{
    [Fact]
    public void Corridor_with_road_condition_severity_and_detail_is_leadable()
    {
        var corridor = Corridor(direction: Option<string>.Some("eastbound"));

        VoiceabilityClassifier.Classify(corridor).ShouldBe(Voiceability.Leadable);
    }

    [Fact]
    public void Corridor_with_road_condition_and_detail_but_no_severity_is_standalone()
    {
        var corridor = Corridor(severity: 0, extent: Option<string>.Some("between King Street and Queen Street"));

        VoiceabilityClassifier.Classify(corridor).ShouldBe(Voiceability.Standalone);
    }

    [Fact]
    public void Corridor_with_road_condition_and_no_usable_detail_is_grouped_only()
    {
        var corridor = Corridor();

        VoiceabilityClassifier.Classify(corridor).ShouldBe(Voiceability.GroupedOnly);
    }

    [Theory]
    [InlineData("", "congestion")]
    [InlineData("M1", "")]
    public void Corridor_missing_road_or_condition_is_do_not_voice(string road, string condition)
    {
        var corridor = Corridor(road: road, primaryType: condition);

        VoiceabilityClassifier.Classify(corridor).ShouldBe(Voiceability.DoNotVoice);
    }

    [Fact]
    public void Incident_with_road_condition_severity_and_detail_is_leadable()
    {
        var incident = new TrafficIncident(
            "i1", "Crash", 2, "M1", "accident", Direction: Option<string>.Some("northbound"));

        VoiceabilityClassifier.Classify(incident).ShouldBe(Voiceability.Leadable);
    }

    [Fact]
    public void Incident_with_road_condition_and_no_detail_is_grouped_only()
    {
        var incident = new TrafficIncident("i1", "Slow", 2, "M1", "congestion");

        VoiceabilityClassifier.Classify(incident).ShouldBe(Voiceability.GroupedOnly);
    }

    [Fact]
    public void Incident_below_the_road_and_condition_floor_is_do_not_voice()
    {
        var incident = new TrafficIncident("i1", "Unknown", 2, "", "");

        VoiceabilityClassifier.Classify(incident).ShouldBe(Voiceability.DoNotVoice);
    }

    private static TrafficCorridor Corridor(
        string road = "M1",
        string primaryType = "congestion",
        int severity = 2,
        Option<string> direction = default,
        Option<string> extent = default) =>
        new(
            road,
            severity,
            1,
            0,
            1,
            primaryType,
            "",
            [],
            Direction: direction,
            Extent: extent);
}
