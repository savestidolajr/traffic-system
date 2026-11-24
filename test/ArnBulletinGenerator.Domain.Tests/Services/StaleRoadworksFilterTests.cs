using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Ages out planned works that stopped being news, and nothing else.
///     <para>
///         Cairns on 27 Aug: 16 of 17 corridors were construction and every incident behind them was
///         entered on 19 August. A 22 second slot was being contested by week-old cones.
///     </para>
/// </summary>
public sealed class StaleRoadworksFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 4, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private static TrafficCorridor Corridor(string road, params (string Type, int DaysAgo)[] incidents) =>
        new(road, 1, 3, 1000, 100, incidents.Length > 0 ? incidents[0].Type : "congestion", "", [
            .. incidents.Select((i, n) => new TrafficIncident(
                $"i{n}", "detail", 1, road, i.Type, EntryTimeUtc: Now.AddDays(-i.DaysAgo)))
        ]);

    [Fact]
    public void A_week_old_work_zone_is_dropped()
    {
        var corridors = new[] { Corridor("Captain Cook Highway", ("construction", 7)) };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).ShouldBeEmpty();
    }

    [Fact]
    public void Works_reported_this_morning_stay()
    {
        var corridors = new[] { Corridor("Bruce Highway", ("construction", 0)) };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).Count.ShouldBe(1);
    }

    [Fact]
    public void A_reissued_work_zone_uses_its_original_start_time()
    {
        // HERE resets entryTime on revisions. The start time keeps this five-week-old Sydney work
        // zone out of the bulletin even though the record was re-issued this morning.
        var corridors = new[]
        {
            new TrafficCorridor("Joynton Ave", 1, 4, 138, 100, "construction", "",
            [
                new TrafficIncident(
                    "i0", "Closed", 1, "Joynton Ave", "construction",
                    EntryTimeUtc: Now,
                    StartTimeUtc: Now.AddDays(-35))
            ])
        };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).ShouldBeEmpty();
    }

    [Fact]
    public void A_week_old_crash_is_never_dropped()
    {
        // Only WORKS age out. Something that happened is still the most important thing on the road
        // however long it has been there, and ageing crashes out would be a silent safety failure.
        var corridors = new[] { Corridor("Kennedy Highway", ("accident", 7)) };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).Count.ShouldBe(1);
    }

    [Fact]
    public void Congestion_has_no_report_time_and_never_ages_out()
    {
        // A flow-derived corridor is a live speed reading with no incidents behind it.
        var corridors = new[] { Corridor("Mulgrave Road") };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).Count.ShouldBe(1);
    }

    [Fact]
    public void One_fresh_item_keeps_the_whole_corridor()
    {
        // A week-old work zone that has since had a breakdown inside it is news again.
        var corridors = new[] { Corridor("Bruce Highway", ("construction", 7), ("accident", 0)) };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).Count.ShouldBe(1);
    }

    [Fact]
    public void An_undateable_work_zone_counts_as_old()
    {
        // HERE supplies entryTime on every incident measured so far, so a missing one means a feed
        // that cannot say when this started - which is exactly the standing cone field.
        var corridors = new[]
        {
            new TrafficCorridor("Pease Street", 1, 3, 1000, 100, "construction", "",
                [new TrafficIncident("i0", "detail", 1, "Pease Street", "construction")])
        };

        StaleRoadworksFilter.Fresh(corridors, Now, OneDay).ShouldBeEmpty();
    }

    [Fact]
    public void Zero_max_age_disables_the_filter_entirely()
    {
        // The shipped default. Test and prod keep every work zone until someone opts in.
        var corridors = new[] { Corridor("Captain Cook Highway", ("construction", 30)) };

        StaleRoadworksFilter.Fresh(corridors, Now, TimeSpan.Zero).Count.ShouldBe(1);
    }
}
