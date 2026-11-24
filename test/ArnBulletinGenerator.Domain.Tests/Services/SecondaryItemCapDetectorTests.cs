using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The cross-sentence roll-call gate. Live dev job 8b895081 (Sydney, 22s, 2026-08-22) aired six
///     single-road minor items with six different idioms: RollCallDetector counts names inside one
///     sentence, SentenceCloneDetector needs a repeated frame, and the draft prompt's palette rule
///     guarantees the frames differ — so nothing caught it.
/// </summary>
public sealed class SecondaryItemCapDetectorTests
{
    private static TrafficCorridor Corridor(string road, int delayMinutes = 2) => new(
        road, 1, 3, 500, 40, "congestion", $"{road} is slow", [],
        Extent: Option<string>.None,
        DriveTimeMinutes: Option<int>.None,
        NormalDriveTimeMinutes: Option<int>.None,
        FlowDelayMinutes: Option<int>.Some(delayMinutes));

    private static BroadcastWithTraffic Context(params TrafficCorridor[] corridors) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 22,
        JobId = "secondary-cap-test",
        Location = "sydney",
        VoiceId = Option<string>.Some("voice"),
        Model = Option<string>.Some("eleven_v3"),
        VoiceSettings = Option<VoiceSettings>.Some(VoiceSettings.Default),
        Incidents = [],
        Corridors = corridors,
        Content = new BulletinContentSelection()
    };

    [Fact]
    public void LeadPlusTwoNamedSecondaries_IsTheAllowedShape()
    {
        const string script =
            "Victoria Road is crawling, adding about 10 minutes. "
            + "Arthur Street is seeing heavy traffic. "
            + "Park Avenue has a bit of a queue.";
        var context = Context(Corridor("Victoria Road", 10), Corridor("Arthur Street"), Corridor("Park Avenue"));

        var result = SecondaryItemCapDetector.Detect(script, context);

        result.HasExcessItems.ShouldBeFalse();
        result.TrafficItemCount.ShouldBe(3);
    }

    [Fact]
    public void TheLiveSydneyRead_TripsTheGate_AndTheRepairSweepsTheTail()
    {
        // Exactly what aired on job 8b895081.
        const string script =
            "Now looking over Sydney. "
            + "Victoria Road is crawling, adding about 10 minutes. "
            + "Arthur Street is seeing heavy traffic. "
            + "Park Avenue has a bit of a queue. "
            + "Rohini Street is taking an extra minute or two. "
            + "Bay Street has a slight queue. "
            + "Albert Avenue is a little slower. "
            + "Sunny, 16 degrees.";
        var context = Context(
            Corridor("Victoria Road", 10), Corridor("Arthur Street"), Corridor("Park Avenue"),
            Corridor("Rohini Street"), Corridor("Bay Street"), Corridor("Albert Avenue"));

        var result = SecondaryItemCapDetector.Detect(script, context);
        result.HasExcessItems.ShouldBeTrue();
        result.TrafficItemCount.ShouldBe(6);
        result.Feedback.ShouldContain("Rohini Street");

        string repaired = SecondaryItemCapDetector.Repair(script, context);

        // The three excess minor items collapse into ONE sweep naming all three.
        repaired.ShouldContain("Rohini Street, Bay Street and Albert Avenue are all running a little slower.");
        repaired.ShouldNotContain("Bay Street has a slight queue");
        repaired.ShouldNotContain("Albert Avenue is a little slower.");
        // The lead, the two kept secondaries, the opener and the weather close all survive.
        repaired.ShouldContain("Victoria Road is crawling, adding about 10 minutes.");
        repaired.ShouldContain("Arthur Street is seeing heavy traffic.");
        repaired.ShouldContain("Park Avenue has a bit of a queue.");
        repaired.ShouldContain("Now looking over Sydney.");
        repaired.ShouldContain("Sunny, 16 degrees.");
        // Denser read: fewer sentences AND fewer words than the list it replaced.
        ScriptSentenceSplitterCount(repaired).ShouldBeLessThan(ScriptSentenceSplitterCount(script));
        repaired.Split(' ').Length.ShouldBeLessThan(script.Split(' ').Length);
        // And the swept read now passes the gate.
        SecondaryItemCapDetector.Detect(repaired, context).HasExcessItems.ShouldBeFalse();
    }

    [Fact]
    public void ItemsAboveModerate_AreNeverSwept_BecauseTheyAreLeadMaterial()
    {
        // A 25-minute corridor is Major: sweeping it into "a little slower" would understate it.
        const string script =
            "Victoria Road is crawling, adding about 10 minutes. "
            + "Arthur Street is seeing heavy traffic. "
            + "Park Avenue has a bit of a queue. "
            + "The M4 is at a standstill. "
            + "The M5 is barely moving.";
        var context = Context(
            Corridor("Victoria Road", 10), Corridor("Arthur Street"), Corridor("Park Avenue"),
            Corridor("The M4", 25), Corridor("The M5", 32));

        var result = SecondaryItemCapDetector.Detect(script, context);
        result.HasExcessItems.ShouldBeTrue();

        string repaired = SecondaryItemCapDetector.Repair(script, context);

        repaired.ShouldBe(script);
    }

    [Fact]
    public void SentencesNamingSeveralRoads_AreAlreadySweeps_AndDoNotCount()
    {
        const string script =
            "Victoria Road is crawling, adding about 10 minutes. "
            + "Arthur Street, Park Avenue and Bay Street are all running a little slower.";
        var context = Context(
            Corridor("Victoria Road", 10), Corridor("Arthur Street"),
            Corridor("Park Avenue"), Corridor("Bay Street"));

        SecondaryItemCapDetector.Detect(script, context).HasExcessItems.ShouldBeFalse();
    }

    private static int ScriptSentenceSplitterCount(string script) =>
        ArnBulletinGenerator.Domain.Services.ScriptSentenceSplitter.Split(script).Count;
}
