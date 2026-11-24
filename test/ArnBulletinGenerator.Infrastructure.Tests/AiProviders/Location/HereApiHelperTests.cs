using System.Net;
using System.Text;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public class HereApiHelperTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public async Task ExecuteGetAsync_SuccessfulResponse_ReturnsDeserializedResult()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse("""{"name":"test","value":42}""", HttpStatusCode.OK);
        using var http = new HttpClient(handler);

        var result = await HereApiHelper.ExecuteGetAsync<TestDto>(http, "https://test.api/data", _logger, "Test");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("test");
        result.Value.Value.ShouldBe(42);
    }

    [Fact]
    public async Task ExecutePostAsync_SuccessfulResponse_ReturnsDeserializedResult()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse("""{"name":"posted","value":99}""", HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var result =
            await HereApiHelper.ExecutePostAsync<TestDto>(http, "https://test.api/data", content, _logger, "Test");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("posted");
        result.Value.Value.ShouldBe(99);
    }

    [Fact]
    public async Task ExecuteGetAsync_HttpError_ReturnsFailureWithStatusCode()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse("server error", HttpStatusCode.InternalServerError);
        using var http = new HttpClient(handler);

        var result = await HereApiHelper.ExecuteGetAsync<TestDto>(http, "https://test.api/data", _logger, "TestOp");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("TestOp failed");
    }

    [Fact]
    public async Task ExecuteGetAsync_NullJsonResponse_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse("null", HttpStatusCode.OK);
        using var http = new HttpClient(handler);

        var result = await HereApiHelper.ExecuteGetAsync<TestDto>(http, "https://test.api/data", _logger, "TestOp");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("null response");
    }

    [Fact]
    public async Task ExecuteGetAsync_InvalidJson_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse("not-valid-json!!!", HttpStatusCode.OK);
        using var http = new HttpClient(handler);

        var result = await HereApiHelper.ExecuteGetAsync<TestDto>(http, "https://test.api/data", _logger, "TestOp");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("exception");
    }

    private record TestDto(string Name, int Value);

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private string _content = "";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;

        public void SetResponse(string content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_content)
            });
        }
    }
}
