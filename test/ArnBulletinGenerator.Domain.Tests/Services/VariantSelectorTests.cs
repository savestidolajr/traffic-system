using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class VariantSelectorTests
{
    [Fact]
    public void DraftedSentence_RoundTripsVariants_AndEnumeratesPreferredFirst()
    {
        var original = new DraftedSentence(
            1,
            "The M1 is slow.",
            ["corridor:M1:incident:0"],
            [
                new DraftedVariant("standard", "The M1 is slow."),
                new DraftedVariant("concise", "The M1 is moving slowly."),
                new DraftedVariant("expanded", "Traffic is building on the M1.")
            ]);

        string json = JsonSerializer.Serialize(original);
        var parsed = JsonSerializer.Deserialize<DraftedSentence>(json);

        parsed.ShouldNotBeNull();
        parsed!.Text.ShouldBe("The M1 is slow.");
        parsed.Variants!.Select(v => v.Text).ShouldBe([
            "The M1 is slow.",
            "The M1 is moving slowly.",
            "Traffic is building on the M1."
        ]);
        parsed.EvidenceIds.ShouldBe(["corridor:M1:incident:0"]);
    }

    [Fact]
    public void DraftedVariantsShareTheCommittedEvidenceIds()
    {
        var sentence = new DraftedSentence(
            6,
            "The M1 is slow.",
            ["corridor:M1:incident:0"],
            [new DraftedVariant("concise", "The M1 is moving slowly."),
             new DraftedVariant("expanded", "Traffic is building on the M1.")]);

        foreach (DraftedVariant variant in sentence.Variants!)
        {
            variant.Text.ShouldNotBeNullOrWhiteSpace();
            sentence.EvidenceIds.ShouldBe(["corridor:M1:incident:0"]);
        }
    }

    [Fact]
    public void Select_UsesFirstPassingPhrasing_InModelOrder()
    {
        var sentence = new DraftedSentence(
            2,
            "It is sunny.",
            ["weather:condition"],
            [new DraftedVariant("concise", "The sun is out."),
             new DraftedVariant("expanded", "Skies are clear.")]);

        VariantSelection result = VariantSelector.Select(sentence,
            phrasing => phrasing is "The sun is out." or "Skies are clear.");

        result.Phrasing.ShouldBe("The sun is out.");
        result.VariantHelped.ShouldBeTrue();
        result.UsedAlternate.ShouldBeTrue();
        sentence.EvidenceIds.ShouldBe(["weather:condition"]);
    }

    [Fact]
    public void Select_FallsBackToPreferredText_WhenNoPhrasingPasses()
    {
        var sentence = new DraftedSentence(
            3,
            "Unleaded 91 is steady.",
            ["fuel:unleaded:price"],
            [new DraftedVariant("concise", "Fuel prices are unchanged."),
             new DraftedVariant("expanded", "The pump price is flat.")]);

        VariantSelection result = VariantSelector.Select(sentence, _ => false);

        result.Phrasing.ShouldBe("Unleaded 91 is steady.");
        result.VariantHelped.ShouldBeFalse();
        result.UsedAlternate.ShouldBeFalse();
    }
}
