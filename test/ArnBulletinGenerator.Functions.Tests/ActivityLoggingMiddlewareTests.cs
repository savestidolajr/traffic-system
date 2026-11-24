using System.Net;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Middleware;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class ActivityLoggingMiddlewareTests
{
    private readonly IActivityLogRepository _activityLog = Substitute.For<IActivityLogRepository>();
    private readonly ILogger<ActivityLoggingMiddleware> _logger = Substitute.For<ILogger<ActivityLoggingMiddleware>>();
    private readonly ActivityLoggingMiddleware _sut;

    public ActivityLoggingMiddlewareTests()
    {
        _activityLog.RecordAsync(Arg.Any<ActivityLogEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _sut = new ActivityLoggingMiddleware(_activityLog, TimeProvider.System, _logger);
    }

    [Fact]
    public async Task Invoke_GetRequest_CallsNextAndNeverRecords()
    {
        var request = MockHttpRequestData.CreateGet("http://localhost/api/schedules/adelaide");
        var context = CreateHttpContext(request);
        bool nextCalled = false;

        await _sut.Invoke(context, ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.ShouldBeTrue();
        await _activityLog.DidNotReceive().RecordAsync(Arg.Any<ActivityLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_SuccessfulPut_RecordsEntryAndPreservesRequestBodyForReplay()
    {
        const string body = """{"voiceProfileId":"vp-new"}""";
        var request = MockHttpRequestData.CreatePutRaw(body, "http://localhost/api/schedules/adelaide");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, ctx =>
        {
            ctx.GetInvocationResult().Value = response;
            return Task.CompletedTask;
        });

        await _activityLog.Received(1).RecordAsync(
            Arg.Is<ActivityLogEntry>(e =>
                e.Resource == "schedules" &&
                e.EntityId == "adelaide" &&
                e.Method == "PUT" &&
                e.ActorName == "Jamie Operator" &&
                e.ActorObjectId == "op-123" &&
                e.StatusCode == 200 &&
                e.RequestBodyJson == body),
            Arg.Any<CancellationToken>());

        // Stream-replay proof: downstream body parsing (HttpRequestParser reads once, to end,
        // with no reset) must still see the full original body after the middleware runs.
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        (await reader.ReadToEndAsync()).ShouldBe(body);
    }

    [Fact]
    public async Task Invoke_RejectedPut_NeverRecords()
    {
        var request = MockHttpRequestData.CreatePutRaw(
            """{"voiceProfileId":"vp-new"}""", "http://localhost/api/schedules/adelaide");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.BadRequest };

        await _sut.Invoke(context, ctx =>
        {
            ctx.GetInvocationResult().Value = response;
            return Task.CompletedTask;
        });

        await _activityLog.DidNotReceive().RecordAsync(Arg.Any<ActivityLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ExcludedPreviewPath_NeverRecords()
    {
        var request = MockHttpRequestData.CreatePostEmpty("http://localhost/api/voices/profiles/vp-1/preview");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, ctx =>
        {
            ctx.GetInvocationResult().Value = response;
            return Task.CompletedTask;
        });

        await _activityLog.DidNotReceive().RecordAsync(Arg.Any<ActivityLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_NoPrincipalHeader_NeverRecords()
    {
        var request = MockHttpRequestData.CreatePutRaw(
            """{"voiceProfileId":"vp-new"}""", "http://localhost/api/schedules/adelaide");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, ctx =>
        {
            ctx.GetInvocationResult().Value = response;
            return Task.CompletedTask;
        });

        await _activityLog.DidNotReceive().RecordAsync(Arg.Any<ActivityLogEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_PathHasNoEntityId_FallsBackToResponseScopeId()
    {
        // PUT /api/safety/terms carries Scope+ScopeId (market) in the BODY, not the URL --
        // ActivityPathResolver alone resolves entityId=null for this route.
        var request = MockHttpRequestData.CreatePutRaw(
            """{"Kind":"Banned","Scope":"Market","ScopeId":"melbourne","Term":"blocked"}""",
            "http://localhost/api/safety/terms");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, async ctx =>
        {
            await response.WriteStringAsync(
                """{"Id":"term-1","Kind":"Banned","Scope":"Market","ScopeId":"melbourne","Term":"blocked","Enabled":true,"Note":null,"CreatedBy":"Jamie Operator","CreatedAtUtc":"2026-07-07T00:00:00Z","UpdatedAtUtc":"2026-07-07T00:00:00Z"}""");
            ctx.GetInvocationResult().Value = response;
        });

        await _activityLog.Received(1).RecordAsync(
            Arg.Is<ActivityLogEntry>(e => e.Resource == "safety-terms" && e.EntityId == "melbourne"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_SafetyTermDelete_FallsBackToResponseScopeId()
    {
        // DELETE /api/safety/terms/{id} carries the term's OWN id in the URL, but grouping
        // deletes with the same market's upserts needs the market, not the term id -- the
        // resolver deliberately leaves this route's entityId null so the response fallback
        // (ScopeId first) resolves it, exactly like the PUT case above.
        var request = MockHttpRequestData.CreateDelete("http://localhost/api/safety/terms/term-1?scope=Market&scopeId=melbourne");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, async ctx =>
        {
            await response.WriteStringAsync("""{"Deleted":"term-1","Id":"term-1","ScopeId":"melbourne"}""");
            ctx.GetInvocationResult().Value = response;
        });

        await _activityLog.Received(1).RecordAsync(
            Arg.Is<ActivityLogEntry>(e =>
                e.Resource == "safety-terms" && e.Method == "DELETE" && e.EntityId == "melbourne"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ResponseHasNoScopeId_FallsBackToResponseId()
    {
        // POST /api/voices/profiles (create) has no id in the URL yet -- the new profile's id
        // only exists in the response.
        var request = MockHttpRequestData.CreatePostRaw(
            """{"Name":"New Voice"}""", "http://localhost/api/voices/profiles");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, async ctx =>
        {
            await response.WriteStringAsync("""{"Id":"vp-new","Name":"New Voice"}""");
            ctx.GetInvocationResult().Value = response;
        });

        await _activityLog.Received(1).RecordAsync(
            Arg.Is<ActivityLogEntry>(e => e.Resource == "voices/profiles" && e.EntityId == "vp-new"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_PathResolvedEntityId_IsNeverOverriddenByResponseBody()
    {
        var request = MockHttpRequestData.CreatePutRaw(
            """{"voiceProfileId":"vp-new"}""", "http://localhost/api/schedules/adelaide");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, async ctx =>
        {
            // A response body carrying unrelated Id/ScopeId fields must never override the
            // path-resolved "adelaide" -- the fallback only fires when the path yields nothing.
            await response.WriteStringAsync("""{"Id":"other-id","ScopeId":"other-scope"}""");
            ctx.GetInvocationResult().Value = response;
        });

        await _activityLog.Received(1).RecordAsync(
            Arg.Is<ActivityLogEntry>(e => e.EntityId == "adelaide"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_MalformedResponseBody_FallbackNeverThrows()
    {
        var request = MockHttpRequestData.CreatePutRaw(
            """{"Term":"blocked"}""", "http://localhost/api/safety/terms");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await Should.NotThrowAsync(async () => await _sut.Invoke(context, async ctx =>
        {
            await response.WriteStringAsync("this is not json");
            ctx.GetInvocationResult().Value = response;
        }));

        await _activityLog.Received(1).RecordAsync(
            Arg.Is<ActivityLogEntry>(e => e.EntityId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_ResponseBodyFallback_PreservesResponseBytesForRealClient()
    {
        // The riskiest line in the fallback: reading response.Body to inspect it MUST leave it
        // exactly as the real client would receive it, or every successful admin API response
        // this fallback touches would ship corrupted (empty/truncated) to the caller.
        const string original = """{"Id":"term-1","ScopeId":"melbourne"}""";
        var request = MockHttpRequestData.CreatePutRaw(
            """{"Term":"blocked"}""", "http://localhost/api/safety/terms");
        AddPrincipal(request, "op-123", "Jamie Operator", "Admin");
        var context = CreateHttpContext(request);
        var response = new FakeHttpResponseData(context) { StatusCode = HttpStatusCode.OK };

        await _sut.Invoke(context, async ctx =>
        {
            await response.WriteStringAsync(original);
            ctx.GetInvocationResult().Value = response;
        });

        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, leaveOpen: true);
        (await reader.ReadToEndAsync()).ShouldBe(original);
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static void AddPrincipal(HttpRequestData request, string objectId, string name, string role)
    {
        string json = $$"""
            {"auth_typ":"aad","claims":[
                {"typ":"oid","val":"{{objectId}}"},
                {"typ":"name","val":"{{name}}"},
                {"typ":"roles","val":"{{role}}"}
            ]}
            """;
        request.Headers.Add(ClientPrincipal.HeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    ///     Builds a <see cref="FunctionContext" /> substitute whose <c>GetHttpRequestDataAsync</c>
    ///     resolves to <paramref name="request" /> and whose <c>GetInvocationResult()</c> round-trips
    ///     through a real backing store — the same reflection-obtained <c>IFunctionBindingsFeature</c>
    ///     trick used by <see cref="GlobalExceptionHandlingMiddlewareTests" />.
    /// </summary>
    private static FunctionContext CreateHttpContext(HttpRequestData request)
    {
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        var features = new TestInvocationFeatures();

        var httpFeature = Substitute.For<IHttpRequestDataFeature>();
        httpFeature.GetHttpRequestDataAsync(Arg.Any<FunctionContext>())
            .Returns(new ValueTask<HttpRequestData?>(request));
        features.Set(httpFeature);

        var coreAsm = typeof(InvocationResult).Assembly;
        var bindingsType = coreAsm.GetType(
            "Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature")!;
        object? bindingsSub = Substitute.For(new[] { bindingsType }, Array.Empty<object>());
        features.Register(bindingsType, bindingsSub);

        context.Features.Returns(features);
        return context;
    }
}
