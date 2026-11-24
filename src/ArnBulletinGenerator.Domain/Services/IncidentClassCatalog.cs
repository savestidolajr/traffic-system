namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The canonical incident classes, and the single place a provider's raw type is mapped onto one.
///     <para>
///         These ids are ARN-facing. They key the per-market boost sliders in the incident-priorities
///         screen, and they key the per-condition voicing blocks a station may rewrite in the
///         playbook studio, so the same ten words name the same ten things in the console, in
///         storage, and in the prompts. Renaming an id orphans both a market's boosts and a
///         station's wording edits, so they are treated as a published contract rather than an
///         internal enum.
///     </para>
///     <para>
///         The mapping lived as a private helper in <c>TrafficEvidenceBuilder</c>, which meant the
///         classification existed only long enough to add a score bonus and was then thrown away —
///         nothing downstream could tell a crash from a ferry closure, so nothing downstream could be
///         configured per class. It is promoted here so the class travels with the evidence.
///     </para>
/// </summary>
public static class IncidentClassCatalog
{
    /// <param name="Id">Stable, ARN-facing key. Also the suffix of the voicing block id.</param>
    /// <param name="Name">Display name, as the incident-priorities screen shows it.</param>
    /// <param name="Description">What the class covers, for the operator and for the prompt.</param>
    public sealed record IncidentClass(string Id, string Name, string Description);

    /// <summary>
    ///     Ordered as the console lists them, worst first. Kept byte-identical to the web's
    ///     <c>CANONICAL_INCIDENT_CLASSES</c>; <c>IncidentClassCatalogTests</c> pins the pairing.
    /// </summary>
    public static IReadOnlyList<IncidentClass> All { get; } =
    [
        new("Crash", "Crashes and Collisions",
            "Single and multi-vehicle collisions, rollovers, and emergency incidents."),
        new("Closure", "Road Closures",
            "Full carriageway and arterial shutdowns with detour routes."),
        new("Fire", "Vehicle and Bushfires",
            "Active vehicle fires and emergency smoke hazard zones."),
        new("Flooding", "Flooding and Water Over Road",
            "Submerged causeways, flash flooding, and weather disruptions."),
        new("Breakdown", "Breakdowns and Stalls",
            "Disabled vehicles, heavy vehicle tow aways, and lane stalls."),
        new("Hazard", "Hazards and Road Damage",
            "Debris, traffic signal faults, bridge/culvert damage, and cautionary notices."),
        new("Congestion", "Heavy Congestion and Queues",
            "Queuing traffic, bottleneck delays, and changed traffic conditions."),
        new("Roadworks", "Roadworks and Construction",
            "Maintenance, resurfacing, upgrades, utility works, and lane reductions."),
        new("SpecialEvent", "Special Events and Operations",
            "Community events, races, and scheduled police operations."),
        new("Ferry", "Ferry Closures",
            "Vehicular river ferry service maintenance and closures.")
    ];

    /// <summary>
    ///     The canonical class for a provider's raw type. Falls back to <c>Hazard</c>, which is the
    ///     catalogue's own catch-all ("cautionary notices") rather than a null the callers would each
    ///     have to handle.
    /// </summary>
    public static string Classify(string? primaryType)
    {
        string norm = primaryType?.ToLowerInvariant() ?? string.Empty;
        if (norm.Contains("accident") || norm.Contains("crash") || norm.Contains("collision")) return "Crash";
        if (norm.Contains("close") || norm.Contains("closure")) return "Closure";
        if (norm.Contains("break") || norm.Contains("stall") || norm.Contains("disabled")) return "Breakdown";
        if (norm.Contains("flood") || norm.Contains("water")) return "Flooding";
        if (norm.Contains("fire")) return "Fire";
        if (norm.Contains("congestion") || norm.Contains("queue")) return "Congestion";
        if (norm.Contains("construct") || norm.Contains("work") || norm.Contains("maintenance")) return "Roadworks";
        if (norm.Contains("event")) return "SpecialEvent";
        if (norm.Contains("ferry")) return "Ferry";
        return "Hazard";
    }

    /// <summary>The voicing block id a station rewrites to change how this class is spoken.</summary>
    public static string VoicingBlockId(string classId) => $"voice.{classId.ToLowerInvariant()}";

    /// <summary>Every voicing block id, for the prompt-completeness invariant.</summary>
    public static IReadOnlyList<string> VoicingBlockIds { get; } =
        [.. All.Select(c => VoicingBlockId(c.Id))];
}
