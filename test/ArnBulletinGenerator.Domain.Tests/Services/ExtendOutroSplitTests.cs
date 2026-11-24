using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Pins the split the underrun-extend path relies on. The duplicate-outro defect has now aired
///     twice from two different callers appending to a COMPOSED take:
///     <list type="bullet">
///         <item>dev sydney <c>ffcf233d</c> (2026-08-25) - the required-fixture splice added the
///             weather line after the closer.</item>
///         <item>dev sydney <c>562ee6a7</c> (2026-08-26) - the word-target extend grew a
///             content-limited read after the closer, so the composer added a second one.</item>
///     </list>
///     Both callers now split the trailing outro, grow the BODY, and reattach. This fixture holds
///     the shared contract so a third caller cannot rediscover it.
/// </summary>
public sealed class ExtendOutroSplitTests
{
    private const string Intro = "Now looking over Sydney.";
    private const string Outro = "That's KIIS traffic and weather together, powered by";

    private static Option<ResolvedBookends> Bookends() =>
        Option<ResolvedBookends>.Some(new ResolvedBookends(Intro, Outro, "bk-test", 1));

    [Fact]
    public void A_composed_take_yields_its_body_without_the_closer()
    {
        string composed = $"{Intro} WestConnex is closed at Beverly Hills. {Outro}";

        var (body, outro) = BulletinBookendComposer.SplitTrailingOutro(composed, Bookends());

        outro.ShouldNotBeNull();
        outro.TrimEnd().ShouldEndWith("powered by");
        body.ShouldNotContain("powered by");
        body.ShouldContain("WestConnex is closed at Beverly Hills.");
    }

    [Fact]
    public void Growing_the_body_then_reattaching_leaves_exactly_one_closer()
    {
        string composed = $"{Intro} WestConnex is closed at Beverly Hills. {Outro}";
        var (body, outro) = BulletinBookendComposer.SplitTrailingOutro(composed, Bookends());

        // What the extend path does: new content joins the BODY, never the composed read.
        string grown = $"{body.TrimEnd()} Mostly cloudy across Sydney, with 16 degrees.";
        string recomposed = $"{grown} {outro!.TrimStart()}";

        CountOf(recomposed, "powered by").ShouldBe(1);
        recomposed.TrimEnd().ShouldEndWith("powered by");
        // The aired defect: weather buried between two closers.
        recomposed.ShouldContain("16 degrees. That's KIIS");
    }

    [Fact]
    public void Appending_to_the_composed_take_is_what_produced_the_defect()
    {
        // Documents the shape being guarded against, so the guard cannot be quietly removed.
        string composed = $"{Intro} WestConnex is closed at Beverly Hills. {Outro}";
        string naive = $"{composed} Mostly cloudy across Sydney, with 16 degrees. {Outro}";

        CountOf(naive, "powered by").ShouldBe(2);
    }

    [Fact]
    public void An_outro_less_station_is_unchanged_by_the_split()
    {
        var introOnly = Option<ResolvedBookends>.Some(new ResolvedBookends(Intro, string.Empty, "bk-test", 1));
        string composed = $"{Intro} WestConnex is closed at Beverly Hills.";

        var (body, outro) = BulletinBookendComposer.SplitTrailingOutro(composed, introOnly);

        outro.ShouldBeNullOrWhiteSpace();
        body.ShouldBe(composed);
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
