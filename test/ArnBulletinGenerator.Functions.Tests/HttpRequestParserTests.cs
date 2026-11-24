using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Contracts;
using Shouldly;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class HttpRequestParserTests
{
    [Fact]
    public async Task ParseAsync_ValidJson_ReturnsSuccess()
    {
        var req = MockHttpRequestData.CreatePost(new SamplePayload("test", 42));

        var result = await HttpRequestParser.ParseAsync<SamplePayload>(req, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("test");
        result.Value.Value.ShouldBe(42);
    }

    [Fact]
    public async Task ParseAsync_EmptyBody_ReturnsFailure()
    {
        var req = MockHttpRequestData.CreatePostEmpty();

        var result = await HttpRequestParser.ParseAsync<SamplePayload>(req, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Http.RequestBodyRequired);
    }

    [Fact]
    public async Task ParseAsync_InvalidJson_ReturnsFailure()
    {
        var req = MockHttpRequestData.CreatePostRaw("not-valid-json");

        var result = await HttpRequestParser.ParseAsync<SamplePayload>(req, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Invalid JSON format");
    }

    [Fact]
    public async Task ParseAsync_WhitespaceBody_ReturnsFailure()
    {
        var req = MockHttpRequestData.CreatePostRaw("   ");

        var result = await HttpRequestParser.ParseAsync<SamplePayload>(req, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Http.RequestBodyRequired);
    }

    [Fact]
    public async Task ParseAsync_IntegerEnumValue_ReturnsFailure()
    {
        // Out-of-range integer enum values must be rejected, not coerced into an undefined
        // enum member (allowIntegerValues: false on the request converter). Regression for the
        // bug where {"Kind":99} was accepted and persisted as a corrupt enum.
        var req = MockHttpRequestData.CreatePostRaw("{\"Kind\":99,\"Scope\":\"Global\",\"Term\":\"x\"}");

        var result = await HttpRequestParser.ParseAsync<UpsertSafetyTermRequest>(req, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Invalid JSON format");
    }

    private sealed record SamplePayload(string Name, int Value);
}
