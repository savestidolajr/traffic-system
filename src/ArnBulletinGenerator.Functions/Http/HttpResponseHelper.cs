using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Contracts;
using Microsoft.Azure.Functions.Worker.Http;

namespace ArnBulletinGenerator.Functions.Http;

/// <summary>
///     Builds consistent JSON HTTP responses for the Functions triggers: standard
///     success/error codes plus Result-to-response failure mapping.
/// </summary>
public static class HttpResponseHelper
{
    public static Task<HttpResponseData> OkJsonAsync<T>(HttpRequestData req, T data)
    {
        return JsonResponseAsync(req, HttpStatusCode.OK, data);
    }

    public static Task<HttpResponseData> AcceptedJsonAsync<T>(HttpRequestData req, T data)
    {
        return JsonResponseAsync(req, HttpStatusCode.Accepted, data);
    }

    /// <summary>
    ///     Maps a failed <see cref="Result" /> using its TYPED disposition when one is present,
    ///     falling back to <see cref="MapFailureAsync" />'s message matching otherwise.
    ///     <para>
    ///     Message matching alone cannot see a typed failure: a Conflict whose text does not happen
    ///     to contain the word "conflict" (e.g. "still assigned to KIIS 101.1") would fall through
    ///     to a 500. Prefer this overload wherever the service returns an OperationalFailure.
    ///     </para>
    /// </summary>
    public static async Task<HttpResponseData> MapTypedFailureAsync(
        HttpRequestData req, string error, Option<OperationalFailure> metadata,
        Func<HttpRequestData, string, Task<HttpResponseData>> onOtherAsync,
        string? extraNotFoundNeedle = null)
    {
        if (metadata.HasValue)
            switch (metadata.Value.Disposition)
            {
                case FailureDisposition.Conflict:
                    return await ConflictAsync(req, error);
                case FailureDisposition.InvalidInput:
                    return await BadRequestAsync(req, error);
            }

        return await MapFailureAsync(req, error, onOtherAsync, extraNotFoundNeedle);
    }

    public static Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string message, string? detail = null)
    {
        return ErrorAsync(req, HttpStatusCode.BadRequest, message, detail);
    }

    public static Task<HttpResponseData> NotFoundAsync(HttpRequestData req, string message)
    {
        return ErrorAsync(req, HttpStatusCode.NotFound, message);
    }

    public static Task<HttpResponseData> ConflictAsync(HttpRequestData req, string message)
    {
        return ErrorAsync(req, HttpStatusCode.Conflict, message);
    }

    public static Task<HttpResponseData> PreconditionRequiredAsync(HttpRequestData req, string message)
    {
        return ErrorAsync(req, (HttpStatusCode)428, message);
    }

    public static Task<HttpResponseData> ForbiddenAsync(HttpRequestData req, string message)
    {
        return ErrorAsync(req, HttpStatusCode.Forbidden, message);
    }

    public static Task<HttpResponseData> InternalErrorAsync(HttpRequestData req, string message, string? detail = null)
    {
        return ErrorAsync(req, HttpStatusCode.InternalServerError, message, detail);
    }

    public static Task<HttpResponseData> BadGatewayAsync(HttpRequestData req, string message, string? detail = null)
    {
        return ErrorAsync(req, HttpStatusCode.BadGateway, message, detail);
    }

    /// <summary>
    ///     Maps a failed <c>Result</c>/<c>Result&lt;T&gt;</c> error string to the response the
    ///     bulk of the config-library triggers agree on: "not found" (case-insensitive; an
    ///     optional <paramref name="extraNotFoundNeedle" /> widens the match, e.g. "does not
    ///     exist") → 404; "conflict" (an ETag write race, always phrased "Conflict: ...") → 409;
    ///     anything else falls through to <paramref name="onOtherAsync" />, which callers use to
    ///     preserve their own current default (Conflict, BadRequest, or InternalError) or a
    ///     bespoke extra branch (e.g. a required/Invalid validation check before defaulting).
    /// </summary>
    public static async Task<HttpResponseData> MapFailureAsync(
        HttpRequestData req, string error,
        Func<HttpRequestData, string, Task<HttpResponseData>> onOtherAsync,
        string? extraNotFoundNeedle = null)
    {
        bool notFound = error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || (extraNotFoundNeedle is not null
                && error.Contains(extraNotFoundNeedle, StringComparison.OrdinalIgnoreCase));
        if (notFound)
            return await NotFoundAsync(req, error);

        if (error.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            return await ConflictAsync(req, error);

        return await onOtherAsync(req, error);
    }

    public static HttpResponseData NoContent(HttpRequestData req)
    {
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static Task<HttpResponseData> ErrorAsync(
        HttpRequestData req, HttpStatusCode status, string message, string? detail = null)
    {
        return JsonResponseAsync(req, status, new ErrorResponse(message, detail, (int)status));
    }

    private static async Task<HttpResponseData> JsonResponseAsync<T>(
        HttpRequestData req, HttpStatusCode status, T data)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(data, JsonSerializationDefaults.Response));
        return response;
    }
}
