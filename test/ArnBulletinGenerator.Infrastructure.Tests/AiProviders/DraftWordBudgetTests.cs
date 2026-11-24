using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public class DraftWordBudgetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Compute_LowCorridorCount_IsLow(int corridors)
    {
        var (_, isLow) = DraftWordBudget.Compute(targetWordCount: 80, corridorCount: corridors);

        isLow.ShouldBeTrue();
    }

    [Fact]
    public void Compute_ThreeOrMoreCorridors_NotLow()
    {
        var (_, isLow) = DraftWordBudget.Compute(targetWordCount: 80, corridorCount: 5);

        isLow.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void Compute_FloorIsSlotFloor_RegardlessOfCorridors(int corridors)
    {
        // Exactly-slot product: every bulletin must fill the slot, so the floor (slow-fill
        // floor ~90% of target) does NOT shrink for quiet/sparse markets - they fill with good-runs.
        var (floor, _) = DraftWordBudget.Compute(targetWordCount: 80, corridorCount: corridors);

        floor.ShouldBe(72); // Max(10, 80 * 9 / 10)
    }

    [Fact]
    public void Compute_FloorNeverBelowTen()
    {
        var (floor, _) = DraftWordBudget.Compute(targetWordCount: 8, corridorCount: 0);

        floor.ShouldBe(10); // Max(10, 8 * 9 / 10 = 7)
    }

    [Theory]
    [InlineData(85, 97)]
    [InlineData(80, 92)]
    [InlineData(30, 38)]
    [InlineData(154, 177)]
    [InlineData(10, 18)]
    public void Ceiling_AddsSlowdownHeadroom_ClampedToMinimumEight(int targetWordCount, int expectedCeiling)
    {
        DraftWordBudget.Ceiling(targetWordCount).ShouldBe(expectedCeiling);
    }

    // --- TrimToCeiling ---------------------------------------------------------------------
    //
    // A sentence is classified as an "incident" solely by whether any of its EvidenceIds starts
    // with "corridor:" (TrimToCeiling.IsIncident); every other prefix (weather:/fuel:/goodrun:/
    // route:/cleared:) is treated as a non-incident "closer" that always survives trimming.

    private static string Words(int count) =>
        string.Join(' ', Enumerable.Range(1, count).Select(i => $"w{i}"));

    private static DraftSentence Incident(int order, int wordCount, string corridor = "A") =>
        new(order, new[] { $"corridor:{corridor}#1:incident:0" }, Words(wordCount));

    private static DraftSentence Closer(int order, int wordCount, string evidenceId) =>
        new(order, new[] { evidenceId }, Words(wordCount));

    [Fact]
    public void TrimToCeiling_UnderCeiling_ReturnsAllSentencesInOrder()
    {
        var sentences = new[]
        {
            Incident(1, 5),
            Closer(2, 4, "weather:condition"),
            Incident(3, 5)
        };

        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 20);

        result.Select(s => s.Order).ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public void TrimToCeiling_TenShortIncidentsUnderCeiling_KeepsAll_WordsNotItemCountDriveTrimming()
    {
        // Ten incidents at 2 words each = 20 words total, comfortably under a 25-word ceiling -
        // a no-op despite the item count, proving the guard is word-budgeted, not count-capped.
        var sentences = Enumerable.Range(1, 10)
            .Select(order => Incident(order, wordCount: 2, corridor: $"R{order}"))
            .ToArray();

        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 25);

        result.Count.ShouldBe(10);
        result.Select(s => s.Order).ShouldBe(Enumerable.Range(1, 10));
    }

    [Fact]
    public void TrimToCeiling_DenseOverCeiling_DropsLaterHigherOrderIncidentsToFitBudgetMinusClosers()
    {
        var sentences = new[]
        {
            Incident(1, 8, "A"),
            Incident(2, 8, "B"),
            Incident(3, 8, "C"),
            Incident(4, 8, "D"),
            Closer(10, 5, "weather:condition"),
            Closer(20, 5, "fuel:price")
        };

        // reserved = 10 (closers); incidentBudget = 30 - 10 = 20; incidents 1+2 = 16 fit, +3 = 24 overflows.
        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 30);

        result.Select(s => s.Order).ShouldBe(new[] { 1, 2, 10, 20 });
        result.Sum(s => s.Text.Split(' ').Length).ShouldBeLessThanOrEqualTo(30);
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:C#1:incident:0");
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:D#1:incident:0");
    }

    [Fact]
    public void TrimToCeiling_WeatherFuelGoodrunClosersAfterOverBudgetIncidents_AllSurvive()
    {
        var sentences = new[]
        {
            Incident(1, 15, "A"),
            Incident(2, 15, "B"),
            Closer(3, 4, "weather:condition"),
            Closer(4, 4, "fuel:price"),
            Closer(5, 4, "goodrun:status")
        };

        // reserved = 12; incidentBudget = 25 - 12 = 13; the lead incident (15 words) already
        // busts that budget alone, so incident 2 drops - but every closer still survives.
        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 25);

        result.Select(s => s.Order).ShouldBe(new[] { 1, 3, 4, 5 });
        result.SelectMany(s => s.EvidenceIds).ShouldContain("weather:condition");
        result.SelectMany(s => s.EvidenceIds).ShouldContain("fuel:price");
        result.SelectMany(s => s.EvidenceIds).ShouldContain("goodrun:status");
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:B#1:incident:0");
    }

    [Fact]
    public void TrimToCeiling_SingleIncidentAloneExceedsCeiling_IsStillKept()
    {
        var sentences = new[] { Incident(1, 50, "A") };

        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 20);

        result.Count.ShouldBe(1);
        result[0].Order.ShouldBe(1);
        result[0].EvidenceIds.ShouldContain("corridor:A#1:incident:0");
    }

    [Fact]
    public void TrimToCeiling_PrefixSemantics_ShortLaterIncidentThatWouldFitAloneIsAlsoDropped()
    {
        var sentences = new[]
        {
            Incident(1, 30, "A"), // busts the budget alone but is always kept as the lead incident
            Incident(2, 2, "B")   // only 2 words - would fit under the 25-word ceiling in isolation
        };

        // No closers: incidentBudget = 25. Incident 1 alone (30 words) already exceeds it, so the
        // prefix rule drops every later incident - incident 2 never gets evaluated on its own merit.
        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 25);

        result.Count.ShouldBe(1);
        result[0].Order.ShouldBe(1);
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:B#1:incident:0");
    }

    [Fact]
    public void TrimToCeiling_PrefixSemantics_NoSkipFit_LaterIncidentFittingRemainingHeadroomStillDropped()
    {
        var sentences = new[]
        {
            Incident(1, 20, "A"), // fits within the 30-word incidentBudget on its own
            Incident(2, 15, "B"), // 20 + 15 = 35 overflows -> rejected, flips the exhausted flag
            Incident(3, 8, "C")   // 20 + 8 = 28 WOULD fit the remaining headroom if evaluated alone
        };

        // No closers: incidentBudget = 30. Once incident 2 overflows, incident 3 is dropped too,
        // even though its own words would fit what incident 1 left behind - no skip-fit re-check.
        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 30);

        result.Count.ShouldBe(1);
        result[0].Order.ShouldBe(1);
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:B#1:incident:0");
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:C#1:incident:0");
    }

    [Fact]
    public void TrimToCeiling_ShuffledInput_ReturnsAscendingByOrder()
    {
        var shuffled = new[]
        {
            Closer(5, 3, "cleared:0"),
            Incident(1, 3, "A"),
            Closer(3, 3, "route:driveTime")
        };

        var result = DraftWordBudget.TrimToCeiling(shuffled, ceiling: 50);

        result.Select(s => s.Order).ShouldBe(new[] { 1, 3, 5 });
    }

    [Fact]
    public void TrimToCeiling_ClosersAloneExceedCeiling_ClampsIncidentBudgetToZero_KeepsLeadIncidentPlusClosers()
    {
        var sentences = new[]
        {
            Closer(1, 20, "weather:condition"),
            Closer(2, 20, "fuel:price"),
            Incident(3, 5, "A"),
            Incident(4, 5, "B")
        };

        // reserved = 40 already exceeds the 30-word ceiling, so incidentBudget clamps to
        // Max(0, 30 - 40) = 0 - only the lead incident survives (always-kept), not the second.
        var result = DraftWordBudget.TrimToCeiling(sentences, ceiling: 30);

        result.Select(s => s.Order).ShouldBe(new[] { 1, 2, 3 });
        result.SelectMany(s => s.EvidenceIds).ShouldContain("weather:condition");
        result.SelectMany(s => s.EvidenceIds).ShouldContain("fuel:price");
        result.SelectMany(s => s.EvidenceIds).ShouldContain("corridor:A#1:incident:0");
        result.SelectMany(s => s.EvidenceIds).ShouldNotContain("corridor:B#1:incident:0");
    }

    // --- MaxVoicedIncidents -----------------------------------------------------------------
    //
    // incidentBudget = Max(10, targetWordCount * 85 / 100) (integer arithmetic). Per distinct
    // corridor (grouped by SourceKey, first-seen = worst-first order), estWords =
    // Clamp(5 + 2*unitCount, 7, 18). The lead corridor is always counted; every subsequent
    // corridor is counted only while running-used + its estimate stays within the budget - the
    // first overflow stops the walk outright (no skip-fit of a later, cheaper corridor).

    private static EvidenceUnit CorridorUnit(string road, string field, int index = 0) =>
        new($"corridor:{road}#1:{field}:{index}", $"corridor:{road}", field, "value");

    private static IEnumerable<EvidenceUnit> Corridor(string road, int unitCount) =>
        Enumerable.Range(0, unitCount).Select(i => CorridorUnit(road, "field", i));

    private static readonly EvidenceUnit[] NonCorridorCloserUnits =
    {
        new("weather:condition", "weather", "condition", "clear"),
        new("fuel:ULP:price", "fuel:ULP", "price", "$1.59"),
        new("goodrun:M1:status", "goodrun:M1", "status", "flowing well"),
        new("route:M1:driveTime", "route:M1", "driveTime", "12 minutes"),
        new("cleared:0", "cleared", "cleared", "the Monash cleared")
    };

    [Fact]
    public void MaxVoicedIncidents_OnlyNonCorridorUnits_ReturnsZero()
    {
        // weather/fuel/goodrun/route/cleared source keys never start with "corridor:" - with no
        // corridor units at all the walk never starts, regardless of how generous the budget is.
        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 100, NonCorridorCloserUnits).ShouldBe(0);
    }

    [Fact]
    public void MaxVoicedIncidents_SingleTwoUnitCorridor_UnderComfortableBudget_ReturnsOne()
    {
        // 2-unit corridor -> est = Clamp(5+4,7,18) = 9. target=50 -> budget = Max(10,42) = 42.
        var units = Corridor("M1", unitCount: 2).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 50, units).ShouldBe(1);
    }

    [Fact]
    public void MaxVoicedIncidents_SeveralEqualTwoUnitCorridors_StopsAtExactFitCount()
    {
        // Each corridor costs 9 (2 units). target=32 -> budget = Max(10, 32*85/100=27) = 27 (int
        // division floors 27.2). 9,18,27 all fit (three corridors); the fourth would need 36 and
        // overflows, so the walk stops there - the count is 3, not 4.
        var units = Corridor("A", 2).Concat(Corridor("B", 2)).Concat(Corridor("C", 2)).Concat(Corridor("D", 2)).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 32, units).ShouldBe(3);
    }

    [Fact]
    public void MaxVoicedIncidents_HeavyCorridorsConsumeMoreBudgetThanLightOnes_FewerFitForSameBudget()
    {
        // Same budget (target=32 -> 27) as the equal-light-corridor case above, which fit 3 of the
        // 9-word light corridors. Here each corridor carries 7 units, clamped to the 18-word max,
        // so only the always-counted lead corridor fits (18 alone; a second would need 36) -
        // proving the aim is driven by summed unit-count/word-estimate, not corridor count.
        var units = Corridor("H1", 7).Concat(Corridor("H2", 7)).Concat(Corridor("H3", 7)).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 32, units).ShouldBe(1);
    }

    [Theory]
    [InlineData(16, 1)] // budget=13 (16*85/100=13.6->13); 2*7=14 > 13, second corridor overflows
    [InlineData(17, 2)] // budget=14 (17*85/100=14.45->14); 2*7=14 fits exactly
    public void MaxVoicedIncidents_ClampFloor_OneUnitCorridorCostsExactlySeven(int targetWordCount, int expectedCount)
    {
        var units = Corridor("A", 1).Concat(Corridor("B", 1)).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount, units).ShouldBe(expectedCount);
    }

    [Theory]
    [InlineData(42, 1)] // budget=35 (42*85/100=35.7->35); 2*18=36 > 35, second corridor overflows
    [InlineData(43, 2)] // budget=36 (43*85/100=36.55->36); 2*18=36 fits exactly
    public void MaxVoicedIncidents_ClampCeiling_TenUnitCorridorCostsExactlyEighteenNotTwentyFive(int targetWordCount, int expectedCount)
    {
        // Raw (unclamped) cost for a 10-unit corridor would be 5+20=25; if that were used, even
        // the 36-word budget couldn't fit two (25+25=50). The boundary at 36 = 2*18 proves the
        // actual per-corridor cost is the clamped 18, not the raw 25.
        var units = Corridor("A", 10).Concat(Corridor("B", 10)).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount, units).ShouldBe(expectedCount);
    }

    [Fact]
    public void MaxVoicedIncidents_LeadCorridorAloneExceedsBudget_IsStillCounted()
    {
        // 10-unit corridor -> est=18. target=10 -> budget = Max(10, 8) = 10. 18 > 10, but the
        // lead corridor is always counted regardless of its own cost.
        var units = Corridor("A", 10).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 10, units).ShouldBe(1);
    }

    [Fact]
    public void MaxVoicedIncidents_PrefixSemantics_LeadBustsBudgetThenCheapFollowerStillDropped()
    {
        // Lead corridor (10 units, est=18) alone already exceeds the target=10 -> budget=10
        // ceiling and is counted anyway (always-counted lead). The follower (1 unit, est=7) would
        // individually fit under a fresh 10-word budget in isolation, but cumulatively
        // 18+7=25 > 10 overflows - it is dropped, proving there is no skip-fit re-check.
        var units = Corridor("A", 10).Concat(Corridor("B", 1)).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 10, units).ShouldBe(1);
    }

    [Fact]
    public void MaxVoicedIncidents_PrefixSemantics_NoSkipFit_MidSequenceCorridorFittingRemainingHeadroomStillDropped()
    {
        // target=20 -> budget=17 (20*85/100=17). Lead A (2 units, est=9) fits: used=9. B (2
        // units, est=9) would push used to 18 > 17 and overflows - the walk stops there. C (1
        // unit, est=7) would fit the 8 words of headroom left after A (9+7=16<=17) if the walk
        // resumed past B's overflow, but the first overflow ends the walk outright, so C is never
        // reached: count stays 1, not 2.
        var units = Corridor("A", 2).Concat(Corridor("B", 2)).Concat(Corridor("C", 1)).ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 20, units).ShouldBe(1);
    }

    [Fact]
    public void MaxVoicedIncidents_ClosersInterleavedAmongCorridorUnits_AreIgnored()
    {
        // Non-corridor units sit before, between, and after the corridor units. They must not be
        // grouped as pseudo-corridors, must not consume any of the word budget, and must not
        // reset/split a corridor's unit count. Two 2-unit corridors (est=9 each) under a target=50
        // (budget=42) budget both fit - identical to the no-closers case.
        var units = new[] { NonCorridorCloserUnits[0] } // weather, before any corridor unit
            .Concat(new[] { CorridorUnit("A", "impact") })
            .Concat(new[] { NonCorridorCloserUnits[1] }) // fuel, between corridor A's two units
            .Concat(new[] { CorridorUnit("A", "location", 1) })
            .Concat(new[] { NonCorridorCloserUnits[2] }) // goodrun, between corridors A and B
            .Concat(new[] { CorridorUnit("B", "impact") })
            .Concat(new[] { NonCorridorCloserUnits[4] }) // cleared, between corridor B's two units
            .Concat(new[] { NonCorridorCloserUnits[3] }) // route
            .Concat(new[] { CorridorUnit("B", "location", 1) })
            .ToArray();

        DraftWordBudget.MaxVoicedIncidents(targetWordCount: 50, units).ShouldBe(2);
    }
}
