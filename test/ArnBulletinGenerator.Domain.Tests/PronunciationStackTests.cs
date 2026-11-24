using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests;

/// <summary>
///     The ordering contract for the locator set handed to one synthesis call.
///     <para>
///         ElevenLabs applies locators sequentially and the FIRST one to match a word wins it,
///         while non-colliding rules from every layer still apply (both verified live 2026-07-31
///         with two dictionaries disagreeing on one word). So station-before-global is what makes a
///         station override a platform-wide respelling per word and inherit the rest; reversed,
///         every station-local correction is silently dead.
///     </para>
/// </summary>
public sealed class PronunciationStackTests
{
    private static readonly PronunciationLocator Station = new("dict-station", "ver-station");
    private static readonly PronunciationLocator Global = new("dict-global", "ver-global");

    private static Option<PronunciationLocator> Some(PronunciationLocator l) =>
        Option<PronunciationLocator>.Some(l);

    private static Option<PronunciationLocator> None => Option<PronunciationLocator>.None;

    [Fact]
    public void Of_BothLayers_OrdersStationBeforeGlobal()
    {
        var stack = PronunciationStack.Of(Some(Station), Some(Global));

        stack.Locators.ShouldBe([Station, Global]);
        stack.Station.Value.ShouldBe(Station);
        stack.Global.Value.ShouldBe(Global);
        stack.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Of_StationOnly_CarriesOneLocatorAndNoGlobal()
    {
        var stack = PronunciationStack.Of(Some(Station), None);

        stack.Locators.ShouldBe([Station]);
        stack.Global.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Of_GlobalOnly_KeepsItIdentifiedAsTheGlobalLayer()
    {
        // Layer identity is never inferred from position: a station with no dictionary of its own
        // still gets the platform-wide layer, and calling that the station layer would aim an
        // operator's word fix at the dictionary every other station reads from.
        var stack = PronunciationStack.Of(None, Some(Global));

        stack.Locators.ShouldBe([Global]);
        stack.Station.HasValue.ShouldBeFalse();
        stack.Global.Value.ShouldBe(Global);
    }

    [Fact]
    public void Of_NeitherLayer_IsEmpty()
    {
        var stack = PronunciationStack.Of(None, None);

        stack.IsEmpty.ShouldBeTrue();
        stack.Locators.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_HasNoLayers()
    {
        PronunciationStack.Empty.IsEmpty.ShouldBeTrue();
        PronunciationStack.Empty.Station.HasValue.ShouldBeFalse();
        PronunciationStack.Empty.Global.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Of_NullableOverload_MatchesTheOptionOverload()
    {
        PronunciationStack.Of(Station, Global).Locators.ShouldBe([Station, Global]);
        PronunciationStack.Of(Station, null).Locators.ShouldBe([Station]);
        PronunciationStack.Of(null, Global).Locators.ShouldBe([Global]);
        PronunciationStack.Of(null, null).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Of_NeverExceedsTheProviderCap()
    {
        // Two layers can never breach the three-locator limit; this pins the invariant so a future
        // third layer has to make a deliberate decision rather than silently overflow the request.
        PronunciationStack.Of(Station, Global).Locators.Count
            .ShouldBeLessThanOrEqualTo(PronunciationStack.MaxLocators);
    }

    [Fact]
    public void Locator_RequiresBothHalvesOfAPersistedPair()
    {
        // ElevenLabs rejects a locator without a version id, so a half-recorded pair must resolve
        // to nothing rather than reach the wire.
        PronunciationStack.Locator(
                Option<string>.Some("dict-1"), Option<string>.Some("ver-1"))
            .Value.ShouldBe(new PronunciationLocator("dict-1", "ver-1"));

        PronunciationStack.Locator(Option<string>.Some("dict-1"), Option<string>.None)
            .HasValue.ShouldBeFalse();
        PronunciationStack.Locator(Option<string>.None, Option<string>.Some("ver-1"))
            .HasValue.ShouldBeFalse();
        PronunciationStack.Locator(Option<string>.None, Option<string>.None)
            .HasValue.ShouldBeFalse();
    }
}

/// <summary>Scope is reach, not rule content — promoting a dictionary must not mint a version.</summary>
public sealed class PronunciationScopeTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc);

    private static PronunciationDictionary Make(PronunciationScope scope = PronunciationScope.Station) =>
        PronunciationDictionary.Create(
            "Everywhere", null, "dict-1", "v1",
            [PronunciationRule.CreateAlias("Keilor", "Keeler").Value],
            "init", "operator", Now, scope: scope).Value;

    [Fact]
    public void Create_DefaultsToStationScope()
    {
        var dict = Make();

        dict.Scope.ShouldBe(PronunciationScope.Station);
        dict.IsGlobal.ShouldBeFalse();
    }

    [Fact]
    public void Create_Global_IsGlobal()
    {
        Make(PronunciationScope.Global).IsGlobal.ShouldBeTrue();
    }

    [Fact]
    public void Retarget_ChangesScopeAndTimestamp_ButMintsNoVersion()
    {
        var dict = Make();
        int versionsBefore = dict.Versions.Count;
        var later = Now.AddMinutes(5);

        dict.Retarget(PronunciationScope.Global, later);

        dict.IsGlobal.ShouldBeTrue();
        dict.UpdatedAt.ShouldBe(later);
        dict.Versions.Count.ShouldBe(versionsBefore);
    }

    [Fact]
    public void Reconstitute_WithoutScope_ReadsBackAsStation()
    {
        // Every row written before the platform-wide layer existed was a station dictionary.
        var dict = PronunciationDictionary.Reconstitute(
            "pd-1", "dict-1", "Legacy", null, "active", Now, Now, "operator", [],
            [new PronunciationDictionaryVersion(
                1, "v1", [PronunciationRule.CreateAlias("Keilor", "Keeler").Value], null, Now, "operator")]);

        dict.Scope.ShouldBe(PronunciationScope.Station);
    }

    [Fact]
    public void IsActive_TracksArchiveState()
    {
        var dict = Make();
        dict.IsActive.ShouldBeTrue();

        dict.Archive(Now);
        dict.IsActive.ShouldBeFalse();

        dict.Unarchive(Now);
        dict.IsActive.ShouldBeTrue();
    }
}
