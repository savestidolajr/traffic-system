using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Observation-only road-grounding detector. Computes whether a draft voices a road-like name
///     that the supplied traffic data actually contains. Its result is logged for observation and
///     is not enforced: it does not reject, repair, or re-route a script. This is the converse of
///     citation verification: a citation can be valid while a sentence appends a second,
///     world-knowledge road that was never supplied. Weather and fuel sentences are supplementary
///     sections, so they are deliberately excluded from this traffic-road check just as
///     <see cref="RequiredSourceDetector" /> and <see cref="SectionOrderDetector" /> classify them.
/// </summary>
public static class RoadNameGroundingDetector
{
    public readonly record struct RoadNameGroundingResult(
        bool HasUngroundedRoad,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RoadNameGroundingResult(bool hasUngroundedRoad, string feedback)
            : this(hasUngroundedRoad, feedback, [])
        {
        }

    }
    private readonly record struct Word(string Value, int Index, int Length);

    private static readonly Regex WordRegex = new(
        @"[\p{L}\p{Nd}]+(?:['’][\p{L}\p{Nd}]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // These are clause boundaries, not road vocabulary. A road name may contain ordinary lower-case
    // words, but a generated bulletin's proper-name run ends at a locative or connective phrase.
    private static readonly HashSet<string> NameBoundaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "at", "near", "on", "in", "from", "to", "between", "past", "along", "off", "outside",
        "opposite", "towards", "toward", "via", "with", "causing", "which", "that", "has", "have",
        "is", "are", "was", "were"
    };

    public static RoadNameGroundingResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new RoadNameGroundingResult(false, string.Empty, []);

        var suppliedRoads = SuppliedRoadNames(context).ToList();

        var unknown = new List<(string Name, string Sentence)>();
        foreach (string sentence in ContentSourceSignals.SplitSentences(script))
        {
            // Weather/fuel and their station/bookend tail are supplementary content, not traffic
            // evidence. Do not mistake a station name or a place mentioned in those lines for a road.
            if (ContentSourceSignals.IsWeatherLine(sentence) || ContentSourceSignals.IsFuelLine(sentence))
                continue;

            foreach (string candidate in RoadCandidates(sentence))
            {
                if (suppliedRoads.Any(known => RoadNameFormatter.IsSameRoad(candidate, known)))
                    continue;
                if (unknown.All(item => !string.Equals(item.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                    unknown.Add((candidate, sentence));
            }
        }

        if (unknown.Count == 0)
            return new RoadNameGroundingResult(false, string.Empty, []);

        string list = string.Join("; ", unknown.Select(item => $"\"{item.Name}\""));
        string feedback =
            $"ROAD NOT IN SUPPLIED DATA: the script names road-like place(s) that do not resolve to any " +
            $"corridor, incident, cross-street, anchor, or supplied asset: {list}. Remove each invented road " +
            "or replace it only with a road the data supplied; do not rely on world knowledge.";

        var sentences = ScriptSentenceSplitter.Split(script);
        var issues = new List<ReviewIssue>(unknown.Count);

        foreach (var (name, rawSentence) in unknown)
        {
            int? order = null;
            for (int i = 0; i < sentences.Count; i++)
            {
                if (sentences[i].Contains(name, StringComparison.OrdinalIgnoreCase)
                    || sentences[i].Contains(rawSentence.Trim(), StringComparison.Ordinal)
                    || rawSentence.Contains(sentences[i].Trim(), StringComparison.Ordinal))
                {
                    order = i + 1;
                    break;
                }
            }

            string reason =
                $"ROAD NOT IN SUPPLIED DATA: \"{name}\" does not resolve to any supplied corridor, incident or anchor. " +
                "Remove this invented road or replace it with a supplied road.";

            issues.Add(order is > 0
                ? ReviewIssue.ForSentence(order.Value, "ROAD_NAME_GROUNDING", reason)
                : ReviewIssue.ForUnresolvedSentence("ROAD_NAME_GROUNDING", reason));
        }

        return new RoadNameGroundingResult(true, feedback, issues);
    }

    private static IEnumerable<string> SuppliedRoadNames(BroadcastWithTraffic context)
    {
        var names = new List<string>();

        void AddText(string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                names.AddRange(RoadCandidates(text));
        }

        void AddIncident(TrafficIncident incident)
        {
            AddText(incident.Location);
            AddText(incident.Description);
            if (incident.ClosedAsset.HasValue) AddText(incident.ClosedAsset.Value);
            if (incident.Fact is { } fact)
            {
                AddText(fact.RoadName);
                if (fact.Anchor.HasValue) AddText(fact.Anchor.Value);
                if (fact.SecondaryAnchor.HasValue) AddText(fact.SecondaryAnchor.Value);
                if (fact.RampTarget.HasValue) AddText(fact.RampTarget.Value);
                foreach (string asset in fact.OtherAssets ?? []) AddText(asset);
            }
        }

        foreach (TrafficCorridor corridor in context.Corridors)
        {
            AddText(corridor.RoadName);
            AddText(corridor.Description);
            if (corridor.Extent.HasValue) AddText(corridor.Extent.Value);
            if (corridor.OfficialCrossStreet.HasValue) AddText(corridor.OfficialCrossStreet.Value);
            foreach (TrafficIncident incident in corridor.Incidents) AddIncident(incident);
            foreach (OfficialEvidence evidence in corridor.OfficialEvidences ?? [])
            {
                AddText(evidence.RoadName);
                AddText(evidence.CrossStreet);
                AddText(evidence.Headline);
                AddText(evidence.Description);
            }
        }

        foreach (TrafficIncident incident in context.Incidents) AddIncident(incident);
        foreach (string road in context.GoodRunRoads) AddText(road);
        foreach (FlowRoad road in context.FlowPool) AddText(road.RoadName);

        return names.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> RoadCandidates(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        string speakable = RoadNameFormatter.ToSpeakable(text);
        MatchCollection matches = WordRegex.Matches(speakable);
        if (matches.Count == 0)
            return [];

        var words = matches.Select(m => new Word(m.Value, m.Index, m.Length)).ToArray();
        var candidates = new List<string>();
        for (int i = 0; i < words.Length; i++)
        {
            if (RoadNameFormatter.IsRouteCodeOnly(words[i].Value))
                AddCandidate(words[i].Value);

            if (!RoadNameFormatter.IsRoadTypeToken(words[i].Value))
                continue;

            int start = i;
            while (start > 0
                && IsNameWord(words[start - 1].Value)
                && !NameBoundaries.Contains(words[start - 1].Value)
                && HasOnlyNameSeparators(speakable, words[start - 1], words[start]))
            {
                start--;
            }

            // A bare "road" / "street" is an incident type or generic prose, not a road name.
            if (start == i)
                continue;

            string candidate = speakable[words[start].Index..(words[i].Index + words[i].Length)].Trim();
            if (candidate.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                candidate = candidate[4..];
            AddCandidate(candidate);

            void AddCandidate(string value)
            {
                if (!string.IsNullOrWhiteSpace(value)
                    && candidates.All(existing => !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(value);
                }
            }
        }

        return candidates;
    }

    private static bool IsNameWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || NameBoundaries.Contains(word))
            return false;
        if (RoadNameFormatter.IsRouteCodeOnly(word) || word.Any(char.IsDigit))
            return true;
        return char.IsUpper(word[0]) || word.All(c => !char.IsLetter(c) || char.IsUpper(c));
    }

    private static bool HasOnlyNameSeparators(string text, Word previous, Word next)
    {
        int start = previous.Index + previous.Length;
        int length = next.Index - start;
        if (length <= 0)
            return true;
        string gap = text.Substring(start, length);
        return gap.All(c => char.IsWhiteSpace(c) || c is '-');
    }
}
