using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Deterministic gate on the SHAPE of a read, independent of what it says.
///     <para>
///         Two defects, both observed live on 2026-08-23 and both invisible to every existing gate
///         because the reads were factually correct and fitted their slots exactly:
///     </para>
///     <list type="number">
///         <item>
///             A forty-word compound sentence carrying three roads, joined with a SEMICOLON — a mark
///             with no spoken equivalent. The slot-fit selector actively PREFERRED it, because a long
///             phrasing filled the slot well and nothing priced sentence shape.
///         </item>
///         <item>
///             Consecutive sentences opening on the same frame ("A breakdown ... A breakdown ..."),
///             which reads as templated even when each sentence is well formed on its own.
///             <see cref="SentenceCloneDetector" /> misses this: it needs a four-word shared frame
///             after skeletonising road names, and a two-word echo slips under that threshold.
///         </item>
///     </list>
///     On 2026-08-24 the live Sydney read repeated the state phrase "a slower run" twice. The
///     <see cref="RepeatedStatePhraseDetector" /> only counts one word and requires three uses
///     (<c>"slower"</c> and <c>"run"</c> each appeared twice), while <see cref="SentenceCloneDetector" />
///     requires four contiguous shared words and the phrase has three. The existing opening-frame
///     check in this detector only catches repeated sentence openings; "Victoria Road is" and
///     "Southern Cross Drive has" start differently, so it also missed the state phrase.
///     <see cref="SentenceShapeDetector" /> already owns read shape and short echoes, so the phrase
///     floor lives here rather than as a fifth overlapping detector.
///     <para>
///         The prompt already forbids all three shapes; this makes the ban enforceable rather than
///         advisory. Pure text analysis: no traffic context is needed, so it runs anywhere in the
///         pipeline and inside the selector's own tests.
///     </para>
/// </summary>
public static partial class SentenceShapeDetector
{
    /// <summary>
    ///     Sentence length past which a driver stops parsing. The writer prompt asks for 15-18 words;
    ///     the gate trips at 24 so ordinary well-built sentences are never flagged and only a genuine
    ///     run-on is. The observed defect was 40 words.
    /// </summary>
    private const int MaxSpokenSentenceWords = 24;

    /// <summary>
    ///     Consecutive short "X is/has Y" sentences that trip the database-read-out gate. Four is
    ///     chosen so a station opener, one brief item and a short weather line can never combine to
    ///     trip it: those do not carry the "is/has" predicate, which breaks the run.
    /// </summary>
    private const int MaxStaccatoRun = 3;

    /// <summary>Word count at or below which a sentence counts as a staccato beat.</summary>
    private const int StaccatoWords = 9;
    /// <summary>Leading words compared when deciding whether two sentences share a frame.</summary>
    private const int FrameWords = 2;
    private const int RepeatedPhraseMinWords = 3;
    private const int RepeatedPhraseMaxWords = 5;

    [GeneratedRegex(@"[;:]")]
    private static partial Regex UnspokenPunctuation();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)?")]
    private static partial Regex Word();

    /// <summary>One shape defect found in a read, in the order the sentence appears (1-based).</summary>
    public readonly record struct ShapeIssue(int Order, string Kind, string Detail);

    public readonly record struct SentenceShapeResult(bool HasIssue, IReadOnlyList<ShapeIssue> Issues)
    {
        /// <summary>Reviewer-ready feedback naming every defect, or empty when the read is clean.</summary>
        public string Feedback => Issues.Count == 0
            ? string.Empty
            : string.Join(" ", Issues.Select(issue => $"Sentence {issue.Order}: {issue.Detail}"));

        /// <summary>
        ///     Structured issues for the refine loop. A repeated phrase is attached to its later
        ///     sentence so the sentence-scoped patcher can vary that item without rewriting the read.
        /// </summary>
        public IReadOnlyList<ReviewIssue> ReviewIssues => Issues
            .Select(issue => ReviewIssue.ForSentence(
                issue.Order,
                DefectName(issue.Kind),
                issue.Detail))
            .ToList();
    }
    private static string DefectName(string kind) => kind switch
    {
        "RepeatedStatePhrase" => "REPEATED_STATE_PHRASE",
        "UnspokenPunctuation" => "UNSPOKEN_PUNCTUATION",
        "RunOnSentence" => "RUN_ON_SENTENCE",
        "RepeatedOpeningFrame" => "REPEATED_OPENING_FRAME",
        "StaccatoRun" => "STACCATO_RUN",
        _ => kind.ToUpperInvariant()
    };

    /// <summary>
    ///     Detect shape defects. When <paramref name="stationLines" /> is supplied, the canonical
    ///     <see cref="StationLineSplitter" /> peels bookends and filler from the analysis, exactly as
    ///     the operator rewrite path does. Normal review calls already receive the generated body and
    ///     leave this argument empty.
    /// </summary>
    public static SentenceShapeResult Detect(
        string? script,
        IEnumerable<string>? stationLines = null)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SentenceShapeResult(false, []);

        string body = stationLines is null
            ? script
            : StationLineSplitter.Split(script, stationLines).Body;
        var sentences = ScriptSentenceSplitter.Split(body);
        if (sentences.Count == 0)
            return new SentenceShapeResult(false, []);

        var issues = new List<ShapeIssue>();
        int staccatoRun = 0;
        int staccatoStart = 0;
        var seenFrames = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < sentences.Count; index++)
        {
            string sentence = sentences[index];
            int order = index + 1;

            if (UnspokenPunctuation().IsMatch(sentence))
                issues.Add(new ShapeIssue(order, "UnspokenPunctuation",
                    "uses a semicolon or colon, which has no spoken equivalent — split it into separate sentences."));

            int words = CountWords(sentence);
            if (words > MaxSpokenSentenceWords)
                issues.Add(new ShapeIssue(order, "RunOnSentence",
                    $"runs to {words} words; a driver cannot parse it. Split it into one thought per sentence."));

            // Scope is the WHOLE read, not just the previous sentence. An adjacent-only rule let
            // "More roadworks are at Gillies Highway..." and "More roadworks are affecting Captain
            // Cook Highway..." both air in one Cairns read (2026-08-23 panel) because a different
            // sentence sat between them. A listener hears the template either way.
            string frame = Frame(sentence);
            if (frame.Length > 0 && !seenFrames.Add(frame))
                issues.Add(new ShapeIssue(order, "RepeatedOpeningFrame",
                    $"opens on words already used to open another sentence in a read (\"{frame}\"). "
                    + "Two items of the same kind should be LINKED, not announced with the same "
                    + "template — \"there's another ...\", \"it's a similar story on ...\"."));

            if (IsStaccatoBeat(sentence, words))
            {
                staccatoRun++;
                staccatoStart = staccatoRun == 1 ? order : staccatoStart;
                if (staccatoRun == MaxStaccatoRun + 1)
                    issues.Add(new ShapeIssue(staccatoStart, "StaccatoRun",
                        $"starts a run of {MaxStaccatoRun + 1} or more short \"road is state\" sentences. "
                        + "That is a database read-out, not a bulletin: name the two strongest roads and "
                        + "sweep the rest into ONE phrase."));
            }
            else
            {
                staccatoRun = 0;
            }
        }

        AddRepeatedPhraseIssue(sentences, issues);
        return new SentenceShapeResult(issues.Count > 0, issues);
    }

    private static void AddRepeatedPhraseIssue(
        IReadOnlyList<string> sentences,
        List<ShapeIssue> issues)
    {
        var occurrences = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (int sentenceIndex = 0; sentenceIndex < sentences.Count; sentenceIndex++)
        {
            var words = Word().Matches(sentences[sentenceIndex])
                .Select(match => new WordToken(match.Value, match.Value.ToLowerInvariant()))
                .ToList();
            for (int length = RepeatedPhraseMinWords; length <= RepeatedPhraseMaxWords; length++)
            {
                for (int start = 0; start + length <= words.Count; start++)
                {
                    var phraseWords = words.GetRange(start, length);
                    if (phraseWords.Any(IsProperNounToken)
                        || phraseWords.All(word => RepeatedStatePhraseDetector.IsIgnoredWord(word.Normalized))
                        || IsStandardDelayConstruction(phraseWords))
                        continue;

                    string phrase = string.Join(' ', phraseWords.Select(word => word.Normalized));
                    if (!occurrences.TryGetValue(phrase, out var sentenceIndexes))
                        occurrences[phrase] = sentenceIndexes = [];
                    sentenceIndexes.Add(sentenceIndex);
                }
            }
        }

        var repeated = occurrences
            .Where(pair => pair.Value.Count >= 2)
            .OrderByDescending(pair => pair.Key.Split(' ').Length)
            .ThenByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (repeated.Key is null)
            return;

        int secondOccurrence = repeated.Value.OrderBy(index => index).Skip(1).First();
        string feedback =
            $"REPETITION: the phrase \"{repeated.Key}\" is used in {repeated.Value.Count} sentences — the listy, machine tell. "
            + "Vary it: say the condition a different way for each item (reach for the road-feel palette), or "
            + "if several roads genuinely share that state, GROUP them into one sentence instead of repeating the phrase.";
        issues.Add(new ShapeIssue(secondOccurrence + 1, "RepeatedStatePhrase", feedback));
    }

    private readonly record struct WordToken(string Original, string Normalized);

    private static bool IsProperNounToken(WordToken word) =>
        char.IsUpper(word.Original[0]) && !RepeatedStatePhraseDetector.IsIgnoredWord(word.Normalized);


    /// <summary>
    ///     "Adding about N minutes" is the standard delay construction and recurs legitimately when
    ///     two roads have measured delays. Exclude any candidate containing its connective "adding"
    ///     token, rather than allowing a longer window to turn the harmless construction into a
    ///     false phrase repetition.
    /// </summary>
    private static bool IsStandardDelayConstruction(IReadOnlyList<WordToken> words) =>
        words.Any(word => word.Normalized == "adding");
    /// <summary>
    ///     Whether a sentence is one beat of a read-out: short, and asserting a state about a thing
    ///     ("Arthur Street is heavy", "Bay Street has a slight queue"). The predicate test is what
    ///     keeps station openers and weather lines out of the run.
    /// </summary>
    private static bool IsStaccatoBeat(string sentence, int words)
    {
        if (words > StaccatoWords)
            return false;
        string padded = $" {sentence.ToLowerInvariant()} ";
        return padded.Contains(" is ", StringComparison.Ordinal)
            || padded.Contains(" has ", StringComparison.Ordinal)
            || padded.Contains("'s ", StringComparison.Ordinal);
    }

    private static int CountWords(string sentence) =>
        sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The opening words of a sentence, stripped to letters and digits and lowercased.</summary>
    private static string Frame(string sentence)
    {
        var words = sentence
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(word => word.Length > 0)
            .Take(FrameWords)
            .ToList();
        return words.Count < FrameWords ? string.Empty : string.Join(' ', words);
    }
}
