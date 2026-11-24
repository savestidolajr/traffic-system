using System.Web;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>
///     Shared request helpers for admin/operator HTTP triggers: market-scope filtering
///     (guidance + priority roads' <c>?markets=a,b,c</c> query, intersected with the caller's
///     allowed markets so a scoped operator silently receives only their markets) and the
///     from/to/limit date-range paging query shared by Activity and Jobs history.
/// </summary>
internal static class MarketScopeRequest
{
    /// <summary>Reads a single query-string value (null when absent).</summary>
    public static string? Query(HttpRequestData req, string key)
        => HttpUtility.ParseQueryString(req.Url.Query).Get(key);

    /// <summary>Parses an ISO-8601 query param into a <see cref="DateTimeOffset" />, null when absent or unparseable.</summary>
    public static DateTimeOffset? ParseDate(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, out var value)
            ? value
            : null;

    /// <summary>Splits the comma-separated <c>markets</c> param into a trimmed, de-duplicated list.</summary>
    public static IReadOnlyList<string> ParseMarkets(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (seen.Add(part))
                list.Add(part);
        return list;
    }

    /// <summary>
    ///     Intersects requested markets with the caller's allowed set. <see cref="Option{T}.None" />
    ///     means unrestricted, including admins and operators without a non-empty assignment;
    ///     <see cref="Option{T}.Some(T)" /> contains the non-empty assigned market set.
    /// </summary>
    public static IReadOnlyList<string> FilterToAllowed(
        IReadOnlyList<string> requested, Option<IReadOnlyList<string>> allowed)
    {
        if (allowed.IsNone)
            return requested;

        var set = new HashSet<string>(allowed.Value.Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
        return requested.Where(m => set.Contains(m.Trim())).ToList();
    }
}
