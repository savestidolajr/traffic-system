using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>Fetches live drive times for a market's configured monitored routes and ranks them by delay, worst commute first.</summary>
public static partial class MonitoredRouteReporter
{
    public static async Task<IReadOnlyList<MonitoredRouteStatus>> GetReportsAsync(
        IRoutingProvider routingProvider,
        string market,
        MonitoredRoutesOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(market))
            return [];

        if (!TryGetRoutes(options, market, out var routes) || routes.Count == 0)
            return [];

        var validRoutes = routes.Where(r => r.Enabled && IsValid(r)).ToList();

        // Independent drive-time lookups — fetch concurrently, then assemble in a stable order.
        var driveResults = await Task.WhenAll(validRoutes.Select(route =>
            routingProvider.GetDriveTimeAsync(
                (route.Origin.Lat, route.Origin.Lng),
                (route.Destination.Lat, route.Destination.Lng),
                cancellationToken)));

        var reports = new List<MonitoredRouteStatus>(validRoutes.Count);
        for (int i = 0; i < validRoutes.Count; i++)
        {
            if (driveResults[i].IsFailure)
                continue;

            var route = validRoutes[i];
            var drive = driveResults[i].Value;
            reports.Add(new MonitoredRouteStatus(
                route.Name.Trim(), route.Origin, route.Destination,
                drive.DurationMinutes, drive.TypicalDurationMinutes, drive.DistanceKm));
        }

        return reports
            .OrderByDescending(r => r.DelayMinutes)
            .ThenByDescending(r => r.DriveTimeMinutes)
            .ToList();
    }

    private static bool TryGetRoutes(
        MonitoredRoutesOptions options,
        string market,
        out List<MonitoredRouteDefinition> routes)
    {
        if (options.Markets.TryGetValue(market, out routes!))
            return true;

        string normalized = MarketKey.Normalize(market);
        foreach (var kvp in options.Markets)
        {
            if (kvp.Key.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                routes = kvp.Value;
                return true;
            }
        }

        routes = [];
        return false;
    }

    private static bool IsValid(MonitoredRouteDefinition route)
    {
        return !string.IsNullOrWhiteSpace(route.Name) &&
               route.Origin.Lat is >= -90 and <= 90 &&
               route.Destination.Lat is >= -90 and <= 90 &&
               route.Origin.Lng is >= -180 and <= 180 &&
               route.Destination.Lng is >= -180 and <= 180;
    }
}
