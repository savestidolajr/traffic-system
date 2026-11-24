using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests;

public sealed class PronunciationDictionaryTests
{
    private static readonly DateTime Now = new(2026, 6, 23, 9, 0, 0, DateTimeKind.Utc);

    private static PronunciationRule Alias(string word, string alias) =>
        PronunciationRule.CreateAlias(word, alias).Value;

    private static PronunciationDictionary Make(params PronunciationRule[] rules) =>
        PronunciationDictionary.Create("Gold Coast", "GC names", "dict-1", "v1", rules, "init", "operator", Now).Value;

    [Fact]
    public void Create_MintsVersionOne_WithIdsAndLocator()
    {
        var dict = Make(Alias("Coomera", "Koomera"));

        dict.CurrentVersion.ShouldBe(1);
        dict.DictionaryId.ShouldBe("dict-1");
        dict.CurrentElevenLabsVersionId.ShouldBe("v1");
        dict.CurrentRules.Count.ShouldBe(1);
        dict.CurrentLocator.HasValue.ShouldBeTrue();
        dict.CurrentLocator.Value.DictionaryId.ShouldBe("dict-1");
        dict.CurrentLocator.Value.VersionId.ShouldBe("v1");
    }

    [Fact]
    public void Create_RejectsMissingDictionaryOrVersionId()
    {
        PronunciationDictionary.Create("n", null, "", "v1", [], null, null, Now).IsFailure.ShouldBeTrue();
        PronunciationDictionary.Create("n", null, "d", "", [], null, null, Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ApplyEdit_RuleChange_MintsNewVersion_WithSuppliedVersionId()
    {
        var dict = Make(Alias("Coomera", "Koomera"));

        var minted = dict.ApplyEdit("Gold Coast", null, "v2", [Alias("Coomera", "Koomerah")], "tweak", "ed", Now);

        minted.ShouldBeTrue();
        dict.CurrentVersion.ShouldBe(2);
        dict.CurrentElevenLabsVersionId.ShouldBe("v2");
        dict.CurrentRules[0].Replacement.ShouldBe("Koomerah");
    }

    [Fact]
    public void ApplyEdit_SameRules_NameOnly_DoesNotMintVersion()
    {
        var dict = Make(Alias("Coomera", "Koomera"));

        var minted = dict.ApplyEdit("Gold Coast Renamed", "new desc", "v1", [Alias("Coomera", "Koomera")],
            null, "ed", Now);

        minted.ShouldBeFalse();
        dict.CurrentVersion.ShouldBe(1);
        dict.Name.ShouldBe("Gold Coast Renamed");
        dict.Description.ShouldBe("new desc");
    }

    [Fact]
    public void RulesDifferFrom_DetectsAddRemoveAndContentChange()
    {
        var dict = Make(Alias("Coomera", "Koomera"));

        dict.RulesDifferFrom([Alias("Coomera", "Koomera")]).ShouldBeFalse();
        dict.RulesDifferFrom([Alias("Coomera", "Koomerah")]).ShouldBeTrue();
        dict.RulesDifferFrom([Alias("Coomera", "Koomera"), Alias("Nerang", "Nerrang")]).ShouldBeTrue();
        dict.RulesDifferFrom([]).ShouldBeTrue();
    }

    [Fact]
    public void Archive_Unarchive_DoNotMintVersions()
    {
        var dict = Make(Alias("Coomera", "Koomera"));

        dict.Archive(Now);
        dict.Status.ShouldBe("archived");
        dict.CurrentVersion.ShouldBe(1);

        dict.Unarchive(Now);
        dict.Status.ShouldBe("active");
        dict.CurrentVersion.ShouldBe(1);
    }

    [Fact]
    public void Reconstitute_RoundTripsVersionsAndIds()
    {
        var versions = new List<PronunciationDictionaryVersion>
        {
            new(1, "v1", [Alias("Coomera", "Koomera")], "init", Now, "operator"),
            new(2, "v2", [Alias("Coomera", "Koomerah")], "tweak", Now, "ed")
        };

        var dict = PronunciationDictionary.Reconstitute("pd-1", "dict-1", "Gold Coast", null, "active",
            Now, Now, "operator", [], versions, "etag-v1");

        dict.CurrentVersion.ShouldBe(2);
        dict.CurrentElevenLabsVersionId.ShouldBe("v2");
        dict.Versions.Count.ShouldBe(2);
        dict.ETag.ShouldBe("etag-v1");
    }

    // ── LocatorFor: the single attach decision shared by the airtime resolver and the preview
    //    (bug-bash 2026-08-05 finding 13) so a phoneme rule can never air on a model that would
    //    delete its matched words, and the two paths can never disagree. ─────────────────────

    private static PronunciationDictionary MakeWithAliasOnly(params PronunciationRule[] rules) =>
        PronunciationDictionary.Create("Gold Coast", "GC names", "dict-1", "v1", rules, "init", "operator",
            Now, aliasOnlyVersionId: "v1-alias").Value;

    [Fact]
    public void LocatorFor_NoPhonemeRules_FullLocatorOnEveryModel()
    {
        var dict = Make(Alias("Coomera", "Koomera"));

        dict.LocatorFor("eleven_multilingual_v2").Value.VersionId.ShouldBe("v1");
        dict.LocatorFor("eleven_v3").Value.VersionId.ShouldBe("v1");
        dict.LocatorFor("eleven_turbo_v2").Value.VersionId.ShouldBe("v1");
    }

    [Fact]
    public void LocatorFor_PhonemeRules_SafeModelsKeepTheFullLocator()
    {
        var dict = Make(Alias("Coomera", "Koomera"),
            PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value);

        // v3 skips phoneme benignly; turbo/flash v2 render them — the full dictionary attaches.
        dict.LocatorFor("eleven_v3").Value.VersionId.ShouldBe("v1");
        dict.LocatorFor("eleven_turbo_v2").Value.VersionId.ShouldBe("v1");
    }

    [Fact]
    public void LocatorFor_PhonemeRules_MultilingualV2_ReturnsTheAliasOnlyLocator()
    {
        var dict = MakeWithAliasOnly(Alias("Coomera", "Koomera"),
            PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value);

        var locator = dict.LocatorFor("eleven_multilingual_v2");
        locator.HasValue.ShouldBeTrue();
        locator.Value.DictionaryId.ShouldBe("dict-1");
        locator.Value.VersionId.ShouldBe("v1-alias");
    }

    [Fact]
    public void LocatorFor_PhonemeRules_MultilingualV2_WithoutAliasOnlyLine_ReturnsNone()
    {
        var dict = Make(Alias("Coomera", "Koomera"),
            PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value);

        dict.LocatorFor("eleven_multilingual_v2").HasValue.ShouldBeFalse();
    }

    [Fact]
    public void UnsupportedRuleWordsFor_ListsOnlyThePhonemeWordsOnUnsafeModels()
    {
        var dict = Make(Alias("Coomera", "Koomera"),
            PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value);

        dict.UnsupportedRuleWordsFor("eleven_multilingual_v2").ShouldBe(["Lachlan"]);
        dict.UnsupportedRuleWordsFor("eleven_v3").ShouldBeEmpty();
    }
}
