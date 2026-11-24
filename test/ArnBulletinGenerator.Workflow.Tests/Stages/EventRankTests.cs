using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>
///     The one scale both writing desks are merged on.
///     <para>
///         Pinned tightly because nothing downstream can notice it is wrong. A bad rank does not
///         fail a check or trip a detector — it just airs a two-minute delay ahead of a closed
///         motorway, and the bulletin sounds like it does not know what matters.
///     </para>
/// </summary>
public sealed class EventRankTests
{
    private static int Rank(BulletinEventKind kind, params (string Field, string Value)[] facts) =>
        EventRank.Of(kind, [.. facts.Select(f => new EventFact($"e:{f.Field}", f.Field, f.Value))]);

    [Fact]
    public void No_delay_figure_can_lift_a_queue_above_a_blockage()
    {
        // The bands exist for exactly this. A twenty-minute crawl is miserable; a closed road is a
        // different instruction, and it leads.
        int closure = Rank(BulletinEventKind.Corridor, ("detail", "road closure"));
        int longQueue = Rank(BulletinEventKind.Corridor, ("delayMinutes", "twenty"));

        closure.ShouldBeGreaterThan(longQueue);
    }

    [Fact]
    public void Spoken_delay_figures_rank_the_same_as_numeric_ones()
    {
        // Evidence arrives already worded for speech, so a rank reading only digits would score
        // every real delay zero and order the bulletin by nothing at all.
        Rank(BulletinEventKind.Corridor, ("delayMinutes", "four"))
            .ShouldBe(Rank(BulletinEventKind.Corridor, ("delayMinutes", "4")));
    }

    [Fact]
    public void Worse_delays_outrank_lighter_ones()
    {
        int severe = Rank(BulletinEventKind.Corridor, ("delayMinutes", "twelve"));
        int moderate = Rank(BulletinEventKind.Corridor, ("delayMinutes", "seven"));
        int minor = Rank(BulletinEventKind.Corridor, ("delayMinutes", "three"));

        severe.ShouldBeGreaterThan(moderate);
        moderate.ShouldBeGreaterThan(minor);
    }

    [Fact]
    public void A_clear_run_ranks_below_every_delay_and_above_nothing_but_fixtures()
    {
        int clear = Rank(BulletinEventKind.ClearRun, ("clear road", "M7"));

        clear.ShouldBeLessThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "one")));
        clear.ShouldBeGreaterThan(Rank(BulletinEventKind.Weather, ("condition", "Passing clouds")));
    }

    [Fact]
    public void Fixtures_rank_zero_so_they_never_compete_on_judgement()
    {
        // Weather and fuel are carried by Required. A fixture is not up for judgement, so it must
        // not appear on the scale that expresses judgement.
        Rank(BulletinEventKind.Weather, ("temperature", "15 degrees")).ShouldBe(0);
        Rank(BulletinEventKind.Fuel, ("fuel", "unleaded 170")).ShouldBe(0);
    }

    [Fact]
    public void Extent_separates_two_blockages_when_nothing_else_does()
    {
        // Nine kays of closed motorway is a bigger problem than half a kay of it, and the delay
        // riding along with the closure is the only thing in the facts that says so.
        int big = Rank(BulletinEventKind.Corridor, ("detail", "road closure"), ("delayMinutes", "nine"));
        int small = Rank(BulletinEventKind.Corridor, ("detail", "road closure"), ("delayMinutes", "one"));

        big.ShouldBeGreaterThan(small);
    }

    [Fact]
    public void A_crash_is_a_blockage_even_when_no_lane_count_is_supplied()
    {
        // Feeds describe the same thing as "crash", "collision" or a lane count, and a driver has
        // to route around all three.
        Rank(BulletinEventKind.Corridor, ("incident", "a crash"))
            .ShouldBeGreaterThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "twenty")));
    }

    [Fact]
    public void A_one_or_two_minute_delay_ranks_below_everything_that_is_actually_news()
    {
        // Reported 2026-08-27: a 22 second Sydney slot spent on a two-minute delay at Greenacre, a
        // one-minute at Seven Hills and a one-minute at Mascot. These are measured and real and
        // they are still the first thing that should go when anything else wants the airtime.
        int twoMinutes = Rank(BulletinEventKind.Corridor, ("delayMinutes", "two"));

        twoMinutes.ShouldBeLessThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "three")));
        twoMinutes.ShouldBeLessThan(Rank(BulletinEventKind.Corridor, ("detail", "road closure")));
        twoMinutes.ShouldBeGreaterThan(Rank(BulletinEventKind.ClearRun, ("clear road", "M7")));
    }

    [Fact]
    public void A_monitored_journey_outranks_a_single_corridor_carrying_the_same_delay()
    {
        // Four minutes across the M4 from Parramatta and four minutes on a suburban street are not
        // the same news, and nothing else in the facts distinguishes them.
        Rank(BulletinEventKind.DriveTime, ("delayMinutes", "four"))
            .ShouldBeGreaterThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "four")));
    }

    [Fact]
    public void The_M4_beats_the_queues_that_displaced_it()
    {
        // The exact board from job ffcad4af. The M4 from Parramatta was running four minutes slow
        // and never reached the writer at all; these three went to air in its place. Whatever else
        // changes, this ordering is the point.
        int m4 = Rank(BulletinEventKind.DriveTime, ("delayMinutes", "four"));

        m4.ShouldBeGreaterThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "two")));   // Greenacre
        m4.ShouldBeGreaterThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "one")));   // Seven Hills
        m4.ShouldBeGreaterThan(Rank(BulletinEventKind.Corridor, ("delayMinutes", "one")));   // Mascot
    }
}

