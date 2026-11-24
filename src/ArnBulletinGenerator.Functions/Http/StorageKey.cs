using Microsoft.Azure.Functions.Worker.Http;
using static ArnBulletinGenerator.Domain.Models.StorageKey;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>
///     HTTP-boundary wrapper around <see cref="ArnBulletinGenerator.Domain.Models.StorageKey" />:
///     returns a 400 response when a user-supplied identifier is not a legal storage key, or
///     <c>null</c> when it is valid (so the caller may proceed). An unvalidated key reaches
///     storage and throws a <c>RequestFailedException</c> (400) — which surfaces as an
///     unhandled HTTP 500 and leaks the raw storage error — so triggers reject it up front.
/// </summary>
internal static class StorageKey
{
    public static Task<HttpResponseData>? RejectIfInvalid(HttpRequestData req, string label, string? key)
        => IsValid(key)
            ? null
            : HttpResponseHelper.BadRequestAsync(req,
                $"{label} contains characters that are not allowed (no / \\ # ? or control characters, max {MaxLength}).");
}
