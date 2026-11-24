using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The pre-approval audio review comparer: flags script words with no intelligible
///     counterpart in the transcript, while tolerating transcription respellings — the
///     judge is imperfect, so only clear misses may be flagged.
/// </summary>
public sealed class AudioPreReviewTests
{
    [Fact]
    public void Compare_IdenticalText_CleanFullMatch()
    {
        var r = AudioPreReview.Compare(
            "The Warringah Freeway's closed northbound at Falcon Street.",
            "The Warringah Freeway's closed northbound at Falcon Street.");

        r.Clean.ShouldBeTrue();
        r.MatchRatio.ShouldBe(1.0);
    }

    [Fact]
    public void Compare_TranscriptionRespelling_NotFlagged()
    {
        // Whisper-style respellings of correctly-said words: long shared prefix
        // ("Warringah"/"Warringer") or 1-2 character drift ("Streets"/"Street's").
        var r = AudioPreReview.Compare(
            "The Warringah Freeway is closed at Falcon Street",
            "The Warringer Freeway is closed at Falcon Streets");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_GenuineMispronunciation_Flagged()
    {
        // The real live case: 'Unleaded's' rendered so badly the transcript heard
        // 'unlettered's' — far beyond respelling tolerance.
        var r = AudioPreReview.Compare(
            "Unleaded's cheapest at Guildford",
            "unlettered's cheapest at Guildford");

        r.Clean.ShouldBeFalse();
        r.SuspectWords.ShouldContain("Unleaded's");
    }

    [Fact]
    public void Compare_MangledPlaceName_Flagged()
    {
        // 'Botany' heard as 'Bodernay' (the broken-alias rendering) must be flagged.
        var r = AudioPreReview.Compare(
            "minor delays at Botany this morning",
            "minor delays at Bodernay this morning");

        r.Clean.ShouldBeFalse();
        r.SuspectWords.ShouldContain("Botany");
    }

    [Fact]
    public void Compare_ShortFunctionWords_CarryNoSignal()
    {
        // Articles/prepositions differ freely between script and transcript.
        var r = AudioPreReview.Compare("The M4 is at a standstill", "M4 standstill");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_EmptyScript_CleanByDefinition()
    {
        var r = AudioPreReview.Compare("", "anything at all");

        r.Clean.ShouldBeTrue();
        r.MatchRatio.ShouldBe(1.0);
    }

    [Fact]
    public void Compare_MatchRatio_ReflectsMissedFraction()
    {
        // Distinct significant words: Warringah, Freeway, closed, Botany (4); Botany missing.
        var r = AudioPreReview.Compare(
            "Warringah Freeway closed Botany",
            "Warringah Freeway closed elsewhere entirely");

        r.SuspectWords.ShouldBe(["Botany"]);
        r.MatchRatio.ShouldBe(0.75);
    }

    [Fact]
    public void Compare_FinalScriptWordAbsentFromTranscript_ReportsAnUnheardTail()
    {
        // Regression for prod job c2894ffb... (2026-08-18, cairns-4ca): the WAV stopped
        // mid-"Manoora", although the persisted pre-review had already identified it.
        var r = AudioPreReview.Compare(
            "Right now showery and twenty degrees Unleaded's a dollar ninety-eight in Manoora,",
            "Right now, Showery and 20 degrees. Unleaded's $1.98 in");

        r.UnheardTailWords.ShouldBe(["Manoora"]);
        r.SuspectWords.ShouldContain("Manoora");
    }

    [Fact]
    public void Compare_MidScriptWordAbsent_IsNotAnUnheardTail()
    {
        var r = AudioPreReview.Compare(
            "Minor delays at Botany this morning with a clear final clause",
            "Minor delays at Bodernay this morning with a clear final clause");

        r.UnheardTailWords.ShouldBeEmpty();
        r.SuspectWords.ShouldContain("Botany");
    }

    [Fact]
    public void Compare_EverythingHeard_HasNoUnheardTail()
    {
        var r = AudioPreReview.Compare(
            "Minor delays at Botany this morning",
            "Minor delays at Botany this morning");

        r.UnheardTailWords.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_WholeClosingClauseMissing_ReportsEveryUnheardTailWord()
    {
        var r = AudioPreReview.Compare(
            "Traffic is easing on the Harbour Bridge and lane repairs this morning",
            "Traffic is easing on the Harbour Bridge");
        r.UnheardTailWords.ShouldBe(["lane", "repairs", "this", "morning"]);
    }
}

public sealed class AudioPreReviewNormalizationTests
{
    [Fact]
    public void Compare_TranscriberSplitsCompound_NotFlagged()
    {
        // "roadworks" is routinely transcribed as "road works" — a bigram of the transcript
        // must count as a candidate rendering (live false positive, test job 45d25809).
        var r = AudioPreReview.Compare(
            "closed for roadworks at Falcon Street",
            "closed for road works at Falcon Street");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_SpokenCurrencyWords_NeverSuspect()
    {
        // "a dollar sixty-three" is written back by the transcriber as "$1.63" — the spoken
        // number vocabulary vanishes without any pronunciation problem (live false positive,
        // test job 334d93f8).
        var r = AudioPreReview.Compare(
            "Unleaded cheapest at a dollar sixty three in Guildford",
            "Unleaded cheapest at $1.63 in Guildford");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_TranscriberFusesCompound_NotFlagged()
    {
        // The mirror of the split case, and the single biggest false positive in dev history:
        // "West Gate" comes back as "Westgate", so "West" matched on prefix and "Gate" was
        // orphaned on every Melbourne bulletin naming the road (job 8ff3e223, 2026-08-10).
        var r = AudioPreReview.Compare(
            "Point Cook Road's closed westbound at the West Gate Freeway",
            "Point Cook Road's closed westbound at the Westgate Freeway");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_FusedCompoundCarryingPossessive_NotFlagged()
    {
        // Both fixes at once, as the scripts actually write it: "The West Gate's very busy".
        var r = AudioPreReview.Compare(
            "The West Gate's very busy eastbound",
            "The Westgate's very busy eastbound");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_PossessiveOfRespeltName_NotFlagged()
    {
        // The clitic is not the part the transcriber respells, but it moved the goalposts:
        // bare "Greensborough" cleared against "Greensboro" while "Greensborough's" flagged,
        // in one bulletin (job 43382c2a).
        var r = AudioPreReview.Compare(
            "Greensborough's ramps to the Greensborough Highway are blocked",
            "Greensboro's ramps to the Greensboro Highway are blocked");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_FusedForm_DoesNotExcuseAMispronouncedNeighbourPair()
    {
        // The fused form is matched against single transcript words only. Were it matched
        // against joined transcript pairs too, "Toorak Road" ≈ "Torrac Road" would pass on
        // the longer string's wider edit budget and swallow a real vowel error.
        var r = AudioPreReview.Compare(
            "CityLink's slow between Toorak Road and Warrigal Road",
            "CityLink's slow between Torrac Road and Warrigal Road");

        r.SuspectWords.ShouldContain("Toorak");
    }

    [Fact]
    public void Compare_FusionNeighbour_MustCarrySignalItself()
    {
        // Fusing with a short function word invents matches out of unrelated vocabulary:
        // "The Eyre" joins to "theeyre", which is two edits from a passing "there".
        var r = AudioPreReview.Compare(
            "The Eyre Highway has roadworks there",
            "The Todd Highway has roadworks there");

        r.SuspectWords.ShouldContain("Eyre");
    }
}

public sealed class AudioPreReviewNumberTests
{
    [Fact]
    public void Compare_SamePriceDifferentForms_NotFlagged()
    {
        // Script digits, transcript currency symbol — same value, different writing.
        var r = AudioPreReview.Compare(
            "Unleaded's cheapest at $1.63 in Guildford",
            "unleaded's cheapest at 1.63 in Guildford");

        r.NumberMismatches.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_DollarWordBridge_NotFlagged()
    {
        // The live case: the LLM wrote "a dollar 63"; the transcriber writes "$1.63".
        var r = AudioPreReview.Compare(
            "Unleaded's cheapest at a dollar 63 in Greystanes",
            "unleaded's cheapest at $1.63 in Greystanes");

        r.NumberMismatches.ShouldBeEmpty();
        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_WrongPriceAired_Flagged()
    {
        // The error class nothing else in the pipeline verifies: the voice says a
        // DIFFERENT price than the script.
        var r = AudioPreReview.Compare(
            "Unleaded's cheapest at $1.63 in Guildford",
            "unleaded's cheapest at $1.53 in Guildford");

        r.NumberMismatches.ShouldBe(["$1.63"]);
        r.Clean.ShouldBeFalse();
    }

    [Fact]
    public void Compare_WrongTemperature_Flagged()
    {
        var r = AudioPreReview.Compare(
            "It's cloudy and 15 degrees",
            "it's cloudy and 50 degrees");

        r.NumberMismatches.ShouldBe(["15"]);
    }

    [Fact]
    public void Compare_MatchingTemperature_NotFlagged()
    {
        var r = AudioPreReview.Compare(
            "It's cloudy and 15 degrees",
            "it's cloudy and 15 degrees");

        r.Clean.ShouldBeTrue();
    }

    [Fact]
    public void Compare_NoNumbersInScript_NothingToVerify()
    {
        var r = AudioPreReview.Compare(
            "The Harbour Bridge is moving nicely",
            "the harbour bridge is moving nicely at 99 kilometres");

        r.NumberMismatches.ShouldBeEmpty();
    }
}

/// <summary>
///     Digits in a traffic bulletin are mostly road designations and numbered place names,
///     not quantities. Flagging those trains operators to ignore the gate — reported on dev
///     job ae41aa6d (2026-07-28), whose only "unconfirmed figure" was the 8 in
///     "8 Mile Bridge", which then also painted a warning onto "M80".
/// </summary>
public sealed class AudioPreReviewNumberContextTests
{
    private const string LiveScript =
        "The West Gate's under the pump citybound from 8 Mile Bridge through to Cook Street, " +
        "and Plenty Road's busy northbound at the M80 ramp. Passing clouds, 14 degrees.";

    [Fact]
    public void Compare_NumeralInsidePlaceName_NotAQuantity()
    {
        var r = AudioPreReview.Compare(
            LiveScript,
            "the west gate's under the pump citybound from Eight Mile Bridge through to Cook " +
            "Street, and Plenty Road's busy northbound at the M80 ramp. Passing clouds, 14 degrees.");

        r.NumberMismatches.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_RoadDesignation_NeverYieldsANumber()
    {
        // "M80" must not contribute 80: a transcript writing "M 80" or "Em eighty" is a
        // spelling difference, never a wrong aired value.
        var r = AudioPreReview.Compare(
            "Plenty Road's busy northbound at the M80 ramp",
            "Plenty Road's busy northbound at the M1 ramp");

        r.NumberMismatches.ShouldBeEmpty();
    }

    [Fact]
    public void Compare_TemperatureStillVerified_WhenWrong()
    {
        // The context gate must not blunt the checks that earn the gate its keep.
        var r = AudioPreReview.Compare(LiveScript, LiveScript.Replace("14 degrees", "40 degrees"));

        r.NumberMismatches.ShouldBe(["14"]);
    }

    [Theory]
    [InlineData("allow an extra 15 minutes", "allow an extra 50 minutes", "15")]
    [InlineData("about 4 kilometres affected", "about 9 kilometres affected", "4")]
    [InlineData("about 3 kays back", "about 8 kays back", "3")]
    [InlineData("unleaded at $1.63 today", "unleaded at $1.53 today", "$1.63")]
    public void Compare_UnitQualifiedQuantity_StillFlaggedWhenWrong(
        string script, string transcript, string expected)
    {
        AudioPreReview.Compare(script, transcript).NumberMismatches.ShouldBe([expected]);
    }

    [Theory]
    [InlineData("delays at the M80 ramp", "delays at the M80 ramp")]
    [InlineData("roadworks near 8 Mile Bridge", "roadworks near 9 Mile Bridge")]
    [InlineData("the A1 is clear", "the A9 is clear")]
    [InlineData("Route 31 is slow", "Route 99 is slow")]
    public void Compare_UnqualifiedDigits_AreNeverFlagged(string script, string transcript)
    {
        AudioPreReview.Compare(script, transcript).NumberMismatches.ShouldBeEmpty();
    }
}

/// <summary>
///     The Manoora incident, replayed on real bytes. Prod job
///     c2894ffb109f4c218c73e274e43665b1 (2026-08-18, cairns-4ca, 22s slot) aired a 22.000000s WAV
///     that stops mid-word: the script's final word "Manoora" is not in the audio. The script here
///     is the job's persisted ScriptText verbatim; the transcript is the AIRED WAV re-transcribed
///     (whisper small.en) during the 2026-08-19 investigation, which is why it ends "in...".
/// </summary>
public sealed class AudioPreReviewCutOffReadTests
{
    private const string AiredScript =
        "Checking out the roads...... Work is being done on a pot hole on Anderson Street. " +
        "Mulgrave Road near Earlville moving slowly with roadworks between Toogood Road and " +
        "Mccoombe Street. Wet and slippery around the place today with this weather so please " +
        "take it easy. Right now showery and twenty degrees Unleaded's a dollar ninety-eight in " +
        "Manoora,";

    private const string AiredTranscript =
        "Checking out the roads, work is being done on a pothole on Anderson Street. " +
        "Mulgrave Road near Earlville, moving slowly with roadworks between Two Good Road and " +
        "Macomb Street. Wet and slippery around the place today with this weather, so please " +
        "take it easy. Right now, Showery and 20 degrees. Unlettered's $1.98 in...";

    [Fact]
    public void Compare_TheAiredCairnsBulletin_ReportsTheWholeUnheardTrailingRun()
    {
        // Compare reports raw truth, and on these real bytes the trailing run is TWO words:
        // "Manoora" was cut off, and "Unleaded's" aired mispronounced as "unlettered's" so it has no
        // transcript counterpart either. Narrowing that to the truncation itself is
        // AudioPreReviewReport's job, not the comparer's — the stored list stays unfiltered.
        var r = AudioPreReview.Compare(AiredScript, AiredTranscript);

        r.UnheardTailWords.ShouldBe(["Unleaded's", "Manoora"]);
    }

    [Fact]
    public void For_TheAiredCairnsBulletin_WarnsEvenThoughManooraWasAClearedPronunciation()
    {
        // What actually happened: cairns-4ca carried an enabled "Manoora" verdict, so the only
        // operator-facing signal was "Audio pre-review: 1 suspect pronunciation(s) — hole. Listen
        // before approving." A clearance about how the word SOUNDS hid the fact that it never aired.
        var report = AudioPreReviewReport.For(
            AudioPreReview.Compare(AiredScript, AiredTranscript),
            ["Inkie", "Unleaded's", "arvo", "This", "Munro", "Manoora"]);

        // "Unleaded's" is cleared and drops out; "Manoora" is the anchor and cannot be muted.
        report.UnheardTailWords.ShouldBe(["Manoora"]);
        report.Message.ShouldNotContain("Unleaded");
        report.IsWarning.ShouldBeTrue();
        report.Message.ShouldContain("cut off");
        report.Message.ShouldContain("Manoora");
    }
}
