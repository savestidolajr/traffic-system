namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Validates user-supplied identifiers that flow into Azure Table Storage
///     PartitionKey / RowKey values (market ids, operator object ids, scope ids,
///     entity ids). Azure Table keys forbid <c>/</c>, <c>\</c>, <c>#</c>, <c>?</c>
///     and control characters, and cap at 1 KB.
/// </summary>
public static class StorageKey
{
    public const int MaxLength = 256;

    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= MaxLength
        && !key.Any(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c));
}
