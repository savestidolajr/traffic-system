using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     The pre-draft bookend slot-fit calculation (bug-bash 2026-08-05 finding 4): whether a
///     bookend pair can share a slot with a minimum viable traffic read. This is the single
///     arithmetic both the generation engine's pre-draft short-circuit and the schedule
///     trigger's assignment gate rely on, so the boundary behaviour is pinned here table-driven.
/// </summary>
public class BookendSlotFitPlannerTests
{
    // 150 wpm — the BroadcastOptions default and the calibrated pace measured on the live
    // 15s-slot failure (17 bookend words ≈ 6.8s ≈ 45% of the slot).
    private const double SecondsPerWord = 0.4;

    private static BookendFitResult Evaluate(int bookendWords, double slotSeconds) =>
        BookendSlotFitPlanner.Evaluate(bookendWords, slotSeconds, SecondsPerWord);

    [Theory]
    [InlineData(0, 15.0, true)]    // no pair: the whole slot is traffic
    [InlineData(6, 15.0, true)]    // small pair on a 15s slot still leaves room
    [InlineData(17, 15.0, false)]  // THE live failure (job f9907416): 17 words (6.8s) + 8.4s min traffic = 15.2s > 15s
    [InlineData(17, 15.2, true)]   // boundary: exactly 15.2s just fits
    [InlineData(17, 15.19, false)] // ...and a hair under it does not
    [InlineData(18, 15.0, false)]  // a synthetic 18-word pair needs 15.6s — still cannot share a 15s slot
    [InlineData(18, 16.0, true)]   // ...but a 16s slot absorbs it (15.6 ≤ 16) — the retain guard
    [InlineData(16, 15.0, true)]   // the largest pair a 15s slot still retains (6.4 + 8.4 = 14.8 ≤ 15)
    [InlineData(18, 30.0, true)]   // the ordinary case: standard pair on a standard slot
    [InlineData(25, 15.0, false)]  // a long pair cannot fit a 15s slot at all
    [InlineData(60, 30.0, false)]  // half the slot in bookends is unserviceable even at 30s
    public void Evaluate_BookendWordsOverSlotLengths_FitsMatchesExpectation(
        int bookendWords, double slotSeconds, bool fits)
    {
        var result = Evaluate(bookendWords, slotSeconds);

        result.Fits.ShouldBe(fits);
    }

    [Fact]
    public void Evaluate_ReportsTheArithmeticBehindTheVerdict()
    {
        // The measured live failure: the 17-word pair leaves 8.2s for traffic, which the
        // duration-fit loop could not converge on — so the pair is dropped with a 0.2s shortfall.
        var result = Evaluate(17, 15.0);

        result.Fits.ShouldBeFalse();
        result.BookendSeconds.ShouldBe(6.8);
        result.MinimumViableTrafficSeconds.ShouldBe(8.4); // 21 words × 0.4 s/word
        result.SlotSeconds.ShouldBe(15.0);
        result.RequiredSeconds.ShouldBe(15.2);
        result.ShortfallSeconds.ShouldBe(0.2);
    }

    [Fact]
    public void Evaluate_JustFittingBoundary_HasZeroShortfall()
    {
        var result = Evaluate(17, 15.2);

        result.Fits.ShouldBeTrue();
        result.ShortfallSeconds.ShouldBe(0);
    }

    [Fact]
    public void Evaluate_ResolvedBookendPair_UsesItsCombinedWordCount()
    {
        // 7-word intro + 10-word outro = 17 words → the live failure's pair.
        var pair = Option<ResolvedBookends>.Some(new ResolvedBookends(
            "Looking over Adelaide today, your traffic update.", // 7 words
            "That is it for Adelaide traffic on ZZ Test FM.",    // 10 words
            "bk-1", 3));

        var result = BookendSlotFitPlanner.Evaluate(pair, 15.0, SecondsPerWord);

        result.Fits.ShouldBeFalse();
        result.BookendSeconds.ShouldBe(6.8);
    }

    [Fact]
    public void Evaluate_MovesWithTheVoicePace()
    {
        // The threshold is seconds derived from the voice's pace, not a constant: the SAME
        // 18-word pair needs 7.2s at 150 wpm but only 6.75s at 160 wpm, so on a 15s slot the
        // faster voice still fits (14.625 ≤ 15) while the slower one does not (15.6 > 15).
        BookendSlotFitPlanner.Evaluate(18, 15.0, secondsPerWord: 0.4).Fits.ShouldBeFalse();
        BookendSlotFitPlanner.Evaluate(18, 15.0, secondsPerWord: 0.375).Fits.ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_ZeroOrNegativeSlot_NeverFitsAPair()
    {
        Evaluate(18, 0).Fits.ShouldBeFalse();
        Evaluate(0, 0).Fits.ShouldBeFalse();
    }
}
