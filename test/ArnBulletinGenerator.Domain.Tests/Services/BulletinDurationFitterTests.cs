using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class BulletinDurationFitterTests
{
    // Three sentences. Character offsets (0-based), with 1.0s per character, give exact
    // sentence end-times: "One two three." ends at 14s, "Four five six." at 29s,
    // "Seven eight nine." at 47s.
    private const string ThreeSentenceScript = "One two three. Four five six. Seven eight nine.";

    /// <summary>Builds a uniform per-character alignment at <paramref name="secondsPerChar"/>.</summary>
    private static AudioAlignment UniformAlignment(string script, double secondsPerChar = 1.0)
    {
        var chars = new string[script.Length];
        var starts = new double[script.Length];
        var ends = new double[script.Length];
        for (int i = 0; i < script.Length; i++)
        {
            chars[i] = script[i].ToString();
            starts[i] = i * secondsPerChar;
            ends[i] = (i + 1) * secondsPerChar;
        }

        return new AudioAlignment(chars, starts, ends);
    }

    /// <summary>The read's weather/fuel lines as the workflow would have tagged them.</summary>
    private static FixtureLines Fixtures(bool required, params string[] lines) =>
        FixtureLines.From(
            lines.Select((line, index) => new Domain.Contracts.DraftedSentence(index + 1, line, [])
            {
                Section = BulletinSegment.Weather
            }),
            required)!;

    [Fact]
    public void MapSentenceSpans_MapsEachSentenceToItsAlignmentTimes()
    {
        var spans = BulletinDurationFitter.MapSentenceSpans(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript));

        spans.Count.ShouldBe(3);
        spans[0].Text.ShouldBe("One two three.");
        spans[0].StartSeconds.ShouldBe(0.0);
        spans[0].EndSeconds.ShouldBe(14.0);
        spans[1].EndSeconds.ShouldBe(29.0);
        spans[2].EndSeconds.ShouldBe(47.0);
    }

    [Fact]
    public void MapSentenceSpans_ReturnsEmpty_ForEmptyAlignment()
    {
        var spans = BulletinDurationFitter.MapSentenceSpans(
            ThreeSentenceScript, new AudioAlignment([], [], []));

        spans.ShouldBeEmpty();
    }

    [Fact]
    public void FitByTrimming_DropsTheGoodRunSweep_BeforeTheOnlyIncident()
    {
        // Live 2026-08-21, Port Lincoln 15s: the fitter dropped the bulletin's only incident (a
        // closure leading the read) and aired "roads are clear" because the old objective was
        // editorially blind. At equal dropped count, the sweep must go first.
        const string script =
            "Railway Place in Port Lincoln is closed while asphalt works are underway. "
            + "Tod Highway, Port Kenny Road, Stringers Highway and Sectus Tanks Road are clear. "
            + "Cloudy skies, 13 degrees. In Cummins, unleaded is $2.18 and diesel is $2.50.";
        var alignment = UniformAlignment(script, 0.27);

        // Budget fits three of the four sentences but not all: ~39.4s of speech into ~34s.
        string? trimmed = BulletinDurationFitter.FitByTrimming(script, alignment, targetSeconds: 32.0,
            fixtures: Fixtures(false, "Cloudy skies, 13 degrees.", "In Cummins, unleaded is $2.18 and diesel is $2.50."));

        trimmed.ShouldNotBeNull();
        trimmed.ShouldContain("Railway Place in Port Lincoln is closed");
        trimmed.ShouldNotContain("are clear");
    }

    [Fact]
    public void FitByTrimming_DropsTrailingSentence_OnOverrun()
    {
        // 47s of audio into a 30s slot: keep the two leading (priority) sentences (29s <= 32
        // budget with the default 2s overrun allowance), drop the trailing one.
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript), targetSeconds: 30.0);

        // The old prefix expectation was the defect: keeping a later short item is better than
        // preserving an arbitrary leading prefix.
        trimmed.ShouldBe("One two three. Seven eight nine.");
    }

    // Same three sentences, each carrying an inline ElevenLabs v3 delivery tag. The tags are
    // silent, so the alignment still runs one second per character over the tagged text.
    private const string TaggedScript =
        "[urgent] One two three. [emphatic] Four five six. [calm] Seven eight nine.";

    [Fact]
    public void MapSentenceSpans_AttachesLeadingDeliveryTagToItsSentence()
    {
        var spans = BulletinDurationFitter.MapSentenceSpans(TaggedScript, UniformAlignment(TaggedScript));

        spans.Count.ShouldBe(3);
        // Text stays the SPOKEN words (word counting, calibration and provenance all read it).
        spans[0].Text.ShouldBe("One two three.");
        spans[0].SourceText.ShouldBe("[urgent] One two three.");
        spans[1].SourceText.ShouldBe("[emphatic] Four five six.");
        spans[2].SourceText.ShouldBe("[calm] Seven eight nine.");
    }

    [Fact]
    public void MapSentenceSpans_SourceTextDefaultsToSpokenText_WhenScriptHasNoTags()
    {
        var spans = BulletinDurationFitter.MapSentenceSpans(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript));

        spans[0].SourceText.ShouldBe(spans[0].Text);
    }

    [Fact]
    public void FitByTrimming_KeepsDeliveryTagsOnTheSentencesItKeeps()
    {
        // The trimmed read is re-synthesized verbatim, so dropping the tags here would silently
        // air every overrunning bulletin with no delivery direction at all. Tags are silent but
        // still occupy characters: the sentences end at 23s, 49s and 74s on this alignment, so a
        // 50s slot (52s with the default allowance) keeps the first two.
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            TaggedScript, UniformAlignment(TaggedScript), targetSeconds: 50.0);

        trimmed.ShouldBe("[emphatic] Four five six. [calm] Seven eight nine.");
    }

    [Fact]
    public void FitByTrimming_DropsMultipleTrailingSentences_ForLargeOverrun()
    {
        // 16s slot (budget 18 with the default 2s allowance): only the first sentence (14s) fits.
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript), targetSeconds: 16.0);

        trimmed.ShouldBe("Seven eight nine.");
    }

    [Fact]
    public void FitByTrimming_ReturnsNull_WhenFullScriptAlreadyFits()
    {
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript), targetSeconds: 60.0);

        trimmed.ShouldBeNull();
    }

    [Fact]
    public void FitByTrimming_ReturnsNull_ForSingleSentence()
    {
        const string single = "Only one sentence here.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            single, UniformAlignment(single), targetSeconds: 5.0);

        trimmed.ShouldBeNull();
    }

    [Fact]
    public void FitByTrimming_KeepsAtLeastOneSentence_WhenEvenFirstExceedsBudget()
    {
        // Budget below the first sentence's 14s end: best-effort keeps exactly one sentence
        // (the micro-stretch closes the remainder), still dropping the other two.
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript), targetSeconds: 8.0);

        trimmed.ShouldBe("One two three.");
    }

    [Fact]
    public void SelectFittingCount_ReturnsGreedyPrefixWithinBudget()
    {
        var spans = new List<SentenceSpan>
        {
            new("a", 0, 10),
            new("b", 10, 20),
            new("c", 20, 33),
        };

        // Budget = 30 + 0.75 = 30.75: first two fit (20 <= 30.75), third (33) does not.
        BulletinDurationFitter.SelectFittingCount(spans, targetSeconds: 30, maxOverrunSeconds: 0.75).ShouldBe(2);
    }

    [Fact]
    public void SelectFittingCount_ClampsToMinSentences()
    {
        var spans = new List<SentenceSpan> { new("a", 0, 40), new("b", 40, 80) };

        // Nothing fits a ~10s budget, but at least one sentence is always kept.
        BulletinDurationFitter.SelectFittingCount(spans, targetSeconds: 10, maxOverrunSeconds: 0.75).ShouldBe(1);
    }

    // ── Close protection (weather/fuel tail) ────────────────────────────────────────────────
    // The prompt's fill order is traffic, then weather, then fuel, so the close is always LAST.
    // A naive trailing-sentence trim therefore sacrifices it first and lands the read flat on a
    // mid-list traffic item — observed live on published job 19be05d7. The close is budgeted for
    // first; the overrun comes out of the traffic instead.

    // "One two three." ends 14s, "Four five six." 29s, "Sunny, twelve degrees." 52s.
    private const string ScriptWithClose = "One two three. Four five six. Sunny, twelve degrees.";

    [Fact]
    public void FitByTrimming_KeepsWeatherClose_AndDropsTrafficInstead()
    {
        // 52s into a 30s slot (32s budget). The close costs 23s, leaving 9s for traffic: only the
        // first traffic sentence fits. The plain prefix trim would instead have kept both traffic
        // sentences (29s <= 32s) and dropped the close entirely.
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            ScriptWithClose, UniformAlignment(ScriptWithClose), targetSeconds: 30.0,
            fixtures: Fixtures(false, "Sunny, twelve degrees."));

        trimmed.ShouldBe("One two three. Sunny, twelve degrees.");
    }

    [Fact]
    public void FitByTrimming_KeepsMultiSentenceClose_WhenWeatherAndFuelBothAir()
    {
        // Weather AND fuel form one trailing locked run — both are the close, both are kept.
        const string script = "One two three. Four five six. Cloudy, twelve degrees. Unleaded is cheap.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 50.0,
            fixtures: Fixtures(false, "Cloudy, twelve degrees.", "Unleaded is cheap."));

        trimmed.ShouldBe("One two three. Cloudy, twelve degrees. Unleaded is cheap.");
    }

    [Fact]
    public void FitByTrimming_DropsClose_WhenItCannotFitTheSlotAtAll()
    {
        // Close alone (23s) exceeds the whole 5s budget: protecting it would drop every traffic
        // item, so this degrades to the plain leading-prefix trim.
        const string script = "One two three. Sunny, twelve degrees.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 5.0, maxOverrunSeconds: 0.0,
            fixtures: Fixtures(false, "Sunny, twelve degrees."));

        trimmed.ShouldBe("One two three.");
    }

    [Fact]
    public void FitByTrimming_UsesPlainPrefixTrim_WhenEverySentenceIsLocked()
    {
        // No traffic to sacrifice, so there is no protectable close — plain prefix trim.
        // "Sunny today." ends 12s, "Twelve degrees now." 32s, "Diesel is cheap." 49s.
        const string script = "Sunny today. Twelve degrees now. Diesel is cheap.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 20.0,
            fixtures: Fixtures(false, "Sunny today.", "Twelve degrees now.", "Diesel is cheap."));

        trimmed.ShouldBe("Twelve degrees now.");
    }

    // Regression, deployed dev job 71568e4eac (Port Lincoln, 15s slot, weather AND fuel marked
    // ALWAYS INCLUDE): the required fuel line survived the writer, the detectors and the safety
    // review, then the pre-synthesis trim deleted it, because when the close-protecting branch
    // cannot shed enough traffic the trim degrades to a prefix cut that drops the close. For a
    // station that requires the section that degradation is a contract breach, not a preference.
    [Fact]
    public void FitByTrimming_KeepsRequiredClose_WhenTrafficCannotBeShedFurther()
    {
        // Without the required flag the preferred close may be dropped when it cannot coexist with
        // the minimum traffic floor; the new subset selector keeps the least-content sentence.
        const string script = "One two three. Cloudy, twelve degrees. Unleaded is cheap.";

        BulletinDurationFitter.FitByTrimming(
                script, UniformAlignment(script), targetSeconds: 20.0, maxOverrunSeconds: 0.0,
                fixtures: Fixtures(false, "Cloudy, twelve degrees.", "Unleaded is cheap."))
            .ShouldBe("Unleaded is cheap.");

        BulletinDurationFitter.FitByTrimming(
                script, UniformAlignment(script), targetSeconds: 20.0, maxOverrunSeconds: 0.0,
                fixtures: Fixtures(true, "Cloudy, twelve degrees.", "Unleaded is cheap."))
            .ShouldBeNull();
    }

    [Fact]
    public void FitByTrimming_ShedsTrafficToTheFloor_RatherThanDropARequiredClose()
    {
        // Three traffic sentences and a close that cannot coexist inside the budget: the required
        // close is kept and traffic falls to the single-sentence floor.
        const string script =
            "One two three. Four five six. Seven eight nine. Cloudy, twelve degrees. Unleaded is cheap.";

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 10.0, maxOverrunSeconds: 0.0,
            fixtures: Fixtures(true, "Cloudy, twelve degrees.", "Unleaded is cheap."));

        trimmed.ShouldBe("One two three. Cloudy, twelve degrees. Unleaded is cheap.");
    }

    // ── The station's outro survives the trim ──────────────────────────────
    //
    // Regression: a bookended read that overran was fitted by dropping trailing sentences, which
    // deleted the closer — the last thing the listener hears and the entire point of the feature.
    // Observed live on dev job 7a33aa7160cd (adelaide, 93 words trimmed to 82), where
    // "Closer only for Adelaide." vanished with nothing in the timeline naming it.

    [Fact]
    public void FitByTrimming_KeepsTheLockedOutro_AndTakesTheOverrunFromTraffic()
    {
        // "One two three." ends 14s, "Four five six." 29s, "Bye now." 38s. A 20s slot cannot hold
        // all three, and without the lock the trim would drop "Bye now." — the closer.
        const string script = "One two three. Four five six. Bye now.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 20.0, maxOverrunSeconds: 2.0,
            lockedSuffix: "Bye now.");

        trimmed.ShouldBe("One two three. Bye now.");
    }

    [Fact]
    public void FitByTrimming_LocksTheOutro_OnTopOfTheWeatherFuelClose()
    {
        // Both the close and the closer are protected; the traffic items absorb the overrun.
        const string script = "One two three. Four five six. Sunny, twelve degrees. Bye now.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 45.0, maxOverrunSeconds: 2.0,
            lockedSuffix: "Bye now.", fixtures: Fixtures(false, "Sunny, twelve degrees."));

        trimmed.ShouldBe("Four five six. Sunny, twelve degrees. Bye now.");
    }

    [Fact]
    public void FitByTrimming_LocksAMultiSentenceOutro_Wholesale()
    {
        const string script = "One two three. Four five six. That is all. Stay safe.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 30.0, maxOverrunSeconds: 2.0,
            lockedSuffix: "That is all. Stay safe.");

        trimmed.ShouldBe("One two three. That is all. Stay safe.");
    }

    [Fact]
    public void FitByTrimming_MatchesTheOutroThroughPunctuationCasingAndDeliveryTags()
    {
        // The synthesised script carries inline v3 tags and the splitter may normalise punctuation,
        // so the match is on letters and digits only — an exact compare would miss and silently
        // stop protecting the closer.
        const string script = "One two three. Four five six. [warm]Bye now![/warm]";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 20.0, maxOverrunSeconds: 2.0,
            lockedSuffix: "bye now");

        trimmed.ShouldNotBeNull();
        trimmed!.ShouldContain("Bye now!");
    }

    [Fact]
    public void FitByTrimming_DoesNotLock_WhenTheOutroIsOnlyPartiallyPresent()
    {
        // Half a closer is worse than none: locking a fragment would air a truncated sign-off. The
        // suffix must be accounted for in full or the plain trim applies.
        const string script = "One two three. Four five six. Bye now.";
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, UniformAlignment(script), targetSeconds: 20.0, maxOverrunSeconds: 2.0,
            lockedSuffix: "Something else entirely. Bye now.");

        trimmed.ShouldBe("Four five six.");
    }

    [Fact]
    public void FitByTrimming_NoLockedSuffix_KeepsTheHistoricalBehaviour()
    {
        // A station without an outro must trim exactly as it did before the lock existed.
        string? trimmed = BulletinDurationFitter.FitByTrimming(
            ThreeSentenceScript, UniformAlignment(ThreeSentenceScript), targetSeconds: 30.0,
            lockedSuffix: null);

        trimmed.ShouldBe("One two three. Seven eight nine.");
    }

    // ── Locked intro: always kept, never the sentence the keep-floor "saves" ────────────────

    [Fact]
    public void FitByTrimming_CollapseWithLockedIntro_KeepsARealTrafficSentenceNotJustTheIntro()
    {
        // The weather-only collapse (dev 9bb43d88/dce23ad1): intro + weather close + outro
        // survived while every traffic sentence was shed, because the intro satisfied the
        // one-sentence floor. With the intro locked, the floor must save a TRAFFIC sentence.
        const string script =
            "Now looking over Sydney. Carrington Road is closed near Randwick. " +
            "Mitchell Road is blocked at Maddox Street. Showery, fourteen degrees. " +
            "That's KIIS traffic and weather together.";
        var alignment = UniformAlignment(script, secondsPerChar: 0.2);

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, alignment, targetSeconds: 5.0, maxOverrunSeconds: 0.0,
            lockedSuffix: "That's KIIS traffic and weather together.",
            fixtures: Fixtures(true, "Showery, fourteen degrees."),
            lockedPrefix: "Now looking over Sydney.");

        trimmed.ShouldNotBeNull();
        trimmed.ShouldContain("Now looking over Sydney.");
        trimmed.ShouldContain("Carrington Road is closed near Randwick.");
        trimmed.ShouldContain("Showery, fourteen degrees.");
        trimmed.ShouldContain("That's KIIS traffic and weather together.");
    }

    // ── Fragment guard: a kept traffic sentence must be able to stand alone ─────────────────

    [Fact]
    public void FitByTrimming_DanglingTrafficFragment_IsDroppedNotSplicedIntoTheClose()
    {
        // Dev job b7c7b83b aired "The M4 and Western Motorway are Showery, fourteen degrees."
        const string script =
            "Now looking over Sydney. Carrington Road is closed near Randwick. " +
            "The M4 and Western Motorway are. Showery, fourteen degrees. " +
            "That's KIIS traffic and weather together.";
        var alignment = UniformAlignment(script, secondsPerChar: 0.2);

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, alignment, targetSeconds: 20.0, maxOverrunSeconds: 0.0,
            lockedSuffix: "That's KIIS traffic and weather together.",
            fixtures: Fixtures(true, "Showery, fourteen degrees."),
            lockedPrefix: "Now looking over Sydney.");

        trimmed.ShouldNotBeNull();
        trimmed.ShouldNotContain("Motorway are Showery");
        trimmed.ShouldNotContain("The M4 and Western Motorway are.");
    }

    [Fact]
    public void FitByTrimming_LockedDanglingOutro_IsNeverDropped()
    {
        // A station outro may dangle by config ("…together, powered by"); only TRAFFIC
        // fragments are guarded.
        const string script =
            "Carrington Road is closed near Randwick. Mitchell Road is blocked at Maddox Street. " +
            "Showery, fourteen degrees. That's KIIS traffic and weather together, powered by";
        var alignment = UniformAlignment(script, secondsPerChar: 0.25);

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            script, alignment, targetSeconds: 15.0, maxOverrunSeconds: 0.0,
            lockedSuffix: "That's KIIS traffic and weather together, powered by",
            fixtures: Fixtures(true, "Showery, fourteen degrees."));

        trimmed.ShouldNotBeNull();
        trimmed.ShouldEndWith("powered by");
    }
    [Fact]
    public void FitByTrimming_DropsLongMiddleSentence_AndKeepsLaterShortSentences()
    {
        var spans = new[]
        {
            new SentenceSpan("First short item.", 0, 5),
            new SentenceSpan("A very long middle item that must be dropped.", 5, 35),
            new SentenceSpan("Later short item one.", 35, 40),
            new SentenceSpan("Later short item two.", 40, 45)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 15.0, maxOverrunSeconds: 0.0);

        trimmed.ShouldBe("First short item. Later short item one. Later short item two.");
    }

    [Fact]
    public void FitByTrimming_2c2e609aShape_KeepsAtLeast55WordsNearBudget()
    {
        const string first = "Alpha bravo charlie delta echo foxtrot golf hotel india juliet.";
        const string second = "Kilo lima mike november oscar papa quebec romeo sierra tango uniform.";
        const string middle = "Victor whiskey xray yankee zulu amber bronze copper crimson denim emerald fawn granite hazel indigo jade khaki lilac magenta navy.";
        const string fourth = "Navy ochre peach quartz ruby scarlet silver teal umber.";
        const string fifth = "Violet walnut xanadu yellow azure beige cobalt citrine.";
        const string sixth = "Dove ebony flax gold ivory jade.";
        var spans = new[]
        {
            new SentenceSpan(first, 0, 4.1),
            new SentenceSpan(second, 4.1, 8.2),
            new SentenceSpan(middle, 8.2, 16.7),
            new SentenceSpan(fourth, 16.7, 20.7),
            new SentenceSpan(fifth, 20.7, 24.2),
            new SentenceSpan(sixth, 24.2, 28.2)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 22.0, maxOverrunSeconds: 2.55);

        trimmed.ShouldNotBeNull();
        WordCounter.Count(trimmed!).ShouldBeGreaterThanOrEqualTo(55);
        // The selected sentence durations total 24.2s: within 1.5s below the 24.55s fit budget.
        var kept = spans.Where(span => trimmed!.Contains(span.Text, StringComparison.Ordinal));
        kept.Sum(span => span.EndSeconds - span.StartSeconds).ShouldBe(24.2, 0.001);
    }

    [Fact]
    public void FitByTrimming_ReturnsNull_WhenNoSubsetReachesNaturalReadFloor()
    {
        var spans = new[]
        {
            new SentenceSpan("Six second item.", 0.0, 6.0),
            new SentenceSpan("Seven second item one.", 6.0, 13.0),
            new SentenceSpan("Seven second item two.", 13.0, 20.0),
            new SentenceSpan("Seven second item three.", 20.0, 27.0),
            new SentenceSpan("Seven second item four.", 27.0, 34.0)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 30.0, maxOverrunSeconds: 3.75, minSeconds: 29.7);

        trimmed.ShouldBeNull();
    }

    [Fact]
    public void FitByTrimming_WithZeroNaturalReadFloor_PreservesDropBehaviour()
    {
        var spans = new[]
        {
            new SentenceSpan("Six second item.", 0.0, 6.0),
            new SentenceSpan("Seven second item one.", 6.0, 13.0),
            new SentenceSpan("Seven second item two.", 13.0, 20.0),
            new SentenceSpan("Seven second item three.", 20.0, 27.0),
            new SentenceSpan("Seven second item four.", 27.0, 34.0)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 30.0, maxOverrunSeconds: 3.75, minSeconds: 0.0);

        trimmed.ShouldNotBeNull();
        trimmed.ShouldNotContain("Six second item.");
    }

    [Fact]
    public void FitByTrimming_ReturnsNull_WhenAllSingleDropsRemainBelowNaturalReadFloor()
    {
        var spans = new[]
        {
            new SentenceSpan("Five second item.", 0.0, 5.0),
            new SentenceSpan("Six second item.", 5.0, 11.0),
            new SentenceSpan("Seven second item.", 11.0, 18.0),
            new SentenceSpan("Eight second item one.", 18.0, 26.0),
            new SentenceSpan("Eight second item two.", 26.0, 34.0)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 30.0, maxOverrunSeconds: 3.75, minSeconds: 29.7);

        trimmed.ShouldBeNull();
    }

    [Fact]
    public void FitByTrimming_SelectsDropThatClearsNaturalReadFloor()
    {
        var spans = new[]
        {
            new SentenceSpan("Four second item.", 0.0, 4.0),
            new SentenceSpan("Seven second item.", 4.0, 11.0),
            new SentenceSpan("Eight second item one.", 11.0, 19.0),
            new SentenceSpan("Eight second item two.", 19.0, 27.0),
            new SentenceSpan("Eight second item three.", 27.0, 35.0)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 30.0, maxOverrunSeconds: 3.75, minSeconds: 29.7);

        trimmed.ShouldBe(
            "Seven second item. Eight second item one. Eight second item two. Eight second item three.");
    }

    [Fact]
    public void FitByTrimming_RequiredClose_AllowsFloorOverrunBelowNaturalReadFloor()
    {
        var spans = new[]
        {
            new SentenceSpan("Traffic item one.", 0.0, 6.0),
            new SentenceSpan("Traffic item two.", 6.0, 13.0),
            new SentenceSpan("Traffic item three.", 13.0, 20.0),
            new SentenceSpan("Traffic item four.", 20.0, 27.0),
            new SentenceSpan("Required close.", 27.0, 41.5)
        };

        string? trimmed = BulletinDurationFitter.FitByTrimming(
            spans, targetSeconds: 30.0, maxOverrunSeconds: 3.75,
            fixtures: Fixtures(true, "Required close."), minSeconds: 29.7);

        trimmed.ShouldBe("Traffic item one. Required close.");
    }

}
