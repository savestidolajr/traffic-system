using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Lifts the <c>usage</c> block out of a raw Responses API body. Both transport paths — the
///     Foundry agent endpoint and the raw-model <see cref="OpenAI.Responses.ResponsesClient" /> —
///     already hold the response as a string for <see cref="Domain.Interfaces.ILlmCallCapture" />,
///     so this reads that same string rather than adding a parse of its own upstream.
///     <para>
///         Deliberately total: an error body, a truncated body, or a shape without <c>usage</c>
///         yields <c>null</c>, never an exception. Token accounting is observability — it must
///         never be the reason a bulletin fails.
///     </para>
/// </summary>
internal static class TokenUsageParser
{
    /// <summary>
    ///     Returns the call's token usage, or <c>null</c> when the body carries none (error
    ///     responses, non-JSON payloads).
    /// </summary>
    internal static LlmTokenUsage? TryParse(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
                return null;

            return new LlmTokenUsage(
                ReadInt(usage, "input_tokens"),
                ReadNestedInt(usage, "input_tokens_details", "cached_tokens"),
                ReadInt(usage, "output_tokens"),
                ReadNestedInt(usage, "output_tokens_details", "reasoning_tokens"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadInt(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var el)
        && el.ValueKind == JsonValueKind.Number
        && el.TryGetInt32(out int value)
            ? value
            : 0;

    private static int ReadNestedInt(JsonElement parent, string container, string property) =>
        parent.TryGetProperty(container, out var el) && el.ValueKind == JsonValueKind.Object
            ? ReadInt(el, property)
            : 0;
}
