using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "cloned sentence shape" gate. ATN style varies how each item is brought in;
///     reading the same sentence template down the bulletin — "King Street is moving freely through
///     the CBD. Collins Street is moving freely through the CBD. St Kilda Road is moving freely
///     through the CBD." — is a machine-tell the prompt bans but the model still produces under
///     sampling variance (observed live, Melbourne CBD). <see cref="RollCallDetector" /> only
///     catches a bare LIST inside one sentence; this catches the same frame REPEATED across
///     separate sentences, each with its own road and verb.
///     <para>
///         A clone is detected by skeletonising each sentence — replacing the known road names
///         (corridors, good-runs, flow pool) with a single placeholder, then normalising — and
///         flagging when one skeleton recurs: any repeat of a substantial frame (4+ shared words),
///         or three or more sentences sharing ANY frame. Two short, distinct closures never collide
///         because their tails differ; only a genuinely reused template does.
///     </para>
/// </summary>
public static class SentenceCloneDetector
{
    private const string RoadSentinel = "\u0001"; // never collides with a real word
    private const int SharedWordsThreshold = 4;   // a 4+ word shared frame reads as a template
    private const int TripleThreshold = 3;        // 3 sentences sharing ANY frame is a clone regardless of length

    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
    private static readonly Regex Punctuation = new(@"[^\w\s\u0001]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public readonly record struct SentenceCloneResult(
        bool HasClone,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public SentenceCloneResult(bool hasClone, string feedback)
            : this(hasClone, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static SentenceCloneResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SentenceCloneResult(false, string.Empty, []);

        var roads = KnownRoads(context);
        var roadRegex = BuildRoadRegex(roads);
        var sentences = SentenceSplit.Split(script).Where(s => s.Trim().Length > 0).ToList();
        if (sentences.Count < 2)
            return new SentenceCloneResult(false, string.Empty, []);

        // skeleton -> list of 1-based sentence orders
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            string skeleton = Skeletonize(sentence, roadRegex);
            if (skeleton.Length == 0)
                continue;
            if (!groups.TryGetValue(skeleton, out var orders))
            {
                orders = [];
                groups[skeleton] = orders;
            }
            orders.Add(i + 1);
        }

        foreach (var (skeleton, orders) in groups)
        {
            int count = orders.Count;
            if (count < 2)
                continue;
            int sharedWords = skeleton.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Count(t => t != RoadSentinel);
            if (count >= TripleThreshold || sharedWords >= SharedWordsThreshold)
            {
                string feedback =
                    $"CLONED SHAPE: {count} sentences reuse the same template (\"{Readable(skeleton)}\"), swapping " +
                    "only the road name. Reading the same shape down the bulletin is a machine-tell. Vary how each " +
                    "item comes in — lead one with the road, another with the condition or the spot — or, when several " +
                    "roads are simply clear, name at most one or two and sweep the rest into ONE area phrase " +
                    "(\"the rest of the CBD's flowing freely\") rather than a sentence each.";
                // Flag from the 2nd occurrence onwards so the 1st sentence is kept as the template anchor
                var issues = orders.Skip(1)
                    .Select(order => ReviewIssue.ForSentence(order, "CLONED_SHAPE", feedback))
                    .ToList();
                return new SentenceCloneResult(true, feedback, issues);
            }
        }

        // Same-road echo: the SAME road name leads (or heads) 2+ sentences — "Footscray Road is
        // closed at the CityLink Tollway for roadworks. Footscray Road is closed at Sims Street for
        // roadworks." The skeleton test above misses this when the differing cross-streets aren't
        // known-road tokens, but a road named to open two separate sentences is the same
        // repetitive tell (observed live, Melbourne CBD / Perth). Two DIFFERENT roads each at
        // their own cross-street never trip this — only a single road repeated.
        foreach (string road in roads)
        {
            var matchingOrders = new List<int>();
            for (int i = 0; i < sentences.Count; i++)
            {
                if (StartsWithRoad(sentences[i], road))
                    matchingOrders.Add(i + 1);
            }
            if (matchingOrders.Count >= 2)
            {
                string feedback =
                    $"REPEATED ROAD: \"{road}\" opens {matchingOrders.Count} separate sentences. Voicing the same road twice reads " +
                    "as a machine looping the feed. Combine them into ONE sentence (\"" + road + " is closed at both " +
                    "X and Y\") or, if only one point matters, drop the weaker mention. Name a road once.";
                var issues = matchingOrders.Skip(1)
                    .Select(order => ReviewIssue.ForSentence(order, "REPEATED_ROAD", feedback))
                    .ToList();
                return new SentenceCloneResult(true, feedback, issues);
            }
        }

        return new SentenceCloneResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Mechanical last-resort fix (mirrors <see cref="RollCallDetector.Repair" />): when the
    ///     word-target expansion pass reintroduces a clone after the review loop cleared it, keep
    ///     the FIRST sentence of each cloned group and drop the later duplicates. A shorter, sharper
    ///     read beats a repeated template, and the dropped sentences are — by definition of the
    ///     clone — low-value restatements of the same shape (typically "X is clear too").
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var roads = KnownRoads(context);
        var roadRegex = BuildRoadRegex(roads);
        var sentences = SentenceSplit.Split(script).Where(s => s.Trim().Length > 0).ToList();
        if (sentences.Count < 2)
            return script;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var clonedSkeletons = sentences
            .Select(s => Skeletonize(s, roadRegex))
            .Where(sk => sk.Length > 0)
            .GroupBy(sk => sk, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2 &&
                (g.Count() >= TripleThreshold ||
                 g.Key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(t => t != RoadSentinel) >= SharedWordsThreshold))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (clonedSkeletons.Count == 0)
            return script;

        var kept = new List<string>(sentences.Count);
        foreach (string sentence in sentences)
        {
            string skeleton = Skeletonize(sentence, roadRegex);
            if (clonedSkeletons.Contains(skeleton) && !seen.Add(skeleton))
                continue; // a later duplicate of an already-kept cloned shape — drop it
            kept.Add(sentence.Trim());
        }

        return string.Join(" ", kept);
    }

    private static List<string> KnownRoads(BroadcastWithTraffic context) =>
        context.Corridors.Select(c => c.RoadName)
            .Concat(context.GoodRunRoads)
            .Concat(context.FlowPool.Select(f => f.RoadName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Longest first so "St Kilda Road" is replaced whole before a bare "Road" fragment.
            .OrderByDescending(n => n.Length)
            .ToList();

    /// <summary>
    ///     One alternation regex over every known road, longest-first (mirrors
    ///     <see cref="KnownRoads" /> ordering) so "St Kilda Road" is consumed whole before a
    ///     bare "Road" fragment can match at the same position. Compiled once per
    ///     Detect/Repair call — a per-road pattern per sentence thrashes the 15-entry
    ///     Regex cache on real road sets (measured 2026-07-02: 2.4ms/1.3MB per Detect
    ///     with 25 roads; 60x less with the combined regex). Null when no roads exist.
    /// </summary>
    private static Regex? BuildRoadRegex(IReadOnlyList<string> roads) =>
        roads.Count == 0
            ? null
            : new Regex($@"\b(?:{string.Join("|", roads.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase);

    private static string Skeletonize(string sentence, Regex? roadRegex)
    {
        string s = roadRegex is null ? sentence : roadRegex.Replace(sentence, RoadSentinel);
        s = Punctuation.Replace(s, " ");
        s = Whitespace.Replace(s, " ").Trim().ToLowerInvariant();
        return s;
    }

    /// <summary>
    ///     True when the sentence opens with the road name (allowing a leading "The"), so a road
    ///     that heads two sentences is detected without matching a mere mid-sentence mention (a
    ///     cross-street reference to another road's anchor is not the same as leading with it).
    /// </summary>
    private static bool StartsWithRoad(string sentence, string road)
    {
        string s = sentence.TrimStart();
        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            s = s[4..];
        return s.StartsWith(road, StringComparison.OrdinalIgnoreCase)
            // Whole-token boundary: a following letter OR digit means a longer token ("M1" vs
            // "M11 Motorway", "Reid" vs "Reidvale"), not the same road leading again.
            && (s.Length == road.Length || !char.IsLetterOrDigit(s[road.Length]));
    }

    private static string Readable(string skeleton) =>
        skeleton.Replace(RoadSentinel, "[road]");
}
