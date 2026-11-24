using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class FillerStatementSelectorTests
{
    private static FillerStatement Filler(
        string id, string text, bool enabled = true) =>
        new(id, text, enabled);

    // ── Slot-is-full guards: filler only exists to top up a short read ─────

    [Fact]
    public void EmptyStatements_ReturnsEmpty()
    {
        FillerStatementSelector.Select([], estimatedGapSeconds: 10).ShouldBeEmpty();
    }

    [Fact]
    public void NonPositiveGap_ReturnsEmpty_EvenWithEligibleStatements()
    {
        // A statement whose spoken cost fits any positive gap, but absent the ≤0 guard
        // it would leak through when the slot is already full — that is the tooth this fact defends.
        var statements = new[] { Filler("f1", "School zones are 40km/h.") };

        FillerStatementSelector.Select(statements, estimatedGapSeconds: 0).ShouldBeEmpty();
        FillerStatementSelector.Select(statements, estimatedGapSeconds: -5).ShouldBeEmpty();
    }

    // ── Eligibility filters ────────────────────────────────────────────────

    [Fact]
    public void DisabledStatement_Excluded_EnabledIncluded()
    {
        var statements = new[]
        {
            Filler("on", "Rest if you're tired.", enabled: true),
            Filler("off", "Buckle up back there.", enabled: false),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 10);

        result.Select(s => s.Id).ShouldBe(["on"]);
    }

    [Fact]
    public void CostFitsGapIncluded_CostExceedsGapExcluded()
    {
        // Boundary probed in isolation: under greedy selection an earlier pick shrinks the
        // remaining gap, so a same-call sibling would mask the ≤ boundary check.
        string twentyWords = string.Join(' ', Enumerable.Repeat("word", 20)); // 20/2.5 = 8.0s
        var exactFit = new[] { Filler("equal", twentyWords) };
        FillerStatementSelector.Select(exactFit, estimatedGapSeconds: 8)
            .Select(s => s.Id).ShouldBe(["equal"]); // 8.0 ≤ 8.0 keeps the exact-fit; a "<" bug would drop it

        string twentyFiveWords = string.Join(' ', Enumerable.Repeat("word", 25)); // 25/2.5 = 10.0s
        var tooBig = new[] { Filler("above", twentyFiveWords) };
        FillerStatementSelector.Select(tooBig, estimatedGapSeconds: 8).ShouldBeEmpty(); // 10.0 > 8.0 excluded
    }

    [Fact]
    public void BlankText_Excluded()
    {
        var statements = new[]
        {
            Filler("blank", "   "),
            Filler("real", "Drive safely."),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 10);

        result.Select(s => s.Id).ShouldBe(["real"]);
    }

    // ── Per-station same-day anti-repeat ───────────────────────────────────

    [Fact]
    public void AntiRepeat_ExcludesTextAlreadyVoiced_IgnoringPunctuationAndCase()
    {
        var repeated = Filler("repeat", "School zones are 40km/h.");
        var fresh = Filler("fresh", "Take a break every two hours.");
        // Prior script said the same thing but with different punctuation and case;
        // normalization (strip punctuation + lowercase) must still catch it.
        var previous = "REMEMBER: school zones are 40kmh during term time!";

        var result = FillerStatementSelector.Select(
            [repeated, fresh], estimatedGapSeconds: 10, previousSameDayScript: previous);

        result.Select(s => s.Id).ShouldBe(["fresh"]);
    }

    [Fact]
    public void NullPreviousScript_KeepsAllEligible()
    {
        var statements = new[]
        {
            Filler("a", "School zones are 40km/h."),
            Filler("b", "Take a break every two hours."),
        };

        var result = FillerStatementSelector.Select(
            statements, estimatedGapSeconds: 10, previousSameDayScript: null);

        // Both eligible; "b" (6 words = 2.4s) outranks "a" (4 words = 1.6s) on cost descending.
        result.Select(s => s.Id).ShouldBe(["b", "a"]);
    }

    // ── Gap-covering greedy selection ─────────────────────────────────────

    [Fact]
    public void Ordering_BestFitBySpokenCost_ThenIdAscending()
    {
        // gap = 18. Ranking key is each statement's own spoken cost (wordCount / 2.5):
        //   z-ten  -> 20 words -> 8.0s  ┐ largest cost wins, tie by Id ascending
        //   a-eight-> 15 words -> 6.0s  │
        //   m-six  -> 10 words -> 4.0s  ┘
        // All fit (≤18), so all three are selected in cost-descending, Id-ascending order.
        // Accumulation: 8.0 + 6.0 + 4.0 = 18.0 exactly covers the gap.
        string twentyWords = string.Join(' ', Enumerable.Repeat("word", 20));
        string fifteenWords = string.Join(' ', Enumerable.Repeat("word", 15));
        string tenWords = string.Join(' ', Enumerable.Repeat("word", 10));
        var statements = new[]
        {
            Filler("z-ten", twentyWords),
            Filler("a-eight", fifteenWords),
            Filler("m-six", tenWords),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 18);

        result.Select(s => s.Id).ShouldBe(["z-ten", "a-eight", "m-six"]);
    }

    [Fact]
    public void Selection_StopsWhenGapIsCovered()
    {
        // gap = 8. The largest statement (20 words = 8.0s) exactly covers the gap, so the
        // selector must stop there rather than hand the writer a second statement it doesn't need.
        string twentyWords = string.Join(' ', Enumerable.Repeat("word", 20));
        string tenWords = string.Join(' ', Enumerable.Repeat("word", 10));
        var statements = new[]
        {
            Filler("f1", twentyWords),
            Filler("f2", tenWords),
            Filler("f3", tenWords),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 8);

        result.Count.ShouldBe(1);
        result.Select(s => s.Id).ShouldBe(["f1"]);
    }

    [Fact]
    public void Selection_AccumulatesStatementsUntilLargeGapIsCovered()
    {
        // gap = 12. Each statement is 10 words = 4.0s: one leaves 8s, two leave 4s, three
        // leave 0s — a thin market's hole is covered by ACCUMULATION, not one best fit.
        // Texts are DISTINCT (finding 43 makes identical wording ineligible after one pick,
        // which is a separate contract covered by DuplicateText_* below).
        // maxStatements = 5 to prove the gap, not the cap, stops selection.
        var statements = Enumerable.Range(1, 5)
            .Select(i => Filler($"f{i}", string.Join(' ', Enumerable.Repeat($"w{i}", 10))))
            .ToArray();

        var result = FillerStatementSelector.Select(
            statements, estimatedGapSeconds: 12, maxStatements: 5);

        result.Count.ShouldBe(3);
        result.Select(s => s.Id).ShouldBe(["f1", "f2", "f3"]);
    }

    [Fact]
    public void Selection_RemainingGapGatesTheNextPick()
    {
        // gap = 10. The long statement (20 words = 8.0s) is picked first, leaving 2.0s — the
        // shorter candidate (10 words = 4.0s) no longer fits in the remaining gap even though
        // it fit the ORIGINAL gap.
        string twentyWords = string.Join(' ', Enumerable.Repeat("word", 20));
        string tenWords = string.Join(' ', Enumerable.Repeat("word", 10));
        var statements = new[]
        {
            Filler("long", twentyWords),
            Filler("short", tenWords),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 10);

        result.Select(s => s.Id).ShouldBe(["long"]);
    }

    [Fact]
    public void MaxStatements_CapsAccumulation()
    {
        // A huge gap with many tiny statements still yields at most the ceiling — filler tops up
        // a bulletin, it never becomes the bulletin.
        var statements = Enumerable.Range(1, 6)
            .Select(i => Filler($"f{i}", $"Statement {i} please."))
            .ToArray();

        var result = FillerStatementSelector.Select(
            statements, estimatedGapSeconds: 30, maxStatements: 2);

        result.Count.ShouldBe(2);
        result.Select(s => s.Id).ShouldBe(["f1", "f2"]);
    }

    // ── Overshoot tolerance (post-synthesis rescue) ───────────────────────
    // When NO candidate fits WITHIN the remaining gap and a positive tolerance is given,
    // the selector may close the gap with the SMALLEST statement overshooting by at most
    // the tolerance — then stops. Tolerance 0 (the default) is the strict old contract.

    [Fact]
    public void OvershootDefaultZero_GapSmallerThanEveryStatement_SelectsNothing()
    {
        // Regression pin: callers that do not pass overshootToleranceSeconds keep the
        // strict no-overshoot contract — a gap smaller than every statement selects nothing.
        string tenWords = string.Join(' ', Enumerable.Repeat("word", 10));      // 10/2.5 = 4.0s
        string twelveWords = string.Join(' ', Enumerable.Repeat("word", 12));   // 12/2.5 = 4.8s
        var statements = new[]
        {
            Filler("ten", tenWords),
            Filler("twelve", twelveWords),
        };

        FillerStatementSelector.Select(statements, estimatedGapSeconds: 3).ShouldBeEmpty();
    }

    [Fact]
    public void Overshoot_SelectsSmallestToleranceFit_ThenStopsImmediately()
    {
        // gap = 3, tolerance = 1.5 → overshoot window (3.0, 4.5].
        //   s8  ->  8 words -> 3.2s  in window ┐ SMALLEST cost wins (ascending — the overshoot
        //   s10 -> 10 words -> 4.0s  in window ┘ branch inverts the within-gap descending order)
        //   s12 -> 12 words -> 4.8s  beyond window, never eligible
        // Selection STOPS at the single overshoot pick even though maxStatements allows five.
        var statements = new[]
        {
            Filler("s10", string.Join(' ', Enumerable.Repeat("word", 10))),
            Filler("s8", string.Join(' ', Enumerable.Repeat("word", 8))),
            Filler("s12", string.Join(' ', Enumerable.Repeat("word", 12))),
        };

        var result = FillerStatementSelector.Select(
            statements, estimatedGapSeconds: 3, maxStatements: 5, overshootToleranceSeconds: 1.5);

        result.Count.ShouldBe(1);
        result.Select(s => s.Id).ShouldBe(["s8"]);
    }

    [Fact]
    public void Overshoot_CostBeyondTolerance_NotSelected()
    {
        // gap = 3, tolerance = 0.5 → window (3.0, 3.5]. The only statement costs 4.0s — an
        // overshoot of 1.0s the tolerance does not license, so the gap stays uncovered.
        string tenWords = string.Join(' ', Enumerable.Repeat("word", 10)); // 10/2.5 = 4.0s
        var statements = new[] { Filler("ten", tenWords) };

        FillerStatementSelector.Select(
                statements, estimatedGapSeconds: 3, overshootToleranceSeconds: 0.5)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Overshoot_WithinGapFitStillWins_WhenTheResidualIsInsideThePad()
    {
        // gap = 9, tolerance = 2 → an overshoot candidate exists for the ORIGINAL gap
        // (25 words = 10.0s ∈ (9, 11]) but must not preempt within-gap fits that close the gap:
        //   round 1: within (20 words = 8.0s ≤ 9) is the best fit — picked, remaining 1.0
        //   round 2: tiny (2 words = 0.8s ≤ 1.0) still fits within — greedy continues
        //   round 3: remaining 0.2 is inside the 0.3s pad → covered, no whole-gap fallback
        var statements = new[]
        {
            Filler("over", string.Join(' ', Enumerable.Repeat("word", 25))),
            Filler("within", string.Join(' ', Enumerable.Repeat("word", 20))),
            Filler("tiny", "word word"),
        };

        var result = FillerStatementSelector.Select(
            statements, estimatedGapSeconds: 9, overshootToleranceSeconds: 2, acceptableResidualSeconds: 0.3);

        result.Select(s => s.Id).ShouldBe(["within", "tiny"]);
    }

    [Fact]
    public void Overshoot_GreedyLeavesDeadAir_WholeGapCoverWins()
    {
        // Dev 34402fbb (2026-09-02), scaled: gap = 4.5, tolerance 2.5, pad 0.3.
        //   greedy: eight (8 words = 3.2s ≤ 4.5) picked, remaining 1.3; nothing ≤ 1.3 and nothing
        //   in (1.3, 3.8] → 1.3s of dead air the pipeline cannot close, take rejected.
        //   whole-gap: twelve (12 words = 4.8s ∈ (4.5, 7.0]) covers it with 0.3s the speed-up absorbs.
        var statements = new[]
        {
            Filler("eight", string.Join(' ', Enumerable.Repeat("word", 8))),
            Filler("twelve", string.Join(' ', Enumerable.Repeat("word", 12))),
        };

        var result = FillerStatementSelector.Select(
            statements, estimatedGapSeconds: 4.5, overshootToleranceSeconds: 2.5, acceptableResidualSeconds: 0.3);

        result.Select(s => s.Id).ShouldBe(["twelve"]);

        // Without a tolerance the strict no-overshoot contract holds: greedy result, hole and all.
        FillerStatementSelector.Select(statements, estimatedGapSeconds: 4.5, acceptableResidualSeconds: 0.3)
            .Select(s => s.Id).ShouldBe(["eight"]);
    }

    [Fact]
    public void Overshoot_MeasuredAgainstRemainingGap_AfterWithinGapRoundsExhaust()
    {
        // wordsPerSecond = 2.0 so costs land on halves. gap = 6:
        //   round 1: a-eight (8 words = 4.0s) is the best fit ≤ 6 — remaining 2.0
        //   round 2: b-seven (7 words = 3.5s) no longer fits WITHIN 2.0. The overshoot window
        //   is anchored on the REMAINING gap, not the original: (2.0, 2.0 + tolerance].
        // tolerance 1.5 → 3.5 ≤ 3.5 closes the gap (upper bound INCLUSIVE);
        // tolerance 1.4 → 3.5 > 3.4 is beyond the license and selection ends at a-eight.
        var statements = new[]
        {
            Filler("a-eight", string.Join(' ', Enumerable.Repeat("word", 8))),
            Filler("b-seven", string.Join(' ', Enumerable.Repeat("word", 7))),
        };

        FillerStatementSelector.Select(
                statements, estimatedGapSeconds: 6, wordsPerSecond: 2.0, overshootToleranceSeconds: 1.5)
            .Select(s => s.Id).ShouldBe(["a-eight", "b-seven"]);

        FillerStatementSelector.Select(
                statements, estimatedGapSeconds: 6, wordsPerSecond: 2.0, overshootToleranceSeconds: 1.4)
            .Select(s => s.Id).ShouldBe(["a-eight"]);
    }

    [Fact]
    public void Overshoot_AntiRepeatStillExcludesToleranceFit()
    {
        // The pool filters (enabled, non-blank, anti-repeat) run BEFORE any selection round, so
        // a statement already voiced today is ineligible even as the ONLY tolerance fit.
        var statement = Filler("rescue", "Slow down and enjoy the drive, there is no rush."); // 10 words = 4.0s
        var previous = "Earlier bulletin: slow down and enjoy the drive, there is no rush!";

        // Control: without the previous script the statement IS the tolerance fit for gap 3 (window (3.0, 4.5]).
        FillerStatementSelector.Select(
                [statement], estimatedGapSeconds: 3, overshootToleranceSeconds: 1.5)
            .Select(s => s.Id).ShouldBe(["rescue"]);

        FillerStatementSelector.Select(
                [statement], estimatedGapSeconds: 3, previousSameDayScript: previous,
                overshootToleranceSeconds: 1.5)
            .ShouldBeEmpty();
    }

    [Fact]
    public void WordRate_FasterMeasuredWpsUnlocksWithinGapFit_WithoutTolerance()
    {
        // The rescue re-rates candidates at the MEASURED voice pace. Same statement, same gap,
        // tolerance 0 throughout — only the rate changes:
        //   at 2.5 wps: 10 words = 4.000s > 3.6 → excluded (the pre-synthesis miss)
        //   at 2.85 wps: 10 words = 3.509s ≤ 3.6 → fits WITHIN, no overshoot needed
        string tenWords = string.Join(' ', Enumerable.Repeat("word", 10));
        var statements = new[] { Filler("ten", tenWords) };

        FillerStatementSelector.Select(statements, estimatedGapSeconds: 3.6, wordsPerSecond: 2.5)
            .ShouldBeEmpty();

        FillerStatementSelector.Select(statements, estimatedGapSeconds: 3.6, wordsPerSecond: 2.85)
            .Select(s => s.Id).ShouldBe(["ten"]);
    }

    // ── In-bulletin distinctness (finding 43) ──────────────────────────────

    [Fact]
    public void DuplicateText_DifferentIds_IsSelectedOnlyOnceInOneBulletin()
    {
        // Two entries with IDENTICAL wording under different ids must never both air — the same
        // sentence would be read twice in one bulletin (the copy-paste the composer produces).
        // Both cost 1.6s and the 10s gap fits both, so without distinctness both would be picked.
        var statements = new[]
        {
            Filler("f1", "Buckle up back there."),
            Filler("f2", "Buckle up back there."),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 10);

        result.Count.ShouldBe(1);
        result.Single().Id.ShouldBe("f1"); // Id tie-break keeps the first
    }

    [Fact]
    public void DuplicateText_IgnoringPunctuationAndCase_IsSelectedOnlyOnce()
    {
        // Normalised identity: statements differing only in punctuation or case are the same
        // sentence on air, so they are distinctness-collapsed exactly like the same-day filter.
        var statements = new[]
        {
            Filler("f1", "Buckle up, back there!"),
            Filler("f2", "buckle up back there"),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 10);

        result.Count.ShouldBe(1);
    }

    [Fact]
    public void DistinctTexts_AllRemainEligible()
    {
        // Distinctness must never suppress genuinely different statements — the accumulation
        // contract holds as long as the texts differ.
        var statements = new[]
        {
            Filler("f1", "Buckle up back there."),
            Filler("f2", "Take a break every two hours."),
        };

        var result = FillerStatementSelector.Select(statements, estimatedGapSeconds: 10);

        result.Select(s => s.Id).ShouldBe(["f2", "f1"]); // cost descending, both picked
    }

    [Fact]
    public void Normalize_IsTheSharedPublicContract()
    {
        // The generation pipeline's fallback-padding pass calls this SAME normaliser for its own
        // anti-repeat; the contract is public so the two call sites cannot drift.
        FillerStatementSelector.Normalize("Buckle up, back there!").ShouldBe("buckle up back there");
        FillerStatementSelector.Normalize("  School zones are 40km/h.  ").ShouldBe("school zones are 40kmh");
    }

}
