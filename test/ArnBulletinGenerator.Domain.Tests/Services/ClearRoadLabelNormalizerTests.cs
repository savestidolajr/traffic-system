using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ClearRoadLabelNormalizerTests
{
    [Theory]
    [InlineData("The M1 and M2 are flowing well.", "The M1 and M2 are moving nicely.")]
    [InlineData("Traffic is moving well on the Bruce Highway.", "Traffic is looking good on the Bruce Highway.")]
    [InlineData("Flowing well across the city.", "Moving nicely across the city.")]          // sentence-initial case kept
    [InlineData("A's flowing well and B's moving well.", "A's moving nicely and B's looking good.")]
    public void Normalize_rewrites_banned_clear_labels(string input, string expected) =>
        ClearRoadLabelNormalizer.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("The M1 is a clear run.")]        // allowed clear phrasing untouched
    [InlineData("Traffic is moving slowly there.")] // "moving" without "well" untouched
    [InlineData("No dramas on the Monash.")]
    public void Normalize_leaves_valid_copy_untouched(string script) =>
        ClearRoadLabelNormalizer.Normalize(script).ShouldBe(script);
}
