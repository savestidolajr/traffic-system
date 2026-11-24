namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Whether a tracked incident is still active or has been marked cleared for change detection.</summary>
public enum IncidentStateStatus
{
    Active,
    Cleared
}

/// <summary>
///     Persistent incident state for change detection across bulletins.
///     Immutable — use <c>with</c> expressions to produce updated copies.
/// </summary>
public sealed record IncidentState(
    string IncidentId,
    string MarketId,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    IncidentStateStatus Status,
    string Type,
    int Criticality,
    double LengthMeters,
    string DescriptionHash,
    string Source,
    DateTime UpdatedAt,
    Option<DateTime> LastReportedAt = default,
    Option<string> RoadName = default,
    Option<string> Suburb = default,
    Option<string> Direction = default);
