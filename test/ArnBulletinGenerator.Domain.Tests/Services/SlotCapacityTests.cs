using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class SlotCapacityTests
{
    [Theory]
    [InlineData(30, 3.0, 10)] // dev per-item cost: 30s slot fits 10 items (the fallback-reduction fix)
    [InlineData(30, 5.0, 6)]  // legacy per-item cost: the same 30s slot only fit 6 items
    [InlineData(29, 3.0, 9)]  // truncates toward zero (29/3 = 9.67), never rounds up to 10
    public void Compute_ReturnsExpectedItemCount(double targetDurationSeconds, double secondsPerItem, int expected) =>
        SlotCapacity.Compute(targetDurationSeconds, secondsPerItem).ShouldBe(expected);

    [Fact]
    public void Compute_ShortSlot_FloorsAtMinItems()
    {
        // 15s / 3.0 = 5 raw items, below the floor that keeps short/sparse slots viable and
        // leaves them unchanged from legacy behaviour.
        SlotCapacity.Compute(15, 3.0).ShouldBe(SlotCapacity.MinItems);
    }

    [Fact]
    public void Compute_LongSlot_CapsAtMaxItems()
    {
        // 120s / 3.0 = 40 raw items, bounded by the prompt-size cap.
        SlotCapacity.Compute(120, 3.0).ShouldBe(SlotCapacity.MaxItems);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.5)]
    public void Compute_NonPositiveSecondsPerItem_FallsBackToLegacyRate(double secondsPerItem)
    {
        // A non-positive per-item cost must never throw or divide by zero; it falls back to the
        // legacy 5.0 s/item assumption, so a 30s slot still yields the historical 6 items.
        SlotCapacity.Compute(30, secondsPerItem).ShouldBe(6);
    }
}
