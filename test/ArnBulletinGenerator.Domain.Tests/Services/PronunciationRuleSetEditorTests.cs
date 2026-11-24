using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class PronunciationRuleSetEditorTests
{
    [Fact]
    public void WithAlias_NeverEmitsAPhonemeRule()
    {
        var result = PronunciationRuleSetEditor.WithAlias([], "Coomera", "Koomera");

        result.IsSuccess.ShouldBeTrue();
        // A phoneme rule is ignored by eleven_v3 and DROPS matched words on
        // eleven_multilingual_v2, so this path must never produce one.
        result.Value.Rules.ShouldAllBe(r => r.Type == PronunciationRuleType.Alias);
    }

    [Fact]
    public void WithAlias_PreservesAWholeRoadPhraseTarget()
    {
        var result = PronunciationRuleSetEditor.WithAlias(
            [],
            "Gillies Highway",
            "Gillies Range Road");

        result.IsSuccess.ShouldBeTrue();
        var rule = result.Value.Rules.ShouldHaveSingleItem();
        rule.StringToReplace.ShouldBe("Gillies Highway");
        rule.Replacement.ShouldBe("Gillies Range Road");
        rule.WordBoundaries.ShouldBeTrue();
    }

    [Fact]
    public void WithAlias_KeepsTheExistingRulesMatchFlagsAndSpelling()
    {
        var existing = new PronunciationRule(
            PronunciationRuleType.Alias, "Coomera", "Koomera", null,
            CaseSensitive: true, WordBoundaries: false);

        var result = PronunciationRuleSetEditor.WithAlias([existing], "coomera", "Koomerah");

        result.Value.Outcome.ShouldBe(AliasEditOutcome.Changed);
        var rule = result.Value.Rules.ShouldHaveSingleItem();
        rule.Replacement.ShouldBe("Koomerah");
        // The operator is correcting the SOUND, not re-deciding flags someone set deliberately,
        // and not re-spelling the matched word from however they happened to type it.
        rule.StringToReplace.ShouldBe("Coomera");
        rule.CaseSensitive.ShouldBeTrue();
        rule.WordBoundaries.ShouldBeFalse();
    }

    [Fact]
    public void WithAlias_LeavesOtherWordsUntouched()
    {
        var nerang = PronunciationRule.CreateAlias("Nerang", "Nerrang").Value;
        var coomera = PronunciationRule.CreateAlias("Coomera", "Koomera").Value;

        var result = PronunciationRuleSetEditor.WithAlias([nerang, coomera], "Coomera", "Koomerah");

        result.Value.Rules.Count.ShouldBe(2);
        result.Value.Rules[0].ShouldBe(nerang);
    }

    [Fact]
    public void WithAlias_PhonemeRuleForTheSameWordIsNotTreatedAsItsRule()
    {
        var phoneme = PronunciationRule.CreatePhoneme("Coomera", "kuːmərə", "ipa").Value;

        var result = PronunciationRuleSetEditor.WithAlias([phoneme], "Coomera", "Koomera");

        // The alias is ADDED beside it rather than overwriting a phoneme rule someone authored
        // for a v2 station — replacing it would silently change that station's read.
        result.Value.Outcome.ShouldBe(AliasEditOutcome.Added);
        result.Value.Rules.Count.ShouldBe(2);
    }

    [Fact]
    public void WithAlias_BlankReplacement_Fails()
    {
        PronunciationRuleSetEditor.WithAlias([], "Coomera", "   ").IsFailure.ShouldBeTrue();
    }
}
