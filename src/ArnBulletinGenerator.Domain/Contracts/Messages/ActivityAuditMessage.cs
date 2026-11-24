using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Contracts.Messages;

/// <summary>
///     Durable activity audit payload published after a successful mutating HTTP response.
///     The request identifier is stable for the invocation and is used by the consumer to make
///     retries idempotent.
/// </summary>
public sealed record ActivityAuditMessage(
    DateTime AtUtc,
    string ActorName,
    string ActorObjectId,
    string Method,
    string Path,
    string Resource,
    string? EntityId,
    int StatusCode,
    string? RequestBodyJson,
    string RequestId)
{
    /// <summary>Alias for callers that refer to the event time as a timestamp.</summary>
    [JsonIgnore]
    public DateTime TimestampUtc => AtUtc;
}
