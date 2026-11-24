using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class SpokenPriceNormalizerTests
{
    [Theory]
    // The two forms observed live from the same market on the same config, plus the shipped
    // defect from job 5dfe174e ("$1.79 in Chermside West").
    [InlineData("$1.79", "a dollar seventy-nine")]
    [InlineData("$1.78", "a dollar seventy-eight")]
    [InlineData("$1.80", "a dollar eighty")]
    [InlineData("$2.19", "two dollars nineteen")]
    [InlineData("$2.04", "two dollars four")]
    [InlineData("$2.13", "two dollars thirteen")]
    public void Normalize_CurrencyNumeral_RewritesToSpokenForm(string numeral, string spoken)
    {
        SpokenPriceNormalizer.Normalize($"Unleaded's cheapest at {numeral} in Chermside West.")
            .ShouldBe($"Unleaded's cheapest at {spoken} in Chermside West.");
    }

    [Fact]
    public void Normalize_WholeDollars_OmitsCents()
    {
        SpokenPriceNormalizer.Normalize("Diesel is $2 flat.").ShouldBe("Diesel is two dollars flat.");
        SpokenPriceNormalizer.Normalize("Diesel is $2.00 flat.").ShouldBe("Diesel is two dollars flat.");
    }

    [Fact]
    public void Normalize_SingleCentsDigit_ReadsAsTens()
    {
        // "$1.8" is one dollar eighty, never one dollar eight.
        SpokenPriceNormalizer.Normalize("$1.8").ShouldBe("a dollar eighty");
    }

    [Fact]
    public void Normalize_MultiplePrices_RewritesEveryOne()
    {
        SpokenPriceNormalizer.Normalize("Unleaded 91 is cheapest at $1.78 in Alexandria, and diesel's $2.19 in Petersham.")
            .ShouldBe("Unleaded 91 is cheapest at a dollar seventy-eight in Alexandria, "
                      + "and diesel's two dollars nineteen in Petersham.");
    }

    [Theory]
    [InlineData("Unleaded 91 is 196 cents.", "Unleaded 91 is a dollar ninety-six.")]
    [InlineData("Unleaded 91 is 196 cents a litre.", "Unleaded 91 is a dollar ninety-six a litre.")]
    [InlineData("Unleaded 91 is 235.", "Unleaded 91 is two dollars thirty-five.")]
    [InlineData("Unleaded 91 is 1.96.", "Unleaded 91 is a dollar ninety-six.")]
    [InlineData("Unleaded 91 is $1.96.", "Unleaded 91 is a dollar ninety-six.")]
    public void Normalize_FuelPriceVariants_UseTheSameSpokenForm(string script, string spoken)
    {
        SpokenPriceNormalizer.Normalize(script).ShouldBe(spoken);
    }

    [Fact]
    public void Normalize_ExplicitCentsUnit_IsSufficientFuelContext()
    {
        SpokenPriceNormalizer.Normalize("196 cents").ShouldBe("a dollar ninety-six");
    }

    [Theory]
    [InlineData("16 degrees")]
    [InlineData("adding 6 minutes")]
    [InlineData("M4")]
    [InlineData("Westlink M7")]
    [InlineData("102.7")]
    [InlineData("KIIS 90s")]
    [InlineData("The year is 2026")]
    public void Normalize_NonFuelNumerals_RemainUntouched(string script)
    {
        SpokenPriceNormalizer.Normalize(script).ShouldBe(script);
    }

    [Fact]
    public void Normalize_ThreeDecimalWholesaleFigure_LeftAlone()
    {
        // Not a price we voice; rounding here would invent a number the data did not supply.
        const string script = "The wholesale rate sits at $1.779 a litre.";

        SpokenPriceNormalizer.Normalize(script).ShouldBe(script);
    }

    [Theory]
    // Only the currency form is touched — everything else a bulletin counts stays as written.
    [InlineData("It's cloudy, 15 degrees.")]
    [InlineData("Works affecting about four kays at the Bulleen Road ramp.")]
    [InlineData("The M4, Parramatta to the city, about 22 minutes.")]
    [InlineData("Traffic is flowing well on the M1 this morning.")]
    public void Normalize_NonCurrencyNumbers_ReturnedUnchanged(string script)
    {
        SpokenPriceNormalizer.Normalize(script).ShouldBe(script);
    }

    [Fact]
    public void Normalize_AlreadySpoken_ReturnedUnchanged()
    {
        const string script = "Unleaded's cheapest at a dollar eighty in Williamstown.";

        SpokenPriceNormalizer.Normalize(script).ShouldBe(script);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnedUnchanged()
    {
        SpokenPriceNormalizer.Normalize(string.Empty).ShouldBe(string.Empty);
    }
}
