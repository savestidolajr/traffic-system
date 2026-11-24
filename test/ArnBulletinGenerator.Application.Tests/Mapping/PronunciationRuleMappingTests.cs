using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Mapping;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

/// <summary>
///     Rule-set invariants enforced at the single choke point every write goes through
///     (studio editor, review-cockpit quick-fix, config import). Both guards exist because
///     an operator hit them on dev (2026-07-28): correcting "Toorak" from the review cockpit
///     appended a rule each time, leaving three — only one of which the provider applies, so
///     each "fix" appeared to do nothing and prompted another.
/// </summary>
public sealed class PronunciationRuleMappingTests
{
    private static PronunciationRuleDto Alias(string word, string replacement) =>
        new("alias", word, replacement);

    [Fact]
    public void ParseRules_TwoAliasRulesForOneWord_Rejected()
    {
        var result = PronunciationRuleMapping.ParseRules([
            Alias("Toorak", "Toor-rak"),
            Alias("Toorak", "Tworack")
        ]);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Toorak");
        result.Error.ShouldContain("Toor-rak");
        result.Error.ShouldContain("Tworack");
    }

    [Fact]
    public void ParseRules_DuplicateDiffersOnlyByCase_StillRejected()
    {
        // Matching is case-insensitive by default, so "toorak" and "Toorak" collide on air.
        var result = PronunciationRuleMapping.ParseRules([
            Alias("Toorak", "Toor-rak"),
            Alias("toorak", "Tworack")
        ]);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ParseRules_SameWordAsAliasAndPhoneme_Allowed()
    {
        // Different rule TYPES for one word are legitimate: the provider applies whichever
        // the synthesis model supports, so they never both take effect.
        var result = PronunciationRuleMapping.ParseRules([
            Alias("Toorak", "Toor-rak"),
            new PronunciationRuleDto("phoneme", "Toorak", "tʊˈɹæk", "ipa")
        ]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseRules_DistinctWords_Allowed()
    {
        var result = PronunciationRuleMapping.ParseRules([
            Alias("Toorak", "Toor-rak"),
            Alias("Keilor", "KEE-ler")
        ]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseRules_RespellingIsTheWordItself_Rejected()
    {
        var result = PronunciationRuleMapping.ParseRules([Alias("Toorak", "Toorak")]);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("changes nothing");
    }

    [Fact]
    public void ParseRules_RespellingDiffersOnlyByCase_Rejected()
    {
        // Case alone is not a pronunciation difference when matching is case-insensitive.
        PronunciationRuleMapping.ParseRules([Alias("Toorak", "TOORAK")]).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ParseRules_EmptyOrNull_IsAnEmptyRuleSet()
    {
        PronunciationRuleMapping.ParseRules(null).Value.ShouldBeEmpty();
        PronunciationRuleMapping.ParseRules([]).Value.ShouldBeEmpty();
    }
}
