using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Cleared incidents earn airtime on SEVERITY, not on recency (ARN decision, 2026-08-23).
/// </summary>
/// <remarks>
///     A major closure or crash reopening is useful to any listener choosing a route now. Congestion
///     easing is useful to nobody: not news to someone who heard the earlier bulletin, meaningless to
///     someone who did not — and on a music station almost nobody hears two consecutive bulletins.
///     The rejected alternative was gating on broadcast history, which needed the pipeline to persist
///     what it BROADCAST rather than what it observed, and turned a first-of-day bulletin, a
///     filler-cart slot and a schedule gap into three edge cases. Severity makes them one case.
/// </remarks>
public sealed class ClearedIncidentWorthinessTests
{
    private static IncidentDeltaItem Item(string type, int criticality = 1) =>
        new(
            IncidentId: $"i-{type}-{criticality}",
            Type: type,
            Criticality: criticality,
            RoadName: Option<string>.Some("Sydney Harbour Tunnel"),
            Suburb: Option<string>.None,
            Direction: Option<string>.None,
            LengthMeters: 500,
            Description: Option<string>.None);

    [Theory]
    [InlineData("roadClosure")]
    [InlineData("accident")]
    [InlineData("crash")]
    [InlineData("collision")]
    public void AMajorClosureOrCrashReopening_IsWorthVoicing(string type) =>
        IncidentChangeFormatter.IsClearedWorthVoicing(Item(type)).ShouldBeTrue(
            $"'{type}' clearing changes a listener's route decision even if they never heard it start.");

    [Theory]
    [InlineData("congestion")]
    [InlineData("queuingTraffic")]
    [InlineData("trafficCongestion")]
    public void CongestionEasing_IsNeverWorthVoicing(string type) =>
        IncidentChangeFormatter.IsClearedWorthVoicing(Item(type)).ShouldBeFalse(
            $"'{type}' easing is the absence of a story — it is not news to a returning listener and "
            + "means nothing to a new one. This is the case that spent ~40% of a live Sydney 22s slot.");

    [Theory]
    [InlineData("construction")]
    [InlineData("laneRestriction")]
    public void StandingWorksClearing_IsNeverWorthVoicing(string type) =>
        IncidentChangeFormatter.IsClearedWorthVoicing(Item(type)).ShouldBeFalse(
            $"'{type}' is a standing footprint; its coming and going is not an event.");

    [Fact]
    public void ALowCriticalityClosure_IsNotWorthVoicing() =>
        IncidentChangeFormatter.IsClearedWorthVoicing(Item("roadClosure", criticality: 4))
            .ShouldBeFalse("Severity is the whole test; a trivial closure reopening is not the news.");

    [Fact]
    public void TheCapIsShared_SoEveryPathGivesTheSameAnswer()
    {
        // Three paths formatted cleared items and disagreed about how many belong in a bulletin: the
        // formatter capped at 2 ("the delta is context, not content"), while the narrative block and
        // the production evidence path were both unlimited. The constant is public so they cannot
        // drift apart again; this asserts the value the other two now read.
        IncidentChangeFormatter.DefaultClearedCap.ShouldBe(2);
    }

    [Fact]
    public void TheWriterSeesOnlyWorthyClearedItems_AndNoMoreThanTheCap()
    {
        // The exact live Sydney delta: nine cleared items, of which only structural ones qualify.
        var delta = new IncidentDeltaContext(
            NewIncidents: [Item("roadClosure")],
            UpdatedIncidents: [],
            ClearedIncidents:
            [
                Item("congestion"), Item("congestion"), Item("construction"),
                Item("congestion"), Item("construction"), Item("congestion"),
                Item("construction"), Item("roadClosure"), Item("construction")
            ],
            UnchangedIncidents: []);

        string summary = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        int clearedLines = summary
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains("Cleared:", StringComparison.Ordinal));
        clearedLines.ShouldBeLessThanOrEqualTo(IncidentChangeFormatter.DefaultClearedCap);
        clearedLines.ShouldBe(1, "Only the roadClosure qualifies out of those nine.");
    }
}
