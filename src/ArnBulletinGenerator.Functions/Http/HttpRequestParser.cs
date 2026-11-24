using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>
///     Deserializes a JSON request body into a typed DTO, turning empty bodies, malformed
///     JSON, and stream errors into typed Result failures rather than a 500.
/// </summary>
public static class HttpRequestParser
{
    public static async Task<Result<T>> ParseAsync<T>(HttpRequestData request, CancellationToken ct) where T : class
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            string body = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(body))
                return Result<T>.Failure(ErrorMessages.Http.RequestBodyRequired);

            var result = JsonSerializer.Deserialize<T>(body, JsonSerializationDefaults.Request);
            return result is not null
                ? Result<T>.Success(result)
                : Result<T>.Failure(ErrorMessages.Http.RequestBodyRequired);
        }
        catch (JsonException)
        {
            return Result<T>.Failure("Invalid JSON format");
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — surface as a clean 4xx-style failure rather than 500.
            return Result<T>.Failure("Request cancelled while reading body");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Body stream errors that previously bubbled up as 500. Treat as a bad request:
            // the client either disconnected or sent an unreadable body.
            return Result<T>.Failure($"Failed to read request body: {ex.GetType().Name}");
        }
    }
}
