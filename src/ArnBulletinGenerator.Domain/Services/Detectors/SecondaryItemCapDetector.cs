using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Deterministic cross-sentence roll-call gate: a lead plus ONE OR TWO named secondary items,
///     then a sweep. Beyond that the read is a database read-out even when every item is worded
///     differently — which is exactly the hole the existing gates left. <see cref="RollCallDetector" />
///     counts names inside ONE sentence; <see cref="SentenceCloneDetector" /> needs a repeated
///     frame, and the draft prompt's own "Vary the Palette" rule guarantees the frames differ.
///     <para>
///         Observed live 2026-08-22 (dev job 8b895081, Sydney 22s, 02:47 local): six single-road
///         minor-congestion items, each with its own idiom ("is seeing heavy traffic", "has a bit of
///         a queue", "is taking an extra minute or two", "has a slight queue", "is a little
///         slower"), passed every gate and aired as a list. The prompt already caps secondaries at
///         one or two (prompts/agents/arn-draft.md, "Secondary Updates"); the model ignored it, so
///         the cap needs deterministic enforcement like every other shape rule in this suite.
///     </para>
///     <para>
///         The repair merges a run of excess items into ONE sweep sentence, and only ever merges
///         items whose corridors share the same measured <see cref="MagnitudeTier" /> — the shared
///         state it voices is then true of every road it names by construction, so the merge can
///         never overclaim or understate. Items above Moderate are never merged: a Major or Severe
///         corridor is lead material, not sweep material.
///     </para>
/// </summary>
public static class SecondaryItemCapDetector
{
    /// <summary>Named secondary items allowed after the lead, per the draft prompt's own shape rule.</summary>
    private const int SecondaryCap = 2;

    /// <summary>Most roads one merged sweep sentence may name (RollCallDetector's per-sentence cap).</summary>
    private const int MaxSweepNames = 3;

    public readonly record struct SecondaryItemCapResult(
        bool HasExcessItems,
        string Feedback,
        int TrafficItemCount);

    public static SecondaryItemCapResult Detect(string script, BroadcastWithTraffic context)
    {
        var items = SingleRoadItems(script, context);
        if (items.Count <= 1 + SecondaryCap)
            return new SecondaryItemCapResult(false, string.Empty, items.Count);

        string names = string.Join(", ", items.Skip(1 + SecondaryCap).Select(item => item.Road));
        return new SecondaryItemCapResult(
            true,
            $"ROLL-CALL (cross-sentence): the read names {items.Count} roads in {items.Count} separate items. "
            + $"Keep the lead and at most {SecondaryCap} named secondaries, then sweep the rest into one phrase "
            + $"({names} are the excess).",
            items.Count);
    }

    /// <summary>
    ///     Merges the excess single-road items into one same-tier sweep sentence, placed where the
    ///     first merged item sat. Items whose tier differs from the dominant excess tier are left
    ///     alone rather than folded into a claim their own data does not support.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        var items = SingleRoadItems(script, context);
        if (items.Count <= 1 + SecondaryCap)
            return script;

        var excess = items.Skip(1 + SecondaryCap).ToList();
        var mergeable = excess
            .Where(item => item.Tier is MagnitudeTier.Minor or MagnitudeTier.Moderate)
            .GroupBy(item => item.Tier)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault()
            ?.Take(MaxSweepNames)
            .ToList();
        if (mergeable is null || mergeable.Count < 2)
            return script;

        var sentences = ScriptSentenceSplitter.Split(script).ToList();
        var mergedIndexes = mergeable.Select(item => item.Index).ToHashSet();
        string sweep = SweepSentence(mergeable.Select(item => item.Road).ToList(), mergeable[0].Tier);

        var rebuilt = new List<string>(sentences.Count);
        bool placed = false;
        for (int i = 0; i < sentences.Count; i++)
        {
            if (!mergedIndexes.Contains(i))
            {
                rebuilt.Add(sentences[i]);
                continue;
            }
            if (placed)
                continue;
            rebuilt.Add(sweep);
            placed = true;
        }

        return string.Join(" ", rebuilt);
    }

    /// <summary>
    ///     "A, B and C are all running a little slower." — the shared state comes from the measured
    ///     tier every named road shares, never from one road's own wording.
    /// </summary>
    private static string SweepSentence(IReadOnlyList<string> roads, MagnitudeTier tier)
    {
        string joined = roads.Count == 2
            ? $"{roads[0]} and {roads[1]}"
            : $"{string.Join(", ", roads.Take(roads.Count - 1))} and {roads[^1]}";
        string state = tier == MagnitudeTier.Moderate ? "are all busy" : "are all running a little slower";
        return $"{joined} {state}.";
    }

    private readonly record struct Item(int Index, string Road, MagnitudeTier Tier);

    /// <summary>
    ///     The read's traffic items that name exactly ONE corridor. A sentence naming two or three
    ///     roads is already a sweep and never counts toward the list, and weather/fuel lines are
    ///     fixtures rather than items.
    /// </summary>
    private static List<Item> SingleRoadItems(string script, BroadcastWithTraffic context)
    {
        var items = new List<Item>();
        if (string.IsNullOrWhiteSpace(script) || context.Corridors.Count == 0)
            return items;

        var mapped = ScriptProvenanceMapper.MapCorridors(script, context.Corridors);
        for (int i = 0; i < mapped.Count; i++)
        {
            var (sentence, corridors) = mapped[i];
            if (corridors.Count != 1)
                continue;
            if (ContentSourceSignals.IsWeatherLine(sentence) || ContentSourceSignals.IsFuelLine(sentence))
                continue;
            items.Add(new Item(i, corridors[0].RoadName, CorridorMagnitude.Classify(corridors[0])));
        }

        return items;
    }
}
