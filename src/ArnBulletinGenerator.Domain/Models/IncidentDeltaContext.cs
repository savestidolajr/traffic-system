using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>One incident's identity and detail as carried into a new/updated/cleared/unchanged delta bucket for the script generator.</summary>
public sealed record IncidentDeltaItem(
    string IncidentId,
    string Type,
    int Criticality,
    Option<string> RoadName,
    Option<string> Suburb,
    Option<string> Direction,
    double LengthMeters,
    Option<string> Description,
    /// <summary>
    ///     How current HERE's own validity window says this incident is. A
    ///     <see cref="WindowClass.Scheduled" /> item may never carry continuity language: a worksite
    ///     scheduled 2025-02-04 → 2026-12-30 collected "still" every cycle for three weeks.
    /// </summary>
    WindowClass Window = WindowClass.Unknown);

public sealed record IncidentDeltaContext(
    IReadOnlyList<IncidentDeltaItem> NewIncidents,
    IReadOnlyList<IncidentDeltaItem> UpdatedIncidents,
    IReadOnlyList<IncidentDeltaItem> ClearedIncidents,
    IReadOnlyList<IncidentDeltaItem> UnchangedIncidents);
