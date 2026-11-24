using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     REPRODUCTION — dev jobs f553c893 and 7d4664af (cairns-4ca, 2026-08-24). Both shipped
///     <c>Warning: script shipped with an unresolved style gate</c> on the same gate.
///     <para>
///         Cairns supplies TWO Bruce Highway corridors, at Edmonton and at Gordonvale. The slot held
///         one of them — the writer is told to group a road's spots into one item, and the
///         secondary-item cap trimmed the rest — so the read named the Bruce Highway once, placed at
///         Edmonton. The detector loops per CORRIDOR, so naming the road once made the read liable
///         for every suburb that road carries anywhere in the data, and Gordonvale flagged as
///         "dropped" though its spot was never aired.
///     </para>
///     <para>
///         Nothing could clear it. Voicing Gordonvale is exactly the content the word budget
///         removed, and <see cref="SuburbRetentionDetector.Repair" /> declines too: its
///         already-placed guard correctly refuses to stack "near Gordonvale" onto "Bruce Highway
///         near Edmonton". So the gate demanded content the slot could not hold, burned both refine
///         attempts, and shipped the warning — on every cairns bulletin.
///     </para>
/// </summary>
public sealed class SuburbRetentionMultiSpotTests
{
    private static TrafficCorridor Spot(string road, string suburb) =>
        new(road, 2, 1, 1000, 40, "works", "Roadworks", [], Option<string>.Some(suburb));

    private static BroadcastWithTraffic Ctx(params TrafficCorridor[] corridors) => new()
    {
        TargetDurationSeconds = 22,
        JobId = "7d4664af",
        Location = "Cairns",
        Traffic = string.Empty,
        Incidents = [],
        Corridors = corridors,
        GoodRunRoads = [],
        FlowPool = []
    };

    /// <summary>The read as it aired, with the Bruce Highway placed at the spot it voiced.</summary>
    private const string AiredRead =
        "Northbound on the Bruce Highway near Edmonton at Foster Road, roadworks add about a minute. "
        + "And there is another run of roadworks on the Captain Cook Highway, through the Macalister "
        + "Range, Aeroglen and Cairns North.";

    [Fact]
    public void A_road_placed_at_the_spot_it_voiced_is_not_flagged_for_a_spot_it_dropped()
    {
        var result = SuburbRetentionDetector.Detect(
            AiredRead,
            Ctx(Spot("Bruce Highway", "Edmonton"), Spot("Bruce Highway", "Gordonvale")));

        result.HasMissingSuburb.ShouldBeFalse(
            "the listener can place the Bruce Highway — demanding the suburb of a spot the slot "
            + "dropped asks for content the bulletin deliberately does not carry, which no refine "
            + "and no mechanical repair can supply");
    }

    // ── The guarantee this detector exists for must survive ──

    [Fact]
    public void A_road_named_with_none_of_its_suburbs_is_still_flagged()
    {
        const string unplaced =
            "Northbound on the Bruce Highway at Foster Road, roadworks add about a minute.";

        var result = SuburbRetentionDetector.Detect(
            unplaced,
            Ctx(Spot("Bruce Highway", "Edmonton"), Spot("Bruce Highway", "Gordonvale")));

        result.HasMissingSuburb.ShouldBeTrue(
            "a road the listener cannot place is the whole reason this gate exists");
    }

    [Fact]
    public void The_single_spot_case_is_unchanged()
    {
        SuburbRetentionDetector.Detect("The M4 is slow at Church Street.", Ctx(Spot("M4", "Parramatta")))
            .HasMissingSuburb.ShouldBeTrue("one corridor, one suburb, dropped — still a defect");

        SuburbRetentionDetector.Detect("The M4 near Parramatta is slow.", Ctx(Spot("M4", "Parramatta")))
            .HasMissingSuburb.ShouldBeFalse("one corridor, one suburb, retained");
    }

    [Fact]
    public void Each_road_is_judged_on_its_own_suburbs()
    {
        // Bruce Highway is placed; Captain Cook Highway is named with none of its suburbs.
        const string mixed =
            "Northbound on the Bruce Highway near Edmonton at Foster Road, roadworks add a minute. "
            + "There are more works on the Captain Cook Highway.";

        var result = SuburbRetentionDetector.Detect(
            mixed,
            Ctx(Spot("Bruce Highway", "Edmonton"),
                Spot("Bruce Highway", "Gordonvale"),
                Spot("Captain Cook Highway", "Aeroglen")));

        result.HasMissingSuburb.ShouldBeTrue();
        result.Feedback.ShouldContain("Captain Cook Highway");
        result.Feedback.ShouldNotContain("Bruce Highway", customMessage:
            "a placed road must not be reported alongside an unplaced one");
    }
}
