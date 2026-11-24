using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Per-market incident class priority rule configured by operators.
///     ScoreBonus ranges from -100 (heavily deprioritised / suppressed) to +100 (critical priority boost).
/// </summary>
/// <param name="IncidentClass">Canonical incident class (Crash, Closure, Hazard, Roadworks, Breakdown, Flooding, Congestion, SpecialEvent, Fire, Ferry).</param>
/// <param name="ScoreBonus">Additive scoring bonus (-100 to +100) applied during corridor selection.</param>
/// <param name="Enabled">Whether this class is active for the station/market.</param>
public sealed record IncidentPriorityRule(
    string IncidentClass,
    int ScoreBonus = 0,
    bool Enabled = true);

/// <summary>
///     An observed incident signature dynamically accumulated per station/market during bulletin generation.
/// </summary>
/// <param name="Provider">Source feed (e.g. HERE, Live Traffic NSW, QLDTraffic).</param>
/// <param name="CanonicalClass">Resolved canonical domain incident class.</param>
/// <param name="EventType">Primary event type / category name from provider.</param>
/// <param name="Details">Observed detail wording or location context.</param>
/// <param name="Impact">Reported impact / lane condition.</param>
/// <param name="Codes">ALERT-C codes when provided (e.g. "ALERT-C 735").</param>
/// <param name="Count">Total times this signature has been observed in this market.</param>
/// <param name="FirstSeenUtc">Timestamp when this signature was first encountered.</param>
/// <param name="LastSeenUtc">Timestamp when this signature was most recently seen.</param>
public sealed record IncidentLandscapeEntry(
    string Provider,
    string CanonicalClass,
    string EventType,
    string Details,
    string Impact,
    string? Codes,
    int Count,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

/// <summary>
///     One bounded page of landscape entries. <see cref="ContinuationToken" /> is opaque to
///     callers and is null when the requested view is exhausted.
/// </summary>
public sealed record IncidentLandscapePage(
    IReadOnlyList<IncidentLandscapeEntry> Entries,
    int TotalCount,
    string? ContinuationToken = null);
