using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CorridorMagnitudeTests
{
    private static TrafficCorridor Corridor(
        string type = "congestion",
        int functionalClass = 1,
        double lengthMeters = 0,
        int? delayMinutes = null,
        int severity = 1) =>
        new(
            "Test Road",
            severity,
            functionalClass,
            lengthMeters,
            ImpactScore: 50.0,
            type,
            Description: "test",
            Incidents: [],
            DriveTimeMinutes: delayMinutes is null ? default : Option<int>.Some(20 + delayMinutes.Value),
            NormalDriveTimeMinutes: delayMinutes is null ? default : Option<int>.Some(20));

    // ── Measured delay wins, and maps to the FormatImpact-aligned bands ──

    [Theory]
    [InlineData(45, MagnitudeTier.Severe)]
    [InlineData(31, MagnitudeTier.Severe)]
    [InlineData(30, MagnitudeTier.Major)]
    [InlineData(16, MagnitudeTier.Major)]
    [InlineData(15, MagnitudeTier.Moderate)]
    [InlineData(6, MagnitudeTier.Moderate)]
    [InlineData(5, MagnitudeTier.Minor)]
    [InlineData(1, MagnitudeTier.Minor)]
    public void Classify_UsesDelayBands_WhenDelayPresent(int delay, MagnitudeTier expected)
    {
        // congestion on a freeway has only a Minor type floor, so the delay band decides.
        CorridorMagnitude.Classify(Corridor("congestion", delayMinutes: delay)).ShouldBe(expected);
    }

    // ── No measured delay: a real queue promotes, a works footprint does not ──

    [Theory]
    [InlineData(900, MagnitudeTier.Minor)]   // below the queue threshold
    [InlineData(2000, MagnitudeTier.Moderate)]
    [InlineData(3500, MagnitudeTier.Major)]
    public void Classify_UsesQueueLength_WhenNoDelay(double length, MagnitudeTier expected)
    {
        CorridorMagnitude.Classify(Corridor("congestion", lengthMeters: length)).ShouldBe(expected);
    }

    [Fact]
    public void Classify_StandingFootprintLength_NeverPromotes()
    {
        // A 5km roadworks footprint with no delay is an extent, not a queue — stays Minor.
        CorridorMagnitude.Classify(Corridor("construction", lengthMeters: 5000))
            .ShouldBe(MagnitudeTier.Minor);
    }

    // ── Type floor keeps blocking events prominent before routing ──

    [Fact]
    public void Classify_AccidentOnFreeway_IsMajor_WithNoDelayOrQueue()
    {
        CorridorMagnitude.Classify(Corridor("accident", functionalClass: 1)).ShouldBe(MagnitudeTier.Major);
    }

    [Fact]
    public void Classify_AccidentOnArterial_FallsToMinor_WithNoMagnitude()
    {
        // The differentiation win: a freeway breakdown/crash with no measured impact no longer
        // floors at "severity 1". An accident on an arterial (FC3) carries no type floor.
        CorridorMagnitude.Classify(Corridor("accident", functionalClass: 3)).ShouldBe(MagnitudeTier.Minor);
    }

    [Fact]
    public void Classify_BreakdownOnFreeway_IsMinor_WithNoMagnitude()
    {
        CorridorMagnitude.Classify(Corridor("disabledVehicle", functionalClass: 1)).ShouldBe(MagnitudeTier.Minor);
    }

    [Theory]
    [InlineData(3, MagnitudeTier.Major)]   // closure on a major/arterial road floors at Major
    [InlineData(4, MagnitudeTier.Minor)]   // local-road closure does not
    public void Classify_RoadClosure_FloorsByRoadClass(int functionalClass, MagnitudeTier expected)
    {
        CorridorMagnitude.Classify(Corridor("roadClosure", functionalClass)).ShouldBe(expected);
    }

    // ── A measured delay/queue can only promote the floor, never demote it ──

    [Fact]
    public void Classify_BlockingFloor_NotDemotedBySmallDelay()
    {
        // Crash on a freeway with a currently-small routed delay stays Major (it just happened).
        CorridorMagnitude.Classify(Corridor("accident", functionalClass: 1, delayMinutes: 3))
            .ShouldBe(MagnitudeTier.Major);
    }

    [Fact]
    public void Classify_LowFloor_PromotedByLargeDelay()
    {
        CorridorMagnitude.Classify(Corridor("congestion", functionalClass: 1, delayMinutes: 40))
            .ShouldBe(MagnitudeTier.Severe);
    }

    [Fact]
    public void Classify_StandingFootprint_PromotedByDelay_NotLength()
    {
        // Roadworks length never promotes, but a real delay it causes does.
        CorridorMagnitude.Classify(Corridor("construction", lengthMeters: 5000, delayMinutes: 20))
            .ShouldBe(MagnitudeTier.Major);
    }
}
