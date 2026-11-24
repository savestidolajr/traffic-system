using System.Text.Json;
using ArnBulletinGenerator.Application.Mapping;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public class RequestParserTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public void DeserializeQueueMessage_ValidJson_ReturnsSuccess()
    {
        string json = """{"Name":"test","Value":42}""";

        var result = RequestParser.DeserializeQueueMessage<SampleDto>(json, _logger, "Test");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("test");
        result.Value.Value.ShouldBe(42);
    }

    [Fact]
    public void DeserializeQueueMessage_InvalidJson_ReturnsFailure()
    {
        string json = "not valid json {{{";

        var result = RequestParser.DeserializeQueueMessage<SampleDto>(json, _logger, "Test");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Invalid JSON");
    }

    [Fact]
    public void DeserializeQueueMessage_NullResult_ReturnsFailure()
    {
        // Deserializing "null" to a class type returns null
        string json = "null";

        var result = RequestParser.DeserializeQueueMessage<SampleDto>(json, _logger, "Test");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("null");
    }

    [Fact]
    public void CaseInsensitiveOptions_IsCaseInsensitive()
    {
        string json = """{"name":"lower","value":1}""";

        var deserialized = JsonSerializer.Deserialize<SampleDto>(json, RequestParser.CaseInsensitiveOptions);

        deserialized.ShouldNotBeNull();
        deserialized!.Name.ShouldBe("lower");
        deserialized.Value.ShouldBe(1);
    }

    private sealed record SampleDto(string Name, int Value);
}
