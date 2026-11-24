using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigTransfer;

namespace ArnBulletinGenerator.Application.ConfigImport;

/// <summary>Small stateless helpers shared by every config-import section plan builder.</summary>
internal static class ConfigImportHelpers
{
    public static PlannedConfigItem ErrorItem(string section, string key, string? name, string detail) =>
        new(new ConfigImportItemDto(section, key, name ?? key, ConfigImportActions.Error, detail), null);

    public static PlannedConfigItem UnchangedItem(string section, string key, string name) =>
        new(new ConfigImportItemDto(section, key, name, ConfigImportActions.Unchanged), null);

    /// <summary>Id match first (exact); otherwise unique-name match so id-less bundles stay idempotent.</summary>
    public static T? FindMatch<T>(
        IReadOnlyList<T> existing, string? id, string? name,
        Func<T, string> idOf, Func<T, string> nameOf) where T : class
    {
        if (!string.IsNullOrWhiteSpace(id))
            return existing.FirstOrDefault(e => string.Equals(idOf(e), id.Trim(), StringComparison.OrdinalIgnoreCase));
        return existing.FirstOrDefault(e =>
            string.Equals(nameOf(e), name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool NamesEqual(string current, string? incoming) =>
        string.Equals(current, incoming?.Trim(), StringComparison.Ordinal);

    public static bool DescriptionsEqual(string? current, string? incoming)
    {
        static string? Normalize(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        return Normalize(current) == Normalize(incoming);
    }

    public static bool SetEquals(IEnumerable<string> a, IEnumerable<string> b)
    {
        var setA = new HashSet<string>(a, StringComparer.Ordinal);
        var setB = new HashSet<string>(b, StringComparer.Ordinal);
        return setA.SetEquals(setB);
    }

    public static bool JsonEquals<T>(T a, T b) =>
        JsonSerializer.Serialize(a) ==
        JsonSerializer.Serialize(b);

    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max] + "…";

    public static async Task<Result> AsResult<T>(Task<Result<T>> task)
    {
        var result = await task;
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
