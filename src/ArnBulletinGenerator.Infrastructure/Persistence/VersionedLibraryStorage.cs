using System.Collections.Concurrent;
using System.Text;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>Shared key, cursor and bounded bulk-read mechanics for versioned libraries.</summary>
internal static class VersionedLibraryStorage
{
    internal const string CurrentRowKey = "Current";
    internal const string CurrentRowKind = "current";
    internal const string VersionRowKind = "version";

    internal static string PartitionKey(string libraryId) => $"Library_{libraryId}";

    internal static string VersionRowKey(int version) => version.ToString("D10");

    internal static VersionedLibraryListPage<T> Page<T>(
        IReadOnlyList<T> sortedItems, VersionedLibraryListQuery query)
    {
        int offset = DecodeOffset(query.Cursor);
        if (!query.PageSize.HasValue)
            return new VersionedLibraryListPage<T>(sortedItems, sortedItems.Count);

        int pageSize = query.PageSize.Value;
        if (offset >= sortedItems.Count)
            return new VersionedLibraryListPage<T>([], sortedItems.Count);

        int count = Math.Min(pageSize, sortedItems.Count - offset);
        string? next = offset + count < sortedItems.Count ? EncodeOffset(offset + count) : null;
        return new VersionedLibraryListPage<T>(sortedItems.Skip(offset).Take(count).ToList(), sortedItems.Count, next);
    }

    internal static Result<VersionedLibraryListQuery> Validate(VersionedLibraryListQuery query)
    {
        if (query.PageSize is <= 0)
            return Result<VersionedLibraryListQuery>.Failure("PageSize must be greater than zero.");

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            try
            {
                _ = DecodeOffset(query.Cursor);
            }
            catch (FormatException)
            {
                return Result<VersionedLibraryListQuery>.Failure("Cursor is invalid.");
            }
        }

        return Result<VersionedLibraryListQuery>.Success(query);
    }

    internal static async Task<Result<IReadOnlyDictionary<string, Option<T>>>> GetManyAsync<T>(
        IReadOnlyCollection<string> ids,
        bool includeHistory,
        int maxConcurrency,
        Func<string, bool, CancellationToken, Task<Result<Option<T>>>> getAsync,
        CancellationToken cancellationToken)
    {
        var uniqueIds = ids.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (uniqueIds.Length == 0)
            return Result<IReadOnlyDictionary<string, Option<T>>>.Success(
                new Dictionary<string, Option<T>>(StringComparer.Ordinal));

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var values = new ConcurrentDictionary<string, Option<T>>(StringComparer.Ordinal);
        try
        {
            var tasks = uniqueIds.Select(async id =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var result = await getAsync(id, includeHistory, cancellationToken);
                    if (result.IsFailure)
                        return result.PropagateFailure<bool>();
                    values[id] = result.Value;
                    return Result<bool>.Success(true);
                }
                finally
                {
                    gate.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            var failure = results.FirstOrDefault(result => result.IsFailure);
            if (failure is not null && failure.IsFailure)
                return Result<IReadOnlyDictionary<string, Option<T>>>.Failure(failure.Error);

            return Result<IReadOnlyDictionary<string, Option<T>>>.Success(
                uniqueIds.ToDictionary(id => id, id => values[id], StringComparer.Ordinal));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static string EncodeOffset(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static int DecodeOffset(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return int.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int offset) && offset >= 0
            ? offset
            : throw new FormatException("Cursor offset is invalid.");
    }
}
