using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class DynamicSlotSelectorTests
{
    private static readonly double[] StandardSlots = [15, 20, 30, 60];

    // ── Early exits: nothing to shorten ──────────────────────────────────────

    [Fact]
    public void Select_ReadIsZero_ReturnsOriginalTarget()
    {
        DynamicSlotSelector.Select(0, 30, StandardSlots).ShouldBe(30);
    }

    [Fact]
    public void Select_ReadIsNegative_ReturnsOriginalTarget()
    {
        DynamicSlotSelector.Select(-5, 30, StandardSlots).ShouldBe(30);
    }

    [Fact]
    public void Select_NullStandardSlots_ReturnsOriginalTarget()
    {
        DynamicSlotSelector.Select(10, 30, null!).ShouldBe(30);
    }

    [Fact]
    public void Select_EmptyStandardSlots_ReturnsOriginalTarget()
    {
        DynamicSlotSelector.Select(10, 30, []).ShouldBe(30);
    }

    [Fact]
    public void Select_ReadAtOriginalTarget_ReturnsOriginalTarget()
    {
        DynamicSlotSelector.Select(30, 30, StandardSlots).ShouldBe(30);
    }

    [Fact]
    public void Select_ReadOverOriginalTarget_ReturnsOriginalTarget()
    {
        DynamicSlotSelector.Select(35, 30, StandardSlots).ShouldBe(30);
    }

    // ── Picks the smallest slot the read fits within the speed-up factor ────

    [Fact]
    public void Select_ReadFitsSmallestSlotWithHeadroom_PicksSmallestSlot()
    {
        // read 16s, slots [15,20,30,60], factor 1.10: 15*1.1=16.5 >= 16, so even the smallest
        // slot has enough compression headroom — it wins over 20.
        DynamicSlotSelector.Select(16, 30, StandardSlots).ShouldBe(15);
    }

    [Fact]
    public void Select_ReadTooLongForSmallestSlot_PicksNextSlotUp()
    {
        // read 17s: 15*1.1=16.5 < 17, so 15 does not fit; 20*1.1=22 >= 17, so 20 does.
        DynamicSlotSelector.Select(17, 30, StandardSlots).ShouldBe(20);
    }

    [Fact]
    public void Select_CompressionHeadroomBoundary_ExactlyAtFactorLimit_Fits()
    {
        // read exactly equals slot * factor (15 * 1.10 = 16.5) — the boundary counts as fitting.
        DynamicSlotSelector.Select(16.5, 30, StandardSlots).ShouldBe(15);
    }

    [Fact]
    public void Select_CompressionHeadroomBoundary_JustOverFactorLimit_DoesNotFit()
    {
        DynamicSlotSelector.Select(16.51, 30, StandardSlots).ShouldBe(20);
    }

    [Theory]
    [InlineData(2.0)] // read 2s fits 15s slot trivially
    [InlineData(14.9)]
    public void Select_ShortReads_PickSmallestStandardSlot(double read)
    {
        DynamicSlotSelector.Select(read, 60, StandardSlots).ShouldBe(15);
    }

    // ── Slots larger than the original target, or non-positive, are ignored ─

    [Fact]
    public void Select_SlotsLargerThanOriginal_AreIgnored()
    {
        // original target 20: 30 and 60 exceed it and must never be chosen even though the read
        // would fit them; only 15 (<=20) is eligible, and it fits (10*1.1=11>=10).
        DynamicSlotSelector.Select(10, 20, StandardSlots).ShouldBe(15);
    }

    [Fact]
    public void Select_NonPositiveSlotsInList_AreIgnored()
    {
        double[] slotsWithJunk = [-5, 0, 15, 20];

        DynamicSlotSelector.Select(2, 30, slotsWithJunk).ShouldBe(15);
    }

    // ── No smaller slot fits: falls back to the original target ─────────────

    [Fact]
    public void Select_NoSlotFits_ReturnsOriginalTarget()
    {
        // read 12s: no slot <= original(30) is small enough that 12 <= slot*1.10 gives a slot
        // smaller than the read would need... actually verify explicit unfitting case below.
        double[] onlyLargeSlot = [60];

        DynamicSlotSelector.Select(25, 30, onlyLargeSlot).ShouldBe(30);
    }

    [Fact]
    public void Select_ReadExceedsHeadroomOfEveryEligibleSlot_ReturnsOriginalTarget()
    {
        // Every standard slot <= original(30) is 15/20/30, but read=29 does not fit within
        // 1.10x of 15 (16.5) or 20 (22). It DOES fit 30 (33) but 30 is not < chosen default 30,
        // so nothing smaller qualifies and the original target is kept.
        DynamicSlotSelector.Select(29, 30, StandardSlots).ShouldBe(30);
    }
}
