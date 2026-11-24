using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class MarketScopeRequestTests
{
    [Fact]
    public void ParseMarkets_NullOrWhitespace_ReturnsEmpty()
    {
        MarketScopeRequest.ParseMarkets(null).ShouldBeEmpty();
        MarketScopeRequest.ParseMarkets("   ").ShouldBeEmpty();
    }

    [Fact]
    public void ParseMarkets_SplitsTrimsAndDedupesCaseInsensitively()
    {
        MarketScopeRequest.ParseMarkets(" Sydney , melbourne ,Sydney, ")
            .ShouldBe(new[] { "Sydney", "melbourne" });
    }

    [Fact]
    public void FilterToAllowed_None_ReturnsAllRequested()
    {
        var requested = new[] { "Sydney", "Perth" };

        MarketScopeRequest
            .FilterToAllowed(requested, Option<IReadOnlyList<string>>.None)
            .ShouldBe(requested);
    }

    [Fact]
    public void FilterToAllowed_Scoped_IntersectsCaseInsensitively()
    {
        var allowed = Option<IReadOnlyList<string>>.Some(
            (IReadOnlyList<string>)new[] { "sydney" });

        MarketScopeRequest
            .FilterToAllowed(new[] { "Sydney", "Perth" }, allowed)
            .ShouldBe(new[] { "Sydney" });
    }
}
