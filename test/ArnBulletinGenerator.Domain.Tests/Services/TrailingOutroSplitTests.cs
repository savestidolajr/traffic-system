using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
/// Pins the split the required-fixture splice relies on. Dev sydney ffcf233d (2026-08-25) aired the
/// KIIS sign-off twice with the weather sandwiched between the two copies, because the weather line
/// was appended to a read whose tail was already the closer.
/// </summary>
public sealed class TrailingOutroSplitTests
{
    private const string Outro = "That's KIIS traffic and weather together, powered by";

    private static Option<ResolvedBookends> Kiis() =>
        Option<ResolvedBookends>.Some(new ResolvedBookends("Now looking over Sydney.", Outro, "bk-test", 1));

    [Fact]
    public void SplitsAComposedReadIntoBodyAndOutro()
    {
        const string composed =
            "Now looking over Sydney. King Georges Road is blocked at South Hurstville. "
            + Outro;

        var (body, outro) = BulletinBookendComposer.SplitTrailingOutro(composed, Kiis());

        outro.ShouldBe(Outro);
        body.ShouldNotContain("powered by");
        body.ShouldContain("King Georges Road");
    }

    [Fact]
    public void LeavesABodyWithNoOutroAlone()
    {
        const string body = "King Georges Road is blocked at South Hurstville.";

        var (result, outro) = BulletinBookendComposer.SplitTrailingOutro(body, Kiis());

        outro.ShouldBeNull();
        result.ShouldBe(body);
    }

    [Fact]
    public void LeavesTextAloneWhenTheStationHasNoBookends()
    {
        const string body = "King Georges Road is blocked.";

        var (result, outro) =
            BulletinBookendComposer.SplitTrailingOutro(body, Option<ResolvedBookends>.None);

        outro.ShouldBeNull();
        result.ShouldBe(body);
    }

    [Fact]
    public void ADifferentTailIsNotMistakenForTheOutro()
    {
        // A tail that merely mentions the station must not be cut - only an exact word run.
        const string body = "That's the run through the city on KIIS.";

        var (result, outro) = BulletinBookendComposer.SplitTrailingOutro(body, Kiis());

        outro.ShouldBeNull();
        result.ShouldBe(body);
    }

    [Fact]
    public void SplicingAfterTheSplitCannotBuryTheOutro()
    {
        // The exact aired shape, rebuilt: body + outro, then a weather line spliced in.
        const string composed =
            "Now looking over Sydney. King Georges Road is blocked at South Hurstville. " + Outro;

        var (body, outro) = BulletinBookendComposer.SplitTrailingOutro(composed, Kiis());
        string repaired = $"{body} Light rain, 17 degrees. {outro}";

        // One sign-off, and it is last.
        repaired.Split("powered by").Length.ShouldBe(2);
        repaired.TrimEnd().ShouldEndWith("powered by");
        repaired.ShouldContain("Light rain, 17 degrees.");
    }
}
