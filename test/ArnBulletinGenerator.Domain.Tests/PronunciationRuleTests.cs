using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests;

public sealed class PronunciationRuleTests
{
    [Fact]
    public void CreateAlias_Trims_And_DefaultsCaseInsensitiveWordBounded()
    {
        var rule = PronunciationRule.CreateAlias("  Warringah  ", "  Warringer  ");

        rule.IsSuccess.ShouldBeTrue();
        rule.Value.Type.ShouldBe(PronunciationRuleType.Alias);
        rule.Value.StringToReplace.ShouldBe("Warringah");
        rule.Value.Replacement.ShouldBe("Warringer");
        rule.Value.Alphabet.ShouldBeNull();
        rule.Value.CaseSensitive.ShouldBeFalse();
        rule.Value.WordBoundaries.ShouldBeTrue();
        rule.Value.AppliesOnAllModels.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "Warringer")]
    [InlineData("   ", "Warringer")]
    [InlineData("Warringah", "")]
    public void CreateAlias_RejectsEmptyFields(string word, string alias)
    {
        PronunciationRule.CreateAlias(word, alias).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CreatePhoneme_NormalizesAlphabet_AndIsV2Only()
    {
        var rule = PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "IPA");

        rule.IsSuccess.ShouldBeTrue();
        rule.Value.Type.ShouldBe(PronunciationRuleType.Phoneme);
        rule.Value.Alphabet.ShouldBe("ipa");
        rule.Value.AppliesOnAllModels.ShouldBeFalse();
    }

    [Theory]
    [InlineData("klingon")]
    [InlineData("")]
    public void CreatePhoneme_RejectsBadAlphabet(string alphabet)
    {
        PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", alphabet).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CreatePhoneme_RejectsEmptyPhoneme()
    {
        PronunciationRule.CreatePhoneme("Lachlan", "", "ipa").IsFailure.ShouldBeTrue();
    }

    // ── MatchesIn: the measured-match semantics the preview counts use (bug-bash 2026-08-05
    //    finding 14). Mirrors web/src/features/jobs/lib/pronunciation-matches.ts so the preview's
    //    "N rules applied" agrees with the frontend's script highlights and ElevenLabs' own
    //    word-boundary/case matching. ───────────────────────────────────────────────────────

    [Fact]
    public void MatchesIn_WordBoundary_RequiresWholeWord()
    {
        var rule = PronunciationRule.CreateAlias("Warringah", "Warringa").Value;

        rule.MatchesIn("Expect delays on Warringah Road.").ShouldBeTrue();
        rule.MatchesIn("Warringah is closed.").ShouldBeTrue();
        // Inside a longer word is NOT a match — the word must stand alone.
        rule.MatchesIn("Warringahville is closed.").ShouldBeFalse();
        rule.MatchesIn("The Warringah-Road junction.").ShouldBeTrue();
    }

    [Fact]
    public void MatchesIn_WordBoundary_TreatsUnderscoreAndDigitsAsWordChars()
    {
        // The boundary is "not a letter/digit/underscore" on either side, matching the JS
        // reference matcher's [\p{L}\p{N}_] class.
        var rule = PronunciationRule.CreateAlias("M1", "Em-one").Value;

        rule.MatchesIn("Slow on the M1 freeway.").ShouldBeTrue();
        rule.MatchesIn("M1 is stop-start.").ShouldBeTrue();
        rule.MatchesIn("M11 is slow.").ShouldBeFalse();
        rule.MatchesIn("the_M1_exit").ShouldBeFalse();
    }

    [Fact]
    public void MatchesIn_Default_CaseInsensitive()
    {
        var rule = PronunciationRule.CreateAlias("Coomera", "Koomera").Value;

        rule.MatchesIn("flows through coomera").ShouldBeTrue();
        rule.MatchesIn("flows through Coomera").ShouldBeTrue();
        rule.MatchesIn("flows through COOMERA").ShouldBeTrue();
    }

    [Fact]
    public void MatchesIn_CaseSensitive_RespectsTheFlag()
    {
        var rule = PronunciationRule.CreateAlias("Coomera", "Koomera", caseSensitive: true).Value;

        rule.MatchesIn("flows through Coomera").ShouldBeTrue();
        rule.MatchesIn("flows through coomera").ShouldBeFalse();
    }

    [Fact]
    public void MatchesIn_WordBoundariesFalse_MatchesInsideWords()
    {
        var rule = PronunciationRule.CreateAlias("St", "Saint", wordBoundaries: false).Value;

        rule.MatchesIn("St Kilda is busy.").ShouldBeTrue();
        rule.MatchesIn("inside Stree").ShouldBeTrue(); // no boundary requirement
    }

    [Fact]
    public void MatchesIn_PhraseRule_MatchesTheWholePhrase()
    {
        var rule = PronunciationRule.CreateAlias("St Kilda", "Saint Kilda").Value;

        rule.MatchesIn("Expect delays around St Kilda.").ShouldBeTrue();
        rule.MatchesIn("Expect delays around St Kildavale.").ShouldBeFalse();
    }

    // ── AppliesOn: which model a rule's TYPE takes effect on (finding 13). ────────────────

    [Theory]
    [InlineData("eleven_v3")]
    [InlineData("eleven_multilingual_v2")]
    [InlineData("eleven_turbo_v2")]
    [InlineData("eleven_flash_v2_5")]
    [InlineData("some-future-model")]
    public void AppliesOn_AliasRules_ApplyOnEveryModel(string model)
    {
        PronunciationRule.CreateAlias("Warringah", "Warringa").Value.AppliesOn(model).ShouldBeTrue();
    }

    [Fact]
    public void AppliesOn_PhonemeRules_OnlyOnTheTurboFlashV2Family()
    {
        var phoneme = PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value;

        phoneme.AppliesOn("eleven_turbo_v2").ShouldBeTrue();
        phoneme.AppliesOn("eleven_flash_v2_5").ShouldBeTrue();
        // v3 skips phoneme rules (benign); multilingual_v2 would DELETE matched words.
        phoneme.AppliesOn("eleven_v3").ShouldBeFalse();
        phoneme.AppliesOn("eleven_multilingual_v2").ShouldBeFalse();
    }

    [Fact]
    public void Signature_DistinguishesTypeAndContent()
    {
        var alias = PronunciationRule.CreateAlias("M1", "Monash").Value;
        var phoneme = PronunciationRule.CreatePhoneme("M1", "ˈmoʊnæʃ", "ipa").Value;

        alias.Signature.ShouldNotBe(phoneme.Signature);
        alias.Signature.ShouldBe(PronunciationRule.CreateAlias("M1", "Monash").Value.Signature);
    }

    // These rules ship to ElevenLabs on EVERY synthesis, and the replacement is read
    // aloud verbatim wherever the word appears — an unbounded one blows the slot. The
    // review cockpit made rule creation a one-click operator action, so the cap has to
    // live at the domain choke point, not just in the studio form (dev, 2026-07-28).

    [Fact]
    public void CreateAlias_OverlongReplacement_Rejected()
    {
        var result = PronunciationRule.CreateAlias(
            "Keilor", new string('x', PronunciationRule.MaxReplacementLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("replacement");
    }

    [Fact]
    public void CreateAlias_ReplacementAtTheLimit_Accepted()
    {
        PronunciationRule.CreateAlias(
                "Keilor", new string('x', PronunciationRule.MaxReplacementLength))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CreateAlias_OverlongWord_Rejected()
    {
        var result = PronunciationRule.CreateAlias(
            new string('x', PronunciationRule.MaxWordLength + 1), "Kee-lor");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("word");
    }

    [Fact]
    public void CreatePhoneme_OverlongPhoneme_Rejected()
    {
        PronunciationRule.CreatePhoneme(
                "Keilor", new string('ə', PronunciationRule.MaxReplacementLength + 1), "ipa")
            .IsFailure.ShouldBeTrue();
    }
}
