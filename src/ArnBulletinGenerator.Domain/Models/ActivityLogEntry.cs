namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One recorded change: an authenticated, successful (2xx), mutating (POST/PUT/DELETE)
///     admin/operator request. Captured generically by <c>ActivityLoggingMiddleware</c> so
///     new admin routes are audited automatically, with no per-trigger instrumentation.
/// </summary>
public sealed record ActivityLogEntry(
    DateTime AtUtc,
    string ActorName,
    string ActorObjectId,
    string Method,
    string Path,
    string Resource,
    string? EntityId,
    int StatusCode,
    string? RequestBodyJson,
    string? RequestId = null);
