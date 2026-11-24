using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.ContentSafety;
using ArnBulletinGenerator.Infrastructure.Tests.ContentSafety;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>Deterministic, offline tests for the duration, style, and safety scorers.</summary>
public sealed class BulletinScorerTests
{
    [Fact]
    public void Duration_WithinTolerance_RespectsFraction()
    {
        new DurationResult(30, 30).WithinTolerance(0.25).ShouldBeTrue();
        new DurationResult(36, 30).WithinTolerance(0.25).ShouldBeTrue();   // +20%
        new DurationResult(45, 30).WithinTolerance(0.25).ShouldBeFalse();  // +50%
        new DurationResult(30, 30).DeltaSeconds.ShouldBe(0);
    }

    [Fact]
    public void Duration_Evaluate_LongerScriptEstimatesLonger()
    {
        var shortScore = DurationScorer.Evaluate("Roads are clear.", 30);
        var longScore = DurationScorer.Evaluate(
            string.Join(" ", Enumerable.Repeat("delays on the motorway", 30)), 30);

        longScore.EstimatedSeconds.ShouldBeGreaterThan(shortScore.EstimatedSeconds);
        DurationScorer.Evaluate("", 30).EstimatedSeconds.ShouldBe(0);
    }

    [Fact]
    public void Style_SpeakerLabel_FailsViaProductionValidator()
    {
        var result = StyleScorer.Evaluate("Alex: Good morning, here's your traffic update.");

        result.Passed.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Contains("speaker label"));
    }

    [Fact]
    public async Task Safety_BannedWord_IsBlocked()
    {
        var filter = new ConfigurableContentSafetyFilter(
            Options.Create(new ContentSafetyOptions { BannedWords = ["catastrophe"] }),
            new FakeSafetyTermRepository(),
            NullLogger<ConfigurableContentSafetyFilter>.Instance);

        var blocked = await SafetyScorer.EvaluateAsync(filter, "A catastrophe on the M4 this morning.");
        blocked.Passed.ShouldBeFalse();
        blocked.Violations.ShouldNotBeEmpty();

        var clean = await SafetyScorer.EvaluateAsync(filter, "Minor delays on the M4 this morning.");
        clean.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Duration_Underfill_IsHardFail()
    {
        // ~20s estimated for 30s target → below 0.9× floor → FAIL
        var underfilled = new DurationResult(20, 30);
        underfilled.IsUnderfilled.ShouldBeTrue();
        underfilled.Passed.ShouldBeFalse();

        // ~28s estimated for 30s target → above floor → PASS
        var filled = new DurationResult(28, 30);
        filled.IsUnderfilled.ShouldBeFalse();
        filled.Passed.ShouldBeTrue();

        // Boundary: exactly at 0.9× floor → PASS (not underfilled)
        var atFloor = new DurationResult(27, 30); // 27 = 30 * 0.9
        atFloor.IsUnderfilled.ShouldBeFalse();
        atFloor.Passed.ShouldBeTrue();

        // Just below floor → FAIL
        var justBelow = new DurationResult(26.9, 30);
        justBelow.IsUnderfilled.ShouldBeTrue();
        justBelow.Passed.ShouldBeFalse();
    }

    [Fact]
    public void ScenarioScore_Underfill_DrivesOverallFail()
    {
        // All gates pass except duration is underfilled → overall FAIL
        var underfilled = new ScenarioScore(
            Market: "SYD",
            CorridorCount: 2,
            Fabrication: new FabricationResult([]),
            Duration: new DurationResult(20, 30),
            Style: new StyleResult(true, []),
            Safety: new SafetyResult(true, [], []),
            Script: "Minor delays on the M4 this morning.");
        underfilled.Passed.ShouldBeFalse();

        // All gates pass including duration → overall PASS
        var allPass = new ScenarioScore(
            Market: "SYD",
            CorridorCount: 2,
            Fabrication: new FabricationResult([]),
            Duration: new DurationResult(28, 30),
            Style: new StyleResult(true, []),
            Safety: new SafetyResult(true, [], []),
            Script: "Minor delays on the M4 this morning.");
        allPass.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Scorecard_RendersUnderfillMarker()
    {
        var scores = new[]
        {
            new ScenarioScore(
                Market: "MEL",
                CorridorCount: 1,
                Fabrication: new FabricationResult([]),
                Duration: new DurationResult(20, 30),
                Style: new StyleResult(true, []),
                Safety: new SafetyResult(true, [], []),
                Script: "Short script.")
        };

        var card = BulletinScorecard.Render(scores);
        card.ShouldContain("UNDERFILL");
        card.ShouldContain("FAIL");
    }
}
