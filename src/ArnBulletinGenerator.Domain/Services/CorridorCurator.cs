using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Applies an operator's corridor curation (Workshop 2 / SDD §4.2: reorder, include/exclude,
///     omit stale) to a FRESHLY fetched corridor set during regeneration. Matching freshly
///     fetched data instead of reusing stored corridors preserves the ≤30-minute freshness rule
///     while honouring the operator's selection: kept corridors carry current incident data,
///     selections that have since cleared simply drop out, and fresh corridors the operator did
///     not select are excluded.
/// </summary>
public static partial class CorridorCurator
{
    /// <summary>
    ///     Filters and reorders <paramref name="freshCorridors" /> to the operator's selection.
    ///     Selection entries are corridor road names exactly as surfaced on the job
    ///     (case-insensitive match). Output order follows the selection order.
    /// </summary>
    public static CorridorCurationResult Apply(
        IReadOnlyList<TrafficCorridor> freshCorridors,
        IReadOnlyList<string> selectedRoadNames)
    {
        var kept = new List<TrafficCorridor>(selectedRoadNames.Count);
        var missing = new List<string>();
        var matched = new HashSet<int>();

        foreach (string selection in selectedRoadNames)
        {
            int index = FindCorridor(freshCorridors, selection, matched);
            if (index >= 0)
            {
                matched.Add(index);
                kept.Add(freshCorridors[index]);
            }
            else
            {
                missing.Add(selection);
            }
        }

        return new CorridorCurationResult(kept, missing, freshCorridors.Count - matched.Count);
    }

    private static int FindCorridor(
        IReadOnlyList<TrafficCorridor> corridors, string roadName, HashSet<int> alreadyMatched)
    {
        for (int i = 0; i < corridors.Count; i++)
        {
            if (alreadyMatched.Contains(i))
                continue;
            if (string.Equals(corridors[i].RoadName.Trim(), roadName.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

}

/// <param name="Corridors">Kept corridors, in the operator's order.</param>
/// <param name="MissingSelections">Selected roads with no matching fresh corridor (incident cleared).</param>
/// <param name="ExcludedFreshCount">Fresh corridors dropped because the operator did not select them.</param>
public sealed record CorridorCurationResult(
    IReadOnlyList<TrafficCorridor> Corridors,
    IReadOnlyList<string> MissingSelections,
    int ExcludedFreshCount);
