using System.Globalization;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic script-vs-transcript comparison for the pre-approval audio review.
///     A significant script word that does not survive transcription (exactly or fuzzily)
///     is a suspect pronunciation — chiefly road and place names, where TTS most often
///     fails on air. Transcription itself is imperfect (homophone spellings, "Warringer"
///     for a correctly-said Warringah), so the comparison is deliberately tolerant: exact
///     match, prefix overlap, or small edit distance all count as intelligible; only words
///     with NO close counterpart anywhere in the transcript are flagged. Word boundaries are
///     tolerated in BOTH directions — the transcriber both splits compounds it does not know
///     ("roadworks" → "road works") and fuses ones it does ("West Gate" → "Westgate") — and a
///     possessive is compared on its stem, because that is the part the transcriber respells.
/// </summary>
public static partial class AudioPreReview
{
    /// <summary>Words shorter than this carry no pronunciation signal (articles, prepositions).</summary>
    private const int MinSignificantLength = 4;

    /// <summary>
    ///     Spoken-number vocabulary the transcriber writes back as symbols/digits ("a dollar
    ///     sixty-three" → "$1.63"), making the words vanish from the transcript without any
    ///     pronunciation problem. Never suspects. First live catch of the gate was exactly
    ///     this false positive ("dollar", test job 334d93f8, 2026-07-20).
    /// </summary>
    private static readonly HashSet<string> NeverSuspect = new(StringComparer.OrdinalIgnoreCase)
    {
        "dollar", "dollars", "cent", "cents",
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        "hundred", "thousand",
        // Spoken unit shorthand the transcriber writes as symbols ("two kays" → "2Ks";
        // live flag on test job 898658ca, 2026-07-20).
        "kays", "kay", "kilometre", "kilometres",
    };

    /// <summary>
    ///     Unit nouns that mark the preceding digits as a VERIFIABLE quantity. Deliberately a
    ///     whitelist, and deliberately excluding "mile" — Australian bulletins measure in
    ///     kilometres, so a numeral before "Mile" is a place name ("8 Mile Bridge"), never a
    ///     distance. Missing a unit costs one unverified figure; guessing wrong flags a road
    ///     name on every bulletin that mentions it.
    /// </summary>
    private static readonly HashSet<string> QuantityUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "degree", "degrees",
        "dollar", "dollars", "cent", "cents",
        "minute", "minutes", "hour", "hours",
        "kilometre", "kilometres", "kilometer", "kilometers", "km", "kms",
        "kay", "kays", "metre", "metres", "meter", "meters",
        "percent"
    };

    public sealed record Result(
        double MatchRatio,
        IReadOnlyList<string> SuspectWords,
        IReadOnlyList<string> NumberMismatches,
        IReadOnlyList<string> UnheardTailWords)
    {
        // Unheard tail words are a subset of SuspectWords, so Clean is already false when one exists.
        public bool Clean => SuspectWords.Count == 0 && NumberMismatches.Count == 0;
    }

    public static Result Compare(string script, string transcript)
    {
        var numberMismatches = CompareNumbers(script, transcript);

        // Positions matter, not just the vocabulary: a fused rendering is only credible
        // against the neighbour the script actually put next to the word (see IsHeard).
        var scriptTokens = WordRegex().Matches(script).Select(m => m.Value).ToList();
        var occurrences = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var distinctWords = new List<string>();
        for (int i = 0; i < scriptTokens.Count; i++)
        {
            if (!IsSignificant(scriptTokens[i]))
                continue;
            if (!occurrences.TryGetValue(scriptTokens[i], out var positions))
            {
                occurrences[scriptTokens[i]] = positions = [];
                distinctWords.Add(scriptTokens[i]);
            }
            positions.Add(i);
        }

        if (distinctWords.Count == 0)
            return new Result(1.0, [], numberMismatches, []);

        var transcriptWords = Significant(transcript);
        // The transcriber splits compounds it doesn't know ("roadworks" → "road works"):
        // joined adjacent pairs count as candidate renderings too. Built from ALL transcript
        // tokens (not just significant ones) so "road"+"works" survives the length filter.
        var transcriptTokens = WordRegex().Matches(transcript).Select(m => m.Value).ToList();
        var candidates = new List<string>(transcriptWords);
        for (int i = 0; i + 1 < transcriptTokens.Count; i++)
            candidates.Add(transcriptTokens[i] + transcriptTokens[i + 1]);

        var suspects = new List<string>();
        int matched = 0;
        foreach (var word in distinctWords)
        {
            if (IsHeard(word, occurrences[word], scriptTokens, candidates, transcriptWords))
                matched++;
            else
                suspects.Add(word);
        }

        var suspectByKey = suspects.ToDictionary(
            word => word,
            word => word,
            StringComparer.OrdinalIgnoreCase);
        var tail = new List<string>();
        var tailKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = scriptTokens.Count - 1; i >= 0; i--)
        {
            var token = scriptTokens[i];
            if (!IsSignificant(token))
                continue;

            if (!suspectByKey.TryGetValue(token, out var suspect))
                break;

            if (tailKeys.Add(suspect))
                tail.Add(suspect);
        }

        tail.Reverse();
        return new Result((double)matched / distinctWords.Count, suspects, numberMismatches, tail);
    }

    /// <summary>
    ///     Whether a script word survived transcription in any credible form: as written, as
    ///     its bare stem behind a possessive, or fused with a significant neighbour.
    ///     <para>
    ///         The fused form is the missing mirror of the transcript-side join in
    ///         <see cref="Compare" />. That join covers the transcriber SPLITTING a compound;
    ///         nothing covered it FUSING one, so a script two-worder written as one word came
    ///         back with its first half matched on prefix and its second half orphaned —
    ///         permanently. "West Gate Freeway" → "Westgate Freeway" flagged "Gate" on every
    ///         Melbourne bulletin naming the road: 25 of 96 flags in dev history, one road name,
    ///         all false (verified against re-transcribed audio, 2026-08-10).
    ///     </para>
    ///     <para>
    ///         A fused form is matched against SINGLE transcript words only, never the joined
    ///         pairs. Comparing pair against pair lets two loosely-heard words vouch for each
    ///         other under the longer string's larger edit budget — "Toorak Road" ≈ "Torrac
    ///         Road" passes on a distance the bare words fail, and a genuinely suspect vowel is
    ///         exactly what this gate exists to surface.
    ///     </para>
    /// </summary>
    private static bool IsHeard(
        string word, List<int> positions, List<string> scriptTokens,
        List<string> candidates, List<string> transcriptWords)
    {
        if (candidates.Any(t => Intelligible(word, t)))
            return true;

        string stem = StripPossessive(word);
        bool stemUsable = stem.Length >= MinSignificantLength;
        if (stemUsable && stem.Length != word.Length && candidates.Any(t => Intelligible(stem, t)))
            return true;

        string fusable = stemUsable ? stem : word;
        foreach (int i in positions)
        {
            if (i > 0 && IsSignificant(scriptTokens[i - 1])
                && transcriptWords.Any(t => Intelligible(scriptTokens[i - 1] + fusable, t)))
                return true;
            if (i + 1 < scriptTokens.Count && IsSignificant(scriptTokens[i + 1])
                && transcriptWords.Any(t => Intelligible(fusable + scriptTokens[i + 1], t)))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The bare stem behind a possessive. The transcriber respells the STEM and keeps the
    ///     clitic ("Greensborough's" → "Greensboro's"), but those two extra characters raise the
    ///     prefix rule's threshold by two and widen the length gap past the edit budget — so the
    ///     bare name matches where its possessive does not. Live on dev job 43382c2a: one
    ///     bulletin, "Greensborough" clean and "Greensborough's" flagged.
    /// </summary>
    private static string StripPossessive(string word) =>
        word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) ? word[..^2] : word;

    /// <summary>
    ///     Verifies the QUANTITIES the bulletin airs — prices, temperatures, distances — the
    ///     one error class word-matching cannot see (spoken-number vocabulary is excluded from
    ///     it precisely because the transcriber writes numbers back as digits). Both sides are
    ///     reduced to canonical digit-strings; a script number with NO acceptable rendering
    ///     anywhere in the transcript is a mismatch. Precision-first: constructs with several
    ///     legitimate readings contribute EVERY reading as acceptable ("a dollar 63" accepts
    ///     both 63 and 163; "$1.63" accepts 163 and 1.63), so only genuinely different values
    ///     flag — a misread price ("$1.53" for "$1.63") no longer passes the gate.
    ///     <para>
    ///         A digit only counts as a quantity when its CONTEXT says so (see
    ///         <see cref="QuantityUnits" />). Road designations and numbered place names are
    ///         the overwhelming majority of digits in a traffic bulletin — "M80", "8 Mile
    ///         Bridge", "Route 31" — and none of them is a value anyone can verify by ear, so
    ///         flagging them was pure noise that trained operators to ignore the gate
    ///         (reported on dev job ae41aa6d, 2026-07-28).
    ///     </para>
    /// </summary>
    private static IReadOnlyList<string> CompareNumbers(string script, string transcript)
    {
        // The context gate decides what we VERIFY, never what counts as EVIDENCE: the
        // transcript is the pool of candidate renderings, and the transcriber routinely
        // drops the unit the script carried ("$1.63" read back as a bare "1.63"). Gating
        // that side too would invent mismatches out of its own vocabulary.
        var scriptNumbers = ExtractNumbers(script, requireQuantityContext: true);
        if (scriptNumbers.Count == 0)
            return [];

        // The transcriber writes a figure back either way ("forty" or "40"), so both spellings
        // are evidence. Dev 2e158de3 (2026-09-02) warned "aired quantities not verified — 40" for
        // a correctly voiced "40 kilometre-an-hour" that the transcript spelled "forty".
        var transcriptAccepted = new HashSet<string>(
            ExtractNumbers(transcript, requireQuantityContext: false).SelectMany(n => n.Accepted)
                .Concat(SpokenNumbers(transcript)),
            StringComparer.Ordinal);

        return scriptNumbers
            .Where(n => !n.Accepted.Any(transcriptAccepted.Contains))
            .Select(n => n.Display)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private sealed record NumberMention(string Display, IReadOnlyList<string> Accepted);

    private static List<NumberMention> ExtractNumbers(string text, bool requireQuantityContext)
    {
        var mentions = new List<NumberMention>();
        foreach (Match m in NumberRegex().Matches(text))
        {
            string raw = m.Value;
            string digits = raw.Replace("$", "").Replace(",", "");
            string flat = digits.Replace(".", "");
            var accepted = new List<string> { flat, digits };

            // "a dollar 63" / "dollar 1.63": the currency word bridges a leading implicit 1 —
            // accept the fused dollars+cents form too (the transcriber writes "$1.63").
            int before = m.Index;
            string prefix = text[Math.Max(0, before - 12)..before];
            bool currencyPrefixed = DollarWordRegex().IsMatch(prefix);
            if (currencyPrefixed && !raw.StartsWith('$') && flat.Length <= 2)
                accepted.Add("1" + flat.PadLeft(2, '0'));

            // Only a number the bulletin presents AS a quantity is verifiable by ear.
            if (requireQuantityContext
                && !raw.StartsWith('$')
                && !currencyPrefixed
                && !FollowedByUnit(text, m.Index + m.Length))
                continue;

            mentions.Add(new NumberMention(raw, accepted));
        }
        return mentions;
    }

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
        ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
    };

    /// <summary>
    ///     Digit strings for the spoken numbers in <paramref name="text" />: single words and
    ///     tens-unit compounds joined by a hyphen or a space ("twenty-two", "forty five").
    /// </summary>
    private static IEnumerable<string> SpokenNumbers(string text)
    {
        string[] words = WordRegex().Matches(text).Select(m => m.Value).ToArray();
        for (int i = 0; i < words.Length; i++)
        {
            if (!NumberWords.TryGetValue(words[i], out int value))
                continue;
            yield return value.ToString(CultureInfo.InvariantCulture);
            if (value >= 20 && value % 10 == 0 && i + 1 < words.Length
                && NumberWords.TryGetValue(words[i + 1], out int unit) && unit is > 0 and < 10)
                yield return (value + unit).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    ///     True when the token immediately after a number names a unit — the signal that
    ///     separates "14 degrees" from "8 Mile Bridge". A hyphen or whitespace may sit
    ///     between ("two-twenty" is spoken, but "40-minute" is not).
    /// </summary>
    private static bool FollowedByUnit(string text, int afterIndex)
    {
        int i = afterIndex;
        if (i < text.Length && text[i] == '%')
            return true;
        while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '-'))
            i++;
        int start = i;
        while (i < text.Length && char.IsLetter(text[i]))
            i++;
        return i > start && QuantityUnits.Contains(text[start..i]);
    }

    /// <summary>Carries pronunciation signal: long enough to hear, and not spoken-number vocabulary.</summary>
    private static bool IsSignificant(string word) =>
        word.Length >= MinSignificantLength && !NeverSuspect.Contains(word);

    private static List<string> Significant(string text) =>
        WordRegex().Matches(text)
            .Select(m => m.Value)
            .Where(IsSignificant)
            .ToList();

    /// <summary>
    ///     A transcript word counts as an intelligible rendering of a script word when they
    ///     match case-insensitively, share a long prefix (transcription respells endings:
    ///     "Warringah"/"Warringer"), or sit within a small edit distance for their length.
    /// </summary>
    private static bool Intelligible(string scriptWord, string transcriptWord)
    {
        if (scriptWord.Equals(transcriptWord, StringComparison.OrdinalIgnoreCase))
            return true;

        int prefix = CommonPrefixLength(scriptWord, transcriptWord);
        if (prefix >= Math.Max(MinSignificantLength, scriptWord.Length - 3))
            return true;

        int budget = scriptWord.Length <= 6 ? 1 : 2;
        return Math.Abs(scriptWord.Length - transcriptWord.Length) <= budget
               && EditDistanceAtMost(scriptWord.ToLowerInvariant(), transcriptWord.ToLowerInvariant(), budget);
    }

    private static int CommonPrefixLength(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
        return i;
    }

    /// <summary>Banded Levenshtein with early exit — only ever called with budget 1 or 2.</summary>
    private static bool EditDistanceAtMost(string a, string b, int budget)
    {
        if (Math.Abs(a.Length - b.Length) > budget) return false;
        int[] prev = new int[b.Length + 1];
        int[] cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }
            if (rowMin > budget) return false;
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length] <= budget;
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z']*")]
    private static partial Regex WordRegex();

    // Boundaries keep road designations out entirely: "M80" must never yield the number 80,
    // and "A1" must never yield 1.
    [GeneratedRegex(@"(?<![A-Za-z0-9])\$?\d+(?:[.,]\d+)*(?![A-Za-z0-9])")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\bdollars?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DollarWordRegex();
}
