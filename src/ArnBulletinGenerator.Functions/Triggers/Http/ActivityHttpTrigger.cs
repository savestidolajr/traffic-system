using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Compliance-sensitive read surface over the cross-cutting activity/change audit trail
///     written by <see cref="ActivityLoggingMiddleware" />. Admin-only, enforced by
///     <see cref="RouteRoleMatrix" />.
/// </summary>
public sealed class ActivityHttpTrigger(IActivityLogRepository activityLog, TimeProvider timeProvider)
{
    private const int DefaultLimit = 200;
    private const int MinLimit = 1;
    private const int MaxLimit = 1000;
    private static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(180);
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    [Function("ListActivity")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "activity")]
        HttpRequestData req, FunctionContext ctx)
    {
        string? cursorRaw = MarketScopeRequest.Query(req, "cursor")
            ?? MarketScopeRequest.Query(req, "to");
        var cursor = ParseCursor(cursorRaw, timeProvider.GetUtcNow());
        var to = cursor.Timestamp;
        var from = MarketScopeRequest.ParseDate(MarketScopeRequest.Query(req, "from"))
            ?? to - DefaultLookback;

        if (to < from)
            return await HttpResponseHelper.BadRequestAsync(req, "to must not be before from");
        if (to - from > MaxRange)
            return await HttpResponseHelper.BadRequestAsync(req, "date range too wide — max 366 days");

        int limit = int.TryParse(MarketScopeRequest.Query(req, "limit"), out int parsedLimit)
            ? Math.Clamp(parsedLimit, MinLimit, MaxLimit)
            : DefaultLimit;
        var query = new ActivityLogQuery(
            from, to, MarketScopeRequest.Query(req, "resource"), MarketScopeRequest.Query(req, "entityId"), limit,
            cursor.RowKey);
        var result = await activityLog.QueryAsync(query, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var items = result.Value.Select(ActivityLogEntryDto.From).ToList();
        // A full page suggests more lies further back; the client re-queries with
        // to = the oldest item's AtUtc to page backward.
        bool hasMore = items.Count == limit;
        return await HttpResponseHelper.OkJsonAsync(req, new ActivityLogListResponseDto(items, items.Count, hasMore));
    }

    /// <summary>
    ///     Parses the legacy timestamp cursor and the additive composite form
    ///     <c>{ISO timestamp}|{storage row key}</c>. Junk retains the pre-existing lenient fallback.
    /// </summary>
    private static ActivityCursor ParseCursor(string? raw, DateTimeOffset fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new ActivityCursor(fallback, null);

        int separator = raw.IndexOf('|');
        if (separator >= 0)
        {
            string timestampPart = raw[..separator];
            string rowKey = raw[(separator + 1)..];
            if (DateTimeOffset.TryParse(timestampPart, out var compositeTimestamp)
                && !string.IsNullOrWhiteSpace(rowKey))
                return new ActivityCursor(compositeTimestamp, rowKey);
        }

        return MarketScopeRequest.ParseDate(raw) is { } parsed
            ? new ActivityCursor(parsed, null)
            : new ActivityCursor(fallback, null);
    }

    private sealed record ActivityCursor(DateTimeOffset Timestamp, string? RowKey);
}
