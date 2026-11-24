namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Configuration options for the incident state service.
/// </summary>
public sealed record IncidentStateOptions
{
    public const string SectionName = "IncidentState";

    public HashSet<string> GenericLocations { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Unidentified Road",
        "Unknown Location"
    };

    public int LengthChangeToleranceMeters { get; init; } = 50;
    public string DefaultSource { get; init; } = "HERE";
}
