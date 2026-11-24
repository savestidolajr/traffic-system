using System.Diagnostics;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Stages.Conversational;

/// <summary>
///     The deterministic floor under the conversational workflow.
///     <para>
///         That workflow deliberately drops machine-checkable citations: ids cost roughly half the
///         tokens and latency of a bulletin and bought provenance a human reviewer did not need. The
///         trade is real, and the first live run showed its price. Adelaide's data carried
///         <c>extentLength = about half a kay</c> and <c>delayMinutes = 1</c>; the read said "making
///         it about a kay slower around the area", doubling the extent AND stating a distance as a
///         delay. The editor returned <c>{"feedback":[],"approved":true}</c>.
///     </para>
///     <para>
///         The answer is not to restore the id apparatus but to stop asking a model to do arithmetic
///         a string comparison can do. Every quantity ARN airs arrives pre-worded in the evidence
///         precisely so it can be checked by equality, so a figure in the read that appears in no
///         supplied value is a fabrication, full stop, and no judgement is involved.
///     </para>
/// </summary>
public static class ConversationalChecks
{
    /// <summary>
    ///     Quantity shapes that must trace back to a supplied value. Deliberately narrow: each
    ///     matches a form ARN actually airs, so an unmatched hit is a real defect rather than a
    ///     wording variation. Anything not matched here is left to the editor's judgement.
    /// </summary>
    private static readonly Regex[] QuantityPatterns =
    [
        // "about half a kay", "about a kay", "three kays"
        new(@"\b(?:about\s+)?(?:half\s+a|a|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+kays?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "a dollar ninety-three", "two dollars forty-four"
        new(@"\b(?:a\s+dollar|\w+\s+dollars)\s+[a-z-]+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "13 degrees", "sixteen degrees"
        new(@"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\s+degrees\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    /// <summary>
    ///     Delay figures need their own rule. The normalizer renders <c>delayMinutes</c> as the bare
    ///     spoken number ("three") so a presenter can say it, but the read naturally says
    ///     "three-minute delays" or "about three minutes slower". Requiring the whole phrase to
    ///     appear verbatim rejected correct, grounded copy on the first run. What must match is the
    ///     NUMBER; the words around it are the composer's to choose.
    ///     <para>
    ///         The quantity is captured loosely and resolved afterwards by
    ///         <see cref="TryParseSpokenNumber" />, rather than listed in the pattern. An
    ///         alternation of number words is a list that silently stops covering things: this one
    ///         ran out at "twelve", so "forty minutes" against data reading eleven was never
    ///         examined at all. A word that resolves to no number is not a figure and is skipped.
    ///     </para>
    /// </summary>
    private static readonly Regex MinutesPattern = new(
        @"\b(?<n>[a-z]+(?:-[a-z]+)?|\d+)[\s-]minutes?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    ///     The spoken fractions <c>FormatPace</c> emits for a measured pace ratio. A fraction is a
    ///     figure, so it may not be authored in a desk condition, and "half" is admitted only in a
    ///     pace context because it is also how a legitimate extent reads ("about half a kay").
    /// </summary>
    private static Regex PaceFractionPattern() => PaceFractionRegex;

    private static readonly Regex PaceFractionRegex = new(
        @"\b(?:a\s+)?(?:quarter|third|fifth|sixth|two[-\s]thirds)\b"
        + @"|\bhalf\b(?=[^.]{0,24}\b(?:pace|speed)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] AdvicePhrases =
    [
        "allow extra time", "take care", "drive safely", "plan ahead", "stay safe",
        "slow down", "be careful", "motorists are warned", "stay alert"
    ];

    private static readonly string[] BookendPhrases = ["powered by", "now looking over", "looking over the"];

    /// <param name="previousScript">The previous same-day read, when available; null skips the repetition check.</param>
    /// <param name="traceStage">Trace stage name used by callers that score a candidate before final gates.</param>
    public static IReadOnlyList<CheckResult> Run(
        NormalizedInput input,
        string read,
        BulletinBriefs briefs,
        TraceRecorder trace,
        string? previousScript = null,
        string traceStage = "Checks")
    {
        var stopwatch = Stopwatch.StartNew();
        trace.StageStart(traceStage, "Running deterministic checks over the finished read.");

        var results = new List<CheckResult>
        {
            PlainSpokenProse(read),
            SuppliedValueFidelity(input, read),
            SpokenNumberForm(read),
            ReasoningNotVoiced(input, briefs, read),
            RequiredStoryVoiced(input, read),
            NoAdvice(read),
            NoStationBookend(read),
            SegmentRunningOrder(briefs, input, read),
            DeskConditionCarriesNoFigure(briefs),
            TrafficSentenceNamesRoad(input, briefs, read)
        };

        if (!string.IsNullOrWhiteSpace(previousScript))
            results.Add(PhraseOverlapCheck(input, read, previousScript));

        foreach (var result in results)
            trace.Check(traceStage, $"{result.Name}: {result.Detail}", result.Passed);

        int failed = results.Count(r => !r.Passed);
        trace.StageEnd(traceStage, $"{results.Count} checks, {failed} failed.", stopwatch.Elapsed.TotalMilliseconds);
        return results;
    }

    /// <summary>
    ///     The read must be words a presenter can speak: no code fences, no JSON wrapping, no
    ///     markdown headings.
    ///     <para>
    ///         Defence in depth behind the playbook's Output-section refusal. Measured live
    ///         2026-08-26: a station block instructing "reply in JSON" beat the locked reply-format
    ///         rule, every grounding check passed (the facts inside the JSON were real), the editor
    ///         approved it, and a <c>{"script": …}</c> blob shipped as a finished read. Format is a
    ///         machine property, so a machine — not the editor — refuses it.
    ///     </para>
    /// </summary>
    private static CheckResult PlainSpokenProse(string read)
    {
        string trimmed = read.TrimStart();
        var violations = new List<string>();
        if (trimmed.Contains("```", StringComparison.Ordinal))
            violations.Add("a code fence");
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            violations.Add("a JSON/bracket wrapper");
        if (trimmed.StartsWith('#'))
            violations.Add("a markdown heading");

        return violations.Count == 0
            ? new CheckResult("PlainSpokenProse", true, "The read is plain spoken prose.")
            : new CheckResult("PlainSpokenProse", false,
                $"The read is not speakable as returned — it contains {string.Join(" and ", violations)}. "
                + "Reply with only the bulletin words, as continuous spoken prose.");
    }

    /// <summary>
    ///     The read voices its segments - traffic, weather, fuel - in the order the stitched
    ///     briefs carry, which is the station's configured running order.
    ///     <para>
    ///         Segment order is station CONFIGURATION, enforced here rather than trusted to the
    ///         prompt. Measured live 2026-08-27 (job e858b062): a weather-first
    ///         <c>SegmentOrder</c> reached the workflow, the stitch ordered the briefs correctly,
    ///         and the read still closed with weather - the composer followed editorial
    ///         convention over brief order and nothing objected. A segment's position is found by
    ///         the first appearance of any of its supplied fact values, so the check is exact on
    ///         the same material <see cref="SuppliedValueFidelity" /> already guarantees is
    ///         verbatim. Segments the read never voices are skipped: presence is
    ///         <see cref="RequiredStoryVoiced" />'s concern.
    ///     </para>
    /// </summary>
    private static CheckResult SegmentRunningOrder(
        BulletinBriefs briefs, NormalizedInput input, string read)
    {
        // Planned segment sequence in stitched order, first appearance wins.
        var planned = new List<string>();
        foreach (StoryBrief story in briefs.Stories)
        {
            string segment = SegmentOf(story);
            if (!planned.Contains(segment))
                planned.Add(segment);
        }

        // First position in the read where any of a segment's supplied values is spoken.
        var firstMention = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (StoryBrief story in briefs.Stories)
        {
            string segment = SegmentOf(story);
            int index = FirstFactMention(story, input, read);
            if (index < 0)
                continue;
            if (!firstMention.TryGetValue(segment, out int existing) || index < existing)
                firstMention[segment] = index;
        }

        var voiced = planned.Where(firstMention.ContainsKey).ToList();
        var actual = voiced.OrderBy(segment => firstMention[segment]).ToList();
        if (voiced.SequenceEqual(actual, StringComparer.Ordinal))
            return new CheckResult("SegmentRunningOrder", true,
                $"The read follows the configured running order ({string.Join(" then ", voiced)}).");

        return new CheckResult("SegmentRunningOrder", false,
            $"The station's configured running order is {string.Join(" then ", voiced)}, but the read "
            + $"voices them {string.Join(" then ", actual)}. Reorder the sentences so each section "
            + "appears in the configured order; reword nothing else.");
    }

    private static string SegmentOf(StoryBrief story) =>
        story.StoryId.Equals("fuel", StringComparison.OrdinalIgnoreCase) ? "fuel"
        : story.StoryId.Equals("weather", StringComparison.OrdinalIgnoreCase) ? "weather"
        : "traffic";

    /// <summary>
    ///     Index of the first supplied fact value of <paramref name="story" /> spoken in
    ///     <paramref name="text" />, or -1. Values shorter than four characters are too ambiguous
    ///     to place ("M2", "22").
    /// </summary>
    private static int FirstFactMention(StoryBrief story, NormalizedInput input, string text)
    {
        int first = -1;
        foreach (EventFact supplied in story.ResolveFacts(input))
        {
            string value = supplied.Value.Trim();
            if (value.Length < 4)
                continue;
            int index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (first < 0 || index < first))
                first = index;
        }

        return first;
    }

    /// <summary>
    ///     The bulletin segment one finished sentence voices, decided the way
    ///     <see cref="SegmentRunningOrder" /> decides it: by which story's supplied facts the
    ///     sentence carries. Null for a sentence that carries none (connective, intro, outro).
    ///     This is the lineage the engine protects required weather/fuel lines with at the
    ///     synthesis choke point - a word list there cut "clear and 22 for Sydney" as traffic
    ///     (dev 2e158de3, 2026-09-02) after this very check had passed the read.
    /// </summary>
    public static BulletinSegment? SegmentOfSentence(string sentence, BulletinBriefs briefs, NormalizedInput input)
    {
        BulletinSegment? found = null;
        foreach (StoryBrief story in briefs.Stories)
        {
            if (FirstFactMention(story, input, sentence) < 0)
                continue;
            BulletinSegment segment = SegmentOf(story) switch
            {
                "weather" => BulletinSegment.Weather,
                "fuel" => BulletinSegment.Fuel,
                _ => BulletinSegment.Traffic
            };
            // A weather condition can be a plain word ("Clear"), so a road sentence that says "a
            // clear run" also matches the weather story. A sentence that names a road is traffic;
            // a fixture only claims a sentence no traffic story does.
            if (found is null || segment == BulletinSegment.Traffic)
                found = segment;
        }

        return found;
    }

    /// <summary>
    ///     Every traffic AIRABLE UNIT names a supplied road, and a voiced desk condition stays in
    ///     that road's unit.
    ///     <para>
    ///         This closes the failure measured in dev Perth job <c>a409dbd4</c> (2026-09-03):
    ///         "Through traffic cannot use the northbound section, but access from either end remains
    ///         possible." aired after the ceiling trim dropped "Craig Street is closed northbound..."
    ///         and kept the condition sentence. The fitter drops whole sentences and cannot know two
    ///         sentences are one story, so the read must never split a story across sentences in the
    ///         first place.
    ///     </para>
    ///     <para>
    ///         "Unit" rather than "sentence" because a full stop is not the only way to orphan a
    ///         condition. Dev goldcoast job <c>de2d1b60</c> (2026-09-03) aired the same defect
    ///         through a SEMICOLON — "Country Club Drive at Helensvale is closed northbound for
    ///         roadworks over about half a kay; Northbound traffic cannot use the closed section." —
    ///         and this check passed it, because the local splitter it used broke on <c>.!?</c> only
    ///         and the whole thing was therefore one sentence that did contain a road name. The
    ///         delivery director then rewrote the semicolon to an ellipsis, which the canonical
    ///         <see cref="ScriptSentenceSplitter" /> DOES treat as a boundary, so the read the
    ///         listener heard had a genuine orphan the fitter could cut loose from its road.
    ///         <see cref="SplitSentences" /> now uses the canonical splitter and splits again on
    ///         semicolons, so both spellings of one story-split are the same defect here.
    ///     </para>
    /// </summary>
    private static CheckResult TrafficSentenceNamesRoad(
        NormalizedInput input, BulletinBriefs briefs, string read)
    {
        string[] roadNames = input.Events
            .SelectMany(ExtractEventRoadNames)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var offenders = new List<(string Sentence, List<string> StoryIds)>();
        foreach (string sentence in SplitSentences(read))
        {
            if (IsOpenerTokenSentence(sentence))
                continue;

            BulletinSegment? segment = SegmentOfSentence(sentence, briefs, input);
            if (segment is BulletinSegment.Weather or BulletinSegment.Fuel)
                continue;

            if (!roadNames.Any(road => sentence.Contains(road, StringComparison.OrdinalIgnoreCase)))
                AddTrafficSentenceOffender(offenders, sentence, null);

            string[] sentenceWords = Normalise(sentence)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (StoryBrief story in briefs.Stories)
            {
                if (story.Condition.Length == 0)
                    continue;

                string condition = Normalise(story.Condition);
                if (condition.Length == 0)
                    continue;

                string firstSixWords = FirstWords(condition, 6);
                if (!ContainsRun(sentenceWords, condition)
                    && !ContainsRun(sentenceWords, firstSixWords))
                {
                    continue;
                }

                string[] storyRoads = story.EventIds
                    .Select(eventId => input.Events.FirstOrDefault(evt =>
                        string.Equals(evt.EventId, eventId, StringComparison.Ordinal)))
                    .Where(evt => evt is not null)
                    .SelectMany(evt => ExtractEventRoadNames(evt!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!storyRoads.Any(road => sentence.Contains(road, StringComparison.OrdinalIgnoreCase)))
                    AddTrafficSentenceOffender(offenders, sentence, story.StoryId);
            }
        }

        if (offenders.Count == 0)
        {
            return new CheckResult(
                "TrafficSentenceNamesRoad",
                true,
                "Every traffic sentence names its road and each condition rides with the road it describes.");
        }

        return new CheckResult(
            "TrafficSentenceNamesRoad",
            false,
            "These traffic sentences cannot be placed against a road: "
            + string.Join("; ", offenders.Select(FormatTrafficSentenceOffender))
            + ". Fix: merge it into the sentence that names its road; a sentence with no road cannot "
            + "be placed by a listener and can be orphaned by the slot fitter.");
    }

    private static IEnumerable<string> ExtractEventRoadNames(BulletinEvent evt)
    {
        foreach (var fact in evt.Facts)
        {
            if (IsRoadNameFact(fact))
                yield return fact.Value.Trim();
        }

        string roadName = evt.Kind switch
        {
            BulletinEventKind.Corridor => RoadFromCorridorLabel(evt.Label),
            BulletinEventKind.DriveTime when evt.EventId.StartsWith("route:", StringComparison.OrdinalIgnoreCase)
                => evt.EventId["route:".Length..].Trim(),
            BulletinEventKind.ClearRun when evt.EventId.StartsWith("goodrun:", StringComparison.OrdinalIgnoreCase)
                => evt.EventId["goodrun:".Length..].Trim(),
            _ => string.Empty
        };
        if (roadName.Length > 0 && !roadName.Equals("Clear runs", StringComparison.OrdinalIgnoreCase))
            yield return roadName;
    }

    private static string RoadFromCorridorLabel(string label)
    {
        int at = label.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        int comma = label.IndexOf(',');
        int end = at >= 0 && comma >= 0 ? Math.Min(at, comma) : Math.Max(at, comma);
        return (end >= 0 ? label[..end] : label).Trim();
    }

    private static bool IsRoadNameFact(EventFact fact) =>
        fact.Field.Equals("road", StringComparison.OrdinalIgnoreCase)
        || fact.Field.Equals("clear road", StringComparison.OrdinalIgnoreCase);

    private static void AddTrafficSentenceOffender(
        List<(string Sentence, List<string> StoryIds)> offenders,
        string sentence,
        string? storyId)
    {
        int existing = offenders.FindIndex(offender =>
            string.Equals(offender.Sentence, sentence, StringComparison.Ordinal));
        if (existing < 0)
        {
            var storyIds = new List<string>();
            if (storyId is not null)
                storyIds.Add(storyId);
            offenders.Add((sentence, storyIds));
            return;
        }

        if (storyId is not null
            && !offenders[existing].StoryIds.Contains(storyId, StringComparer.OrdinalIgnoreCase))
        {
            offenders[existing].StoryIds.Add(storyId);
        }
    }

    private static string FormatTrafficSentenceOffender(
        (string Sentence, List<string> StoryIds) offender)
    {
        string story = offender.StoryIds.Count == 0
            ? string.Empty
            : $" (condition for story '{string.Join("', '", offender.StoryIds)}')";
        return $"{QuoteTrafficSentence(offender.Sentence)}{story}";
    }

    private static string QuoteTrafficSentence(string sentence)
    {
        const int maximumLength = 80;
        string snippet = sentence.Trim();
        if (snippet.Length > maximumLength)
            snippet = snippet[..(maximumLength - 3)].TrimEnd() + "...";
        return $"\"{snippet}\"";
    }

    private static bool IsOpenerTokenSentence(string sentence)
    {
        string[] words = Normalise(sentence)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            && words[^1].Equals("opener", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The read's AIRABLE UNITS: canonical sentences, then each split again on semicolons.
    ///     <para>
    ///         The canonical splitter is <see cref="ScriptSentenceSplitter" /> — the same one
    ///         <c>BulletinDurationFitter</c> segments by when it drops whole sentences to fit the
    ///         slot, and the same one the console's provenance mapping uses. This check used to
    ///         carry its own <c>.!?</c> scan, so the two disagreed about <c>…</c>: the read that
    ///         passed here as six sentences shipped as seven (dev goldcoast <c>de2d1b60</c>). Two
    ///         definitions of "sentence" in one pipeline is the defect, not the ellipsis.
    ///     </para>
    ///     <para>
    ///         Semicolons and colons are split because both join two INDEPENDENT clauses: the
    ///         listener hears a break, and promoting one to a full stop is a pure punctuation edit.
    ///         Dev cairns <c>8f49609a</c> (2026-09-03) aired the colon form — "Mulgrave Road in
    ///         Westcourt and Sheridan Street in Cairns North are clear: Both roads are running
    ///         freely." — where the second half names its roads only by pronoun. A comma clause is
    ///         deliberately NOT split: "…is closed northbound, with nothing getting through" is the
    ///         woven form the composer is asked for, and splitting there would reject exactly the
    ///         copy the prompt prescribes.
    ///     </para>
    /// </summary>
    private static IReadOnlyList<string> SplitSentences(string text)
    {
        var units = new List<string>();
        foreach (string sentence in ScriptSentenceSplitter.Split(text))
        {
            foreach (string clause in sentence.Split(';', ':'))
                AddSentence(clause, units);
        }

        return units;
    }

    private static void AddSentence(string text, List<string> sentences)
    {
        string trimmed = text.Trim();
        if (trimmed.Length > 0)
            sentences.Add(trimmed);
    }

    private static string FirstWords(string normalizedText, int count) =>
        string.Join(' ', normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(count));


    /// <summary>
    ///     Every quantity spoken in the read appears verbatim in some supplied fact value.
    ///     <para>
    ///         Values arrive already worded for air ("about half a kay", "a dollar ninety-three"),
    ///         so this is substring equality rather than parsing. A model that rephrases a figure
    ///         has changed it: there is no correct paraphrase of a distance.
    ///     </para>
    /// </summary>
    private static CheckResult SuppliedValueFidelity(NormalizedInput input, string read)
    {
        string haystack = string.Join(" || ", input.Events.SelectMany(e => e.Facts).Select(f => f.Value));

        var unsupported = QuantityPatterns
            .SelectMany(pattern => pattern.Matches(read).Select(m => m.Value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(quantity => haystack.IndexOf(quantity, StringComparison.OrdinalIgnoreCase) < 0)
            .ToList();

        // Minute figures compare on the NUMBER against any value that carries a minute, because the
        // same figure legitimately arrives in several forms: delayMinutes supplies the bare spoken
        // word ("three"), while a drive-time fact reads "25 min run (typical 20 min...)". Matching
        // the literal phrase rejected both. Measured live 2026-08-26: brisbane's priority-100 line
        // "the Pacific Motorway into the city is a 25-minute run" was correctly sourced from a real
        // drive-time fact, and this check flagged it, burned two recomposes, and left the read worse
        // than it started ("allow 25 minutes"). A false positive here costs more than a miss.
        var minuteValues = input.Events
            .SelectMany(e => e.Facts)
            .Where(f => f.Field.Equals("delayMinutes", StringComparison.OrdinalIgnoreCase)
                        || f.Value.Contains("min", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Value)
            .ToList();

        foreach (Match match in MinutesPattern.Matches(read))
        {
            string number = match.Groups["n"].Value.Trim();

            // Not a quantity at all: "a minute or two", "several minutes", "ten minutes' walk".
            // Only a resolvable figure makes a claim this check can adjudicate.
            if (TryParseSpokenNumber(number) is not { } spoken)
                continue;

            bool grounded = NumberForms(spoken).Any(form =>
                minuteValues.Any(value => ContainsToken(value, form)));

            // A HEDGED figure is a band, not a measurement: the composer is licensed to coarsen an
            // off-lead delay ("around twenty minutes" for a recorded twenty-three) so two roads can
            // share one breath. Accept a hedged figure when a supplied minute value sits within the
            // band (±5); a bare figure still has to match exactly, because without the hedge the
            // read is asserting the measurement itself.
            if (!grounded && IsHedged(read, match.Index))
            {
                grounded = Enumerable.Range(Math.Max(1, spoken - 5), 11).Any(near =>
                    NumberForms(near).Any(form => minuteValues.Any(value => ContainsToken(value, form))));
            }

            if (!grounded && !unsupported.Contains(match.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                unsupported.Add(match.Value.Trim());
        }

        return unsupported.Count == 0
            ? new CheckResult("SuppliedValueFidelity", true,
                "Every figure spoken in the read appears in the supplied data exactly as given.")
            : new CheckResult("SuppliedValueFidelity", false,
                $"The read states figures that no supplied value contains: {string.Join("; ", unsupported.Select(u => $"\"{u}\""))}. "
                + "Use the figure the data gives, or drop the claim.");
    }

    /// <summary>
    ///     A desk's <see cref="StoryBrief.Condition" /> states how a road is behaving, in the desk's
    ///     own words, and is the one string in the pipeline the composer may voice without it having
    ///     been cited. That licence covers qualitative state and nothing else.
    ///     <para>
    ///         A figure arriving here would bypass citation completely:
    ///         <see cref="SuppliedValueFidelity" /> grounds spoken quantities against supplied fact
    ///         VALUES, and a condition is not a fact value — so "banked up for two kays" in a
    ///         condition would be voiced, found in the briefs, and never checked against the source.
    ///         Rejecting the whole condition rather than editing it keeps the rule one the desk can
    ///         learn: figures travel as facts, always.
    ///     </para>
    ///     <para>
    ///         Spoken FRACTIONS count as figures, and are checked separately because the quantity
    ///         patterns are built for digits and units. "About a third of its usual pace" is the
    ///         emitter's own wording for a measured pace ratio: a desk that wrote it from its own
    ///         impression would be asserting a measurement, and one that shifted a third to a fifth
    ///         would be falsifying one. The fractions listed are exactly those <c>FormatPace</c>
    ///         emits, so the check and the emitter share one vocabulary.
    ///     </para>
    /// </summary>
    private static CheckResult DeskConditionCarriesNoFigure(BulletinBriefs briefs)
    {
        var offenders = briefs.Stories
            .Where(story => story.Condition.Length > 0)
            .Select(story => (story.StoryId, Figures: QuantityPatterns
                .SelectMany(pattern => pattern.Matches(story.Condition).Select(m => m.Value.Trim()))
                .Concat(MinutesPattern.Matches(story.Condition).Select(m => m.Value.Trim()))
                .Concat(PaceFractionPattern().Matches(story.Condition).Select(m => m.Value.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .Where(pair => pair.Figures.Count > 0)
            .ToList();

        return offenders.Count == 0
            ? new CheckResult("DeskConditionCarriesNoFigure", true,
                "No desk condition carries a figure; every quantity travels as a cited fact.")
            : new CheckResult("DeskConditionCarriesNoFigure", false,
                "A story's condition states a figure, which would reach air uncited: "
                + string.Join("; ", offenders.Select(o => $"'{o.StoryId}' has {string.Join(", ", o.Figures)}"))
                + ". Describe how the road is behaving in words and let the figures travel as facts.");
    }

    private static readonly string[] NumberWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty"
    ];

    private static readonly Dictionary<string, int> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20,
        ["thirty"] = 30,
        ["forty"] = 40,
        ["fifty"] = 50,
        ["sixty"] = 60,
        ["seventy"] = 70,
        ["eighty"] = 80,
        ["ninety"] = 90
    };

    /// <summary>
    ///     The figure a spoken quantity denotes, or null when the word names no number.
    ///     <para>
    ///         Handles digits, the words to twenty, the tens, and hyphenated compounds such as
    ///         "twenty-five", which is how a drive time reaches air. Returning null for anything
    ///         else is what keeps "a minute or two" and "several minutes" out of a check that can
    ///         only reason about figures.
    ///     </para>
    /// </summary>
    private static int? TryParseSpokenNumber(string word)
    {
        if (int.TryParse(word, out int digits))
            return digits;

        int index = Array.FindIndex(NumberWords, w => w.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            return index;

        if (Tens.TryGetValue(word, out int tens))
            return tens;

        string[] parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && Tens.TryGetValue(parts[0], out int high))
        {
            int unit = Array.FindIndex(NumberWords, w => w.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if (unit is > 0 and < 10)
                return high + unit;
        }

        return null;
    }

    /// <summary>
    ///     Both spellings of a figure. The data says "three" in one field and "25 min" in another,
    ///     and a read may say either, so a check comparing one spelling only is a coin toss.
    /// </summary>
    private static IEnumerable<string> NumberForms(int number)
    {
        yield return number.ToString();
        if (number >= 0 && number < NumberWords.Length)
            yield return NumberWords[number];
        else if (Tens.FirstOrDefault(t => t.Value == number) is { Key: not null } exact)
            yield return exact.Key;
    }

    /// <summary>Whole-token containment, so "5" does not match inside "25".</summary>
    private static bool ContainsToken(string haystack, string token) =>
        Regex.IsMatch(haystack, $@"(?<![\w-]){Regex.Escape(token)}(?![\w-])", RegexOptions.IgnoreCase);

    /// <summary>
    ///     True when the quantity at <paramref name="matchIndex" /> is spoken as an approximation:
    ///     "about", "around", "roughly", "nearly", "almost", "just over/under", or an "-odd"
    ///     compound immediately before it. Only these forms earn band tolerance in
    ///     <see cref="SuppliedValueFidelity" />.
    /// </summary>
    private static bool IsHedged(string read, int matchIndex)
    {
        int start = Math.Max(0, matchIndex - 16);
        string preceding = read[start..matchIndex];
        return Regex.IsMatch(
            preceding,
            @"\b(about|around|roughly|nearly|almost|just over|just under|near)\s*$|-odd\s*$",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    ///     Nothing in the read is written in a form a presenter cannot say.
    ///     <para>
    ///         This exists because the other checks share a blind spot, seen three times on
    ///         2026-08-26: they validate figures that are stated in the EXPECTED form and therefore
    ///         say nothing at all about a figure stated in an unexpected one. "adding one to two"
    ///         (no unit) matched no minute pattern; "$1. 98" matched no money pattern. In both cases
    ///         the check passed because it never fired.
    ///     </para>
    ///     <para>
    ///         So look for the wrong form directly rather than for the right form's absence. ARN
    ///         supplies every price pre-worded as "a dollar ninety-eight", so a currency symbol or a
    ///         decimal price in the read is by construction not the supplied value, whatever else is
    ///         true of it.
    ///     </para>
    /// </summary>
    private static CheckResult SpokenNumberForm(string read)
    {
        var offenders = new List<string>();

        foreach (Match m in Regex.Matches(read, @"\$\s*[\d.,\s]+"))
            offenders.Add(m.Value.Trim());

        // "1.98", "2. 44" - a decimal figure is never how a price is spoken.
        foreach (Match m in Regex.Matches(read, @"\b\d+\s*\.\s*\d+\b"))
        {
            string hit = m.Value.Trim();
            if (!offenders.Any(o => o.Contains(hit, StringComparison.Ordinal)))
                offenders.Add(hit);
        }

        return offenders.Count == 0
            ? new CheckResult("SpokenNumberForm", true, "Every figure is written in a form a presenter can read aloud.")
            : new CheckResult("SpokenNumberForm", false,
                $"These are written as symbols or decimals rather than the way they are said: "
                + $"{string.Join("; ", offenders.Select(o => $"\"{o}\""))}. "
                + "Use the supplied wording, for example \"a dollar ninety-eight\".");
    }

    /// <summary>
    ///     Function words carry no claim, so their absence from the evidence proves nothing. Without
    ///     this list "is the biggest story" trips on "is" and "the" and every reasoning block leaks.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "is", "are", "was", "were", "be", "been", "being",
        "of", "in", "on", "at", "to", "for", "with", "from", "by", "into", "through", "over",
        "this", "that", "these", "those", "it", "its", "as", "so", "than", "then", "there",
        "has", "have", "had", "will", "would", "can", "could", "not", "no", "any", "all", "both"
    };

    /// <summary>
    ///     The writer's reasoning about importance never reaches air as though it were a fact.
    ///     <para>
    ///         A brief separates what is true ("delayMinutes: three") from why it leads ("the
    ///         broadest citybound route"). Only the first is evidence. Rendered to the composer the
    ///         two are labelled, but a label is a request, and the failure mode is silent: a
    ///         judgement copied out of the reasoning block reads exactly like a supplied value and
    ///         nothing downstream can tell the difference, because there is no source to compare it
    ///         against.
    ///     </para>
    ///     <para>
    ///         So compare it here. What makes a leak a leak is not that the wording is shared - a
    ///         brief naturally reasons ABOUT the facts, so "a road closure southbound is the biggest
    ///         story today" repeats data it is entitled to repeat. What makes it a leak is a word
    ///         the evidence never contains: "major", "arterial", "busiest". So a run of four or more
    ///         words is flagged only when it carries at least one content word found nowhere in any
    ///         supplied value, which is precisely the case where the read asserts something no
    ///         source can support.
    ///     </para>
    ///     <para>
    ///         Four words, not three, and content words only, because a false positive costs more
    ///         than a miss: on 2026-08-26 one wrongly rejected a correct read, burned two
    ///         recomposes, and shipped worse copy than it started with. The first version of THIS
    ///         check made the same mistake against "a road closure southbound", where the article
    ///         alone broke a literal comparison, and its own regression test caught it.
    ///     </para>
    /// </summary>
    private static CheckResult ReasoningNotVoiced(NormalizedInput input, BulletinBriefs briefs, string read)
    {
        const int minimumRun = 4;

        // Field NAMES are grounded vocabulary, not just values. A fact is supplied as
        // "delayMinutes: two", and "a two-minute delay" is the plainest correct rendering of it -
        // but "delay" appears in no value, so a values-only vocabulary called correct copy a
        // fabrication. Measured live on 2026-08-26 this fired twice on one sydney read. Splitting
        // camel-case names admits exactly the words that describe what the data means.
        var factWords = Normalise(string.Join(" ", input.Events
                .SelectMany(e => e.Facts)
                .SelectMany(f => new[] { f.Value, SplitCamelCase(f.Field) })))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        string[] readWords = Normalise(read).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var leaked = new List<string>();
        foreach (StoryBrief story in briefs.Stories)
        {
            if (story.WhyItMatters.Length == 0)
                continue;

            string[] reasonWords = Normalise(story.WhyItMatters)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int start = 0; start + minimumRun <= reasonWords.Length; start++)
            {
                string[] run = reasonWords.Skip(start).Take(minimumRun).ToArray();

                // Every word already in the data means the run asserts nothing new, however it is
                // phrased. Only an ungrounded content word makes this a claim of the writer's own.
                bool assertsSomethingNew = run.Any(word =>
                    !Stopwords.Contains(word) && !IsGrounded(word, factWords));
                if (!assertsSomethingNew)
                    continue;

                string phrase = string.Join(' ', run);
                if (ContainsRun(readWords, phrase) && !leaked.Contains(phrase, StringComparer.Ordinal))
                    leaked.Add(phrase);
            }
        }

        return leaked.Count == 0
            ? new CheckResult("ReasoningNotVoiced", true,
                "The read voices supplied facts, not the writer's reasoning about them.")
            : new CheckResult("ReasoningNotVoiced", false,
                "These come from a brief's \"why it matters\", which is editorial reasoning rather than "
                + $"supplied data, so the read asserts something no source states: {string.Join("; ", leaked.Select(p => $"\"{p}\""))}. "
                + "Voice the facts instead.");
    }

    /// <summary>
    ///     Every story the station contract demands actually reaches air.
    ///     <para>
    ///         A fixture is the one thing in a bulletin nobody is entitled to drop for length, and
    ///         it is exactly what a length-pressed composer drops first, because it is rarely the
    ///         most urgent thing in the market. Measured live on 2026-08-26 an Adelaide read spent
    ///         all 98 of its words on five traffic stories and shipped with no weather on a station
    ///         that always carries it.
    ///     </para>
    ///     <para>
    ///         Presence is judged against <see cref="NormalizedInput.RequiredEventIds" />, the
    ///         station's contract, and NOT against the stories the writer happened to brief. The
    ///         distinction is the whole check: asking whether every briefed fixture was voiced
    ///         passes vacuously in the one case that matters, where the writer never briefed the
    ///         fixture at all. The earlier pipeline made exactly that mistake and reported a pass on a melbourne read
    ///         with no weather in it; the first version of this check repeated it.
    ///     </para>
    ///     <para>
    ///         A fixture counts as voiced when at least one of its own supplied values appears in
    ///         the read - the same standard of evidence the rest of these checks use.
    ///     </para>
    /// </summary>
    private static CheckResult RequiredStoryVoiced(NormalizedInput input, string read)
    {
        if (input.RequiredEventIds.Count == 0)
            return new CheckResult("RequiredStoryVoiced", true, "This station requires no fixture stories.");

        string normalisedRead = Normalise(read);
        var missing = new List<string>();
        foreach (string requiredEventId in input.RequiredEventIds)
        {
            BulletinEvent? required = input.Event(requiredEventId);
            if (required is null)
                continue;

            if (!required.Facts.Any(fact => ValueIsAudible(fact.Value, normalisedRead)))
                missing.Add(required.Label.Length > 0 ? required.Label : requiredEventId);
        }

        return missing.Count == 0
            ? new CheckResult("RequiredStoryVoiced", true,
                $"Every story this station always carries is voiced ({input.RequiredEventIds.Count} required).")
            : new CheckResult("RequiredStoryVoiced", false,
                $"This station always carries these, and the read never voices them: {string.Join("; ", missing)}. "
                + "Voice them, dropping a lower-priority optional story if the slot needs the words.");
    }

    /// <summary>
    ///     Fails when the composer has copied too much of the previous same-day read. Road, suburb
    ///     and other evidence anchors are deliberately ignored by <see cref="PhraseOverlap.Score" />:
    ///     a valid continuation must repeat those names, but not the idioms and sentence shapes around
    ///     them.
    /// </summary>
    private static CheckResult PhraseOverlapCheck(
        NormalizedInput input,
        string read,
        string previousScript)
    {
        double score = PhraseOverlap.Score(read, previousScript, input);
        bool passed = score < PhraseOverlap.FailureThreshold;
        string detail = passed
            ? $"The read shares {score:P1} of eligible four-word phrases with the previous read."
            : $"The read repeats the previous read ({score:P1} of eligible four-word phrases); vary its opening, idioms and sentence shapes.";
        return new CheckResult("repeats the previous read", passed, detail);
    }

    /// <summary>
    ///     Whether a supplied value was actually said. Values arrive pre-worded for air, so the
    ///     read either contains one or it does not.
    /// </summary>
    private static bool ValueIsAudible(string value, string normalisedRead)
    {
        string normalised = Normalise(value);
        return normalised.Length > 0 && ContainsRun(normalisedRead.Split(' '), normalised);
    }

    /// <summary>Lowercased, punctuation-free, single-spaced, so a run compares on words alone.</summary>
    private static string Normalise(string text) =>
        string.Join(' ', Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary><c>delayMinutes</c> becomes <c>delay Minutes</c>, so both words count as supplied.</summary>
    private static string SplitCamelCase(string field) =>
        Regex.Replace(field, "(?<=[a-z0-9])(?=[A-Z])", " ");

    /// <summary>
    ///     Whether a word is a rendering of something the evidence supplies.
    ///     <para>
    ///         Matched on a normalised token rather than the literal string, because the same fact
    ///         legitimately reaches air spelled differently: <c>delayMinutes: 11</c> is correctly
    ///         voiced as "an eleven minute delay", where "eleven" is the digit, "minute" is the
    ///         plural field name, and "delay" is the field name. Comparing literals called that
    ///         fabrication - measured live twice on one sydney read on 2026-08-26 - while the
    ///         judgements this check exists to catch ("major", "arterial", "busiest") survive
    ///         every one of these foldings, because no notation of them appears in the data at all.
    ///     </para>
    /// </summary>
    private static bool IsGrounded(string word, HashSet<string> factWords)
    {
        if (factWords.Contains(word))
            return true;

        // Plurality is not a claim: "minute" and "minutes" say the same thing about the same fact.
        if (word.EndsWith('s') && factWords.Contains(word[..^1]))
            return true;
        if (factWords.Contains(word + "s"))
            return true;

        // Nor is notation: the data says 11, the read says eleven.
        return TryParseSpokenNumber(word) is { } figure && NumberForms(figure).Any(factWords.Contains);
    }

    private static bool ContainsRun(string[] words, string phrase) =>
        $" {string.Join(' ', words)} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static CheckResult NoAdvice(string read)
    {
        var found = AdvicePhrases.Where(p => read.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 0
            ? new CheckResult("NoAdvice", true, "The read states conditions without telling drivers what to do.")
            : new CheckResult("NoAdvice", false,
                $"Traffic radio states what is happening, never what to do. Remove: {string.Join("; ", found)}.");
    }

    private static CheckResult NoStationBookend(string read)
    {
        var found = BookendPhrases.Where(p => read.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 0
            ? new CheckResult("NoStationBookend", true, "The read carries no station intro or sign-off.")
            : new CheckResult("NoStationBookend", false,
                $"The station's intro and sign-off are added around this read, so writing one duplicates it. Remove: {string.Join("; ", found)}.");
    }
}
