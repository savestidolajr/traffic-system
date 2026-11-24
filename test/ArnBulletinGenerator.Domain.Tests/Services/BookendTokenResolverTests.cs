using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class BookendTokenResolverTests
{
    // ── Built-in resolution ────────────────────────────────────────────────

    [Fact]
    public void BuiltInTokens_ResolveFromScheduleData()
    {
        var result = BookendTokenResolver.Resolve(
            "Checking your {station} traffic across {city} — market {market}.",
            marketId: "adelaide", station: "KIIS 102.3", overrides: null);

        // The market token is the human-readable place name, never the raw market-id slug.
        result.Text.ShouldBe("Checking your KIIS 102.3 traffic across Adelaide — market Adelaide.");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void City_DropsCallSignSuffix_FromMarketId()
    {
        // "cairns-4ca" is a real pilot market id; the call sign is not part of the place name.
        BookendTokenResolver.Resolve("Over {city}.", "cairns-4ca", "Star 102.7", null)
            .Text.ShouldBe("Over Cairns.");
    }

    [Fact]
    public void MultiWordMarkets_ResolveToTheRealPlaceName_AndOverrideStillWins()
    {
        // "goldcoast" is one market id but two words; the shared MarketNames table supplies the
        // human-readable name instead of slug-munging it into "Goldcoast".
        BookendTokenResolver.Resolve("Looking over {city} today.", "goldcoast", "Gold FM", null)
            .Text.ShouldBe("Looking over Gold Coast today.");

        // The station override still wins — the goldcoast dev override is "the Gold Coast".
        BookendTokenResolver.Resolve("Looking over {city} today.", "goldcoast", "Gold FM",
                new Dictionary<string, string> { ["city"] = "the Gold Coast" })
            .Text.ShouldBe("Looking over the Gold Coast today.");
    }

    [Fact]
    public void TokenNames_AreCaseInsensitive()
    {
        BookendTokenResolver.Resolve("Over {City} and {CITY}.", "perth", "Mix", null)
            .Text.ShouldBe("Over Perth and Perth.");
    }

    // ── Overrides ──────────────────────────────────────────────────────────

    [Fact]
    public void CustomToken_ResolvesFromOverridesOnly()
    {
        var result = BookendTokenResolver.Resolve(
            "That's your {brand} traffic.", "sydney", "KIIS 1065",
            new Dictionary<string, string> { ["brand"] = "KIIS" });

        result.Text.ShouldBe("That's your KIIS traffic.");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void BlankOverride_DoesNotShadowTheBuiltIn()
    {
        // A half-filled token row in the studio must not silently blank a working built-in.
        BookendTokenResolver.Resolve("Over {city}.", "melbourne", "3MP",
                new Dictionary<string, string> { ["city"] = "   " })
            .Text.ShouldBe("Over Melbourne.");
    }

    // ── Unresolved tokens ──────────────────────────────────────────────────

    [Fact]
    public void UnknownToken_IsReported_AndNeverAiredAsLiteralBraces()
    {
        var result = BookendTokenResolver.Resolve(
            "Your {brand} traffic for {city}.", "darwin", "Hot 100", null);

        result.IsComplete.ShouldBeFalse();
        result.UnresolvedTokens.ShouldBe(["brand"]);
        result.Text.ShouldNotContain("{");
        result.Text.ShouldNotContain("}");
        // The gap left by the dropped token is closed, so the remnant still reads as a sentence.
        result.Text.ShouldBe("Your traffic for Darwin.");
    }

    [Fact]
    public void UnresolvedTokens_AreDistinct()
    {
        BookendTokenResolver.Resolve("{a} then {a} then {b}.", "perth", "Mix", null)
            .UnresolvedTokens.ShouldBe(["a", "b"]);
    }

    // ── Extraction (drives assignment-time validation and the studio prompts) ──

    [Fact]
    public void ExtractTokens_ReturnsDistinctNames_InFirstAppearanceOrder()
    {
        BookendTokenResolver.ExtractTokens("{city} {brand} {city} {station}")
            .ShouldBe(["city", "brand", "station"]);
    }

    [Fact]
    public void ExtractTokens_IgnoresNonTokenBraces()
    {
        // Guards the regex: a brace pair that is not a valid identifier is prose, not a token.
        BookendTokenResolver.ExtractTokens("cost {} and {123} and {a-b}").ShouldBeEmpty();
    }

    [Fact]
    public void ExtractTokens_EmptyTemplate_ReturnsEmpty()
    {
        BookendTokenResolver.ExtractTokens("").ShouldBeEmpty();
        BookendTokenResolver.ExtractTokens("   ").ShouldBeEmpty();
    }

    // ── Value surface used by the studio preview ───────────────────────────

    [Fact]
    public void BuildValues_ExposesBuiltIns_WithOverridesApplied()
    {
        var values = BookendTokenResolver.BuildValues("sydney", "KIIS 1065",
            new Dictionary<string, string> { ["city"] = "Sydney town", ["brand"] = "KIIS" });

        values["city"].ShouldBe("Sydney town");
        values["market"].ShouldBe("Sydney");
        values["station"].ShouldBe("KIIS 1065");
        values["brand"].ShouldBe("KIIS");
    }

    // ── Human-readable market names (finding 36) ───────────────────────────

    [Theory]
    [InlineData("adelaide", "Adelaide")]
    [InlineData("brisbane", "Brisbane")]
    [InlineData("cairns-4ca", "Cairns")]
    [InlineData("darwin", "Darwin")]
    [InlineData("demo-coast", "Demo Coast")]
    [InlineData("goldcoast", "Gold Coast")]
    [InlineData("melbourne", "Melbourne")]
    [InlineData("mildura", "Mildura")]
    [InlineData("perth", "Perth")]
    [InlineData("portaugusta", "Port Augusta")]
    [InlineData("portlincoln", "Port Lincoln")]
    [InlineData("queenstown", "Queenstown")]
    [InlineData("sydney", "Sydney")]
    public void EveryConfiguredMarketId_YieldsTheHumanReadablePlaceName(string marketId, string expected)
    {
        var values = BookendTokenResolver.BuildValues(marketId, "Some FM", null);

        // Both human-facing tokens voice the place name — never the raw slug, which carries the
        // call-sign suffix ("cairns-4ca") or storage-key munging ("portaugusta").
        values["city"].ShouldBe(expected);
        values["market"].ShouldBe(expected);
        values["market"].ShouldNotBe(marketId);
    }

    [Fact]
    public void UnknownMarket_LeavesHumanTokensUnresolved_SoTheAssignmentGateCanFail()
    {
        // A market not in the shared table gets an EMPTY value, not a slug-mangled guess — the
        // resolution reports the token as unfillable and the existing assignment gate rejects
        // the bookend instead of airing a mangled word.
        var result = BookendTokenResolver.Resolve(
            "That's it for {market} traffic.", "brand-new-market", "New FM", null);

        result.IsComplete.ShouldBeFalse();
        result.UnresolvedTokens.ShouldBe(["market"]);
        result.Text.ShouldNotContain("{");
    }
}
