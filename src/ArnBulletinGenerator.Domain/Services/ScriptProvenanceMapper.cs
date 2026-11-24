using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic per-sentence provenance (Phase 3). Maps each script sentence to the source
///     corridors it names by matching the corridor's road name (raw and speakable form) in the
///     sentence text. Replaces the post-hoc attribution LLM call: provenance is derived from the
///     data the read was built on, not reconstructed by a model after paraphrasing, so it cannot
///     hallucinate a lineage and costs no tokens. Sentences that name no corridor (clear-run
///     summaries, weather, fuel, filler, safety lines) correctly carry no corridor keys.
/// </summary>
public static partial class ScriptProvenanceMapper
{
    /// <summary>
    ///     Words of a road name or a sentence: runs of letters, digits and apostrophes
    ///     ("W'mooloo", "M1"). Everything else — punctuation, hyphens, and the parenthetical
    ///     direction residue HERE emits — is a separator, and matching is therefore whole-word:
    ///     "M4" can no longer match inside "M40".
    /// </summary>
    [GeneratedRegex(@"[\p{L}\p{Nd}']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\[[^\]]+\]", RegexOptions.CultureInvariant)]
    private static partial Regex DirectionCueRegex();

    /// <summary>
    ///     A trailing possessive/contraction clitic, stripped so a read that contracts a road name
    ///     still names it: the naturalness polish routinely rewrites "The M4 is slow" as "The M4's
    ///     slow", and the polish gate compares corridor lineage before and after. An apostrophe
    ///     INSIDE a word ("W'mooloo") is untouched, and both the road name and the sentence are
    ///     normalized the same way, so "St Mary's Road" still matches itself.
    /// </summary>
    [GeneratedRegex(@"'s?$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCliticRegex();

    /// <summary>
    ///     Whole words the read may insert between two consecutive words of a road name and still
    ///     count as naming it. A read legitimately breaks a HERE road name apart: given
    ///     "M1 General Holmes Drive", and a global guidance rule requiring the definite article on
    ///     route codes, the writer voices "the M1 at General Holmes Drive". A contiguous match saw
    ///     no citation there, so the corridor backing that sentence was invisible to the operator
    ///     console (job 1e1db059, "Cited 1" on a read naming ten roads). Two is enough for the
    ///     article/preposition fillers a read inserts and tight enough that scattered common words
    ///     ("Drive", "Street") cannot accidentally spell out an unrelated road.
    /// </summary>
    private const int MaxInsertedWords = 2;

    /// <param name="monitoredRoutes">
    ///     The station's configured point-to-point commuter runs measured for this bulletin. A
    ///     monitored route is a ROUTE, not a road segment, so it never appears in
    ///     <paramref name="corridors" /> — omit it and a grounded drive-time sentence ("the M4 from
    ///     Parramatta to the city is a slower run, taking 33 minutes") lands in the operator
    ///     console with no lineage and is flagged unsourced. Null/empty for callers that only ask
    ///     about roads (the polish gate's protected-name list).
    /// </param>
    /// <param name="flowRoads">
    ///     The full data-backed flow pool available to the bulletin. Flow roads are persisted in
    ///     the corridor-key channel because they are road lineage, while monitored routes remain in
    ///     their separate route-key channel.
    /// </param>
    /// <param name="goodRunRoads">
    ///     The selected clear-running roads the writer was actually offered. A matched name receives
    ///     a synthetic <c>goodrun:</c> evidence id so the review UI can distinguish a clear-run fact
    ///     from incident traffic without treating every raw flow candidate as voiced.
    /// </param>
    public static IReadOnlyList<JobScriptSentence> Map(
        string script,
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<MonitoredRouteStatus>? monitoredRoutes = null,
        IReadOnlyList<FlowRoad>? flowRoads = null,
        IReadOnlyList<string>? goodRunRoads = null)
    {
        IReadOnlyList<string> sentences = ScriptSentenceSplitter.Split(script);
        if (sentences.Count == 0)
            return [];

        IReadOnlyList<MonitoredRouteStatus> routes = monitoredRoutes ?? [];
        IReadOnlyList<FlowRoad> flows = flowRoads ?? [];
        IReadOnlyList<string> goodRuns = goodRunRoads ?? [];
        if (corridors.Count == 0 && flows.Count == 0 && routes.Count == 0 && goodRuns.Count == 0)
            return sentences.Select(s => new JobScriptSentence(s, [], [])).ToList();

        var result = new List<JobScriptSentence>(sentences.Count);
        foreach (string sentence in sentences)
        {
            string[] words = Words(sentence);
            var keys = new List<string>();
            var evidenceIds = new List<string>();
            foreach (var corridor in corridors)
            {
                if (string.IsNullOrWhiteSpace(corridor.RoadName)
                    || keys.Contains(corridor.RoadName, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (Mentions(words, corridor.RoadName))
                    keys.Add(corridor.RoadName);
            }

            foreach (FlowRoad flow in flows)
            {
                if (string.IsNullOrWhiteSpace(flow.RoadName)
                    || keys.Contains(flow.RoadName, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (Mentions(words, flow.RoadName))
                    keys.Add(flow.RoadName);
            }

            foreach (string goodRunRoad in goodRuns)
            {
                if (string.IsNullOrWhiteSpace(goodRunRoad) || !Mentions(words, goodRunRoad))
                    continue;
                if (!keys.Contains(goodRunRoad, StringComparer.OrdinalIgnoreCase))
                    keys.Add(goodRunRoad);
                string evidenceId = $"goodrun:{goodRunRoad}";
                if (!evidenceIds.Contains(evidenceId, StringComparer.OrdinalIgnoreCase))
                    evidenceIds.Add(evidenceId);
            }

            // Matched on the configured name as a whole phrase, the same word-aware rule corridors
            // use. The writer is handed that exact name (ToSpeakable of it) in the data block, so a
            // read that voices the run matches; a read that paraphrases it away doesn't, and stays
            // honestly unsourced rather than claiming a lineage nobody can check.
            var routeKeys = new List<string>();
            foreach (var route in routes)
            {
                if (string.IsNullOrWhiteSpace(route.Name)
                    || routeKeys.Contains(route.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (Mentions(words, route.Name))
                    routeKeys.Add(route.Name);
            }

            result.Add(new JobScriptSentence(sentence, keys, evidenceIds, routeKeys));
        }

        return result;
    }

    /// <summary>
    ///     The distinct corridor road names actually named somewhere in <paramref name="script" />.
    ///     Single source of truth shared by the polish prompt (the "protected roads" list handed to
    ///     the model) and the polish gate (verbatim-preservation + lineage checks), so the contract
    ///     the model is told to honour is exactly the contract the gate enforces. Derived from
    ///     <see cref="Map" /> rather than scanning the whole script as one string, so a name cannot
    ///     be assembled out of words that sit in different sentences.
    /// </summary>
    public static IReadOnlyList<string> MentionedRoadNames(string script, IReadOnlyList<TrafficCorridor> corridors)
    {
        if (string.IsNullOrWhiteSpace(script) || corridors.Count == 0)
            return [];

        var names = new List<string>();
        foreach (var sentence in Map(script, corridors))
            foreach (string key in sentence.CorridorKeys)
                if (!names.Contains(key))
                    names.Add(key);

        return names;
    }
    /// <summary>
    ///     Finds every data-backed road phrase in a script and returns its exact UTF-16 range.
    ///     This is the server-owned matching contract for operator pronunciation editing: the
    ///     client renders these ranges but does not carry provider abbreviations, road aliases, or
    ///     traffic matching rules of its own.
    /// </summary>
    public static IReadOnlyList<ScriptRoadPhraseDto> MapRoadPhraseSpans(
        string script,
        IEnumerable<string> roadNames)
    {
        if (string.IsNullOrWhiteSpace(script))
            return [];

        var candidates = roadNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(BuildRoadCandidates)
            .GroupBy(candidate => $"{candidate.RoadName}\u0000{string.Join('\u0000', candidate.Words)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (candidates.Count == 0)
            return [];

        var matches = new List<(string RoadName, int Start, int End)>();
        foreach (var words in TokenizeRoadText(script))
        {
            foreach (var candidate in candidates)
            {
                for (int start = 0; start < words.Count; start++)
                {
                    if (candidate.Words.Count > words.Count - start
                        || !string.Equals(
                            candidate.Words[0],
                            words[start].Normalized,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    int cursor = start;
                    int matched = 1;
                    while (matched < candidate.Words.Count)
                    {
                        int found = -1;
                        int limit = Math.Min(words.Count - 1, cursor + 1 + MaxInsertedWords);
                        for (int index = cursor + 1; index <= limit; index++)
                        {
                            if (CrossesSentenceBoundary(script, words[cursor], words[index]))
                                continue;
                            if (string.Equals(
                                    candidate.Words[matched],
                                    words[index].Normalized,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                found = index;
                                break;
                            }
                        }

                        if (found < 0)
                            break;
                        cursor = found;
                        matched++;
                    }

                    if (matched == candidate.Words.Count)
                        matches.Add((candidate.RoadName, words[start].Start, words[cursor].End));
                }
            }
        }

        var selected = new List<ScriptRoadPhraseDto>();
        foreach (var match in matches
                     .OrderBy(match => match.Start)
                     .ThenByDescending(match => match.End)
                     .ThenBy(match => match.RoadName, StringComparer.OrdinalIgnoreCase))
        {
            if (selected.Any(existing =>
                    existing.Start < match.End && match.Start < existing.End))
                continue;

            string? target = RoadTargetText(script[match.Start..match.End]);
            if (target is null)
                continue;

            selected.Add(new ScriptRoadPhraseDto(
                match.RoadName,
                match.Start,
                match.End,
                target));
        }

        return selected;
    }

    /// <summary>
    ///     Per-sentence corridor objects (not just keys) for the polish stage's FACTS annotation.
    ///     Same matching as <see cref="Map" />, but exposes the full corridor so the polisher can be
    ///     TOLD each item's role — direction, condition type, severity band — instead of inferring it
    ///     from prose (which caused a bare direction token to be mis-voiced as a state, e.g. "Bruce
    ///     Highway is citybound"). Scoped to the roads the sentence already names, so it annotates
    ///     what the draft voiced rather than handing the polisher the full corridor list to re-draft.
    /// </summary>
    public static IReadOnlyList<(string Sentence, IReadOnlyList<TrafficCorridor> Corridors)> MapCorridors(
        string script, IReadOnlyList<TrafficCorridor> corridors)
    {
        IReadOnlyList<string> sentences = ScriptSentenceSplitter.Split(script);
        var result = new List<(string, IReadOnlyList<TrafficCorridor>)>(sentences.Count);
        foreach (string sentence in sentences)
        {
            string[] words = Words(sentence);
            var hit = new List<TrafficCorridor>();
            foreach (var c in corridors)
                if (!string.IsNullOrWhiteSpace(c.RoadName) && hit.All(h => h.RoadName != c.RoadName)
                    && Mentions(words, c.RoadName))
                    hit.Add(c);
            result.Add((sentence, hit));
        }
        return result;
    }

    private sealed record RoadWord(string Raw, int Start, int End, string Normalized);

    private sealed record RoadCandidate(string RoadName, IReadOnlyList<string> Words);

    private static IEnumerable<RoadCandidate> BuildRoadCandidates(string roadName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in new[] { roadName, RoadNameFormatter.ToSpeakable(roadName) })
            foreach (RoadCandidate candidate in AddSource(source))
                yield return candidate;

        foreach ((string alias, string canonical) in CorridorAggregator.AbbreviationMap)
        {
            if (!string.Equals(canonical, roadName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (RoadCandidate candidate in AddSource(alias))
                yield return candidate;
        }

        IEnumerable<RoadCandidate> AddSource(string source)
        {
            string[] words = Words(source)
                .Select(NormalizeRoadWord)
                .Where(word => word.Length > 0)
                .ToArray();
            if (words.Length == 0)
                yield break;

            foreach (RoadCandidate candidate in AddCandidate(words))
                yield return candidate;
            if (words.Length > 1)
                foreach (RoadCandidate candidate in AddCandidate([string.Concat(words)]))
                    yield return candidate;
        }

        IEnumerable<RoadCandidate> AddCandidate(string[] words)
        {
            string key = string.Join('\u0000', words);
            if (seen.Add(key))
                yield return new RoadCandidate(roadName.Trim(), words);
        }
    }

    private static IReadOnlyList<IReadOnlyList<RoadWord>> TokenizeRoadText(string script)
    {
        var allWords = WordRegex().Matches(script);
        var cues = DirectionCueRegex().Matches(script);
        var groups = new List<List<RoadWord>> { new() };
        int cueIndex = 0;

        foreach (Match match in allWords)
        {
            while (cueIndex < cues.Count && match.Index >= cues[cueIndex].Index + cues[cueIndex].Length)
            {
                groups.Add(new());
                cueIndex++;
            }

            if (cueIndex < cues.Count
                && match.Index >= cues[cueIndex].Index
                && match.Index < cues[cueIndex].Index + cues[cueIndex].Length)
                continue;

            groups[^1].Add(new RoadWord(
                match.Value,
                match.Index,
                match.Index + match.Length,
                NormalizeRoadWord(match.Value)));
        }

        return groups.Where(group => group.Count > 0).Cast<IReadOnlyList<RoadWord>>().ToList();
    }

    private static string NormalizeRoadWord(string raw)
    {
        string word = TrailingCliticRegex().Replace(raw, string.Empty);
        if (word.Length == 0)
            return string.Empty;

        string lower = word.ToLowerInvariant();
        if (CorridorAggregator.AbbreviationMap.TryGetValue(lower, out string? canonical)
            && !canonical.Contains(' ', StringComparison.Ordinal))
            return canonical.ToLowerInvariant();
        return lower;
    }

    private static bool CrossesSentenceBoundary(string script, RoadWord previous, RoadWord next)
    {
        string gap = script[previous.End..next.Start];
        if (gap.IndexOfAny(['!', '?', '…']) >= 0)
            return true;

        int period = gap.IndexOf('.');
        return period >= 0 && !IsAbbreviatedPeriod(previous.Raw);
    }

    private static bool IsAbbreviatedPeriod(string raw)
    {
        string lower = raw.TrimEnd('.').ToLowerInvariant();
        return lower is "ave" or "av" or "blvd" or "br" or "cct" or "cres" or "ct"
            or "dr" or "esp" or "expy" or "fwy" or "gr" or "hwy" or "ln" or "mt"
            or "mtwy" or "mwy" or "pde" or "pkwy" or "pl" or "rd" or "st" or "tce"
            or "ter" or "trl";
    }

    private static string? RoadTargetText(string raw)
    {
        string trimmed = Regex.Replace(raw.Trim(), @"^[^\p{L}\p{N}]+", string.Empty);
        trimmed = TrailingCliticRegex().Replace(trimmed, string.Empty);
        trimmed = Regex.Replace(trimmed, @"[^\p{L}\p{N}]+$", string.Empty);
        return trimmed.Length is 0 or > PronunciationRule.MaxWordLength ? null : trimmed;
    }

    private static string[] Words(string text)
    {
        var matches = WordRegex().Matches(text);
        var words = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            string word = matches[i].Value;
            string trimmed = TrailingCliticRegex().Replace(word, "");
            // A token that is nothing but an apostrophe keeps its raw form rather than becoming
            // empty, so it can never match every name word at once.
            words[i] = trimmed.Length > 0 ? trimmed : word;
        }
        return words;
    }

    /// <summary>
    ///     True when the sentence names this corridor's road — by its canonical form, its
    ///     TTS-speakable form, or a colloquialism the script legitimately used instead ("Tulla"
    ///     for "Tullamarine", "Westgate" for "West Gate" — see
    ///     <see cref="CorridorAggregator.AbbreviationMap" />). Without the alias check, a
    ///     correctly-grounded sentence using a common nickname silently lost its corridor
    ///     citation, which then starved the operator console's claim checker of the one corridor
    ///     that actually backs the sentence and left it comparing the sentence's claim against an
    ///     unrelated corridor named elsewhere in the same sentence.
    /// </summary>
    private static bool Mentions(string[] sentenceWords, string roadName)
    {
        if (NamesRoad(sentenceWords, roadName))
            return true;

        string speakable = RoadNameFormatter.ToSpeakable(roadName);
        if (!string.IsNullOrWhiteSpace(speakable)
            && !string.Equals(speakable, roadName, StringComparison.OrdinalIgnoreCase)
            && NamesRoad(sentenceWords, speakable))
            return true;

        foreach (var (alias, canonical) in CorridorAggregator.AbbreviationMap)
            if (string.Equals(canonical, roadName, StringComparison.OrdinalIgnoreCase)
                && NamesRoad(sentenceWords, alias))
                return true;

        return false;
    }

    /// <summary>
    ///     True when <paramref name="sentenceWords" /> voices every word of <paramref name="name" />
    ///     in order, allowing at most <see cref="MaxInsertedWords" /> inserted words between
    ///     consecutive name words. Word-aware rather than a substring scan, so a read may put an
    ///     article or preposition inside a road name without losing its citation, while a name can
    ///     still only match on whole words.
    /// </summary>
    private static bool NamesRoad(string[] sentenceWords, string name)
    {
        string[] nameWords = Words(name);
        if (nameWords.Length == 0 || nameWords.Length > sentenceWords.Length)
            return false;

        int last = nameWords.Length - 1;
        for (int start = 0; start <= sentenceWords.Length - nameWords.Length; start++)
        {
            if (!Same(sentenceWords[start], nameWords[0], allowPlural: last == 0))
                continue;

            int cursor = start;
            int matched = 1;
            while (matched < nameWords.Length)
            {
                int found = -1;
                int limit = Math.Min(sentenceWords.Length - 1, cursor + 1 + MaxInsertedWords);
                for (int j = cursor + 1; j <= limit; j++)
                    if (Same(sentenceWords[j], nameWords[matched], allowPlural: matched == last))
                    {
                        found = j;
                        break;
                    }

                if (found < 0)
                    break;

                cursor = found;
                matched++;
            }

            if (matched == nameWords.Length)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Word equality, optionally accepting the sentence's PLURAL of the name word. A read
    ///     collapses a shared road type across an enumeration — "Narellan, Showground and Wallgrove
    ///     Roads" names Wallgrove Road — and the substring scan this replaced matched those by
    ///     accident. Allowed on the FINAL word of a name only: a plural anywhere earlier is a
    ///     different road.
    /// </summary>
    private static bool Same(string sentenceWord, string nameWord, bool allowPlural = false)
    {
        if (string.Equals(sentenceWord, nameWord, StringComparison.OrdinalIgnoreCase))
            return true;

        return allowPlural
            && sentenceWord.Length == nameWord.Length + 1
            && char.ToLowerInvariant(sentenceWord[^1]) == 's'
            && string.Equals(sentenceWord[..^1], nameWord, StringComparison.OrdinalIgnoreCase);
    }
}
