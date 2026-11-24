using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Middleware;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class GlobalExceptionHandlingMiddlewareTests
{
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger =
        Substitute.For<ILogger<GlobalExceptionHandlingMiddleware>>();

    private readonly GlobalExceptionHandlingMiddleware _sut;

    public GlobalExceptionHandlingMiddlewareTests()
    {
        _sut = new GlobalExceptionHandlingMiddleware(_logger);
    }

    // ── Happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task Invoke_NoException_CallsNextDelegate()
    {
        var context = MockHttpRequestData.CreateFunctionContext();
        bool nextCalled = false;
        FunctionExecutionDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await _sut.Invoke(context, next);

        nextCalled.ShouldBeTrue();
    }

    // ── Non-HTTP: unexpected exceptions rethrow for host retry/poison ─

    [Fact]
    public async Task Invoke_NextThrows_UnexpectedException_NonHttpContext_Rethrows()
    {
        var context = MockHttpRequestData.CreateFunctionContext();
        var expected = new InvalidOperationException("boom");
        FunctionExecutionDelegate next = _ => throw expected;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => _sut.Invoke(context, next));
        thrown.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Invoke_NextThrowsHttpRequestException_NonHttpContext_Rethrows()
    {
        var context = MockHttpRequestData.CreateFunctionContext();
        var expected = new HttpRequestException("gateway timeout");
        FunctionExecutionDelegate next = _ => throw expected;

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => _sut.Invoke(context, next));
        thrown.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Invoke_NextThrowsAggregateException_NonHttpContext_Rethrows()
    {
        var context = MockHttpRequestData.CreateFunctionContext();
        var expected = new AggregateException("multiple failures");
        FunctionExecutionDelegate next = _ => throw expected;

        var thrown = await Should.ThrowAsync<AggregateException>(() => _sut.Invoke(context, next));
        thrown.ShouldBeSameAs(expected);
    }

    // ── Cancellation: always rethrow, both contexts ──────────────────

    [Fact]
    public async Task Invoke_NextThrowsOperationCanceledException_NonHttpContext_Rethrows()
    {
        var context = MockHttpRequestData.CreateFunctionContext();
        var expected = new OperationCanceledException("operation canceled");
        FunctionExecutionDelegate next = _ => throw expected;

        OperationCanceledException? caught = null;
        try { await _sut.Invoke(context, next); }
        catch (OperationCanceledException ex) { caught = ex; }

        caught.ShouldNotBeNull();
        caught.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Invoke_NextThrowsTaskCanceledException_NonHttpContext_Rethrows()
    {
        var context = MockHttpRequestData.CreateFunctionContext();
        var expected = new TaskCanceledException("operation canceled");
        FunctionExecutionDelegate next = _ => throw expected;

        TaskCanceledException? caught = null;
        try { await _sut.Invoke(context, next); }
        catch (TaskCanceledException ex) { caught = ex; }

        caught.ShouldNotBeNull();
        caught.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Invoke_NextThrowsOperationCanceledException_HttpContext_Rethrows()
    {
        var (context, _) = CreateHttpContext();
        var expected = new OperationCanceledException("timeout");
        FunctionExecutionDelegate next = _ => throw expected;

        OperationCanceledException? caught = null;
        try { await _sut.Invoke(context, next); }
        catch (OperationCanceledException ex) { caught = ex; }

        caught.ShouldNotBeNull();
        caught.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Invoke_NextThrowsTaskCanceledException_HttpContext_Rethrows()
    {
        var (context, _) = CreateHttpContext();
        var expected = new TaskCanceledException("timeout");
        FunctionExecutionDelegate next = _ => throw expected;

        TaskCanceledException? caught = null;
        try { await _sut.Invoke(context, next); }
        catch (TaskCanceledException ex) { caught = ex; }

        caught.ShouldNotBeNull();
        caught.ShouldBeSameAs(expected);
    }

    // ── HTTP: unexpected exceptions produce 500 response ────────────

    [Fact]
    public async Task Invoke_NextThrows_UnexpectedException_HttpContext_Sets500Response()
    {
        var (context, _) = CreateHttpContext();
        FunctionExecutionDelegate next = _ => throw new InvalidOperationException("something broke");

        await _sut.Invoke(context, next);

        var result = context.GetInvocationResult();
        result.Value.ShouldNotBeNull();
        var response = (HttpResponseData)result.Value;
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("unexpected error");
    }

    [Fact]
    public async Task Invoke_NextThrowsHttpRequestException_HttpContext_Sets500Response()
    {
        var (context, _) = CreateHttpContext();
        FunctionExecutionDelegate next = _ => throw new HttpRequestException("gateway timeout");

        await _sut.Invoke(context, next);

        var result = context.GetInvocationResult();
        result.Value.ShouldNotBeNull();
        var response = (HttpResponseData)result.Value;
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Invoke_NextThrowsAggregateException_HttpContext_Sets500Response()
    {
        var (context, _) = CreateHttpContext();
        FunctionExecutionDelegate next = _ => throw new AggregateException("multiple failures");

        await _sut.Invoke(context, next);

        var result = context.GetInvocationResult();
        result.Value.ShouldNotBeNull();
        var response = (HttpResponseData)result.Value;
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // ── Helper: build an HTTP FunctionContext with invocation result ──

    private static (FunctionContext Context, InvocationResult InvocationResult) CreateHttpContext()
    {
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        var request = new FakeHttpRequestData(context, new Uri("http://localhost/api/test"), "GET", Stream.Null);
        var invocationResult = Substitute.For<InvocationResult>();

        var features = new TestInvocationFeatures();

        var httpFeature = Substitute.For<IHttpRequestDataFeature>();
        httpFeature.GetHttpRequestDataAsync(Arg.Any<FunctionContext>())
            .Returns(new ValueTask<HttpRequestData?>(request));
        features.Set(httpFeature);

        var coreAsm = typeof(InvocationResult).Assembly;
        var bindingsType = coreAsm.GetType(
            "Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature")!;
        var bindingsSub = Substitute.For(new[] { bindingsType }, Array.Empty<object>());
        var invocationResultProp = bindingsType.GetProperty("InvocationResult")!;
        invocationResultProp.SetValue(bindingsSub, invocationResult);
        features.Register(bindingsType, bindingsSub);

        context.Features.Returns(features);
        context.GetInvocationResult().Returns(invocationResult);

        return (context, invocationResult);
    }
}
