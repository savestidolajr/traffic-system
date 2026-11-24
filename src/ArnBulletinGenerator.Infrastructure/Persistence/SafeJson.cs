using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Safe JSON deserialization helpers that eliminate repeated
///     null-check → try/catch → swallow patterns across repositories.
/// </summary>
internal static class SafeJson
{
    /// <summary>
    ///     Deserializes JSON to <typeparamref name="T" /> or returns null,
    ///     logging a warning on deserialization failure.
    /// </summary>
    internal static T? Deserialize<T>(
        string? json,
        ILogger logger,
        string context,
        JsonSerializerOptions? options = null) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize JSON for {Context}", context);
            return null;
        }
    }

    /// <summary>
    ///     Deserializes JSON into an <see cref="Option{T}" />, logging on failure.
    /// </summary>
    internal static Option<T> DeserializeOption<T>(
        string? json,
        ILogger logger,
        string context,
        JsonSerializerOptions? options = null) where T : class
    {
        var value = Deserialize<T>(json, logger, context, options);
        return value is not null ? Option<T>.Some(value) : Option<T>.None;
    }
}
