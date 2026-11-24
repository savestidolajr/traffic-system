using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts.Messages;

/// <summary>
/// Durable command for the single delivery owner. The reviewed ETag and deterministic idempotency
/// key are persisted with the pending claim so a queue retry cannot deliver a different revision.
/// </summary>
public sealed record DeliveryQueueCommand(
    string JobId,
    string ReviewedEtag,
    string IdempotencyKey,
    string RequestedBy = "unknown",
    string? RequestedByObjectId = null,
    DeliveryKind Kind = DeliveryKind.Publish,
    string? FallbackSourceJobId = null,
    string? FallbackAudioUrl = null,
    string? FallbackReason = null,
    string? FallbackNotes = null,
    double? FallbackDurationSeconds = null);

/// <summary>Whether a pending command delivers the approved take or first applies a fallback.</summary>
public enum DeliveryKind
{
    Publish,
    Fallback
}
