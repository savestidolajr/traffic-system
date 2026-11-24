using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The cost-ordered reduction of an overrunning toned read. Every rung must preserve the
///     spoken words — that is the whole point of reducing instead of re-drafting — and must remove
///     the devices that actually cost time rather than the ones that merely look expensive.
/// </summary>
public class DeliveryTimeCostTests
{
    private const string Approved =
        "The Tullamarine is chockers between Melbourne Airport and Mickleham Road. " +
        "The Frankston Freeway is slow outbound between EastLink and Dandenong-Frankston.";

    /// <summary>The tone that shipped on dev job de45b38b and was thrown away wholesale.</summary>
    private const string Toned =
        "[excitedly] The Tullamarine is CHOCKERS between Melbourne Airport and Mickleham Road... " +
        "[exhales sharply] The Frankston Freeway is slow outbound between EastLink and Dandenong-Frankston.";

    // ── Pause punctuation, the expensive rung ──────────────────────────────────────────────────

    [Fact]
    public void WithoutPausePunctuation_RemovesEllipses_AndKeepsWordsAndTags()
    {
        var reduced = DeliveryTimeCost.WithoutPausePunctuation(Toned);

        reduced.ShouldNotContain("...");
        DeliveryTags.Extract(reduced).ShouldBe(["excitedly", "exhales sharply"]);
        DeliveryTags.WordsPreserved(Approved, reduced).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Slow outbound — allow ten minutes.")]
    [InlineData("Slow outbound – allow ten minutes.")]
    [InlineData("Slow outbound - allow ten minutes.")]
    public void WithoutPausePunctuation_RemovesSpacedDashPauses(string text)
    {
        var reduced = DeliveryTimeCost.WithoutPausePunctuation(text);

        reduced.ShouldBe("Slow outbound allow ten minutes.");
    }

    [Fact]
    public void WithoutPausePunctuation_LeavesHyphenatedNamesIntact()
    {
        // "Dandenong-Frankston" is a road name, not a pause. Splitting it would change a fact.
        var reduced = DeliveryTimeCost.WithoutPausePunctuation(Approved);

        reduced.ShouldContain("Dandenong-Frankston");
        DeliveryTags.WordsPreserved(Approved, reduced).ShouldBeTrue();
    }

    // ── Tags, the cheaper rung ─────────────────────────────────────────────────────────────────

    [Fact]
    public void WithoutTimeCostingTags_DropsBreathTags_AndKeepsMannerTags()
    {
        // The measured point: [excitedly] is time-NEGATIVE, so removing it buys nothing and only
        // costs the operator the register they asked for. Only the breath tag goes.
        var reduced = DeliveryTimeCost.WithoutTimeCostingTags(Toned);

        DeliveryTags.Extract(reduced).ShouldBe(["excitedly"]);
        DeliveryTags.WordsPreserved(Approved, reduced).ShouldBeTrue();
    }

    [Theory]
    [InlineData("sighs")]
    [InlineData("exhales")]
    [InlineData("exhales sharply")]
    [InlineData("clears throat")]
    [InlineData("frustrated sigh")]
    [InlineData("short pause")]
    [InlineData("long pause")]
    public void TimeCostingTags_CoversEverySoundInsertingTag(string label)
    {
        DeliveryTimeCost.TimeCostingTags.ShouldContain(label);
    }

    [Theory]
    [InlineData("excitedly")]
    [InlineData("brightly")]
    [InlineData("delighted")]
    [InlineData("whispering")]
    [InlineData("firmly")]
    [InlineData("quietly")]
    [InlineData("gently")]
    public void TimeCostingTags_ExcludesMannerTags_BecauseTheyAreFreeOrFaster(string label)
    {
        DeliveryTimeCost.TimeCostingTags.ShouldNotContain(label);
    }

    [Fact]
    public void WithoutTimeCostingTags_IsCaseInsensitive()
    {
        DeliveryTags.Extract(DeliveryTimeCost.WithoutTimeCostingTags("[SIGHS] One. [Brightly] Two."))
            .ShouldBe(["Brightly"]);
    }

    // ── Detection ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("The M1 is closed... take Burke Road.")]
    [InlineData("The M1 is closed — take Burke Road.")]
    [InlineData("[sighs] The M1 is closed.")]
    [InlineData("[short pause] The M1 is closed.")]
    public void HasCostlyDevices_DetectsWhatActuallyCostsTime(string text)
    {
        DeliveryTimeCost.HasCostlyDevices(text).ShouldBeTrue();
    }

    [Theory]
    [InlineData("The M1 is closed. Take Burke Road.")]
    [InlineData("[brightly] The M1 is CLEAR.")]
    [InlineData("Dandenong-Frankston is slow.")]
    [InlineData("")]
    [InlineData(null)]
    public void HasCostlyDevices_IgnoresFreeDevices(string? text)
    {
        DeliveryTimeCost.HasCostlyDevices(text).ShouldBeFalse();
    }

    // ── The reduction is monotonic ─────────────────────────────────────────────────────────────

    [Fact]
    public void EachRung_ShortensOrHolds_AndNeverChangesTheWords()
    {
        var rung1 = DeliveryTimeCost.WithoutPausePunctuation(Toned);
        var rung2 = DeliveryTimeCost.WithoutPausePunctuation(
            DeliveryTimeCost.WithoutTimeCostingTags(Toned));

        rung1.Length.ShouldBeLessThan(Toned.Length);
        rung2.Length.ShouldBeLessThan(rung1.Length);
        DeliveryTags.WordsPreserved(Approved, rung1).ShouldBeTrue();
        DeliveryTags.WordsPreserved(Approved, rung2).ShouldBeTrue();
    }
}
