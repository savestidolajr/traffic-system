using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Formats corridor data into human-readable traffic descriptions for LLM script generation.
/// </summary>
internal static class CorridorFormatter
{
    private const string DefaultSeverityLabel = "Low";

    public static string Format(IReadOnlyList<TrafficCorridor> corridors)
    {
        return corridors.Count == 0
            ? "No significant traffic incidents to report."
            : string.Join("\n\n", corridors.Select(FormatOne));
    }

    private static string FormatOne(TrafficCorridor c)
    {
        string road = c.Direction.Match(d => $"**{c.RoadName} ({d})**", () => $"**{c.RoadName}**");
        string type = TrafficTypeMapper.MapType(c.PrimaryType);
        string severity = c.Severity switch { 1 => "Critical", 2 => "Major", 3 => "Minor", _ => DefaultSeverityLabel };

        var lines = new List<string> { road, $"- Type: {type}", $"- Severity: {severity}" };

        // Longest single HERE observation, never the sum across observations — this text becomes
        // BroadcastWithTraffic.Traffic, i.e. a numeric distance the writer can voice. Cairns
        // 2026-08-13: six overlapping Kennedy Highway records summed to "Affected length: 12.2 km".
        if (c.VoicedExtentMeters > 0)
            lines.Add($"- Affected length: {c.VoicedExtentMeters / 1000.0:F1} km");

        if (c.DriveTimeMinutes.HasValue)
        {
            lines.Add($"- Current drive time: {c.DriveTimeMinutes.Value} minutes");
            if (c.NormalDriveTimeMinutes.HasValue && c.DelayMinutes.HasValue && c.DelayMinutes.Value > 0)
                lines.Add(
                    $"- Typical drive time: {c.NormalDriveTimeMinutes.Value} minutes (delay of {c.DelayMinutes.Value} minutes)");
        }

        if (c.Incidents.Count > 0)
        {
            lines.Add("- Details:");
            lines.AddRange(c.Incidents.Take(3).Select(i => $"  • {i.Description}"));
            if (c.Incidents.Count > 3)
                lines.Add($"  • ... and {c.Incidents.Count - 3} more");
        }

        return string.Join("\n", lines);
    }
}
