namespace ArnBulletinGenerator.Domain.Models;

/// <summary>A free-flowing road captured on a Job from HERE's ranked pool, recording whether it was weighted for the station and whether it aired as a good-run mention.</summary>
public sealed record JobFlowRoad(
    string RoadName,
    double JamFactor,
    double LengthMeters,
    bool IsWeighted,
    bool IsSelected,
    double Score = 0,
    bool PriorityBoosted = false,
    double? BaseScore = null,
    double? BoostAmount = null,
    int FunctionalClass = 3);
