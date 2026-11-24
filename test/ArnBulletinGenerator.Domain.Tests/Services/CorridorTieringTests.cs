using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CorridorTieringTests
{
    // PrimaryType "congestion" carries no type floor above Minor, so DelayMinutes alone decides
    // the tier — mirrors CorridorMagnitudeTests' band boundaries (Severe >30, Major 16-30,
    // Moderate 6-15, Minor <=5/none).
    private static TrafficCorridor Corridor(string roadName, int delayMinutes, double impactScore = 50.0) =>
        new(roadName, Severity: 1, FunctionalClass: 3, TotalLengthMeters: 0,
            ImpactScore: impactScore, PrimaryType: "congestion", Description: "test",
            Incidents: [],
            DriveTimeMinutes: Option<int>.Some(20 + delayMinutes),
            NormalDriveTimeMinutes: Option<int>.Some(20));

    // ── Empty input ─────────────────────────────────────────────────────────

    [Fact]
    public void SelectVoiceable_EmptyInput_ReturnsEmpty()
    {
        CorridorTiering.SelectVoiceable([]).ShouldBeEmpty();
    }

    [Fact]
    public void CountVoiceable_EmptyInput_ReturnsZero()
    {
        CorridorTiering.CountVoiceable([]).ShouldBe(0);
    }

    // ── Major tier cap (4) ──────────────────────────────────────────────────

    [Fact]
    public void SelectVoiceable_MajorTier_AtCap_KeepsAllFour()
    {
        var corridors = new[]
        {
            Corridor("M1", 20), Corridor("M2", 19), Corridor("M3", 18), Corridor("M4", 17)
        };

        var result = CorridorTiering.SelectVoiceable(corridors);

        result.Select(c => c.RoadName).ShouldBe(["M1", "M2", "M3", "M4"]);
    }

    [Fact]
    public void SelectVoiceable_MajorTier_OverCap_DropsMildestBeyondCap()
    {
        var corridors = new[]
        {
            Corridor("M1", 25), Corridor("M2", 24), Corridor("M3", 23), Corridor("M4", 22), Corridor("M5", 21)
        };

        var result = CorridorTiering.SelectVoiceable(corridors);

        // Worst delay leads; the 5th (mildest) Major, M5, is dropped by the cap.
        result.Select(c => c.RoadName).ShouldBe(["M1", "M2", "M3", "M4"]);
        result.Count.ShouldBe(CorridorTiering.MajorTierCap);
    }

    // ── Moderate tier cap (3) ───────────────────────────────────────────────

    [Fact]
    public void SelectVoiceable_ModerateTier_AtCap_KeepsAllThree()
    {
        var corridors = new[] { Corridor("Mo1", 12), Corridor("Mo2", 10), Corridor("Mo3", 8) };

        var result = CorridorTiering.SelectVoiceable(corridors);

        result.Select(c => c.RoadName).ShouldBe(["Mo1", "Mo2", "Mo3"]);
    }

    [Fact]
    public void SelectVoiceable_ModerateTier_OverCap_DropsMildestBeyondCap()
    {
        var corridors = new[] { Corridor("Mo1", 15), Corridor("Mo2", 12), Corridor("Mo3", 10), Corridor("Mo4", 6) };

        var result = CorridorTiering.SelectVoiceable(corridors);

        result.Select(c => c.RoadName).ShouldBe(["Mo1", "Mo2", "Mo3"]);
        result.Count.ShouldBe(CorridorTiering.ModerateTierCap);
    }

    // ── Minor-suppression: dropped entirely once a Major/Severe headline exists ─

    [Fact]
    public void SelectVoiceable_MinorTier_DroppedEntirely_WhenMajorOrSeverePresent()
    {
        var corridors = new[]
        {
            Corridor("Major1", 20),
            Corridor("Moderate1", 10), Corridor("Moderate2", 8),
            Corridor("Minor1", 5), Corridor("Minor2", 4), Corridor("Minor3", 3), Corridor("Minor4", 2)
        };

        var result = CorridorTiering.SelectVoiceable(corridors);

        result.Select(c => c.RoadName).ShouldBe(["Major1", "Moderate1", "Moderate2"]);
        result.ShouldNotContain(c => c.RoadName.StartsWith("Minor"));
    }

    [Fact]
    public void SelectVoiceable_MinorTier_KeptCapped_WhenNoMajorOrSeverePresent()
    {
        var corridors = new[]
        {
            Corridor("Moderate1", 10), Corridor("Moderate2", 8),
            Corridor("Minor1", 5), Corridor("Minor2", 4), Corridor("Minor3", 3), Corridor("Minor4", 2)
        };

        var result = CorridorTiering.SelectVoiceable(corridors);

        // No Major/Severe headline: the minors ARE the story and stay, capped at MinorTierCap (3)
        // — the mildest, Minor4 (delay 2), is dropped.
        result.Select(c => c.RoadName).ShouldBe(["Moderate1", "Moderate2", "Minor1", "Minor2", "Minor3"]);
    }

    [Fact]
    public void SelectVoiceable_MinorTier_AtCap_KeepsAllThree_WhenNoMajorPresent()
    {
        var corridors = new[] { Corridor("Mi1", 5), Corridor("Mi2", 3), Corridor("Mi3", 1) };

        var result = CorridorTiering.SelectVoiceable(corridors);

        result.Select(c => c.RoadName).ShouldBe(["Mi1", "Mi2", "Mi3"]);
    }

    // ── ClassifyByTier bucketing and ordering ───────────────────────────────

    [Fact]
    public void ClassifyByTier_MergesSevereAndMajorIntoOneBucket_MostSevereFirst()
    {
        var severe = Corridor("Severe1", 35);
        var major = Corridor("Major1", 20);
        var moderate = Corridor("Moderate1", 10);
        var minor = Corridor("Minor1", 2);

        var (majorBucket, moderateBucket, minorBucket) =
            CorridorTiering.ClassifyByTier([major, severe, moderate, minor]);

        majorBucket.Select(c => c.RoadName).ShouldBe(["Severe1", "Major1"]);
        moderateBucket.Select(c => c.RoadName).ShouldBe(["Moderate1"]);
        minorBucket.Select(c => c.RoadName).ShouldBe(["Minor1"]);
    }

    [Fact]
    public void ClassifyByTier_WithinTier_OrdersByWorstDelayThenImpactScore()
    {
        var lessBad = Corridor("LessBad", 18);
        var worse = Corridor("Worse", 25);
        // Tied on delay (both 20) — ImpactScore breaks the tie.
        var tieLowImpact = Corridor("TieLow", 20, impactScore: 10);
        var tieHighImpact = Corridor("TieHigh", 20, impactScore: 90);

        var (major, _, _) = CorridorTiering.ClassifyByTier([lessBad, worse, tieLowImpact, tieHighImpact]);

        major.Select(c => c.RoadName).ShouldBe(["Worse", "TieHigh", "TieLow", "LessBad"]);
    }

    // ── CountVoiceable tracks actual voiced selection, not raw corridor count ─

    [Fact]
    public void CountVoiceable_ReflectsSuppression_NotRawCorridorCount()
    {
        var corridors = new[]
        {
            Corridor("Major1", 20),
            Corridor("Minor1", 5), Corridor("Minor2", 4), Corridor("Minor3", 3),
            Corridor("Minor4", 2), Corridor("Minor5", 1)
        };

        // The Major headline suppresses all 5 minors — voiceable count is 1, not the raw 6.
        CorridorTiering.CountVoiceable(corridors).ShouldBe(1);
        CorridorTiering.CountVoiceable(corridors).ShouldBe(CorridorTiering.SelectVoiceable(corridors).Count);
    }
}
