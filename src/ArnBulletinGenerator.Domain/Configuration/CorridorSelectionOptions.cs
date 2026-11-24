namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     In-code defaults for corridor selection scoring, diversity, and thresholds. Values are
///     passed explicitly by callers; this type is not bound from configuration.
/// </summary>
public sealed record CorridorSelectionOptions
{

    public int MaxCorridors { get; init; } = 5;
    public int MinSeverity { get; init; } = 4;
    public double MinLengthMeters { get; init; } = 200;
    public int MaxFunctionalClass { get; init; } = 5;
    public bool EnableTimeWeighting { get; init; } = true;
    public bool EnableGeographicDiversity { get; init; } = true;
    public bool EnableDeltaBoost { get; init; } = true;
    public double PeakHourMultiplier { get; init; } = 1.5;
    public double DirectionAlignmentBoost { get; init; } = 1.3;
    public double NewIncidentBoost { get; init; } = 1.5;
    public double GeographicProximityThresholdKm { get; init; } = 2.0;
}
