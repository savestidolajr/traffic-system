namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
/// Query options shared by the versioned library list endpoints and bounded internal readers.
/// The default is deliberately the historical complete response: callers must opt into a current
/// summary or paging explicitly.
/// </summary>
public sealed record VersionedLibraryListQuery(
    bool IncludeHistory = true,
    int? PageSize = null,
    string? Cursor = null)
{
    /// <summary>The unchanged, complete list contract used by existing callers.</summary>
    public static VersionedLibraryListQuery FullHistoryDefault { get; } = new();

    /// <summary>A current-version-only list without a page limit.</summary>
    public static VersionedLibraryListQuery CurrentSummary { get; } = new(false);

    /// <summary>True when the caller explicitly requested a bounded page/cursor.</summary>
    public bool IsPaged => PageSize.HasValue || !string.IsNullOrWhiteSpace(Cursor);
}

/// <summary>One page from a versioned library list. <see cref="NextCursor" /> is null when complete.</summary>
public sealed record VersionedLibraryListPage<T>(
    IReadOnlyList<T> Items,
    int Total,
    string? NextCursor = null);
