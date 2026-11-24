using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>Wire DTO for creating or editing a standing content-guidance directive.</summary>
public sealed record UpsertGuidanceRequest(
    GuidanceScope Scope,
    string? ScopeId,
    GuidanceType Type,
    string Text,
    bool Enabled = true,
    DateTime? ReviewAfterUtc = null,
    string? Id = null);

/// <summary>Wire DTO returned for a directive.</summary>
public sealed record GuidanceDto(
    string Id,
    string Scope,
    string ScopeId,
    string Type,
    string Text,
    bool Enabled,
    string CreatedBy,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? Source,
    DateTime? ReviewAfterUtc)
{
    public static GuidanceDto From(GuidanceEntry e) => new(
        e.Id, e.Scope.ToString(), e.ScopeId, e.Type.ToString(), e.Text, e.Enabled,
        e.CreatedBy, e.CreatedAtUtc, e.UpdatedAtUtc,
        e.Source.Match(s => (string?)s, () => null), e.ReviewAfterUtc.Match(d => (DateTime?)d, () => null));
}

/// <summary>
///     Wire DTO for promoting an operator note (Layer 3 candidate) into a standing
///     directive (Layer 2). The admin chooses the scope and type at promotion time.
/// </summary>
public sealed record PromoteGuidanceRequest(
    GuidanceScope Scope,
    string? ScopeId,
    GuidanceType Type,
    string Text,
    string? JobId = null);

/// <summary>Wire DTO for a stored future lesson in the candidate governance queue.</summary>
public sealed record GuidanceCandidateDto(
    string Id,
    string Station,
    string JobId,
    string Category,
    string Text,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    int OccurrenceCount,
    DateTime ExpiresAtUtc,
    bool Promoted = false,
    string Status = nameof(FutureLessonStatus.Active),
    string Origin = nameof(FutureLessonOrigin.Operator),
    string? ConflictWithLessonId = null)
{
    public static GuidanceCandidateDto From(FutureLessonEntry entry, bool promoted = false) => new(
        entry.Id,
        entry.Station,
        entry.SourceJobId,
        entry.Category.ToString(),
        entry.Text,
        entry.CreatedAtUtc,
        entry.LastSeenAtUtc,
        entry.OccurrenceCount,
        entry.ExpiresAtUtc,
        promoted,
        entry.Status.ToString(),
        entry.Origin.ToString(),
        entry.ConflictWithLessonId);
}
