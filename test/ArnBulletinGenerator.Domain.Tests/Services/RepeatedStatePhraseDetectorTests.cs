using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RepeatedStatePhraseDetectorTests
{
    [Fact]
    public void Flags_a_state_word_repeated_three_times()
    {
        // The exact live pattern: "roadworks" leaned on across item after item.
        const string script =
            "Riverside Drive has roadworks, and Canning Highway has roadworks at South Terrace. " +
            "Winthrop Avenue has roadworks too. The Mitchell Freeway is moving nicely.";

        var result = RepeatedStatePhraseDetector.Detect(script);

        result.HasExcessRepetition.ShouldBeTrue();
        result.Word.ShouldBe("roadworks");
        result.Count.ShouldBe(3);
        result.Feedback.ShouldContain("roadworks");
    }

    [Fact]
    public void Generalises_to_any_word_not_a_fixed_list()
    {
        // A different over-used word ("delays") must fire with no code specific to it.
        const string script =
            "Hoddle Street has minor delays, Punt Road has minor delays, and Russell Street has minor delays.";

        var result = RepeatedStatePhraseDetector.Detect(script);

        result.HasExcessRepetition.ShouldBeTrue();
        result.Word.ShouldBe("delays");
    }

    [Fact]
    public void Ignores_road_type_nouns_that_recur_legitimately()
    {
        // Three different freeways is NOT a repeated word — road-type nouns are ignored.
        const string script =
            "The West Gate Freeway is chockers, the Monash Freeway is a heavy run, and the Eastern " +
            "Freeway is queuing up. The Tullamarine Freeway is moving nicely.";

        var result = RepeatedStatePhraseDetector.Detect(script);

        result.HasExcessRepetition.ShouldBeFalse();
    }

    [Fact]
    public void Passes_a_varied_read()
    {
        const string script =
            "Kings Way is closed after a crash. The West Gate is chockers, backed up from the bridge. " +
            "Punt Road is a touch slow, while Hoddle Street is easing. The Eastern Freeway is looking good.";

        RepeatedStatePhraseDetector.Detect(script).HasExcessRepetition.ShouldBeFalse();
    }

    [Fact]
    public void Empty_script_does_not_flag()
    {
        RepeatedStatePhraseDetector.Detect("").HasExcessRepetition.ShouldBeFalse();
    }

    [Fact]
    public void Ignores_capitalised_road_name_fragments()
    {
        // Live Adelaide case: "South" appears in three different road NAMES — a proper-noun
        // fragment, not a repeated condition word. Case (capitalised) must exclude it.
        const string script =
            "South Road is closed at Sir Donald Bradman Drive. Glen Osmond Road has a slow patch, and " +
            "the South Eastern Freeway is moving nicely, while the North-South Motorway is easing.";

        RepeatedStatePhraseDetector.Detect(script).HasExcessRepetition.ShouldBeFalse();
    }
}
