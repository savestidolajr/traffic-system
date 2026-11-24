using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CompassDirectionTests
{
    [Fact]
    public void Bearing_DueNorth_ReturnsZero()
    {
        double bearing = CompassDirection.Bearing(-37.9, 144.97, -37.8, 144.97);
        bearing.ShouldBe(0.0, 0.5);
    }

    [Fact]
    public void Bearing_DueEast_ReturnsNinety()
    {
        double bearing = CompassDirection.Bearing(-37.8, 144.9, -37.8, 145.0);
        bearing.ShouldBe(90.0, 1.0);
    }

    [Fact]
    public void ResolveCompass_EastwardShape_ReturnsEastbound()
    {
        var result = CompassDirection.ResolveCompass((-37.7, 144.9), (-37.7, 145.05));

        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe("eastbound");
    }

    [Fact]
    public void ResolveCompass_ReversedShape_ReturnsTheOppositeOctant()
    {
        // The direction is a property of the SHAPE's own order, not of any coverage centre: resolving
        // it against the fetching station's centre made one 457 m record air citybound 15 times and
        // outbound once from byte-identical input.
        var forward = CompassDirection.ResolveCompass((-37.9, 144.66), (-37.86, 144.81));
        var reversed = CompassDirection.ResolveCompass((-37.86, 144.81), (-37.9, 144.66));

        forward.Value.ShouldBe("eastbound");
        reversed.Value.ShouldBe("westbound");
    }

    [Fact]
    public void ResolveCompass_NeverReturnsCityboundOrOutbound()
    {
        // Citybound/outbound needs a market CBD reference point, and no such point exists in
        // configuration; it must not be re-derived from the coverage area.
        string[] octants = ["northbound", "southbound", "eastbound", "westbound"];
        foreach ((double lat, double lng) in new[] { (-37.7, 145.05), (-37.95, 144.6), (-37.6, 144.9) })
        {
            var result = CompassDirection.ResolveCompass((-37.8176, 144.97), (lat, lng));
            result.HasValue.ShouldBeTrue();
            octants.ShouldContain(result.Value);
        }
    }

    [Fact]
    public void ResolveCompass_DegenerateShape_ReturnsNone()
    {
        var result = CompassDirection.ResolveCompass((-37.8, 144.9), (-37.8, 144.9));

        result.IsNone.ShouldBeTrue();
    }
}
