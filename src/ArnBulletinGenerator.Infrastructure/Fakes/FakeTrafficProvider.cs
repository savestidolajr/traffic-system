using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for HERE Traffic: returns a fixed set of incidents,
///     corridors, and free-flowing roads instead of calling the live API.
/// </summary>
public sealed class FakeTrafficProvider(FakeExternalCallLog log) : ITrafficProvider
{
    public Task<Result<IEnumerable<TrafficIncident>>> GetIncidentsAsync(
        CoverageArea area,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Result<IEnumerable<TrafficIncident>>.Failure("Fake traffic request was cancelled."));

        log.Add($"traffic incidents lat={area.Latitude:F4} lng={area.Longitude:F4}");
        return Task.FromResult(Result<IEnumerable<TrafficIncident>>.Success(BuildIncidents()));
    }

    public Task<Result<IEnumerable<TrafficCorridor>>> GetCorridorsAsync(
        CoverageArea area,
        int maxCorridors = 5,
        CancellationToken cancellationToken = default,
        IncidentExclusions? exclusions = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Result<IEnumerable<TrafficCorridor>>.Failure("Fake traffic request was cancelled."));

        log.Add($"traffic corridors max={maxCorridors} lat={area.Latitude:F4} lng={area.Longitude:F4}");
        // These fixtures are one record per corridor, so an excluded record takes its corridor with
        // it. A partial keep is deliberately not implemented: every corridor field here is a
        // hand-written constant describing its member, so retaining the corridor without the member
        // would state that member's direction, extent and impact with the record gone — the exact
        // defect IncidentExclusions exists to prevent.
        var corridors = BuildCorridors()
            .Where(c => exclusions is null || !c.Incidents.Any(exclusions.Excludes));
        return Task.FromResult(Result<IEnumerable<TrafficCorridor>>.Success(corridors.Take(maxCorridors)));
    }

    public Task<Result<IReadOnlyList<FlowRoad>>> GetFreeFlowingRoadsAsync(
        CoverageArea area,
        int max = 5,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? excludedRoadNames = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(Result<IReadOnlyList<FlowRoad>>.Failure("Fake traffic request was cancelled."));

        IReadOnlyList<FlowRoad> roads =
        [
            new("Pacific Highway", 1.2, 18_200, true),
            new("M4", 1.6, 15_400, true),
            new("Coronation Drive", 2.0, 8_700),
            new("Captain Cook Drive", 1.4, 7_900),
            new("Anzac Parade", 1.8, 6_200)
        ];
        return Task.FromResult(Result<IReadOnlyList<FlowRoad>>.Success(
            roads
                .Where(road => excludedRoadNames?.Any(
                    excluded => CorridorAggregator.RoadNamesOverlap(excluded, road.RoadName)) != true)
                .Take(max)
                .ToList()));
    }

    internal static IReadOnlyList<TrafficIncident> BuildIncidents()
    {
        var now = DateTimeOffset.UtcNow;
        return new TrafficIncident[]
        {
            new("fake-incident-m1-crash", "Crash near Smith Street Motorway causing heavy delays", 1,
                "M1 Pacific Motorway near Smith Street Motorway, Southport", "accident", 2_100, 1,
                -27.967, 153.383, true, now, "southbound",
                "Crash near Smith Street Motorway"),
            new("fake-incident-smith-roadworks", "Roadworks on Smith Street Motorway approaching Olsen Avenue", 3,
                "Smith Street Motorway near Olsen Avenue, Parkwood", "construction", 1_300, 2,
                -27.951, 153.364, false, now, "westbound",
                "Roadworks approaching Olsen Avenue"),
            new("fake-incident-gold-coast-lane", "Lane restriction on Gold Coast Highway through Broadbeach", 3,
                "Gold Coast Highway at Broadbeach", "laneRestriction", 900, 3,
                -28.029, 153.431, false, now, "northbound",
                "Lane restriction through Broadbeach"),
            new("fake-incident-m3-volume", "Heavy traffic on the M3 between Eight Mile Plains and Brisbane", 4,
                "M3 near Eight Mile Plains", "congestion", 3_200, 1,
                -27.576, 153.093, false, now, "citybound",
                "Heavy but moving traffic"),
            new("fake-incident-gateway-disabled", "Disabled vehicle on Gateway Motorway near Nudgee", 2,
                "Gateway Motorway near Nudgee", "disabledVehicle", 1_100, 1,
                -27.373, 153.085, true, now, "northbound",
                "Disabled vehicle near Nudgee")
        }
        .Select(incident => incident with
        {
            SourceRecordIdentities =
            [
                new SourceRecordIdentity(EvidenceSources.Here, incident.Id, incident.OriginalId)
            ]
        })
        .ToList();
    }

    internal static IReadOnlyList<TrafficCorridor> BuildCorridors()
    {
        var incidents = BuildIncidents();
        return
        [
            new("M1 Pacific Motorway", 1, 1, 2_100, 98.0, "accident",
                "Major congestion after a crash near Smith Street Motorway", [incidents[0]], "Southport", "southbound",
                "near Smith Street Motorway", 24, 14, -27.967, 153.383,
                (-27.944, 153.375), (-28.012, 153.410), 1, 10),
            new("Smith Street Motorway", 3, 2, 1_300, 66.0, "construction",
                "Moderate delay from roadworks approaching Olsen Avenue", [incidents[1]], "Parkwood", "westbound",
                "Olsen Avenue to Kumbari Avenue", 14, 9, -27.951, 153.364,
                (-27.948, 153.384), (-27.956, 153.336), 0, 5),
            new("Gold Coast Highway", 3, 3, 900, 54.0, "laneRestriction",
                "Lane restriction through Broadbeach", [incidents[2]], "Broadbeach", "northbound",
                "through Broadbeach", 12, 8, -28.029, 153.431,
                (-28.046, 153.434), (-28.006, 153.428), -1, 4),
            new("M3", 4, 1, 3_200, 44.0, "congestion",
                "Mixed flow with a short citybound delay", [incidents[3]], "Eight Mile Plains", "citybound",
                "Eight Mile Plains to Brisbane", 22, 16, -27.576, 153.093,
                (-27.603, 153.101), (-27.470, 153.025), 0, 6),
            new("Gateway Motorway", 2, 1, 1_100, 72.0, "disabledVehicle",
                "Delays after a disabled vehicle near Nudgee", [incidents[4]], "Nudgee", "northbound",
                "near Nudgee", 18, 11, -27.373, 153.085,
                (-27.420, 153.084), (-27.344, 153.095), 1, 7)
        ];
    }
}
