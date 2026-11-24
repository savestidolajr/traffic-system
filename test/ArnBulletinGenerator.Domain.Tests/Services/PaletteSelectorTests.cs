using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class PaletteSelectorTests
{
    private const string Vocabulary = """
        ## Heavy / jam
        chockers, crawling, bumper to bumper, stop-start, a sea of brake lights, banked up, chock-a-block, stacks of traffic, very heavy, really slow, an absolute shocker, a struggle

        ## Building / moderate
        busy, building, slow going, a slow patch, patchy, under speed, feeling the pinch, filling up, thickening up, queuing up, getting busier, a little slow through, carrying plenty of traffic

        ## Minor / light
        a touch slow, a bit slow, a few brake lights, just a little under speed, just a minor hold up, a couple of minutes, moving slowly, easing, a slow run

        ## Roadworks
        works are up, works underway, crews are out, resurfacing, a lane closed through the works, speed and lane restrictions

        ## Closure
        closed, shut, blocked off

        ## Crash / hazard
        a crash, a smash, a prang, a breakdown, debris, cars in the breakdown lane

        ## Clear / flowing
        looking good, no dramas, moving nicely, a clear run, running well, all clear, no delays, a smooth run, steady, flying through
        """;

    [Fact]
    public void Is_reproducible_for_the_same_seed()
    {
        PaletteSelector.Render("job-abc", Vocabulary).ShouldBe(PaletteSelector.Render("job-abc", Vocabulary));
    }

    [Fact]
    public void Varies_the_phrase_order_across_seeds()
    {
        var minorLines = new[] { "job-1", "job-2", "job-3", "job-4", "job-5", "job-6" }
            .Select(seed => MinorTierLine(PaletteSelector.Render(seed, Vocabulary)))
            .Distinct()
            .ToList();

        minorLines.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Never_drops_or_duplicates_a_phrase_when_shuffling()
    {
        string minorA = MinorTierLine(PaletteSelector.Render("seed-x", Vocabulary));
        string minorB = MinorTierLine(PaletteSelector.Render("seed-y", Vocabulary));

        var phrasesA = MinorPhrases(minorA);
        var phrasesB = MinorPhrases(minorB);

        phrasesA.ShouldBe(phrasesB, ignoreOrder: true);
        phrasesA.Distinct().Count().ShouldBe(phrasesA.Count);
    }

    [Fact]
    public void Renders_every_severity_tier()
    {
        string rendered = PaletteSelector.Render("any", Vocabulary);

        rendered.ShouldContain("Heavy / jam:");
        rendered.ShouldContain("Building / moderate:");
        rendered.ShouldContain("Minor / light:");
        rendered.ShouldContain("Roadworks:");
        rendered.ShouldContain("Closure:");
        rendered.ShouldContain("Crash / hazard:");
        rendered.ShouldContain("Clear / flowing:");
    }

    [Fact]
    public void Seed_vocabulary_preserves_the_previous_render_for_a_fixed_job()
    {
        PaletteSelector.Render("job-abc", Vocabulary).ShouldBe(
            "Draw from the Australian Traffic Network's real on-air vocabulary below, or anything semantically aligned — these are examples, not a fixed list. The point is variety and road-feel over data labels.\n" +
            "- Heavy / jam: banked up, bumper to bumper, an absolute shocker, chock-a-block, chockers, stop-start, crawling, very heavy, stacks of traffic, a sea of brake lights, a struggle, really slow\n" +
            "- Building / moderate: patchy, feeling the pinch, thickening up, building, slow going, getting busier, under speed, busy, a slow patch, carrying plenty of traffic, filling up, queuing up, a little slow through\n" +
            "- Minor / light: easing, a bit slow, a few brake lights, just a little under speed, just a minor hold up, a slow run, moving slowly, a couple of minutes, a touch slow\n" +
            "- Roadworks: works underway, crews are out, a lane closed through the works, speed and lane restrictions, resurfacing, works are up\n" +
            "- Closure: blocked off, shut, closed\n" +
            "- Crash / hazard: debris, a crash, a smash, a breakdown, cars in the breakdown lane, a prang\n" +
            "- Clear / flowing: moving nicely, a clear run, no dramas, no delays, a smooth run, all clear, steady, looking good, flying through, running well");
    }

    private static string MinorTierLine(string rendered) =>
        rendered.Split('\n').First(l => l.StartsWith("- Minor / light:", StringComparison.Ordinal)).Trim();

    private static List<string> MinorPhrases(string minorLine) =>
        minorLine["- Minor / light:".Length..]
            .Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
}
