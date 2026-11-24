using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Maps a source corridor to the role labels the naturalness-polish stage is handed in its
///     FACTS block: the condition <em>type</em> and the congestion <em>severity band</em>. Giving the
///     polisher these (plus the corridor's direction) lets it voice each token in the right
///     grammatical slot and pick band-appropriate phrasing, instead of inferring role from prose.
/// </summary>
public static partial class PolishFacts
{
    /// <summary>Condition bucket from the corridor's primary incident type.</summary>
    public static string ConditionOf(TrafficCorridor corridor)
    {
        string t = (corridor.PrimaryType ?? string.Empty).ToLowerInvariant();
        if (t.Contains("clos")) return "closure";
        if (t.Contains("accident") || t.Contains("crash")) return "crash";
        if (t.Contains("disabled") || t.Contains("breakdown")) return "breakdown";
        if (t.Contains("construction") || t.Contains("roadwork")) return "roadworks";
        return "congestion";
    }

    /// <summary>
    ///     Congestion severity band ("heavy"/"moderate"/"minor") from the corridor's delay, or null
    ///     when no delay figure exists (the polisher then leans on the condition type alone).
    /// </summary>
    public static string? SeverityBandOf(TrafficCorridor corridor)
    {
        if (!corridor.DelayMinutes.HasValue)
            return null;
        int delay = corridor.DelayMinutes.Value;
        return delay >= 8 ? "heavy" : delay >= 3 ? "moderate" : "minor";
    }
}
