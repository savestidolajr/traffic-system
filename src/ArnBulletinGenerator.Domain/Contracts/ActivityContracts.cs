using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>Wire DTO for one recorded change/access-audit entry.</summary>
public sealed record ActivityLogEntryDto(
    string AtUtc,
    string ActorName,
    string ActorObjectId,
    string Method,
    string Path,
    string Resource,
    string? EntityId,
    int StatusCode,
    string? RequestBodyJson)
{
    // AtUtc round-trips as an explicit ISO-8601 string ("o") rather than relying on the
    // serializer's default DateTime handling — see JobMappingExtensions.Iso for the same
    // reasoning: the stored value is Kind-unspecified UTC and must be unambiguous on the wire.
    public static ActivityLogEntryDto From(ActivityLogEntry e) => new(
        e.AtUtc.ToString("o"), e.ActorName, e.ActorObjectId, e.Method, e.Path,
        e.Resource, e.EntityId, e.StatusCode, e.RequestBodyJson);
}

public sealed record ActivityLogListResponseDto(IReadOnlyList<ActivityLogEntryDto> Items, int Total, bool HasMore = false);
