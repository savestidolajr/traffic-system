using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Persistence for the shared playbook library — named, versioned sets of station edits to a
///     bulletin role's instructions. Low volume (admin-managed), so all playbooks live in a single
///     partition (mirrors <see cref="IDeliveryStyleRepository" />).
/// </summary>
public interface IBulletinPlaybookRepository
{
    /// <summary>Canonical complete/summary/paged library-list contract.</summary>
    async Task<Result<VersionedLibraryListPage<BulletinPlaybook>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(cancellationToken);
        if (list.IsFailure)
            return list.PropagateFailure<VersionedLibraryListPage<BulletinPlaybook>>();
        var ordered = list.Value.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        int offset = DecodeOffset(query.Cursor);
        int pageSize = query.PageSize ?? Math.Max(1, ordered.Count);
        var items = offset >= ordered.Count ? [] : ordered.Skip(offset).Take(pageSize).ToList();
        string? next = offset + items.Count < ordered.Count
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes((offset + items.Count).ToString(
                System.Globalization.CultureInfo.InvariantCulture)))
            : null;
        return Result<VersionedLibraryListPage<BulletinPlaybook>>.Success(
            new VersionedLibraryListPage<BulletinPlaybook>(items, ordered.Count, next));
    }

    private static int DecodeOffset(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return int.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int offset) && offset >= 0
            ? offset
            : throw new FormatException("Cursor offset is invalid.");
    }

    /// <summary>Legacy complete-list shape retained for existing callers during rollout.</summary>
    Task<Result<IReadOnlyList<BulletinPlaybook>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Authoritative keyed reads used by bounded configuration/reference work.</summary>
    async Task<Result<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, Option<BulletinPlaybook>>(StringComparer.Ordinal);
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            var result = await GetAsync(id, cancellationToken);
            if (result.IsFailure)
                return result.PropagateFailure<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>();
            values[id] = result.Value;
        }
        return Result<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>.Success(values);
    }

    Task<Result<Option<BulletinPlaybook>>> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(BulletinPlaybook playbook, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
