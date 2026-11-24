using System.Text.Json;

namespace ArnBulletinGenerator.E2E.Tests;

internal sealed record QueuedResponse(string? JobId, string? Status);

internal static class JsonElementExtensions
{
    public static string? StringProp(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return null;
    }

    public static JsonElement? Prop(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value))
                return value;
        return null;
    }

    public static bool ContainsText(this JsonElement element, string text) =>
        element.ToString().Contains(text, StringComparison.OrdinalIgnoreCase);
}
