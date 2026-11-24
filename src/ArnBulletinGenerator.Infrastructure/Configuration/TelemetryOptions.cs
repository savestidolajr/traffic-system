namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Controls the temporary custom-event compatibility stream for the operations workbook.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    ///     Keeps legacy custom-event rows for workbook panels that have not yet migrated to
    ///     aggregate custom metrics. Defaults to true so existing dashboards remain populated.
    /// </summary>
    public bool LegacyEventsEnabled { get; set; } = true;
}
