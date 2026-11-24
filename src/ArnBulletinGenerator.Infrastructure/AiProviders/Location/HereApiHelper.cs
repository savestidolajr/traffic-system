using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Consolidated HERE API HTTP helpers with unified error handling.
/// </summary>
internal static partial class HereApiHelper
{
    [GeneratedRegex(@"apiKey=[^&]+")]
    private static partial Regex ApiKeyPattern();

    private static string SanitizeUrl(string url)
    {
        return ApiKeyPattern().Replace(url, "apiKey=***");
    }

    public static Task<Result<T>> ExecuteGetAsync<T>(
        HttpClient http, string url, ILogger logger, string op, CancellationToken ct = default)
    {
        return ExecuteAsync<T>(() => http.GetAsync(url, ct), url, logger, op, ct);
    }

    public static Task<Result<T>> ExecutePostAsync<T>(
        HttpClient http, string url, HttpContent content, ILogger logger, string op, CancellationToken ct = default)
    {
        return ExecuteAsync<T>(() => http.PostAsync(url, content, ct), url, logger, op, ct);
    }

    private static async Task<Result<T>> ExecuteAsync<T>(
        Func<Task<HttpResponseMessage>> sendRequest,
        string url, ILogger logger, string op, CancellationToken ct)
    {
        try
        {
            using var response = await sendRequest();
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("HERE {Op} failed. Status: {Status}, Url: {Url}, Response: {Content}",
                    op, response.StatusCode, SanitizeUrl(url), ApiKeyPattern().Replace(error, "apiKey=***"));
                return Result<T>.Failure($"{op} failed: {(int)response.StatusCode} {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<T>(ct);
            return result is not null
                ? Result<T>.Success(result)
                : Result<T>.Failure($"{op} returned null response");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            logger.LogError(ex, "HERE {Op} timed out. Url: {Url}", op, SanitizeUrl(url));
            return Result<T>.Failure($"{op} timed out: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HERE {Op} network error. Status: {Status}, Url: {Url}",
                op, ex.StatusCode, SanitizeUrl(url));
            return Result<T>.Failure($"{op} network error: {ex.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HERE {Op} unexpected error. Url: {Url}", op, SanitizeUrl(url));
            return Result<T>.Failure($"{op} exception: {ex.Message}");
        }
    }
}
