using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Ensures diverse functional-class representation in incident selection.
///     Guarantees minimum slots per road tier so arterial/local incidents
///     aren't crowded out by motorway incidents.
/// </summary>
public static partial class IncidentDiversityFilter
{
    private const int MinPerTier = 3;

    /// <summary>
    ///     Selects up to <paramref name="maxCount" /> incidents ensuring each functional-class
    ///     tier (motorway FC 1-2, arterial FC 3, local FC 4-5) gets at least
    ///     <see cref="MinPerTier" /> slots if enough incidents exist.
    ///     Remaining slots are filled by overall severity ranking.
    /// </summary>
    public static List<TrafficIncident> EnsureDiversity(
        IEnumerable<TrafficIncident> incidents, int maxCount)
    {
        var sorted = incidents
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.FunctionalClass)
            .ThenByDescending(i => i.LengthMeters)
            .ToList();

        if (sorted.Count <= maxCount)
            return sorted;

        var motorway = sorted.Where(i => i.FunctionalClass <= 2).ToList();
        var arterial = sorted.Where(i => i.FunctionalClass == 3).ToList();
        var local = sorted.Where(i => i.FunctionalClass >= 4).ToList();

        var selected = new HashSet<string>();
        var result = new List<TrafficIncident>(maxCount);

        // Guarantee minimum per tier
        AddFromTier(motorway, MinPerTier, selected, result);
        AddFromTier(arterial, MinPerTier, selected, result);
        AddFromTier(local, MinPerTier, selected, result);

        // Fill remaining slots from overall sorted list
        foreach (var incident in sorted)
        {
            if (result.Count >= maxCount) break;
            if (selected.Add(incident.Id))
                result.Add(incident);
        }

        return result;
    }

    private static void AddFromTier(
        List<TrafficIncident> tier, int count,
        HashSet<string> selected, List<TrafficIncident> result)
    {
        foreach (var incident in tier.Take(count))
            if (selected.Add(incident.Id))
                result.Add(incident);
    }
}
