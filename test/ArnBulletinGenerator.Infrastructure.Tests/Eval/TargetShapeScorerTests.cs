using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

public sealed class TargetShapeScorerTests
{
    private static TrafficCorridor Corridor(
        string road, string primaryType, bool hasIncident = true,
        int? driveTime = null, int? normalDriveTime = null, string? direction = null)
    {
        IReadOnlyList<TrafficIncident> incidents = hasIncident
            ? [new TrafficIncident("1", "d", 2, "loc", primaryType)]
            : [];
        return new TrafficCorridor(
            road, 2, 2, 1000, 1.0, primaryType, "desc", incidents,
            Direction: direction is { } dir ? Option<string>.Some(dir) : default,
            DriveTimeMinutes: driveTime is { } dt ? Option<int>.Some(dt) : default,
            NormalDriveTimeMinutes: normalDriveTime is { } n ? Option<int>.Some(n) : default);
    }

    private static double? Score(TargetShapeResult r, string name) =>
        r.Dimensions.First(d => d.Name == name).Score;

    [Fact]
    public void DriveTimeLead_PresentWhenDataHasIt_ScoresFull()
    {
        var corridors = new[] { Corridor("M4", "accident", driveTime: 20, normalDriveTime: 12) };
        var r = TargetShapeScorer.Evaluate(
            "It's a 20-minute run on the M4 eastbound, normally 12. Crash citybound at Mays Hill.",
            corridors, 30);
        Score(r, "drive-time-lead").ShouldBe(1.0);
    }

    [Fact]
    public void DriveTimeLead_IsNA_WhenNoDriveTimeData()
    {
        var corridors = new[] { Corridor("M4", "accident") };
        var r = TargetShapeScorer.Evaluate("Crash citybound on the M4 at Mays Hill.", corridors, 30);
        Score(r, "drive-time-lead").ShouldBeNull();
    }

    [Fact]
    public void DriveTimeLead_SevereDelayMagnitudeInLead_ScoresFull()
    {
        // Leading with the worst corridor's magnitude (not the monitored route) is correct ATN behaviour.
        var corridors = new[] { Corridor("Ring Road", "congestion", driveTime: 69, normalDriveTime: 20) };
        var r = TargetShapeScorer.Evaluate(
            "The Ring Road outbound's a shocker, backed up the best part of an hour near Deer Park Bypass.",
            corridors, 30);
        Score(r, "drive-time-lead").ShouldBe(1.0);
    }

    [Fact]
    public void DriveTimeLead_RoadClosureLead_ScoresFull()
    {
        // Leading with a shut motorway is canonical ATN behaviour; a closure carries little measured
        // delay (the road is shut) so the closure itself is the magnitude.
        var corridors = new[] { Corridor("M80", "roadClosure", driveTime: 2, normalDriveTime: 2) };
        var r = TargetShapeScorer.Evaluate(
            "Big trouble on the M80 eastbound, the ramp to Greensborough Highway South is closed.",
            corridors, 30);
        Score(r, "drive-time-lead").ShouldBe(1.0);
    }

    [Fact]
    public void Numeric_DecimalsArePenalised()
    {
        var r = TargetShapeScorer.Evaluate("Roadworks stretching about 1.6 kays on the M1.", [], 30);
        Score(r, "numeric-budget")!.Value.ShouldBeLessThanOrEqualTo(0.5);
    }

    [Fact]
    public void Numeric_ImperialScoresZero()
    {
        var r = TargetShapeScorer.Evaluate("Backed up for 2 miles on the M1.", [], 30);
        Score(r, "numeric-budget").ShouldBe(0.0);
    }

    [Fact]
    public void CleanClose_TrailingConjunctionFails()
    {
        // The real Sydney defect: bulletin trails off on "as traffic".
        var r = TargetShapeScorer.Evaluate(
            "Crash on the M4. Heavy delays at Victoria Road, as traffic", [], 30);
        Score(r, "clean-close").ShouldBe(0.0);
    }

    [Fact]
    public void CleanClose_CompleteClausePasses()
    {
        var r = TargetShapeScorer.Evaluate("Crash on the M4. Elsewhere it's running clean.", [], 30);
        Score(r, "clean-close").ShouldBe(1.0);
    }

    [Fact]
    public void NoRoadworksLead_PenalisedWhenLiveItemAvailable()
    {
        var corridors = new[] { Corridor("M1", "construction"), Corridor("M4", "accident") };
        var r = TargetShapeScorer.Evaluate(
            "Roadworks on the M1 citybound. Then a crash on the M4.", corridors, 30);
        Score(r, "no-roadworks-lead").ShouldBe(0.0);
    }

    [Fact]
    public void NoRoadworksLead_IsNA_WhenEverythingIsRoadworks()
    {
        var corridors = new[] { Corridor("M1", "construction"), Corridor("M4", "construction") };
        var r = TargetShapeScorer.Evaluate("Roadworks on the M1, more on the M4.", corridors, 30);
        Score(r, "no-roadworks-lead").ShouldBeNull();
    }

    [Fact]
    public void Congestion_VoicedScoresFull()
    {
        var congestion = new[] { Corridor("M4", "congestion", hasIncident: false) };
        var r = TargetShapeScorer.Evaluate("The M4's crawling citybound, no crash, just volume.", congestion, 30);
        Score(r, "congestion-voiced").ShouldBe(1.0);
    }

    [Fact]
    public void Congestion_IsNA_WhenNoCongestionCorridor()
    {
        var corridors = new[] { Corridor("M4", "accident") };
        var r = TargetShapeScorer.Evaluate("Crash on the M4 citybound.", corridors, 30);
        Score(r, "congestion-voiced").ShouldBeNull();
    }

    [Fact]
    public void Length_BandTracksSuppliedVoiceRate()
    {
        // 30 words. At 1.0 w/s the 30s band centres on 30 words (in band); at 4.0 w/s it
        // centres on 120 (way under) — the same script scores differently per voice rate.
        string script = string.Join(' ', Enumerable.Repeat("road", 30)) + ".";
        var corridors = new[] { Corridor("M1", "accident") };

        double slow = Score(TargetShapeScorer.Evaluate(script, corridors, 30, wordsPerSecond: 1.0), "length")!.Value;
        double fast = Score(TargetShapeScorer.Evaluate(script, corridors, 30, wordsPerSecond: 4.0), "length")!.Value;

        slow.ShouldBe(1.0);
        fast.ShouldBeLessThan(0.5);
    }

    [Fact]
    public void Percent_ExcludesNADimensionsAndStaysInRange()
    {
        var r = TargetShapeScorer.Evaluate("Crash on the M4 citybound at Mays Hill. Running clean elsewhere.", [], 30);
        r.Percent.ShouldBeInRange(0, 100);
        // drive-time-lead and congestion-voiced are N/A with no supporting data.
        Score(r, "drive-time-lead").ShouldBeNull();
        Score(r, "congestion-voiced").ShouldBeNull();
    }

    [Fact]
    public void Direction_IsNA_WhenNoCorridorCarriesDirection()
    {
        var corridors = new[] { Corridor("M4", "congestion", hasIncident: false) };
        var r = TargetShapeScorer.Evaluate("The M4's crawling, just volume.", corridors, 30);
        Score(r, "direction").ShouldBeNull();
    }

    [Fact]
    public void Direction_ScoredAgainstDirectionalCorridorsOnly()
    {
        // One directional corridor + one direction-less congestion corridor: voicing the single
        // available direction scores full — congestion must not drag the denominator down.
        var corridors = new[]
        {
            Corridor("M1", "accident", direction: "citybound"),
            Corridor("M4", "congestion", hasIncident: false)
        };
        var r = TargetShapeScorer.Evaluate("Crash on the M1 citybound. The M4's also busy.", corridors, 30);
        Score(r, "direction").ShouldBe(1.0);
    }

    [Fact]
    public void Anchors_IsNA_WhenNoAnchorableCorridor()
    {
        var corridors = new[] { Corridor("M4", "congestion", hasIncident: false) };
        var r = TargetShapeScorer.Evaluate("The M4's crawling citybound, just volume.", corridors, 30);
        Score(r, "anchors").ShouldBeNull();
    }

    [Fact]
    public void Anchors_ScoredAgainstAnchorableCorridorsOnly()
    {
        // One anchorable incident corridor + one congestion corridor: a single anchor phrase covers
        // the anchorable one → full, undragged by the unanchorable congestion corridor.
        var corridors = new[]
        {
            Corridor("M1", "accident"),
            Corridor("M4", "congestion", hasIncident: false)
        };
        var r = TargetShapeScorer.Evaluate("Crash on the M1 at Coleman Street. The M4's also busy.", corridors, 30);
        Score(r, "anchors").ShouldBe(1.0);
    }
}
