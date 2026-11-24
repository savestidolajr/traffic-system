using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The three guards on a register rewrite. Each defends against something that actually
///     happened during the 2026-08-03 listening work: an artefact tag reaching air as a literal
///     sigh, a rewrite inflating 52% against a fixed slot, and facts vanishing in a reword.
/// </summary>
public class DeliveryRewriteValidatorTests
{
    // ── Banned tags ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("[laughs] The M1 is closed.", "laughs")]
    [InlineData("[snorts] The M1 is closed.", "snorts")]
    [InlineData("[gulps] The M1 is closed.", "gulps")]
    [InlineData("[crying] The M1 is closed.", "crying")]
    [InlineData("[happy gasp] The M1 is closed.", "happy gasp")]
    public void UndignifiedSounds_AreBanned_BecauseANewsreaderDoesNotDoThem(string script, string expected)
    {
        // Audible — measurement confirms they land — but wrong for a traffic desk. This is the
        // editorial line, deliberately separate from the audibility one below.
        DeliveryRewriteValidator.FindBannedTags(script).ShouldContain(expected);
    }

    [Theory]
    [InlineData("[sighs] The M1 is closed.")]
    [InlineData("[exhales] The M1 is closed.")]
    [InlineData("[exhales sharply] The M1 is closed.")]
    [InlineData("[clears throat] The M1 is closed.")]
    [InlineData("[frustrated sigh] The M1 is closed.")]
    public void BreathTags_AreAllowed_BecauseTheyAreTheOnlyTagsThatLand(string script)
    {
        // Reversed on 2026-08-05 measurement: sound-inserting tags are the ONLY reliably audible
        // ones, and an operator confirmed these four by ear. Banning them left the writer with
        // nothing that worked, so it returned untagged reprints of the approved script.
        DeliveryRewriteValidator.FindBannedTags(script).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[short pause] The M1 is closed.")]
    [InlineData("[long pause] The M1 is closed.")]
    public void SizedPauseTags_AreAllowed(string script)
    {
        DeliveryRewriteValidator.FindBannedTags(script).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[pause] The M1 is closed.")]
    [InlineData("[sigh] The M1 is closed.")]
    [InlineData("[sighing] The M1 is closed.")]
    public void InertSpellingsOfWorkingTags_AreBanned(string script)
    {
        // The vocabulary is an exact-string lookup: "sighs" fires and "sigh" does nothing,
        // "short pause" fires and bare "pause" does nothing. A silent no-op must not ship.
        DeliveryRewriteValidator.FindBannedTags(script).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("[sad] The M1 is closed.")]
    [InlineData("[solemn] The M1 is closed.")]
    [InlineData("[professional] The M1 is closed.")]
    [InlineData("[urgently] The M1 is closed.")]
    [InlineData("[warmly] The M1 is closed.")]
    [InlineData("[calm] The M1 is closed.")]
    [InlineData("[emphatic] The M1 is closed.")]
    public void MoodLabels_AreBanned_BecauseThisVoiceIgnoresThem(string script)
    {
        // Not a safety rule - a truthfulness one. Offering a control that does nothing is worse
        // than offering none, because the operator believes the read was directed.
        DeliveryRewriteValidator.FindBannedTags(script).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("[quietly] The M1 is closed.")]
    [InlineData("[gently] Take Burke Road instead.")]
    [InlineData("[brightly] Everything is flowing.")]
    [InlineData("[excitedly] Everything is flowing.")]
    [InlineData("[delighted] Everything is flowing.")]
    [InlineData("[whispering] Avoid the area.")]
    [InlineData("[firmly] Avoid the area.")]
    public void MannerTags_AreAllowed(string script)
    {
        DeliveryRewriteValidator.FindBannedTags(script).ShouldBeEmpty();
    }

    [Fact]
    public void UntaggedScript_HasNoBannedTags()
    {
        DeliveryRewriteValidator.FindBannedTags("The M1 is closed at Beenleigh.").ShouldBeEmpty();
        DeliveryRewriteValidator.FindBannedTags(null).ShouldBeEmpty();
        DeliveryRewriteValidator.FindBannedTags("   ").ShouldBeEmpty();
    }

    [Fact]
    public void BannedTagDetection_IsCaseInsensitive_AndDeduplicated()
    {
        var found = DeliveryRewriteValidator.FindBannedTags("[LAUGHS] one. [laughs] two. [Pause] three.");
        found.Count.ShouldBe(2);
    }

    // ── Slot budget ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShorterOrEqualRewrite_FitsTheSlot()
    {
        DeliveryRewriteValidator.ExceedsSlot(100, 100).ShouldBeFalse();
        DeliveryRewriteValidator.ExceedsSlot(100, 80).ShouldBeFalse();
    }

    [Fact]
    public void GrowthWithinFifteenPercent_FitsTheSlot()
    {
        // A framing line and a sign-off cost words; a little headroom is what makes them possible.
        DeliveryRewriteValidator.ExceedsSlot(100, 115).ShouldBeFalse();
    }

    [Fact]
    public void GrowthBeyondFifteenPercent_DoesNotFit()
    {
        // The first version of the writer inflated a 31-word bulletin to 47 (+52%). Against a fixed
        // slot that makes the pipeline speed the read up or drop a road from the end.
        DeliveryRewriteValidator.ExceedsSlot(100, 116).ShouldBeTrue();
        DeliveryRewriteValidator.ExceedsSlot(31, 47).ShouldBeTrue();
    }

    [Fact]
    public void EmptyApprovedScript_CannotExceedTheSlot()
    {
        DeliveryRewriteValidator.ExceedsSlot(0, 50).ShouldBeFalse();
    }

    // ── Facts ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RewordingAroundAFact_KeepsIt()
    {
        const string before = "The Monash Freeway is closed citybound at Toorak Road. Allow 20 minutes.";
        const string after = "[quietly] Sad news. The Monash Freeway is CLOSED citybound at Toorak Road... allow 20 minutes.";
        DeliveryRewriteValidator.FindMissingFacts(before, after).ShouldBeEmpty();
    }

    [Fact]
    public void DroppingARoad_IsReported()
    {
        const string before = "The Monash Freeway is closed. Bell Street is also blocked.";
        const string after = "The Monash Freeway is closed.";
        DeliveryRewriteValidator.FindMissingFacts(before, after).ShouldContain("Bell Street");
    }

    [Fact]
    public void DroppingANumber_IsReported()
    {
        const string before = "Allow an extra 15 minutes through there.";
        const string after = "Allow extra time through there.";
        DeliveryRewriteValidator.FindMissingFacts(before, after).ShouldContain("15");
    }

    [Fact]
    public void AlteringARoadName_IsReported()
    {
        // The dangerous case: plausible-looking but wrong. A listener acts on this.
        const string before = "Nicholson Street is the detour.";
        const string after = "Nicholas Street is the detour.";
        DeliveryRewriteValidator.FindMissingFacts(before, after).ShouldContain("Nicholson Street");
    }

    [Fact]
    public void FactCheck_IgnoresCaseChanges_BecauseCapsAreADeliveryDevice()
    {
        // One or two capitalised words is how the writer marks emphasis; that must not read as a
        // lost fact.
        const string before = "The Monash Freeway is closed.";
        const string after = "The MONASH FREEWAY is closed.";
        DeliveryRewriteValidator.FindMissingFacts(before, after).ShouldBeEmpty();
    }

    [Fact]
    public void NoFactsToLose_ReturnsEmpty()
    {
        DeliveryRewriteValidator.FindMissingFacts("Everything is flowing well.", "[brightly] Everything is FLOWING well.")
            .ShouldBeEmpty();
        DeliveryRewriteValidator.FindMissingFacts(null, "anything").ShouldBeEmpty();
    }
}
