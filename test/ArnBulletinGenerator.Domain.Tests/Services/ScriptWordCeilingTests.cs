using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class ScriptWordCeilingTests
{
    // Six 5-word traffic sentences = 30 words, then a 5-word weather close.
    private const string Traffic6PlusClose =
        "The M4 is running slowly. "
        + "Church Street is a crawl. "
        + "Botany Road has minor delays. "
        + "Marsh Street is a touch slow. "
        + "West Street is moving slowly. "
        + "Victoria Road is under pressure. "
        + "Cloudy, twelve degrees today.";

    private const string Close = "Cloudy, twelve degrees today.";
    private const string FourTenWordTrafficPlusClose =
        "Traffic road one two three four five six seven eight. "
        + "Traffic road alpha beta gamma delta epsilon zeta eta theta. "
        + "Traffic road red blue green black white orange purple brown. "
        + "Traffic road north south east west central coastal inland metro. "
        + "Cloudy skies, twelve degrees today.";

    private const string ThreeTenWordTrafficPlusClose =
        "Traffic road one two three four five six seven eight. "
        + "Traffic road alpha beta gamma delta epsilon zeta eta theta. "
        + "Traffic road red blue green black white orange purple brown. "
        + "Cloudy skies, twelve degrees today.";

    /// <summary>The read's weather/fuel lines as the workflow would have tagged them.</summary>
    private static FixtureLines Fixtures(bool required, params string[] lines) =>
        FixtureLines.From(
            lines.Select((line, index) => new Domain.Contracts.DraftedSentence(index + 1, line, [])
            {
                Section = Domain.Models.BulletinSegment.Weather
            }),
            required)!;

    [Fact]
    public void For_is_target_plus_fifteen_percent_with_an_eight_word_floor()
    {
        // Small slots get the flat floor, larger ones the percentage.
        ScriptWordCeiling.For(20).ShouldBe(28);
        ScriptWordCeiling.For(60).ShouldBe(69);
        ScriptWordCeiling.For(80).ShouldBe(92);
    }

    [Fact]
    public void Excess_is_zero_inside_the_ceiling_and_the_overrun_above_it()
    {
        // 35 words against a 20-word target (28-word ceiling) is 7 words over.
        ScriptWordCeiling.Excess(Traffic6PlusClose, 20).ShouldBe(7);
        ScriptWordCeiling.Excess(Traffic6PlusClose, 60).ShouldBe(0);
    }

    [Fact]
    public void TrimToCeiling_is_a_no_op_when_the_script_already_fits()
    {
        ScriptWordCeiling.TrimToCeiling(Traffic6PlusClose, 60).ShouldBe(Traffic6PlusClose);
    }

    [Fact]
    public void TrimToCeiling_drops_the_last_traffic_sentences_and_keeps_the_close()
    {
        string trimmed = ScriptWordCeiling.TrimToCeiling(Traffic6PlusClose, 20, Fixtures(false, Close));

        ScriptWordCeiling.Excess(trimmed, 20).ShouldBe(0);
        trimmed.ShouldStartWith("The M4 is running slowly.");
        // The close is protected: it survives even though it sits last, which is exactly where a
        // naive trailing-sentence trim would cut first.
        trimmed.ShouldEndWith("Cloudy, twelve degrees today.");
        trimmed.ShouldNotContain("Victoria Road");
    }

    [Fact]
    public void TrimToCeiling_stops_before_the_next_sentence_would_cross_the_word_floor()
    {
        string trimmed = ScriptWordCeiling.TrimToCeiling(
            FourTenWordTrafficPlusClose, 24,
            Fixtures(false, "Cloudy skies, twelve degrees today."),
            minimumWords: 35);

        WordCounter.Count(trimmed).ShouldBe(35);
        trimmed.ShouldContain("Traffic road red blue green black white orange purple brown.");
        trimmed.ShouldNotContain("Traffic road north south east west central coastal inland metro.");
    }

    [Fact]
    public void TrimToCeiling_returns_the_input_when_the_first_drop_would_cross_the_word_floor()
    {
        ScriptWordCeiling.TrimToCeiling(
            ThreeTenWordTrafficPlusClose, 24,
            Fixtures(false, "Cloudy skies, twelve degrees today."),
            minimumWords: 35)
            .ShouldBe(ThreeTenWordTrafficPlusClose);
    }

    [Fact]
    public void TrimToCeiling_zero_minimum_words_preserves_the_existing_behaviour()
    {
        var fixtures = Fixtures(false, Close);
        string defaultMinimum = ScriptWordCeiling.TrimToCeiling(Traffic6PlusClose, 20, fixtures);
        string explicitZero = ScriptWordCeiling.TrimToCeiling(
            Traffic6PlusClose, 20, fixtures, minimumWords: 0);

        explicitZero.ShouldBe(defaultMinimum);
    }

    [Fact]
    public void TrimToCeiling_keeps_at_least_one_traffic_sentence()
    {
        // A ceiling below even one traffic sentence plus the close: the lead item is never dropped.
        string trimmed = ScriptWordCeiling.TrimToCeiling(Traffic6PlusClose, 1, Fixtures(false, Close));

        trimmed.ShouldBe("The M4 is running slowly. Cloudy, twelve degrees today.");
    }

    [Fact]
    public void TrimToCeiling_leaves_an_all_close_script_alone()
    {
        // Every sentence is weather/fuel, so there is no traffic to sacrifice. Cutting into the only
        // content there is would be worse than handing the overrun to the audio fit.
        const string weather = "Cloudy, twelve degrees.";
        const string fuel = "Unleaded is cheapest at a dollar ninety.";
        const string allClose = weather + " " + fuel;

        ScriptWordCeiling.TrimToCeiling(allClose, 1, Fixtures(false, weather, fuel)).ShouldBe(allClose);
    }

    [Fact]
    public void TrimToCeiling_handles_blank_and_single_sentence_input()
    {
        ScriptWordCeiling.TrimToCeiling("", 30).ShouldBe("");
        ScriptWordCeiling.TrimToCeiling("   ", 30).ShouldBe("   ");
        ScriptWordCeiling.TrimToCeiling("One very long single sentence indeed.", 1)
            .ShouldBe("One very long single sentence indeed.");
    }

    // Deployed dev job 8d8e5ec168 (darwin, 22s slot, 60-word aim, 69-word ceiling): the review loop
    // committed a 100-word refine and nothing capped it before the audio stage cut 48 words out on a
    // time basis. The job record only persists the FINAL script, so the 100-word text cannot be
    // pinned here. What IS pinned: the 52-word survivor sits inside the ceiling, so the new guard is
    // a no-op on it and cannot become a second source of trimming.
    private const string DarwinSurvivor =
        "Fog Bay Road's slow going eastbound, with construction across about fifteen kays between Charles Point Road and Railway Alice Springs. "
        + "Cox Peninsula Road's got construction across about fifteen kays citybound from Railway Alice Springs. "
        + "McMillans Road's a bit slow westbound with construction. "
        + "Unleaded's CHEAPEST at two-oh-four in Belyuen, diesel two-forty-three there too!";

    [Fact]
    public void TrimToCeiling_is_a_no_op_on_the_darwin_survivor_at_its_real_aim()
    {
        ScriptWordCeiling.Excess(DarwinSurvivor, 60).ShouldBe(0);
        ScriptWordCeiling.TrimToCeiling(DarwinSurvivor, 60).ShouldBe(DarwinSurvivor);
    }

    // The same real sentences against a 15-second slot's aim (38 words, 45-word ceiling): the read
    // genuinely over-produces there, and the guard drops traffic while keeping the lead item and the
    // fuel close, which is the shape the audio-stage cut used to destroy.
    [Fact]
    public void TrimToCeiling_drops_traffic_and_keeps_the_fuel_close_on_a_real_over_produced_read()
    {
        ScriptWordCeiling.Excess(DarwinSurvivor, 38).ShouldBeGreaterThan(0);

        string trimmed = ScriptWordCeiling.TrimToCeiling(DarwinSurvivor, 38,
            Fixtures(false, "Unleaded's CHEAPEST at two-oh-four in Belyuen, diesel two-forty-three there too!"));

        ScriptWordCeiling.Excess(trimmed, 38).ShouldBe(0);
        trimmed.ShouldStartWith("Fog Bay Road's slow going eastbound");
        trimmed.ShouldEndWith("diesel two-forty-three there too!");
        trimmed.ShouldNotContain("McMillans Road");
    }

    // Test-env Sydney job 2895bb53 (2026-08-21): the required weather line sat SECOND-TO-LAST
    // (good-run sweep after it), and the prosody trim cut it with the trailing traffic because only
    // the trailing close was protected. A required fixture must survive wherever it sits.
    private const string WeatherMidScript =
        "Parramatta Road is bumper to bumper with delays of six minutes. "
        + "Belmore Road is busy heading through town. "
        + "A slight queue on the M4 M8 Link. "
        + "There is a brief delay on the M4 Western Motorway. "
        + "Forbes Street is moving slowly tonight. "
        + "Partly sunny, seventeen degrees. "
        + "The M1 Eastern Distributor is clear.";

    [Fact]
    public void TrimToCeiling_keeps_a_required_weather_line_wherever_it_sits()
    {
        string trimmed = ScriptWordCeiling.TrimToCeiling(WeatherMidScript, 20,
            Fixtures(true, "Partly sunny, seventeen degrees."));

        ScriptWordCeiling.Excess(trimmed, 20).ShouldBe(0);
        trimmed.ShouldContain("Partly sunny, seventeen degrees.");
        trimmed.ShouldStartWith("Parramatta Road is bumper to bumper");
    }

    [Fact]
    public void TrimToCeiling_protects_an_optional_fixture_only_as_the_trailing_close()
    {
        // An optional weather line mid-read is ordinary content and a deep enough trim cuts it: the
        // trailing "is clear" line is traffic, so there is no close to protect.
        string trimmed = ScriptWordCeiling.TrimToCeiling(WeatherMidScript, 20,
            Fixtures(false, "Partly sunny, seventeen degrees."));

        trimmed.ShouldNotContain("Partly sunny");
    }

    // Dev 2e158de3 (2026-09-02): the workflow's final read carried "Looking at the weather, clear
    // and 22 for Sydney." - no lock-list word in it - and the ceiling cut it as traffic after the
    // gates had passed the read. Protection is by the line the workflow tagged, not by vocabulary.
    [Fact]
    public void TrimToCeiling_protects_a_required_weather_line_that_names_no_weather_word()
    {
        const string weather = "Looking at the weather, clear and 22 for Sydney.";
        string script =
            "Out west at Glenfield, Railway Parade is closed northbound. "
            + "Near Orchard Hills, Luddenham Road is closed northbound. "
            + "Through Beecroft, Pennant Hills Road is feeling the pinch, with six minutes added. "
            + "Down at Peakhurst, Forest Road is heavy going, running five minutes late. "
            + weather + " "
            + "Unleaded is a dollar ninety-three at Silverwater.";

        string trimmed = ScriptWordCeiling.TrimToCeiling(script, 30,
            Fixtures(true, weather, "Unleaded is a dollar ninety-three at Silverwater."));

        ScriptWordCeiling.Excess(trimmed, 30).ShouldBe(0);
        trimmed.ShouldContain(weather);
        trimmed.ShouldEndWith("at Silverwater.");
        trimmed.ShouldNotContain("Forest Road");
    }

    [Fact]
    public void TrimToCeiling_keeps_required_fuel_after_spoken_form_normalization()
    {
        const string draftedFuel = "Unleaded is $1.93 at Silverwater.";
        string voicedFuel = SpokenFormNormalizer.Normalize(draftedFuel);
        string script =
            "Out west at Glenfield, Railway Parade is closed northbound. "
            + "Near Orchard Hills, Luddenham Road is closed northbound. "
            + "Through Beecroft, Pennant Hills Road is feeling the pinch, with six minutes added. "
            + "Down at Peakhurst, Forest Road is heavy going, running five minutes late. "
            + voicedFuel;

        string trimmed = ScriptWordCeiling.TrimToCeiling(script, 24,
            Fixtures(true, draftedFuel));

        ScriptWordCeiling.Excess(trimmed, 24).ShouldBe(0);
        trimmed.ShouldEndWith(voicedFuel);
        trimmed.ShouldNotContain("Forest Road");
    }

    // ARN, 2026-08-24 (non-negotiable): the station's bookends and any required weather/fuel line
    // ALWAYS air. Three consecutive test/dev reads shipped with the KIIS closer missing, every one
    // on a `prosody-trim` attempt — the trim drops TRAILING sentences and the outro is last.
    // TrimToCeiling has no concept of a locked prefix/suffix, so ScriptGenerationEngine peels the
    // station lines with StationLineSplitter, trims only the body against a ceiling reduced by what
    // they cost, and recomposes. These pin that composition, which is the contract that must hold.
    private const string Intro = "Now looking over Sydney.";
    private const string Outro = "That's KIIS traffic and weather together, powered by Acme.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Peeling_station_lines_keeps_the_closer_whatever_the_fixture_flag(bool requiredFixtures)
    {
        string script = Intro + " " + Traffic6PlusClose + " " + Outro;
        var split = StationLineSplitter.Split(script, [Intro, Outro]);
        split.PreservedLineCount.ShouldBe(2);

        int stationWords = split.Leading.Concat(split.Trailing).Sum(WordCounter.Count);
        string trimmedBody = ScriptWordCeiling.TrimToCeiling(
            split.Body, Math.Max(1, 20 - stationWords), Fixtures(requiredFixtures, Close));
        string result = split.Recompose(trimmedBody);

        result.ShouldContain(Intro, Case.Sensitive);
        result.ShouldContain("powered by Acme", Case.Sensitive);
        result.ShouldContain("Cloudy, twelve degrees", Case.Sensitive);
        WordCounter.Count(result).ShouldBeLessThan(WordCounter.Count(script));
    }

    [Fact]
    public void The_unpeeled_trim_cuts_the_closer_which_is_why_the_caller_must_peel()
    {
        // Documents the defect this composition exists to avoid: handed the whole read, the trim
        // sacrifices the trailing closer because it is neither weather nor fuel.
        string script = Intro + " " + Traffic6PlusClose + " " + Outro;

        string trimmed = ScriptWordCeiling.TrimToCeiling(script, 20, Fixtures(true, Close));

        trimmed.ShouldNotContain("powered by Acme");
    }
}
