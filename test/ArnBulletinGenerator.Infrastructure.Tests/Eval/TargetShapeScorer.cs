using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Heuristic "distance to the ATN target shape" scorer for a generated traffic bulletin
///     Each dimension scores 0..1;
///     dimensions the input data cannot support — a drive-time headline when no corridor carries
///     a routed/flow drive time, or congestion phrasing when there is no congestion corridor — are
///     marked N/A (null) and excluded so the aggregate is comparable across corpus-replay (no
///     flow/routing) and live runs.
///     <para>
///         This is a repeatable signal to iterate against as the spec's features land, NOT ground
///         truth — always read the actual bulletins too.
///     </para>
/// </summary>
internal static class TargetShapeScorer
{
    private const double DefaultWordsPerSecond = 2.9; // ~AU radio read rate; fallback when no voice calibration.

    private static readonly Regex DriveTime =
        new(@"\b\d+\s*-?\s*minute(s)?\b|\bminute run\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A strong lead can voice the worst corridor's magnitude OR a road closure instead of a drive
    // time — leading with the worst corridor (a severe delay or a shut road) is correct ATN
    // behaviour even when it isn't the monitored route. A closure carries little measured delay
    // because the road is shut, so the closure itself is the magnitude.
    private static readonly Regex SevereLead = new(
        @"\b(severe|shocker|the worst|worst of it|standstill|gridlock|chock|heavy delay|big delay|closed|closure|shut\b|blocked|best part of (an? )?(hour|half[- ]?hour)|(quarter|half)[- ](of an )?hour|over an hour|\d+\s*-?\s*minute)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Direction = new(
        @"\b(citybound|city-bound|inbound|outbound|eastbound|westbound|northbound|southbound|both ways|each way|in both directions)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Anchor = new(
        @"\bat [A-Z]|\bbetween .+? and |\bnear (the )?[A-Z]|\bapproaching |\bjust (before|after) |\b(on|off)[- ]ramp\b|\bramp to ",
        RegexOptions.Compiled);

    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex RoadCode = new(@"\b[ABM]\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex Decimal = new(@"\d+\.\d+", RegexOptions.Compiled);
    // Weather/fuel numerics the prompt exempts from the ≤2 TRAFFIC-figure budget: $-prices,
    // temperatures ("18 degrees"), and rain-chance percentages.
    private static readonly Regex ExemptNumeric =
        new(@"\$\d+(?:\.\d+)?|\b\d+\s*(?:degrees?|°|%)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Lowercase-only: an imperial UNIT reads lowercase ("a mile back", "200 yards"); a capitalised
    // "Mile"/"Yard" is part of a proper road name ("Ten Mile Track", "Yarra…"), not a measurement.
    private static readonly Regex Imperial =
        new(@"\b(mile|miles|yard|yards|feet|foot)\b", RegexOptions.Compiled);

    private static readonly Regex Dangling =
        new(@"(,|\b(and|but|with|so|as|as well|plus|while|where|which)|[-–—])\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Roadworks =
        new(@"\b(roadwork|road work|roadworks|construction|resurfac|contraflow)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Congestion = new(
        @"\b(crawl|chock|bumper|standstill|gridlock|grind|stack|jam|build|backed|heavy|busy)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Words = new(@"[A-Za-z][A-Za-z'-]*", RegexOptions.Compiled);

    public static TargetShapeResult Evaluate(
        string script, IReadOnlyList<TrafficCorridor> corridors, double targetSeconds,
        double wordsPerSecond = DefaultWordsPerSecond)
    {
        string s = script ?? string.Empty;
        var sentences = SplitSentences(s);
        string lead = sentences.Count > 0 ? sentences[0] : s;
        var dims = new List<Dimension>();

        // 1. Headline lead — N/A unless a corridor carries a drive time or delay. A strong lead is a
        //    drive-time comparison OR a voiced severe/heavy delay magnitude (leading with the worst).
        bool driveAvailable = corridors.Any(c => c.DriveTimeMinutes.HasValue || c.DelayMinutes.HasValue);
        // A strong lead = a drive time or a voiced delay magnitude anywhere in the lead SENTENCE.
        bool strongLead = DriveTime.IsMatch(lead) || SevereLead.IsMatch(lead);
        dims.Add(driveAvailable
            ? Dim("drive-time-lead", 0.20, strongLead ? 1 : 0,
                strongLead ? "lead carries drive time / magnitude / closure" : "weak lead — no drive time, magnitude, or closure")
            : Dim("drive-time-lead", 0.20, null, "n/a — no drive-time data"));

        // 2. Direction coverage — direction tokens vs corridors that actually CARRY a direction.
        //    Congestion/flow corridors are direction-less by nature (flow direction only via shape
        //    polyline, usually absent), so counting them would penalise a script for not voicing a
        //    direction the data never had. N/A when no corridor carries a direction.
        int directionalItems = corridors.Count(c => c.Direction.HasValue);
        int dirHits = Direction.Matches(s).Count;
        dims.Add(directionalItems > 0
            ? Dim("direction", 0.15, Math.Min(1.0, dirHits / (double)directionalItems),
                $"{dirHits} dir / {directionalItems} directional items")
            : Dim("direction", 0.15, null, "n/a — no corridor carries a direction"));

        // 3. Anchor coverage — anchor tokens vs corridors that CAN be anchored. A congestion/flow
        //    corridor is road-name-only (no incident cross-street), so it is not anchorable;
        //    counting it would penalise the script for the data's shape. N/A when nothing is.
        int anchorableItems = corridors.Count(c => c.Incidents.Count > 0);
        int anchorHits = Anchor.Matches(s).Count;
        dims.Add(anchorableItems > 0
            ? Dim("anchors", 0.15, Math.Min(1.0, anchorHits / (double)anchorableItems),
                $"{anchorHits} anchors / {anchorableItems} anchorable items")
            : Dim("anchors", 0.15, null, "n/a — no anchorable corridor"));

        // 4. Numeric discipline — digit-runs are the proxy for "hard numerics" (metric, no imperial),
        //    but road codes (M4, A38) are identifiers, not measurements, so they don't eat the budget.
        // Strip weather/fuel exempt numerics (prices, temps, rain%) — the budget is for TRAFFIC figures.
        string numericText = ExemptNumeric.Replace(s, " ");
        int roadCodeDigits = RoadCode.Matches(numericText).Sum(m => Digits.Matches(m.Value).Count);
        int hardNumerics = Math.Max(0, Digits.Matches(numericText).Count - roadCodeDigits);
        bool dec = Decimal.IsMatch(numericText);
        bool imp = Imperial.IsMatch(s);
        double numScore = imp ? 0 : hardNumerics <= 3 ? 1.0 : hardNumerics <= 5 ? 0.5 : 0.0;
        if (dec) numScore = Math.Min(numScore, 0.5);
        dims.Add(Dim("numeric-budget", 0.15, numScore,
            $"{hardNumerics} hard numeric(s){(dec ? ", decimals!" : "")}{(imp ? ", imperial!" : "")}"));

        // 5. Clean close — script ends on terminal punctuation and the last clause doesn't dangle.
        string trimmed = s.Trim();
        bool terminated = trimmed.Length > 0 && (trimmed[^1] is '.' or '!' or '?');
        string last = sentences.Count > 0 ? sentences[^1] : trimmed;
        bool clean = terminated && last.Trim().Length > 0 && !Dangling.IsMatch(last.Trim());
        dims.Add(Dim("clean-close", 0.10, clean ? 1 : 0, clean ? "clean" : "trails off / unterminated"));

        // 6. No-roadworks-lead — only judged when a live (non-roadworks) item was available to lead with.
        bool hasLive = corridors.Any(c => !IsRoadworks(c.PrimaryType));
        if (corridors.Count > 0 && hasLive)
        {
            bool leadsRoadworks = Roadworks.IsMatch(lead);
            dims.Add(Dim("no-roadworks-lead", 0.10, leadsRoadworks ? 0 : 1,
                leadsRoadworks ? "lead is roadworks" : "lead is a live item"));
        }
        else
        {
            dims.Add(Dim("no-roadworks-lead", 0.10, null, "n/a — no live alternative"));
        }

        // 7. Length in band — estimated read within ±30% of target.
        int words = Words.Matches(s).Count;
        double rate = wordsPerSecond > 0 ? wordsPerSecond : DefaultWordsPerSecond;
        double targetWords = Math.Max(1, targetSeconds * rate);
        double ratio = words / targetWords;
        double lenScore = ratio is >= 0.7 and <= 1.3 ? 1.0 : Math.Max(0, 1 - Math.Abs(ratio - 1.0));
        dims.Add(Dim("length", 0.05, lenScore, $"{words}w vs ~{targetWords:F0}w @ {rate:F1}w/s"));

        // 8. Congestion voiced — N/A unless a congestion corridor (no incidents) is present.
        bool hasCongestion = corridors.Any(c => c.Incidents.Count == 0);
        dims.Add(hasCongestion
            ? Dim("congestion-voiced", 0.10, Congestion.IsMatch(s) ? 1 : 0,
                Congestion.IsMatch(s) ? "congestion phrased" : "congestion corridor not voiced")
            : Dim("congestion-voiced", 0.10, null, "n/a — no congestion corridor"));

        return new TargetShapeResult(dims);
    }

    private static Dimension Dim(string name, double weight, double? score, string detail) =>
        new(name, weight, score, detail);

    private static bool IsRoadworks(string primaryType) =>
        primaryType.Contains("construction", StringComparison.OrdinalIgnoreCase)
        || primaryType.Contains("roadwork", StringComparison.OrdinalIgnoreCase)
        || primaryType.Contains("resurfac", StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitSentences(string script) =>
        script.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();
}

/// <summary>One scored target-shape dimension. <see cref="Score" /> is null when N/A (excluded from the aggregate).</summary>
internal sealed record Dimension(string Name, double Weight, double? Score, string Detail);

/// <summary>Aggregate target-shape result. <see cref="Percent" /> is the weighted average over scored dimensions × 100.</summary>
internal sealed record TargetShapeResult(IReadOnlyList<Dimension> Dimensions)
{
    public double Percent
    {
        get
        {
            double w = Dimensions.Where(d => d.Score.HasValue).Sum(d => d.Weight);
            if (w <= 0) return 0;
            double weighted = Dimensions.Where(d => d.Score.HasValue).Sum(d => d.Weight * d.Score!.Value);
            return weighted / w * 100;
        }
    }
}
