using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages.Conversational;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

/// <summary>
///     The deterministic half of the sharded writer: which desk owns an event, and how the desks'
///     briefs are put back in order.
///     <para>
///         Worth pinning before any prompt exists, because these are the failures a reader would
///         notice immediately — the same jam described twice from two desks, or a closure read out
///         after a two-minute delay — and neither is a wording problem a better prompt could fix.
///     </para>
/// </summary>
public sealed class BriefDivisionTests
{
    private static BulletinEvent Event(
        string id, BulletinEventKind kind, int rank = 0, params (string Field, string Value)[] facts) =>
        new(id, kind, id, [.. facts.Select(f => new EventFact($"{id}:{f.Field}", f.Field, f.Value))])
        {
            Rank = rank
        };

    private static StoryBrief Brief(string id, params string[] eventIds) =>
        new(1, id, id, eventIds, Priority: 0, WhyItMatters: "because");

    [Fact]
    public void Weather_and_fuel_never_reach_a_writer()
    {
        // They are fixtures: always voiced, fixed position, one sentence, nothing to select. A
        // model can only get them wrong.
        var events = new[]
        {
            Event("weather", BulletinEventKind.Weather),
            Event("fuel", BulletinEventKind.Fuel)
        };
        var incidents = BriefDivision.IncidentCorridors(events);

        foreach (var evt in events)
            BriefDivision.DeskFor(evt, incidents).ShouldBe(BriefDivision.Desk.Fixture);
    }

    [Fact]
    public void Clear_runs_go_to_the_flow_desk_not_a_desk_of_their_own()
    {
        // "Running clean" is only worth airtime in proportion to how much of the read congestion
        // did not fill, so it has to be judged by whoever holds the congestion.
        var evt = Event("goodrun:M5", BulletinEventKind.ClearRun);

        BriefDivision.DeskFor(evt, BriefDivision.IncidentCorridors([evt]))
            .ShouldBe(BriefDivision.Desk.Flow);
    }

    [Fact]
    public void A_queue_caused_by_a_crash_is_briefed_by_the_desk_holding_the_crash()
    {
        // THE failure this routing rule exists to stop: split them and the bulletin describes the
        // same mess twice, once as an incident and once as congestion, from two briefs neither of
        // which knew about the other.
        var crash = Event("corridor:M5", BulletinEventKind.Corridor, 90,
            ("incident", "a crash"), ("lanesClosed", "2"));
        var plainQueue = Event("corridor:Princes Hwy", BulletinEventKind.Corridor, 40,
            ("delayMinutes", "four"));

        var incidents = BriefDivision.IncidentCorridors([crash, plainQueue]);

        BriefDivision.DeskFor(crash, incidents).ShouldBe(BriefDivision.Desk.Incidents);
        BriefDivision.DeskFor(plainQueue, incidents).ShouldBe(BriefDivision.Desk.Flow);
    }

    [Fact]
    public void Stitched_briefs_are_ordered_by_rank_not_by_desk()
    {
        // The point of the whole design: two independent writers cannot produce comparable
        // priorities, so the ordering comes from the rank derived upstream of both. A flow
        // story that outranks an incident story must lead, or sharding has quietly reordered the
        // bulletin.
        var ranks = new Dictionary<string, int>
        {
            ["corridor:M5"] = 40,
            ["corridor:Warringah"] = 95
        };

        var stitched = BriefDivision.Stitch(
            [
                (BriefDivision.Desk.Incidents, Brief("m5-crash", "corridor:M5")),
                (BriefDivision.Desk.Flow, Brief("warringah-jam", "corridor:Warringah"))
            ],
            ranks);

        stitched.Stories.Select(s => s.StoryId).ShouldBe(["warringah-jam", "m5-crash"]);
    }

    [Fact]
    public void Fixtures_are_appended_last_whatever_they_rank()
    {
        // Weather closes the read by editorial convention, not because it ranks lowest. Giving it
        // a high rank must not float it to the top.
        var ranks = new Dictionary<string, int> { ["weather"] = 999, ["corridor:M5"] = 10 };

        var stitched = BriefDivision.Stitch(
            [
                (BriefDivision.Desk.Fixture, Brief("weather", "weather")),
                (BriefDivision.Desk.Flow, Brief("m5", "corridor:M5"))
            ],
            ranks);

        stitched.Stories.Select(s => s.StoryId).ShouldBe(["m5", "weather"]);
    }
    [Fact]
    public void Configured_segment_order_can_put_fuel_before_traffic()
    {
        var stitched = BriefDivision.Stitch(
            [
                (BriefDivision.Desk.Flow, Brief("traffic", "corridor:M5")),
                (BriefDivision.Desk.Fixture, Brief("weather", "weather")),
                (BriefDivision.Desk.Fixture, Brief("fuel", "fuel"))
            ],
            new Dictionary<string, int> { ["corridor:M5"] = 900 },
            ["Fuel", "Traffic", "Weather"]);

        stitched.Stories.Select(story => story.StoryId).ShouldBe(["fuel", "traffic", "weather"]);
    }

    [Fact]
    public void Worst_last_reverses_the_ranked_traffic_stories()
    {
        var stitched = BriefDivision.Stitch(
            [
                (BriefDivision.Desk.Flow, Brief("worst", "worst")),
                (BriefDivision.Desk.Flow, Brief("minor", "minor"))
            ],
            new Dictionary<string, int> { ["worst"] = 900, ["minor"] = 100 },
            ["Traffic"],
            TrafficRunningOrder.WorstLast);

        stitched.Stories.Select(story => story.StoryId).ShouldBe(["minor", "worst"]);
    }


    [Fact]
    public void Stitching_renumbers_order_so_it_stays_a_usable_tiebreak()
    {
        // Each desk numbers its own briefs from 1, so without renumbering several stories share an
        // Order and BulletinBriefs.ByPriority's secondary sort becomes arbitrary.
        var stitched = BriefDivision.Stitch(
            [
                (BriefDivision.Desk.Incidents, Brief("a", "x")),
                (BriefDivision.Desk.Flow, Brief("b", "y")),
                (BriefDivision.Desk.Fixture, Brief("weather", "weather"))
            ],
            new Dictionary<string, int>());

        stitched.Stories.Select(s => s.Order).ShouldBe([1, 2, 3]);
    }
}
