using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
/// Pins the label vocabulary against the reads that actually aired on the 2026-08-25 end-to-end
/// run. Every string here is a verbatim excerpt from a generated bulletin, not a synthetic case.
/// </summary>
public sealed class EvidenceLabelVocabularyTests
{
    [Theory]
    // Severity/extent bands that the old six-entry selector list did not cover, each taken from
    // the read it aired in.
    [InlineData("Major delays with roadworks in extended work zones, stretching about one and a half kays.")]
    [InlineData("There's a short queue for about half a kay on the Bypass at Craigieburn Bypass.")]
    [InlineData("Roadworks are creating an extended work zone stretching about three kays.")]
    [InlineData("In Carrara, moderate queues run for about three kays between Garden Grove and Chisholm Road.")]
    // The band the list DID cover, still covered.
    [InlineData("The Smith Street Motorway has a minor impact at Gaven, over a short stretch.")]
    // Bare field name - the subtler leak, ordinary English on its face.
    [InlineData("Minor impacts are reported on Stuart Road at roadworks.")]
    [InlineData("Palmers Road and the Eastern Freeway are both seeing minor impacts from roadworks.")]
    // HERE incident-type code read aloud.
    [InlineData("Argyle Street has a minor impact from a planned event.")]
    public void CountLeaks_CatchesEveryLabelThatAired(string aired)
    {
        EvidenceLabelVocabulary.CountLeaks(aired).ShouldBeGreaterThan(0);
    }

    [Theory]
    // Road-feel copy from the same run: what the writer is supposed to produce instead.
    [InlineData("Old Windsor Road at Old Toongabbie is taking an extra four minutes.")]
    [InlineData("Copeland Street in Liverpool has a bit of a queue.")]
    [InlineData("The M8 is running clear today.")]
    [InlineData("Citybound Salisbury Highway is closed for about half a kay, with six minutes added.")]
    [InlineData("Asphalt works with contraflow on Porter Street.")]
    [InlineData("Clear skies, 16 degrees.")]
    public void CountLeaks_LeavesRoadFeelCopyAlone(string clean)
    {
        EvidenceLabelVocabulary.CountLeaks(clean).ShouldBe(0);
    }

    [Fact]
    public void CountLeaks_CountsACompoundLabelOnce()
    {
        // "minor impact" must not also score as bare "impact" - a single leak, a single penalty.
        EvidenceLabelVocabulary.CountLeaks("The road has a minor impact.").ShouldBe(1);
    }

    [Fact]
    public void FirstLeak_NamesTheMostSpecificLabelPresent()
    {
        EvidenceLabelVocabulary.FirstLeak("There is a minor impact here.").ShouldBe("minor impact");
    }

    [Fact]
    public void CountLeaks_ScoresEachLeakSeparatelyInOneRead()
    {
        // The live sydney read: "Major delays ... extended work zones ... A minor impact ... minor
        // roadworks impacts ... a minor impact from a planned event."
        const string airedSydney =
            "Major delays with roadworks in extended work zones, stretching about one and a half "
            + "kays, on the ramp to Alfred Street North in Cammeray. A minor impact continues for "
            + "about half a kay on Church Street in Harris Park. Johnston Street in Annandale has "
            + "minor roadworks impacts, while Argyle Street has a minor impact from a planned event.";

        // major delays, extended work zone, minor impact, impacts, minor impact, planned event
        EvidenceLabelVocabulary.CountLeaks(airedSydney).ShouldBe(6);
    }

    [Fact]
    public void RoadClosedIsDeliberatelyNotALeak()
    {
        // Both a label and the natural way to say it; the closure detectors own honesty here.
        EvidenceLabelVocabulary.CountLeaks("London Circuit has the road closed at Petrie Plaza.")
            .ShouldBe(0);
    }

    [Fact]
    public void SeverityLabels_AndFieldNames_AreAllMatchedByThePattern()
    {
        // No entry may be listed but unmatched: the list is documentation, the regex is behaviour,
        // and a drift between them is exactly how the original six-entry list rotted.
        foreach (string label in EvidenceLabelVocabulary.SeverityLabels)
            EvidenceLabelVocabulary.CountLeaks($"The road has {label} today.").ShouldBeGreaterThan(0, label);

        foreach (string field in EvidenceLabelVocabulary.FieldNames)
            EvidenceLabelVocabulary.CountLeaks($"The road has {field} today.").ShouldBeGreaterThan(0, field);
    }

    [Fact]
    public void EveryBandTheEmitterProducesHasASpokenForm()
    {
        // The translator falls through unmapped values unchanged, which is right for real prose but
        // would silently ship a band we forgot to map. This is the gate that stops that.
        foreach (string band in EvidenceLabelVocabulary.SeverityLabels)
            RoadFeelTranslator.KnownBands.ShouldContain(band);
    }

    [Fact]
    public void NoSpokenFormIsItselfALabel()
    {
        // The whole point is that the writer never SEES a label. A translation that smuggled one
        // back in would defeat the change while looking like it worked.
        foreach (string band in RoadFeelTranslator.KnownBands)
        foreach (string phrasing in RoadFeelTranslator.PhrasingsFor(band))
        {
            EvidenceLabelVocabulary.CountLeaks(phrasing)
                .ShouldBe(0, $"'{band}' can be spoken as '{phrasing}', which still carries a label");
        }
    }

    [Fact]
    public void SpeakHandsOverOnePhrasingWithNoSeparator()
    {
        // Both separators were tried on 2026-08-25 and BOTH aired: "/" as "traffic's barely
        // noticeable but a touch slower", " or " as "there's a slight queue or brief delay on the
        // Craigieburn Bypass". The writer reads options as a compound description, not a choice, so
        // the value it receives must contain exactly one phrasing and nothing to join.
        foreach (string band in RoadFeelTranslator.KnownBands)
        {
            string spoken = RoadFeelTranslator.Speak(band, "Some Road");

            // Exact membership is the whole contract and is stronger than scanning for separators:
            // if the value IS one pool entry, nothing was joined to it. Scanning is also wrong —
            // "an extra minute or two" is a legitimate phrasing that contains " or ".
            RoadFeelTranslator.PhrasingsFor(band).ShouldContain(spoken);
        }
    }

    [Fact]
    public void SpeakIsStableForARoadAcrossRuns()
    {
        // Replay fixtures and successive bulletins for one market must stay comparable, so the
        // rotation cannot use the per-process-randomised string.GetHashCode.
        RoadFeelTranslator.Speak("minor delays", "Pacific Motorway")
            .ShouldBe(RoadFeelTranslator.Speak("minor delays", "Pacific Motorway"));
    }

    [Fact]
    public void SpeakSpreadsPhrasingsAcrossDifferentRoads()
    {
        // The cost of picking one phrasing is cross-road repetition, so the pick must at least vary.
        // The deeper defect ("a touch slower" three times in a read) stays owned by
        // SentenceShapeDetector and RepeatedStatePhraseDetector, which demonstrably fire on it.
        string[] roads =
        [
            "Church Street", "Johnston Street", "Argyle Street", "Pacific Motorway",
            "Gympie Road", "Warrego Highway", "Smith Street Motorway", "Palmers Road",
        ];
        var spoken = roads.Select(r => RoadFeelTranslator.Speak("minor delays", r)).ToList();

        spoken.Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void SpeakLeavesRealProseAlone()
    {
        // Unmapped values are far more likely to be incident-description prose than a missed band.
        const string prose = "Slow moving traffic through the interchange";
        RoadFeelTranslator.Speak(prose, "Pacific Motorway").ShouldBe(prose);
    }
}
