using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests;

/// <summary>Deterministic tests for inline audio-tag stripping and the word-immutability check.</summary>
public sealed class DeliveryTagsTests
{
    [Fact]
    public void Strip_RemovesTags_AndCollapsesWhitespace()
    {
        DeliveryTags.Strip("[urgent] The Ring Road is the WORST of it. [calm] Drive safe.")
            .ShouldBe("The Ring Road is the WORST of it. Drive safe.");
    }

    [Fact]
    public void Strip_PlainText_Unchanged()
    {
        const string plain = "The M80 is backed up to the ramp.";
        DeliveryTags.Strip(plain).ShouldBe(plain);
    }

    [Fact]
    public void HasTags_DetectsPresence()
    {
        DeliveryTags.HasTags("[urgent] Go now").ShouldBeTrue();
        DeliveryTags.HasTags("No tags here at all").ShouldBeFalse();
        DeliveryTags.HasTags("array index [3] is not a tag-shaped token but is bracketed").ShouldBeTrue();
    }

    [Fact]
    public void Extract_ReturnsInnerLabels_InOrder()
    {
        DeliveryTags.Extract("[urgent] one [strong Australian accent] two [sighs] three")
            .ShouldBe(new[] { "urgent", "strong Australian accent", "sighs" });
    }

    [Fact]
    public void WordsPreserved_TrueForTagsCapsAndPunctuation()
    {
        const string plain = "The Ring Road is the worst of it, with roadworks and a lane blocked.";
        const string tagged = "[urgent] The Ring Road is the WORST of it — with roadworks and a lane BLOCKED!";
        DeliveryTags.WordsPreserved(plain, tagged).ShouldBeTrue();
    }

    [Fact]
    public void WordsPreserved_FalseWhenWordChanged()
    {
        const string plain = "The Ring Road is the worst of it.";
        const string tagged = "[urgent] The Ring Road is the WORSTEST of it.";
        DeliveryTags.WordsPreserved(plain, tagged).ShouldBeFalse();
    }

    [Fact]
    public void WordsPreserved_FalseWhenWordAdded()
    {
        const string plain = "The Ring Road is backed up.";
        const string tagged = "The Ring Road is really backed up.";
        DeliveryTags.WordsPreserved(plain, tagged).ShouldBeFalse();
    }

    [Fact]
    public void WordsPreserved_PreservesNumbersAndRoadCodes()
    {
        const string plain = "Delays of around 7 min near the M80.";
        const string tagged = "[serious] Delays of around 7 min near the M80!";
        DeliveryTags.WordsPreserved(plain, tagged).ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // Which models interpret tags. This gate is safety-relevant: a tag that reaches
    // a non-v3 voice is READ ALOUD to air. Before 2026-08-03 a non-v3 model skipped
    // the director entirely, which made a station's delivery style a silent no-op on
    // every professional (multilingual_v2) voice - roughly half the dev network. The
    // director now still runs there, with an empty palette, so punctuation and CAPS
    // direction survives while tags cannot leak.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("eleven_v3")]
    [InlineData("eleven_V3")]
    public void SupportsTags_TrueForV3Family(string model) =>
        DeliveryTags.SupportsTags(model).ShouldBeTrue();

    [Theory]
    [InlineData("eleven_multilingual_v2")]
    [InlineData("eleven_turbo_v2_5")]
    [InlineData("eleven_english_sts_v2")]
    [InlineData("")]
    [InlineData(null)]
    public void SupportsTags_FalseForEverythingElse(string? model) =>
        DeliveryTags.SupportsTags(model).ShouldBeFalse();

    [Fact]
    public void SupportsTags_UnknownModelDefaultsToNoTags() =>
        // An unrecognised model must fail safe: silence beats speaking "[quietly]" on air.
        DeliveryTags.SupportsTags("some_future_model").ShouldBeFalse();

    [Theory]
    [InlineData("eleven_multilingual_v2")]
    [InlineData("eleven_turbo_v2_5")]
    [InlineData("")]
    [InlineData(null)]
    public void ForModel_StripsTags_WhenModelWouldSpeakThem(string? model) =>
        DeliveryTags.ForModel("[excitedly] The M1 is closed.", model)
            .ShouldBe("The M1 is closed.");

    [Fact]
    public void ForModel_LeavesTagsIntact_OnV3() =>
        DeliveryTags.ForModel("[excitedly] The M1 is closed.", "eleven_v3")
            .ShouldBe("[excitedly] The M1 is closed.");

    [Fact]
    public void ForModel_ReturnsTheSameInstance_WhenThereIsNoTagToStrip()
    {
        // On the synthesis path this runs for every bulletin on every model, so the
        // overwhelmingly common untagged case must not allocate a second string.
        const string plain = "The M1 is closed.";

        DeliveryTags.ForModel(plain, "eleven_multilingual_v2").ShouldBeSameAs(plain);
    }

    [Fact]
    public void Strip_RemovesTagsWithoutTouchingSpokenWords()
    {
        // The non-v3 path strips a stray tag then checks word-immutability. The strip must
        // therefore leave the spoken words byte-identical, or a removed tag would look like
        // an altered word and the whole direction would be discarded.
        const string plain = "The Monash Freeway is CLOSED citybound... take Burke Road instead.";
        var tagged = "[quietly] The Monash Freeway is CLOSED citybound... [gently] take Burke Road instead.";
        var stripped = DeliveryTags.Strip(tagged);

        DeliveryTags.HasTags(stripped).ShouldBeFalse();
        DeliveryTags.WordsPreserved(plain, stripped).ShouldBeTrue();
        stripped.ShouldContain("CLOSED");
        stripped.ShouldContain("...");
    }
}
