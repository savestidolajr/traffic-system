using System.Collections.ObjectModel;
using ArnBulletinGenerator.Workflow.Model;

namespace ArnBulletinGenerator.Workflow.Stages;

/// <summary>
///     Estimates the words consumed by semantic content before a composer has written prose.
///     The values are a STARTING calibration to be fitted against measured reads: the deployed
///     system predicts duration from finished prose with a history-calibrated predictor, while
///     pre-prose estimation has strictly less information. This table is therefore the single
///     biggest accuracy risk in the design and must remain measurable and replaceable.
/// </summary>
public static class WordEstimator
{
    private const int StoryOverheadWords = 5;

    private static readonly IReadOnlyDictionary<string, int> Costs =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["suburb"] = 2,
            ["direction"] = 1,
            ["delayMinutes"] = 4,
            ["location"] = 4,
            ["extentLength"] = 4,
            ["incident"] = 2,
            ["detail"] = 3,
            ["condition"] = 3,
            ["temperature"] = 3,
            ["price"] = 6,
            ["worksWindow"] = 6,
            ["sourceDetail"] = 5,
        });

    /// <summary>
    ///     Field costs are deliberately exposed so calibration tooling can compare this starting
    ///     table with measured reads and replace it without hiding the largest pre-prose risk.
    /// </summary>
    public static IReadOnlyDictionary<string, int> FieldCosts => Costs;

    /// <summary>Counts non-empty whitespace-delimited tokens; punctuation and hyphens stay in one token.</summary>
    public static int EstimateWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        var inWord = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    /// <summary>
    ///     Estimates a fact's spoken words in addition to naming its road. Known fields use a
    ///     calibrated floor, but long values win so locations and source details cannot be priced
    ///     as though every name were short.
    /// </summary>
    public static int EstimateFactWords(EventFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var valueWords = EstimateWords(fact.Value);
        if (!Costs.TryGetValue(fact.Field, out var fieldCost))
        {
            return valueWords + 1;
        }

        return Math.Max(fieldCost, valueWords);
    }

    /// <summary>
    ///     Prices one semantic story: one connective/verb/article allowance, each event's spoken
    ///     name, and every selected fact. Facts not selected by evidence id are intentionally free.
    /// </summary>
    public static int EstimateStoryWords(BulletinEvent[] events, IReadOnlyList<string> evidenceIds)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(evidenceIds);

        var selectedIds = new HashSet<string>(evidenceIds, StringComparer.Ordinal);
        var words = StoryOverheadWords;
        foreach (var bulletinEvent in events)
        {
            ArgumentNullException.ThrowIfNull(bulletinEvent);
            var label = bulletinEvent.Label;
            var comma = label.IndexOf(',');
            if (comma >= 0)
            {
                label = label[..comma];
            }

            words += EstimateWords(label);
            foreach (var fact in bulletinEvent.Facts)
            {
                if (selectedIds.Contains(fact.EvidenceId))
                {
                    words += EstimateFactWords(fact);
                }
            }
        }

        return words;
    }
}
