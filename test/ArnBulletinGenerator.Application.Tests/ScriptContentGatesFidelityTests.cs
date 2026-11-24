using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Regression coverage for the four blocking fidelity classes. Measured rejection rates were
///     FakeLocation 0/93 historical and 0/14 current bulletins, FalseClosure 0/93 and 0/14,
///     UnselectedSource 1/93 and 0/14, and MetaLeak 1/14. Production language is never valid on air,
///     so that measured leak now blocks; the lower-stakes suburb-retention omission remains advisory
///     at 2/14.
/// </summary>
public sealed class ScriptContentGatesFidelityTests
{
    [Fact]
    public void False_closure_claim_against_typed_incident_data_is_blocking()
    {
        // Defect class: false closure claim must reject a road the typed data does not mark closed.
        const string script = "The M4 is closed.";

        var (blocking, advisory, style) = ScriptContentGates.DetectViolations(script, Context(
            new TrafficCorridor(
                "M4", 2, 1, 1000, 50, "congestion", "Heavy traffic on M4",
                [new TrafficIncident("incident-1", "Heavy traffic", 2, "M4", "congestion")] )));

        blocking.ShouldBe(["false closure claim"]);
        advisory.ShouldBeEmpty();
        style.ShouldBeEmpty();
    }

    [Fact]
    public void Weather_line_without_weather_data_is_blocking()
    {
        // Defect class: content from an unselected source must reject weather with no supplied data.
        const string script = "Passing clouds, 14 degrees.";

        var (blocking, advisory, style) = ScriptContentGates.DetectViolations(script, Context() with
        {
            Content = new BulletinContentSelection(Traffic: true, Weather: false),
            Weather = Option<WeatherReport>.None
        });

        blocking.ShouldBe(["content from an unselected source"]);
        advisory.ShouldBeEmpty();
        style.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_weather_line_with_supplied_weather_data_is_not_blocking()
    {
        // Defect class: unselected-source detection must key off supplied data, not weather words.
        const string script = "Passing clouds, 14 degrees.";

        var (blocking, _, _) = ScriptContentGates.DetectViolations(script, Context() with
        {
            Content = new BulletinContentSelection(Traffic: true, Weather: true),
            Weather = new WeatherReport("Cloudy", 12.0, default, 10, [])
        });

        blocking.ShouldBeEmpty();
    }

    [Fact]
    public void Production_language_meta_leak_is_blocking()
    {
        // A model echo of our production vocabulary is not an on-air fact and must never reach TTS.
        const string script = "No other incidents are listed in the data.";

        var (blocking, advisory, style) = ScriptContentGates.DetectViolations(script, Context());

        blocking.ShouldBe(["meta leak (production language on air)"]);
        advisory.ShouldBeEmpty();
        style.ShouldBeEmpty();
    }

    [Fact]
    public void Advisory_suburb_retention_finding_does_not_block_the_read()
    {
        // Defect class: suburb dropped from an incident is advisory so the read still airs.
        const string script = "Traffic is slow on the M4.";

        var (blocking, advisory, style) = ScriptContentGates.DetectViolations(script, Context(
            new TrafficCorridor(
                "M4", 2, 1, 1000, 50, "congestion", "Heavy traffic on M4", [],
                Suburb: Option<string>.Some("Haberfield"))));

        blocking.ShouldBeEmpty();
        advisory.ShouldBe(["suburb dropped from an incident"]);
        style.ShouldBeEmpty();
    }

    [Fact]
    public void Style_only_finding_never_appears_in_the_blocking_list()
    {
        // Defect class: style findings remain advisory and cannot reject a factually grounded read.
        const string script = "Traffic is slow near Church Street, near the shopping centre this morning.";

        var (blocking, advisory, style) = ScriptContentGates.DetectViolations(script, Context(
            new TrafficCorridor(
                "Church Street", 2, 3, 1000, 50, "congestion", "Slow traffic on Church Street", [])));

        blocking.ShouldBeEmpty();
        advisory.ShouldBeEmpty();
        style.ShouldBe(["repeated preposition"]);
    }

    private static BroadcastWithTraffic Context(params TrafficCorridor[] corridors) => new()
    {
        TargetDurationSeconds = 30,
        JobId = "fidelity-test",
        Location = "sydney",
        Traffic = "Traffic",
        Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
        Corridors = corridors,
        Content = new BulletinContentSelection(Traffic: true)
    };
}
