namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Canonicalizes gazetted road names to the designation broadcasters and listeners actually
///     use. HERE's map names Sydney's motorways by their formal names ("Western Mwy"), but no
///     bulletin has ever said "the Western Motorway" on air — pilot day-2 operator feedback
///     (2026-08-18, KIIS Sydney): "Western Motorway is known to everyone as just the M4".
///     <para>
///         Applied at the road-identity boundary (<see cref="RoadIdentityResolver" /> winners and
///         <c>CorridorAggregator</c> name normalization), so every downstream consumer — corridor
///         grouping, narrative, evidence chips, the good-run exclusion pool — sees one name. It
///         also unifies grouping when HERE mixes forms across records ("Western Mwy" and
///         "M4 Western Motorway" become one corridor, not two).
///     </para>
///     <para>
///         Only unambiguous entries belong here: either the designation is embedded in the name
///         itself, or the gazetted name maps to exactly one designation in every market we air
///         (bare "Pacific Motorway" is deliberately absent — Brisbane's inner section is the M3).
///     </para>
/// </summary>
public static class RoadDesignationAliases
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sydney (NSW) motorway network.
        ["Western Motorway"] = "M4",
        ["Western Mwy"] = "M4",
        ["M4 Western Motorway"] = "M4",
        ["M4 Western Mwy"] = "M4",
        ["M4 Motorway"] = "M4",
        ["South Western Motorway"] = "M5",
        ["South Western Mwy"] = "M5",
        ["M5 South Western Motorway"] = "M5",
        ["M5 South Western Mwy"] = "M5",
        ["M5 East Motorway"] = "M5",
        ["M5 East Mwy"] = "M5",
        ["Hills Motorway"] = "M2",
        ["Hills Mwy"] = "M2",
        ["M2 Hills Motorway"] = "M2",
        ["M2 Hills Mwy"] = "M2",
        ["M2 Motorway"] = "M2",
        ["Westlink M7"] = "M7",
        ["Westlink Mwy"] = "M7",
        ["Westlink Motorway"] = "M7",
        ["M7 Westlink"] = "M7",
        ["M1 Pacific Motorway"] = "M1",
        ["M1 Pacific Mwy"] = "M1",
        ["M8 Motorway"] = "M8",
        ["M8 Mwy"] = "M8"
    };

    /// <summary>
    ///     The broadcaster designation for <paramref name="roadName" />, or the name unchanged
    ///     when no alias exists. Null/whitespace passes through untouched.
    /// </summary>
    public static string Canonicalize(string roadName) =>
        !string.IsNullOrWhiteSpace(roadName) && Map.TryGetValue(roadName.Trim(), out string? designation)
            ? designation
            : roadName;
}
