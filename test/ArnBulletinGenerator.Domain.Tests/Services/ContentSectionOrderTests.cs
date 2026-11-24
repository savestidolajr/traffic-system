using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class ContentSectionOrderTests
{
    [Fact]
    public void Resolve_honours_a_fuel_before_weather_fill_order()
    {
        var order = ContentSectionOrder.Resolve(
            [FillSource.Fuel, FillSource.Weather, FillSource.MinorTraffic, FillSource.Filler]);

        order.ShouldBe([FillSource.Fuel, FillSource.Weather]);
    }

    [Fact]
    public void Resolve_totalises_a_fill_order_missing_one_source()
    {
        // Weather absent from the list falls to the end in canonical weather-then-fuel order.
        var order = ContentSectionOrder.Resolve([FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]);

        order.ShouldBe([FillSource.Fuel, FillSource.Weather]);
    }

    [Fact]
    public void Resolve_returns_the_canonical_order_for_a_legacy_full_list()
    {
        var order = ContentSectionOrder.Resolve(
            [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]);

        order.ShouldBe([FillSource.Weather, FillSource.Fuel]);
    }

    [Fact]
    public void Resolve_deduplicates_a_fill_order_that_repeats_a_source()
    {
        var order = ContentSectionOrder.Resolve(
            [FillSource.Weather, FillSource.Weather, FillSource.Fuel]);

        order.ShouldBe([FillSource.Weather, FillSource.Fuel]);
    }

    [Fact]
    public void Resolve_returns_both_sections_for_an_empty_fill_order()
    {
        ContentSectionOrder.Resolve([]).ShouldBe([FillSource.Weather, FillSource.Fuel]);
    }

    [Fact]
    public void Describe_reads_weather_then_fuel()
    {
        ContentSectionOrder.Describe([FillSource.Weather, FillSource.Fuel]).ShouldBe("weather, then fuel");
    }

    [Fact]
    public void Describe_reads_fuel_then_weather()
    {
        ContentSectionOrder.Describe([FillSource.Fuel, FillSource.Weather]).ShouldBe("fuel, then weather");
    }

    [Fact]
    public void Describe_names_a_single_section()
    {
        ContentSectionOrder.Describe([FillSource.Weather]).ShouldBe("weather");
        ContentSectionOrder.Describe([FillSource.Fuel]).ShouldBe("fuel");
    }

    [Fact]
    public void Describe_is_empty_for_no_sections()
    {
        ContentSectionOrder.Describe([]).ShouldBe(string.Empty);
    }
}
