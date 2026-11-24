using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Shared JSON helpers for deserializing queue messages and HTTP request bodies.
///     Single source of truth for <see cref="CaseInsensitiveOptions" />.
/// </summary>
public static class RequestParser
{
    /// <summary>
    ///     Shared <see cref="JsonSerializerOptions" /> with case-insensitive property matching.
    ///     Use this everywhere instead of creating per-class copies.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    ///     Deserializes a JSON string to <typeparamref name="T" />, returning a <see cref="Result{T}" /> on failure.
    /// </summary>
    public static Result<T> DeserializeQueueMessage<T>(string json, ILogger logger, string callerName) where T : class
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, CaseInsensitiveOptions);
            if (result is not null) return result;

            logger.LogError("[{Caller}] Failed to deserialize queue message: null result", callerName);
            return Result<T>.Failure("Deserialization returned null");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "[{Caller}] Failed to deserialize queue message (length={Length})", callerName,
                json.Length);
            return Result<T>.Failure($"Invalid JSON: {ex.Message}");
        }
    }
}
