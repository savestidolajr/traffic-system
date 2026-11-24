using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Persists operator verdicts on ledger events (the only authored state the Event Ledger
///     carries — everything else is folded from job records) plus spot-check sign-offs for
///     bulletins that aired without operator approval.
/// </summary>
public interface IEventVerdictRepository
{
    Task<Result<IReadOnlyList<EventVerdict>>> GetVerdictsAsync(
        string market, CancellationToken cancellationToken = default);

    Task<Result> SetVerdictAsync(EventVerdict verdict, CancellationToken cancellationToken = default);

    /// <summary>Removes a verdict, returning the event to unvetted.</summary>
    Task<Result> ClearVerdictAsync(
        string market, string eventKey, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SpotCheckReview>>> GetSpotChecksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a bounded, newest-first spot-check page. The legacy complete-list method above
    ///     remains the default path so existing callers retain their wire shape and history.
    /// </summary>
    async Task<Result<VersionedLibraryListPage<SpotCheckReview>>> GetSpotChecksPageAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var result = await GetSpotChecksAsync(cancellationToken);
        if (result.IsFailure)
            return result.PropagateFailure<VersionedLibraryListPage<SpotCheckReview>>();

        var ordered = result.Value
            .OrderByDescending(s => s.AtUtc)
            .ThenByDescending(s => s.JobId, StringComparer.Ordinal)
            .ToList();
        if (!TryDecodeSpotCheckCursor(query.Cursor, out int offset))
            return Result<VersionedLibraryListPage<SpotCheckReview>>.Failure(
                "Cursor offset is invalid.");
        int pageSize = query.PageSize ?? Math.Max(1, ordered.Count);
        var items = offset >= ordered.Count ? [] : ordered.Skip(offset).Take(pageSize).ToList();
        string? next = offset + items.Count < ordered.Count
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                (offset + items.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            : null;
        return Result<VersionedLibraryListPage<SpotCheckReview>>.Success(
            new VersionedLibraryListPage<SpotCheckReview>(items, ordered.Count, next));
    }

    private static bool TryDecodeSpotCheckCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor))
            return true;
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Records an operator's review of a bulletin that aired without sign-off.</summary>
    Task<Result> MarkSpotCheckAsync(SpotCheckReview review, CancellationToken cancellationToken = default);
}
