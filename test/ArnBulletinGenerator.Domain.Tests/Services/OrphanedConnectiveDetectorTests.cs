using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Guards the orphaned back-reference gate against the read ARN heard on dev job 54befeec
///     (2026-08-24, Cairns): a bulletin whose first words about the roads were "Roadworks are ALSO
///     slowing the Bruce Highway", because the Captain Cook Highway sentence it was adding to had
///     been dropped as unsupported after its refine failed.
/// </summary>
public sealed class OrphanedConnectiveDetectorTests
{
    private static BroadcastWithTraffic Ctx(params string[] roads) =>
        new()
        {
            TargetDurationSeconds = 22,
            JobId = "t",
            Location = "Cairns",
            Traffic = string.Empty,
            Incidents = [],
            FillOrder = [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler],
            Corridors = [.. roads.Select(r => new TrafficCorridor(r, 4, 2, 2000, 50, "roadworks", $"{r} roadworks", []))]
        };

    // The aired read, verbatim, minus the sentence the loop had already dropped.
    private const string AiredCairns =
        "Checking out the roads... Roadworks are also slowing the Bruce Highway at Edmonton and "
        + "Gordonvale. A planned event is affecting westbound Mulgrave Road in Earlville. "
        + "Partly cloudy, 28 degrees.";

    [Fact]
    public void Fires_when_the_first_road_claim_adds_to_nothing()
    {
        var result = OrphanedConnectiveDetector.Detect(AiredCairns, Ctx("Bruce Highway", "Mulgrave Road"));

        result.HasOrphanedConnective.ShouldBeTrue();
        result.Feedback.ShouldContain("ORPHANED BACK-REFERENCE");
        result.Feedback.ShouldContain("also");
        // The opener names no road, so the Bruce Highway sentence is the first claim: order 2.
        result.Issue!.Order.ShouldBe(2);
    }

    [Fact]
    public void Repairs_by_deleting_the_connective_and_nothing_else()
    {
        string repaired = OrphanedConnectiveDetector.Repair(AiredCairns, Ctx("Bruce Highway", "Mulgrave Road"));

        repaired.ShouldBe(
            "Checking out the roads... Roadworks are slowing the Bruce Highway at Edmonton and "
            + "Gordonvale. A planned event is affecting westbound Mulgrave Road in Earlville. "
            + "Partly cloudy, 28 degrees.");
    }

    [Fact]
    public void Silent_when_an_earlier_road_claim_gives_the_connective_something_to_add_to()
    {
        // This is the shape the draft ORIGINALLY had, and it is correct copy — the gate must not
        // touch it, or every legitimate additive connective in the corpus gets stripped.
        const string script =
            "Checking out the roads... Roadworks are causing delays on the Captain Cook Highway "
            + "through Aeroglen. Roadworks are also slowing the Bruce Highway at Edmonton.";

        OrphanedConnectiveDetector.Detect(script, Ctx("Captain Cook Highway", "Bruce Highway"))
            .HasOrphanedConnective.ShouldBeFalse();
        OrphanedConnectiveDetector.Repair(script, Ctx("Captain Cook Highway", "Bruce Highway"))
            .ShouldBe(script);
    }

    [Fact]
    public void Weather_and_fuel_ahead_of_the_claim_are_not_prior_items()
    {
        // A supplementary line is not something a road claim can "also" add to.
        const string script =
            "Partly cloudy, 28 degrees. Unleaded's a dollar ninety-eight at Manoora. "
            + "Roadworks are also slowing the Bruce Highway at Edmonton.";

        OrphanedConnectiveDetector.Detect(script, Ctx("Bruce Highway"))
            .HasOrphanedConnective.ShouldBeTrue();
        OrphanedConnectiveDetector.Repair(script, Ctx("Bruce Highway"))
            .ShouldBe("Partly cloudy, 28 degrees. Unleaded's a dollar ninety-eight at Manoora. "
                + "Roadworks are slowing the Bruce Highway at Edmonton.");
    }

    [Theory]
    [InlineData("Meanwhile, the Bruce Highway is slow.", "The Bruce Highway is slow.")]
    [InlineData("Elsewhere, the Bruce Highway is slow.", "The Bruce Highway is slow.")]
    [InlineData("The Bruce Highway is slow as well.", "The Bruce Highway is slow.")]
    public void Handles_the_other_additive_shapes(string claim, string expected)
    {
        var ctx = Ctx("Bruce Highway");

        OrphanedConnectiveDetector.Detect(claim, ctx).HasOrphanedConnective.ShouldBeTrue();
        OrphanedConnectiveDetector.Repair(claim, ctx).ShouldBe(expected);
    }

    [Fact]
    public void Silent_on_a_read_that_names_no_known_road()
    {
        // Nothing to anchor on: an all-clear or filler-only read is left entirely alone rather than
        // guessed at.
        const string script = "Checking out the roads... It's also worth allowing extra time.";

        OrphanedConnectiveDetector.Detect(script, Ctx("Bruce Highway"))
            .HasOrphanedConnective.ShouldBeFalse();
        OrphanedConnectiveDetector.Repair(script, Ctx("Bruce Highway")).ShouldBe(script);
    }
}
