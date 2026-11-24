using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "false-closure" gate. A corridor whose typed data does not mark it as closed
///     (PrimaryType != "roadClosure" and no incident with RoadClosed = true) is NOT a road closure
///     or blockage — yet the writer can call it "closed", "shut", "blocked off", "blocking",
///     "blocks", or "blocked". Observed live (Brisbane, 2026-07-13): Gilchrist Avenue —
///     construction, RoadClosed false — aired as "closed outbound" across every read, because a HERE
///     status string ("Closed due to road construction") leaked into the location anchor AND a real
///     closure on the adjacent Herston Road chained on with "also closed". Observed live
///     (2026-08-24): "A crash is blocking the M5 at Mascot." aired although the authority said
///     "1 of 2 westbound lanes closed" — not a road blockage. Prompt rules for this are unreliable
///     under sampling, so this counts the mismatch in CODE and forces a re-draft rather than shipping
///     a fabricated road closure or blockage.
///
///     Scope: this detector owns the one rule "road-level closure or blockage claim without
///     supporting closure data". The claim must fall in THIS road's clause (the window between its
///     name and the next corridor's name, or immediately before its name), so a grouped sentence
///     that closes road A and only slows road B does not false-flag B. Lane-level closures inside
///     roadworks ("a lane closed through the works") are legitimate and never flagged here —
///     <see cref="HazardBlockageDetector" /> owns the separate hazard-specific lane-blockage rule.
/// </summary>
public static partial class FalseClosureDetector
{
    /// <summary>
    ///     Road-level closure/blockage claim: "closed", "shut", "blocked off", "blocking", "blocks",
    ///     or "blocked" (not lane-level).
    /// </summary>
    [GeneratedRegex(@"\b(?:closed|shut|blocking|blocks|blocked)\b|\bblocked off\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClosureClaimRegex();

    /// <summary>Lane-level context — a lane closure inside works is legitimate, not a road closure.</summary>
    [GeneratedRegex(@"\blanes?\b", RegexOptions.IgnoreCase)]
    private static partial Regex LaneContextRegex();
    /// <summary>Clause boundaries stop a closure on an earlier grouped road binding to this road.</summary>
    [GeneratedRegex(@"[,;]|\b(?:and|but|while|whereas)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClaimBoundaryRegex();


    public readonly record struct FalseClosureResult(
        bool HasFalseClosure,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public FalseClosureResult(bool hasFalseClosure, string feedback)
            : this(hasFalseClosure, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static FalseClosureResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script) || context.Corridors.Count == 0)
            return new FalseClosureResult(false, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);

        foreach (var corridor in context.Corridors)
        {
            // Shared definition — the evidence emitter suppresses closure wording under exactly
            // this predicate, so the writer can never be handed a closure this check will reject.
            if (IncidentTypeClassifier.DataMarksClosure(corridor))
                continue;

            string roadName = corridor.RoadName;
            if (string.IsNullOrWhiteSpace(roadName))
                continue;

            for (int s = 0; s < sentences.Count; s++)
            {
                var sentence = sentences[s];
                if (ClaimsClosureFor(sentence, corridor, context.Corridors))
                {
                    string impactGuidance = ActualImpactGuidance(corridor);
                    string feedback =
                        $"FALSE-CLOSURE: data classifies {roadName} as {corridor.PrimaryType}, not a road " +
                        "closure (the data does not mark it closed). Report " + impactGuidance +
                        " instead of asserting that the road is blocked or closed; do not chain another " +
                        "road's closure onto it.";
                    int order = s + 1;
                    var issue = ReviewIssue.ForSentence(order, "FALSE_CLOSURE", feedback);
                    return new FalseClosureResult(true, feedback, [issue]);
                }
            }
        }

        return new FalseClosureResult(false, string.Empty, []);
    }
    private static string ActualImpactGuidance(TrafficCorridor corridor)
    {
        OfficialEvidence? laneEvidence = (corridor.OfficialEvidences ?? [])
            .FirstOrDefault(e => e.LanesClosed is > 0);

        if (laneEvidence is not null)
        {
            string laneFact = laneEvidence.LanesTotal is > 0
                ? $"{laneEvidence.LanesClosed} of {laneEvidence.LanesTotal} lanes closed"
                : $"{laneEvidence.LanesClosed} lane{(laneEvidence.LanesClosed == 1 ? string.Empty : "s")} closed";
            return $"the specific lane impact ({laneFact})";
        }

        return corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0
            ? $"the actual delay ({corridor.DelayMinutes.Value} minutes) or queue"
            : "the actual delay or queue";
    }


    /// <summary>
    ///     True when the sentence claims a road-level closure for THIS corridor — the closure verb
    ///     falls in the window from the road's name to the next corridor's name (or immediately
    ///     before its name, as in "blocking the M5"), so it binds to this road, not a different road
    ///     closed in the same grouped sentence. The window is not a lane-level closure inside works.
    /// </summary>
    private static bool ClaimsClosureFor(
        string sentence, TrafficCorridor corridor, IReadOnlyList<TrafficCorridor> all)
    {
        int idx = sentence.IndexOf(corridor.RoadName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        int start = idx + corridor.RoadName.Length;
        int end = sentence.Length;
        foreach (var other in all)
        {
            if (ReferenceEquals(other, corridor) || string.IsNullOrWhiteSpace(other.RoadName))
                continue;
            int oi = sentence.IndexOf(other.RoadName, start, StringComparison.OrdinalIgnoreCase);
            if (oi >= 0 && oi < end)
                end = oi;
        }

        string afterRoad = sentence[start..end];
        if (LaneContextRegex().IsMatch(afterRoad))
            return false;
        if (ClosureClaimRegex().IsMatch(afterRoad))
            return true;

        // A natural crash phrasing puts the blockage verb before the road name. Inspect only the
        // current road's prefix: a comma/conjunction boundary keeps a prior grouped road's closure
        // from binding to this one.
        int previousRoadEnd = 0;
        foreach (var other in all)
        {
            if (ReferenceEquals(other, corridor) || string.IsNullOrWhiteSpace(other.RoadName))
                continue;
            int oi = sentence.IndexOf(other.RoadName, StringComparison.OrdinalIgnoreCase);
            int otherEnd = oi + other.RoadName.Length;
            if (oi >= 0 && otherEnd <= idx)
                previousRoadEnd = Math.Max(previousRoadEnd, otherEnd);
        }

        string beforeRoad = sentence[previousRoadEnd..idx];
        if (LaneContextRegex().IsMatch(beforeRoad))
            return false;

        MatchCollection claims = ClosureClaimRegex().Matches(beforeRoad);
        if (claims.Count == 0)
            return false;

        Match lastClaim = claims[claims.Count - 1];
        string betweenClaimAndRoad = beforeRoad[(lastClaim.Index + lastClaim.Length)..];
        return !ClaimBoundaryRegex().IsMatch(betweenClaimAndRoad);
    }
}
