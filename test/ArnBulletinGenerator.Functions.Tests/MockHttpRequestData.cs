using System.Collections;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using NSubstitute;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Test helpers for constructing <see cref="HttpRequestData" /> and reading
///     <see cref="HttpResponseData" /> in unit tests without a real Functions host.
/// </summary>
public static class MockHttpRequestData
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializationDefaults.Request;

    /// <summary>
    ///     Creates a GET request with no body.
    /// </summary>
    public static HttpRequestData CreateGet(string url = "http://localhost/api/test")
    {
        var context = CreateFunctionContext();
        var request = new FakeHttpRequestData(context, new Uri(url), "GET", Stream.Null);
        return request;
    }

    /// <summary>
    ///     Creates a POST request with the given JSON body.
    /// </summary>
    public static HttpRequestData CreatePost<T>(T body, string url = "http://localhost/api/test")
    {
        string json = JsonSerializer.Serialize(body, JsonOptions);
        return CreatePostRaw(json, url);
    }

    /// <summary>
    ///     Creates a POST request with a raw string body.
    /// </summary>
    public static HttpRequestData CreatePostRaw(string body, string url = "http://localhost/api/test")
    {
        var context = CreateFunctionContext();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new FakeHttpRequestData(context, new Uri(url), "POST", stream);
    }

    /// <summary>
    ///     Creates a PUT request with the given JSON body.
    /// </summary>
    public static HttpRequestData CreatePut<T>(T body, string url = "http://localhost/api/test")
    {
        string json = JsonSerializer.Serialize(body, JsonOptions);
        return CreatePutRaw(json, url);
    }

    /// <summary>
    ///     Creates a PUT request with a raw string body.
    /// </summary>
    public static HttpRequestData CreatePutRaw(string body, string url = "http://localhost/api/test")
    {
        var context = CreateFunctionContext();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new FakeHttpRequestData(context, new Uri(url), "PUT", stream);
    }

    /// <summary>
    ///     Creates a DELETE request with no body.
    /// </summary>
    public static HttpRequestData CreateDelete(string url = "http://localhost/api/test")
    {
        var context = CreateFunctionContext();
        return new FakeHttpRequestData(context, new Uri(url), "DELETE", Stream.Null);
    }

    /// <summary>
    ///     Creates a DELETE request with a raw string body (the etag precondition travels in the
    ///     body, mirroring the approve/publish DTO pattern).
    /// </summary>
    public static HttpRequestData CreateDeleteRaw(string body, string url = "http://localhost/api/test")
    {
        var context = CreateFunctionContext();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new FakeHttpRequestData(context, new Uri(url), "DELETE", stream);
    }

    /// <summary>
    ///     Creates a POST request with an empty body (no content).
    /// </summary>
    public static HttpRequestData CreatePostEmpty(string url = "http://localhost/api/test")
    {
        var context = CreateFunctionContext();
        return new FakeHttpRequestData(context, new Uri(url), "POST", Stream.Null);
    }

    /// <summary>
    ///     Reads the response body as a string.
    /// </summary>
    public static async Task<string> ReadResponseBodyAsync(HttpResponseData response)
    {
        if (response is FakeHttpResponseData fake)
        {
            fake.Body.Position = 0;
            using var reader = new StreamReader(fake.Body, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        response.Body.Position = 0;
        using var r = new StreamReader(response.Body);
        return await r.ReadToEndAsync();
    }

    /// <summary>
    ///     Reads the status code and body as a tuple — shared helper for trigger tests.
    /// </summary>
    public static async Task<(HttpStatusCode Status, string Body)> ReadResponseAsync(HttpResponseData response)
    {
        return (response.StatusCode, await ReadResponseBodyAsync(response));
    }

    /// <summary>
    ///     Reads the response body as deserialized JSON.
    /// </summary>
    public static async Task<T?> ReadResponseJsonAsync<T>(HttpResponseData response)
    {
        string body = await ReadResponseBodyAsync(response);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    public static FunctionContext CreateFunctionContext()
    {
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}

/// <summary>
///     Concrete implementation of <see cref="HttpRequestData" /> for testing.
/// </summary>
public sealed class FakeHttpRequestData : HttpRequestData
{
    public FakeHttpRequestData(FunctionContext context, Uri url, string method, Stream body)
        : base(context)
    {
        Url = url;
        Method = method;
        Body = body;
    }

    public override Stream Body { get; }

    public override HttpHeadersCollection Headers { get; } = [];
    public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = [];
    public override Uri Url { get; }

    public override IEnumerable<ClaimsIdentity> Identities { get; } = [];
    public override string Method { get; }

    public override HttpResponseData CreateResponse()
    {
        return new FakeHttpResponseData(FunctionContext);
    }
}

/// <summary>
///     Concrete implementation of <see cref="HttpResponseData" /> for testing.
/// </summary>
public sealed class FakeHttpResponseData : HttpResponseData
{
    public FakeHttpResponseData(FunctionContext context) : base(context)
    {
        Body = new MemoryStream();
    }

    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; } = [];
    public override Stream Body { get; set; }
    public override HttpCookies Cookies { get; } = null!;
}

/// <summary>
///     Minimal <see cref="IInvocationFeatures" /> implementation for unit tests that need
///     <c>FunctionContext.GetInvocationResult()</c> to work without a real Functions host —
///     e.g. asserting that a middleware set the invocation result to a specific response.
///     Register an <c>IHttpRequestDataFeature</c> substitute plus the internal
///     <c>IFunctionBindingsFeature</c> type (via <see cref="Register" />, reflection-obtained)
///     before calling a middleware's <c>Invoke</c>.
/// </summary>
public sealed class TestInvocationFeatures : IInvocationFeatures
{
    private readonly Dictionary<Type, object> _features = new();

    public T? Get<T>()
    {
        return _features.TryGetValue(typeof(T), out object? val) ? (T)val : default;
    }

    public void Set<T>(T instance)
    {
        _features[typeof(T)] = instance!;
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        return _features.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Register(Type type, object instance)
    {
        _features[type] = instance;
    }
}
