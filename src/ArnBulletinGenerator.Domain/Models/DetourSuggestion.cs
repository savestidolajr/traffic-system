namespace ArnBulletinGenerator.Domain.Models;

/// <summary>An alternate route around a blocked or congested segment, with how much time it saves versus staying on the affected road.</summary>
public sealed record DetourSuggestion(
    int DurationMinutes,
    int SavedMinutes,
    double DistanceKm,
    string? Via = null);
