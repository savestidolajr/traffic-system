using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "road-name roll-call" gate. ATN style names at most a road or two, then sweeps
///     the rest into an area phrase ("the rest of the run's clear"); reading a string of bare road
///     names is the database-read-out AI-tell. Prompt rules for this proved unreliable (the model
///     ignored an explicit "name at most three" cap), so this counts the names in CODE and, when a
///     roll-call is present, forces the review→refine loop to re-draft with a targeted directive —
///     a gate the model cannot silently ignore.
///     <para>
///         Two triggers: (A) THREE OR MORE distinct CLEAR roads (good-run / flowing) named anywhere in
///         the script — the all-clear / good-news-run dump; (B) THREE OR MORE distinct known roads named
///         within a SINGLE sentence — a listy cluster. Cross-street/location anchors are NOT in the
///         known-road set, so a single closure pinned to its cross-streets never trips it.
///     </para>
/// </summary>
public static partial class RollCallDetector
{
    private const int ClearRoadCap = 4; // busy reads: safety net behind the FormatGoodRuns name cap

    /// <summary>
    ///     Clear-road cap for a QUIET read (no voiceable incident corridors): the ATN regional
    ///     corpus names clear roads as the story — Darwin's writer names five in a row ("traffic
    ///     moving well on McMinn Street, Cavanagh Street and along the Esplanade… Bagot Road and
    ///     McMillans Road are also running smoothly"). The roll-call gate exists for the busy-read
    ///     database dump, not for the all-clear read where the names ARE the content.
    /// </summary>
    private const int QuietClearRoadCap = 6;
    // 4, not 3: three roads sharing ONE concrete condition ("X, Y and Z are all at a standstill")
    // is legitimate objective radio under the 1-Jul factual style — names + a real state, not a bare
    // list. The gate exists for the genuine database read-out, which at the data caps (Major 4 /
    // Moderate 3 named) can only manifest as a 4+ name pile-up in one sentence.
    private const int PerSentenceCap = 4;

    /// <summary>
    ///     Minimum bare-chain length Repair collapses. Deliberately BELOW <see cref="PerSentenceCap" />:
    ///     a sentence trips Rule B on its total name count, but the collapsible run inside it (names
    ///     joined only by "," / "and") can be shorter — "A is at a standstill, while B, C and D are
    ///     slow" has 4 names but a 3-link chain; collapsing that chain takes the sentence back
    ///     under the gate.
    /// </summary>
    private const int BareChainMin = 3;

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();
    // v5.3 grouping exemption: a bare chain followed by a shared verb + condition keyword is a
    // legitimate grouped sentence ("X, Y and Z are all congested with minor delays"), not a
    // roll-call. Matches the tail after the last road name in the chain.
    [GeneratedRegex(
        @"^\s+(?:are|is|'re|'s)\s+(?:all\s+)?(?:congested|slow|under\s+(?:speed|minor\s+delays)|in\s+minor\s+delays|affected\s+by\s+(?:construction|roadworks)|showing\s+minor\s+delays|backed\s+up|at\s+a\s+standstill|feeling\s+it|under\s+construction|under\s+pace|bumper\s+to\s+bumper|stop-?start|on\s+the\s+brakes|a\s+touch\s+slow|a\s+bit\s+slow|under\s+a\s+bit\s+of\s+pressure|under\s+speed\s+with\s+minor\s+delays)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SharedCondition();

    public readonly record struct RollCallResult(
        bool HasRollCall,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RollCallResult(bool hasRollCall, string feedback)
            : this(hasRollCall, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RollCallResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new RollCallResult(false, string.Empty, []);
        // Rule A's universe is GoodRunRoads ONLY — the curated subset actually OFFERED to the
        // writer as "flowing well" (RundownPlanner/prompt caps this at 2 named + a sweep). It must
        // NEVER include FlowPool: that is the raw pre-selection candidate set (up to 18 roads),
        // and a name can appear there purely by coincidence with an unrelated mention elsewhere in
        // the script — an incident's own cross-street, or a commuter-route aside — with no
        // relation to "clear road" content at all. Concatenating FlowPool here false-positived a
        // clean script live (2026-07-02 pipeline A/B: Melbourne — the M1 incident's cross-street
        // "Huntingdale Road" and the commuter aside "Monash Freeway" both happened to also be
        // FlowPool entries, and were miscounted as 2 of a "4 clear roads in a row" roll-call that
        // was never voiced).
        var clearRoads = context.GoodRunRoads
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var corridorRoads = context.Corridors
            .Select(c => c.RoadName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Per-host anchor map: for each corridor, the OTHER corridor road names that appear in
        // its own location text (extent + each incident's Location + description). These are the
        // cross-street/endpoint anchors the writer is REQUIRED to voice ("shut between A and B",
        // "ramp to the M80") — Rule B must not count them as roll-called items when their host
        // corridor is co-voiced in the same sentence.
        var anchorsByHost = context.Corridors
            .Where(c => !string.IsNullOrWhiteSpace(c.RoadName))
            .GroupBy(c => c.RoadName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    string loc = string.Join(" | ", g.Select(LocationText));
                    return corridorRoads
                        .Where(r => !r.Equals(g.Key, StringComparison.OrdinalIgnoreCase) && Mentions(loc, r))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                },
                StringComparer.OrdinalIgnoreCase);

        // Rule A — clear-road dump (the good-news-run roll-call). Busy reads must not dump clear
        // names; quiet reads naming their clear roads are the ATN regional product. The band MUST
        // mirror FormatGoodRuns/RundownPlanner (busy = 3+ voiceable incidents): below that the
        // data itself offers up to 4 names, and a cap of 4 flags the exact read the data mandates
        // (observed: atn1 pack — Canberra/Newcastle/Brisbane quiet reads all false-flagged).
        int effectiveClearCap = CorridorTiering.CountVoiceable(context.Corridors, context.EditorialSettings) >= 3
            ? ClearRoadCap
            : QuietClearRoadCap;
        // Role-aware count: a pool road mentioned inside a sentence that names an incident
        // corridor is that incident's ANCHOR ("shut between Gold Coast Highway and Pacific
        // Motorway"), not a clear-run claim — the data itself mandates the mention, and counting it
        // false-flagged every candidate of a faithful read (atn6, Gold Coast: extent anchor "Gold
        // Coast Highway" counted as a 6th clear road). Only mentions in incident-free sentences
        // count toward the clear-run roll-call.
        var rawSentences = SentenceSplit().Split(script);
        var namedClearOrders = new List<(string Road, int Order)>();
        for (int i = 0; i < rawSentences.Length; i++)
        {
            string s = rawSentences[i];
            if (corridorRoads.Any(road => Mentions(s, road)))
                continue;
            foreach (var road in clearRoads.Where(road => Mentions(s, road)))
            {
                namedClearOrders.Add((road, i + 1));
            }
        }

        var distinctNamedClear = namedClearOrders
            .GroupBy(x => x.Road, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (distinctNamedClear.Count >= effectiveClearCap)
        {
            string feedback =
                $"ROLL-CALL: the script reads out {distinctNamedClear.Count} clear roads in a row ({Join(distinctNamedClear.Select(x => x.Road).ToList())}). " +
                "That is a database read-out. Keep the one or two clear runs that genuinely earn airtime (a usual " +
                "hotspot that's moving IS news) and CUT the rest entirely — never replace them with a vague sweep " +
                "(\"the rest is clear\", \"no other dramas\"): a clear claim is a fact about a NAMED road or it is not said.";
            var issues = distinctNamedClear.Skip(1)
                .Select(x => ReviewIssue.ForSentence(x.Order, "ROLL_CALL", feedback))
                .ToList();
            return new RollCallResult(true, feedback, issues);
        }
        // Rule B — listy sentence (3+ distinct known roads packed into one sentence with NO
        // individual detail per road — a single shared verb tacked on the end, "X, Y and Z are
        // all slow"). Distinguish this from legitimate connected prose where each named road
        // carries its OWN verb/clause ("X has minor delays, Y is congested at Z, and W is slow")
        // — that is exactly the "weave the items" style the writer is asked for elsewhere, not a
        // database read-out, even though it names three roads in one sentence.
        var sentences = SentenceSplit().Split(script);
        for (int i = 0; i < sentences.Length; i++)
        {
            string sentence = sentences[i];
            var named = corridorRoads.Where(r => Mentions(sentence, r)).ToList();

            // Discount anchor-role mentions (job fb44a570, 2026-07-08 — Greensborough Bypass):
            // a named road with NO own verb here that is a location anchor of a DIFFERENT corridor
            // voiced with its own verb in this same sentence is playing the anchor role ("Bypass
            // shut BETWEEN Grimshaw Street and Diamond Creek Road"), not being roll-called. In a
            // dense metro cluster that anchor road is often ALSO its own corridor, so it lands in
            // corridorRoads and false-flagged the faithful closure lead into an unfixable refine
            // loop. The data mandates the mention; only names not serving a co-voiced host's
            // anchor role count toward the list.
            var hosts = named.Where(r => HasOwnVerbNearby(sentence, r)).ToList();
            var counted = named
                .Where(r => HasOwnVerbNearby(sentence, r)
                    || !hosts.Any(h => !h.Equals(r, StringComparison.OrdinalIgnoreCase)
                        && anchorsByHost.TryGetValue(h, out var a) && a.Contains(r)))
                .ToList();
            if (counted.Count < PerSentenceCap)
                continue;

            int roadsWithOwnVerb = counted.Count(r => HasOwnVerbNearby(sentence, r));
            // At least ALL-BUT-ONE road must carry its own verb/detail for this to read as
            // connected prose — the last item is allowed to share the lead verb via "and" without
            // repeating it (e.g. "...and Chalmers Street is also slow" still has its own "is").
            // A TRUE bare list (zero or one road with its own verb) is the AI-tell this rule exists for.
            if (roadsWithOwnVerb < counted.Count - 1)
            {
                // v5.3 grouping exemption: a sentence like "Commercial Road, Rose Street and
                // Queen Street are all congested with minor delays" has a shared condition verb
                // followed by a condition keyword — that is a legitimate grouped sentence the
                // prompt explicitly asks for, NOT a bare database read-out. Only flag when the
                // chain is truly bare (no condition follows the shared verb).
                var lastMention = counted.OrderBy(r => sentence.IndexOf(r, StringComparison.OrdinalIgnoreCase)).Last();
                int lastEnd = sentence.IndexOf(lastMention, StringComparison.OrdinalIgnoreCase) + lastMention.Length;
                string tail = sentence[lastEnd..];
                if (SharedCondition().IsMatch(tail))
                    continue; // grouped with a condition — not a roll-call

                int order = i + 1;
                string feedback =
                    $"ROLL-CALL: one sentence names {counted.Count} roads in a row ({Join(counted)}) with a single " +
                    "shared verb tacked on the end, not individual detail per road. " +
                    "Lead with the one or two that matter most; give a genuinely separate item its own short clause, " +
                    "and sweep any merely-minor ones into ONE area phrase (\"a few minor hold-ups around town\"). " +
                    "Never read three or more road names in a row with nothing distinguishing them.";
                var issue = ReviewIssue.ForSentence(order, "ROLL_CALL", feedback);
                return new RollCallResult(true, feedback, [issue]);
            }
        }

        return new RollCallResult(false, string.Empty, []);
    }

    // A chain link — the textual shape of a bare list is road names joined ONLY by "," / "and" /
    // ", and" with nothing else between them. Anything longer between two road mentions (a verb,
    // a detail, a cross-street) means that road carries its own clause and breaks the chain.

    [GeneratedRegex(@"^\s*,?\s*(and\s+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ListConnector();

    /// <summary>
    ///     Mechanical last-resort fix for a Rule-B bare-list roll-call that survives the retry
    ///     budget (mirrors <see cref="IncidentTypeStackDetector.Repair" />). Finds the longest run
    ///     of road names joined only by list connectors ("," / "and") — the textual shape of the
    ///     bare list itself — and collapses everything after the first name in that run down to
    ///     "a couple more spots". Any road with its own clause (separated from its neighbours by
    ///     more than a bare connector) breaks the chain and is left completely untouched.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var corridorRoads = context.Corridors
            .Select(c => c.RoadName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sentences = SentenceSplit().Split(script);
        bool changed = false;

        for (int s = 0; s < sentences.Length; s++)
        {
            string sentence = sentences[s];
            var matched = corridorRoads
                .Select(r => (Road: r, Index: IndexOfWholeWord(sentence, r)))
                .Where(m => m.Index >= 0)
                .OrderBy(m => m.Index)
                .ThenByDescending(m => m.Road.Length)
                .ToList();

            // Corridor names can match OVERLAPPING spans when one contains another ("Ring Road"
            // inside "Northern Ring Road"). Ordered by index the shorter match then begins before
            // the longer one ends, and the gap slice below would run backwards and throw. Longest
            // match wins the span: it is the more specific road, and the text it covers is already
            // accounted for. Verified live — this crashed a finished bulletin (job 306287ef, KIIS
            // 101.1) and surfaced to the operator as an unexplained 'missed-window' fallback.
            var mentions = new List<(string Road, int Index)>(matched.Count);
            int spanEnd = 0;
            foreach (var m in matched)
            {
                if (m.Index < spanEnd)
                    continue;
                mentions.Add(m);
                spanEnd = m.Index + m.Road.Length;
            }

            // Counted AFTER collapsing overlaps: "Northern Ring Road" plus "Ring Road" is one road
            // being named once, not two, and must not count twice toward the roll-call cap.
            if (mentions.Count < PerSentenceCap)
                continue;

            int bestStart = 0, bestLen = 1;
            int runStart = 0, runLen = 1;
            for (int i = 1; i < mentions.Count; i++)
            {
                string gap = sentence[(mentions[i - 1].Index + mentions[i - 1].Road.Length)..mentions[i].Index];
                if (ListConnector().IsMatch(gap))
                {
                    runLen++;
                }
                else
                {
                    if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; }
                    runStart = i;
                    runLen = 1;
                }
            }
            if (runLen > bestLen) { bestLen = runLen; bestStart = runStart; }

            if (bestLen < BareChainMin)
                continue; // no chain shaped like a bare list — nothing safe to collapse here

            var first = mentions[bestStart];
            var last = mentions[bestStart + bestLen - 1];
            int firstEnd = first.Index + first.Road.Length;
            int lastEnd = last.Index + last.Road.Length;

            sentences[s] = sentence[..firstEnd] + " and a couple more spots" + sentence[lastEnd..];
            changed = true;
        }

        return changed ? string.Join(" ", sentences) : script;
    }

    private static int IndexOfWholeWord(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            int end = idx + word.Length;
            bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftOk && rightOk)
                return idx;
            idx = text.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return -1;
    }

    /// <summary>Whole-phrase, case-insensitive match so a road name only counts when actually voiced.</summary>
    private static bool Mentions(string text, string roadName) => IndexOfWholeWord(text, roadName) >= 0;

    // The location text the writer is handed for a corridor — its extent, description, and each
    // incident's own location line; cross-street/endpoint anchor road names live here
    // ("Between Grimshaw Street and Diamond Creek Road", "Ramp to M80 Metropolitan Ring Road").
    private static string LocationText(TrafficCorridor c) =>
        string.Join(" | ", new[] { c.Extent.GetValueOr(string.Empty), c.Description }
            .Concat(c.Incidents.Select(i => i.Location))
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    // A linking/state verb at (or just after) a road mention signals it carries its OWN clause
    // rather than just one more name in a list sharing a verb tacked on the end. An AU direction
    // word may sit between the road and its verb ("Greensborough Bypass southbound is shut") — the
    // road still has its own clause, so allow one optional direction/qualifier token before the
    // verb (job fb44a570, 2026-07-08: without this the closure lead's host corridor went
    // unrecognised and the anchor discount below never engaged, so the false roll-call survived).
    [GeneratedRegex(
        @"^\s*(?:(?:city|out|in|north|south|east|west)bound\s+|anti-?clockwise\s+|clockwise\s+)?(is|are|isn't|aren't|has|hasn't|have|haven't|was|wasn't|were|weren't|'s|remains|continues|becomes)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex OwnVerbNearby();

    /// <summary>
    ///     True when the FIRST mention of <paramref name="roadName" /> in <paramref name="sentence" />
    ///     is directly followed (within a couple of words, e.g. "is also slow", "'s congested") by
    ///     a verb — i.e. the road has its own clause, not just a name feeding a shared verb at the
    ///     sentence's end ("X, Y and Z are all slow").
    /// </summary>
    private static bool HasOwnVerbNearby(string sentence, string roadName)
    {
        int idx = sentence.IndexOf(roadName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;
        int after = idx + roadName.Length;
        if (after >= sentence.Length)
            return false;
        string tail = sentence[after..];
        return OwnVerbNearby().IsMatch(tail);
    }

    private static string Join(IReadOnlyList<string> roads) =>
        string.Join(", ", roads.Take(6)) + (roads.Count > 6 ? ", …" : string.Empty);
}
