using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public class PredictiveTrimReproductionTests
{
    [Fact]
    public void PredictiveTrim_WithBookends_PicksShorterVariants_AndAvoidsDroppingTrafficItems()
    {
        // 1. Arrange the exact sentences and variants from job 075ee7d1a25742b78113c474159331d8:
        var draftedSentences = new List<DraftedSentence>
        {
            new(
                1,
                "Pittwater Road's closed northbound in North Manly between Kentwell Road and Oliver Street for construction works.",
                ["corridor:Pittwater Road#1:detail"],
                [
                    new DraftedVariant("concise", "Pittwater Road's closed northbound in North Manly from Kentwell Road to Oliver Street for construction."),
                    new DraftedVariant("standard", "Pittwater Road's closed northbound in North Manly between Kentwell Road and Oliver Street for construction works.")
                ]),
            new(
                2,
                "Traffic on the M5's crawling through heavy congestion, with delays building to nineteen minutes.",
                ["corridor:M5#1:detail"],
                [
                    new DraftedVariant("concise", "The M5's chockers with heavy congestion."),
                    new DraftedVariant("standard", "Traffic on the M5's crawling through heavy congestion, with delays building to nineteen minutes.")
                ]),
            new(
                3,
                "The M4's slow-moving with heavy delays.",
                ["corridor:M4#1:detail"],
                [
                    new DraftedVariant("concise", "The M4's slow-moving with heavy delays."),
                    new DraftedVariant("standard", "The M4's slow-moving with heavy delays through the motorway.")
                ]),
            new(
                4,
                "The Western Distributor's heavy eastbound through Pyrmont.",
                ["corridor:Western Distributor#1:detail"],
                [
                    new DraftedVariant("concise", "The Western Distributor's heavy eastbound through Pyrmont."),
                    new DraftedVariant("standard", "The Western Distributor's heavy eastbound through Pyrmont between Pyrmont Bridge Road and the Rozelle Interchange.")
                ]),
            new(
                5,
                "Sunny conditions and 20 degrees.",
                ["weather:condition"],
                [
                    new DraftedVariant("concise", "Sunny, 20 degrees."),
                    new DraftedVariant("standard", "Sunny conditions and 20 degrees.")
                ])
        };

        var stationLines = new[]
        {
            "Now looking over Sydney.",
            "That's KIIS traffic and weather together, powered by"
        };

        string composedScript =
            "Now looking over Sydney. " +
            "Pittwater Road's closed northbound in North Manly between Kentwell Road and Oliver Street for construction works. " +
            "Traffic on the M5's crawling through heavy congestion, with delays building to nineteen minutes. " +
            "The M4's slow-moving with heavy delays. " +
            "The Western Distributor's heavy eastbound through Pyrmont. " +
            "Sunny conditions and 20 degrees. " +
            "That's KIIS traffic and weather together, powered by";

        int originalWords = WordCounter.Count(composedScript); // 60 words
        int durationCeiling = 50; // Ceiling allowing all-concise combination to fit
        // 2. Act: run SelectCeilingVariants with stationLines aware
        var variantFit = ScriptReviewLoop.SelectCeilingVariants(
            composedScript, draftedSentences, durationCeiling, stationLines);

        // 3. Assert
        variantFit.ShouldNotBeNull();
        string fittedScript = variantFit!.Value.Script;

        fittedScript.ShouldStartWith("Now looking over Sydney.");
        fittedScript.ShouldEndWith("That's KIIS traffic and weather together, powered by");
        // All traffic items survived because shorter variants were selected!
        fittedScript.ShouldContain("Pittwater Road's closed northbound in North Manly");
        fittedScript.ShouldContain("The M5's chockers with heavy congestion.");
        fittedScript.ShouldContain("The M4's slow-moving with heavy delays.");
        fittedScript.ShouldContain("The Western Distributor's heavy eastbound through Pyrmont.");
        fittedScript.ShouldContain("Sunny, 20 degrees.");
        WordCounter.Count(fittedScript).ShouldBeLessThanOrEqualTo(durationCeiling);
    }
}
