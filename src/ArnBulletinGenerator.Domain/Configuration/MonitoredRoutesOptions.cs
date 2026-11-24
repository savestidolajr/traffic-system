using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Per-market commuter routes whose current-vs-typical drive time should be available to the script.
///     Empty market list disables monitored drive-time headlines for that market.
/// </summary>
public sealed class MonitoredRoutesOptions
{
    public const string SectionName = "MonitoredRoutes";

    public bool Enabled { get; set; } = true;
    public Dictionary<string, List<MonitoredRouteDefinition>> Markets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MonitoredRouteDefinition
{
    public string Name { get; set; } = string.Empty;
    public CoveragePoint Origin { get; set; } = new(0, 0);
    public CoveragePoint Destination { get; set; } = new(0, 0);
    public bool Enabled { get; set; } = true;
}
