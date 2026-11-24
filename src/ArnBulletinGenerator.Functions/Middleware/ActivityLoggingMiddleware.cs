using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Functions.Auth;

namespace ArnBulletinGenerator.Functions.Middleware;

/// <summary>
///     Cross-cutting change/access audit: generically logs every authenticated, successful
///     (2xx), mutating (POST/PUT/DELETE) admin/operator request — actor, method, path, the
///     resolved resource/entity id, status, and the submitted JSON body — with zero changes
///     to any individual HTTP trigger. Runs after <see cref="AuthorizationMiddleware" /> so it
///     only sees already-authenticated requests and never logs a rejected (401/403) call.
///
///     A logging failure never fails the request: recording is best-effort, wrapped so an
///     exception or a <see cref="Result" /> failure only produces a warning log.
/// </summary>
public sealed class ActivityLoggingMiddleware(
    IActivityLogRepository activityLog,
    TimeProvider timeProvider,
    ILogger<ActivityLoggingMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var req = await context.GetHttpRequestDataAsync();
        if (req is null)
        {
            // Non-HTTP trigger (queue/timer). Nothing to audit.
            await next(context);
            return;
        }

        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(req.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            // Zero overhead for the majority of traffic — all read endpoints, polling.
            await next(context);
            return;
        }

        var resolved = ActivityPathResolver.Resolve(req.Url.AbsolutePath);
        if (resolved is null)
        {
            await next(context);
            return;
        }

        var principal = ClientPrincipal.Parse(req);
        if (principal is null)
        {
            // Known, accepted gap: the local/dev Auth__DevFallbackRole bypass carries no
            // X-MS-CLIENT-PRINCIPAL header at all, so activity logging is inert in that mode.
            // Real AAD-authenticated calls (test/prod, and dev once a real session is used)
            // always carry the header. Do not duplicate AuthorizationMiddleware's dev-identity
            // synthesis here — that would duplicate security-sensitive parsing across two files
            // for a dev-only convenience.
            await next(context);
            return;
        }

        // Buffer the request body only if the stream is seekable, so a non-seekable stream is
        // never touched and downstream body parsing (HttpRequestParser.ParseAsync, which reads
        // request.Body exactly once, to end, with no reset) is never at risk of getting an
        // already-drained stream.
        string? bodyJson = null;
        if (req.Body.CanSeek)
        {
            long startPos = req.Body.Position;
            using var reader = new StreamReader(req.Body, leaveOpen: true);
            string raw = await reader.ReadToEndAsync(context.CancellationToken);
            req.Body.Position = startPos;
            bodyJson = string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        await next(context);

        // A failed/rejected request did not change anything — do not log attempts.
        if (context.GetInvocationResult().Value is not HttpResponseData response ||
            (int)response.StatusCode is < 200 or > 299)
            return;

        // Several admin routes carry their natural key in the BODY, not the URL: safety-term
        // upserts (PUT /api/safety/terms, no id/market segment — Scope+ScopeId are body-only)
        // and every POST-create route (a brand-new voice profile/delivery style/pronunciation
        // dictionary/global guidance entry has no id yet when the request lands). The RESPONSE
        // DTO for a successful create/upsert virtually always echoes back ScopeId (market
        // scoping) or Id (the entity's own id), so fall back to those when the path alone
        // resolved no entity id — never overriding a path-resolved id, and never throwing on a
        // response shape that doesn't have either field.
        string? entityId = resolved.Value.EntityId ?? ResolveEntityIdFromResponse(response);

        try
        {
            var entry = new ActivityLogEntry(
                timeProvider.GetUtcNow().UtcDateTime,
                principal.Name, principal.ObjectId,
                req.Method.ToUpperInvariant(), req.Url.AbsolutePath,
                resolved.Value.Resource, entityId,
                (int)response.StatusCode, bodyJson);
            var result = await activityLog.RecordAsync(entry, context.CancellationToken);
            if (result.IsFailure)
                logger.LogWarning("Activity log write failed for {Method} {Path}: {Error}",
                    req.Method, req.Url.AbsolutePath, result.Error);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Activity log write threw for {Method} {Path}", req.Method, req.Url.AbsolutePath);
        }
    }

    // Field names checked in priority order: a market/scope grouping is more useful to browse
    // by than a bare entity id when both are present (e.g. a safety-term upsert response has
    // both ScopeId and Id — ScopeId groups all of a market's terms together).
    private static readonly string[] FallbackEntityIdFields = ["ScopeId", "Id", "MarketId"];

    private static string? ResolveEntityIdFromResponse(HttpResponseData response)
    {
        if (!response.Body.CanSeek)
            return null;

        // WriteStringAsync left Position at the END of the written bytes (proven by
        // MockHttpRequestData.ReadResponseBodyAsync needing its own explicit reset before
        // reading) — capture that real end position to restore, then rewind to 0 to read.
        long endPos = response.Body.Position;
        try
        {
            response.Body.Position = 0;
            using var reader = new StreamReader(response.Body, leaveOpen: true);
            string raw = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (string field in FallbackEntityIdFields)
                if (doc.RootElement.TryGetProperty(field, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();

            return null;
        }
        catch (JsonException)
        {
            // Not a JSON object response (e.g. a raw string or empty body) — no fallback, not an error.
            return null;
        }
        finally
        {
            // MUST reset — the Functions Worker SDK still needs to read this same stream from
            // the start to send the real response bytes over the wire after this middleware
            // returns. Unlike the request-body read above, a wrong position here corrupts every
            // successful mutating admin API response, not just this logging path.
            response.Body.Position = endPos;
        }
    }
}
