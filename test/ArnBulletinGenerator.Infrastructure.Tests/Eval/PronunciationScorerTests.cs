using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Deterministic, offline tests for the pronunciation scorer — crafted (script, transcript) pairs,
///     no Whisper, no audio.
/// </summary>
public sealed class PronunciationScorerTests
{
    [Fact]
    public void CleanTranscript_AllTermsIntelligible()
    {
        var result = PronunciationScorer.Evaluate(
            "Heavy delays on the Warringah Freeway and the M4 this morning.",
            "heavy delays on the warringah freeway and the m4 this morning");

        result.Passed.ShouldBeTrue($"unexpected suspects: {string.Join(", ", result.SuspectTerms)}");
        result.TermsChecked.ShouldBeGreaterThan(0);
        result.TermAccuracy.ShouldBe(1.0);
    }

    [Fact]
    public void GarbledRoadName_IsFlaggedAsSuspect()
    {
        var result = PronunciationScorer.Evaluate(
            "Expect heavy delays on the Warringah Freeway.",
            "expect heavy delays on the wirringer parkway"); // mangled name

        result.Passed.ShouldBeFalse();
        result.SuspectTerms.ShouldContain("Warringah Freeway");
    }

    [Fact]
    public void MinorTranscriptionVariant_StillMatchesViaFuzzy()
    {
        // "vale" → "vail" is a one-letter slip, not a mispronunciation.
        var result = PronunciationScorer.Evaluate(
            "Crash on Mona Vale Road causing delays.",
            "crash on mona vail road causing delays");

        result.Passed.ShouldBeTrue($"unexpected suspects: {string.Join(", ", result.SuspectTerms)}");
    }

    [Fact]
    public void WordErrorRate_ZeroForIdentical_PositiveForSubstitution()
    {
        PronunciationScorer.Evaluate("the m4 is closed", "the m4 is closed")
            .WordErrorRate.ShouldBe(0.0);

        PronunciationScorer.Evaluate("the m4 is closed", "the m4 was closed")
            .WordErrorRate.ShouldBe(0.25, 0.001); // 1 of 4 words
    }

    [Fact]
    public void EmptyTranscript_AllTermsSuspect()
    {
        var result = PronunciationScorer.Evaluate("Delays on the Pacific Motorway.", "");

        result.Passed.ShouldBeFalse();
        result.SuspectTerms.ShouldContain("Pacific Motorway");
        result.WordErrorRate.ShouldBe(1.0);
    }

    [Fact]
    public void KnownSuburb_FromData_IsCheckedAndCanBeFlagged()
    {
        // "Woolloomooloo" is a bare single-word suburb the regexes miss; it is supplied from the data.
        string[] knownPlaces = ["Woolloomooloo"];

        var clean = PronunciationScorer.Evaluate(
            "Heavy delays through Woolloomooloo this morning.",
            "heavy delays through woolloomooloo this morning",
            knownPlaces);
        clean.TermsChecked.ShouldBeGreaterThan(0);
        clean.Passed.ShouldBeTrue($"unexpected suspects: {string.Join(", ", clean.SuspectTerms)}");

        var garbled = PronunciationScorer.Evaluate(
            "Heavy delays through Woolloomooloo this morning.",
            "heavy delays through wahla mahla this morning",
            knownPlaces);
        garbled.Passed.ShouldBeFalse();
        garbled.SuspectTerms.ShouldContain("Woolloomooloo");
    }
}
