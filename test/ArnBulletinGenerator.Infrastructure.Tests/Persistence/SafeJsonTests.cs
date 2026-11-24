using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class SafeJsonTests
{
    private readonly ILogger _logger = Substitute.For<ILogger>();

    // ─── Deserialize (with logger) ───────────────────────────────

    [Fact]
    public void Deserialize_ValidJson_ReturnsObject()
    {
        var result = SafeJson.Deserialize<TestDto>("{\"Name\":\"test\",\"Value\":42}", _logger, "test");
        result.ShouldNotBeNull();
        result!.Name.ShouldBe("test");
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Deserialize_Null_ReturnsNull()
    {
        SafeJson.Deserialize<TestDto>(null, _logger, "test").ShouldBeNull();
    }

    [Fact]
    public void Deserialize_Empty_ReturnsNull()
    {
        SafeJson.Deserialize<TestDto>("", _logger, "test").ShouldBeNull();
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNullAndLogs()
    {
        var result = SafeJson.Deserialize<TestDto>("not json", _logger, "test");

        result.ShouldBeNull();
        _logger.ReceivedWithAnyArgs().LogWarning(default, default!, default!, default!);
    }

    [Fact]
    public void Deserialize_WithOptions_UsesOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = SafeJson.Deserialize<TestDto>("{\"name\":\"low\",\"value\":1}", _logger, "test", options);

        result.ShouldNotBeNull();
        result!.Name.ShouldBe("low");
    }

    // ─── DeserializeOption (with logger) ─────────────────────────

    [Fact]
    public void DeserializeOption_ValidJson_ReturnsSome()
    {
        var result = SafeJson.DeserializeOption<TestDto>("{\"Name\":\"a\",\"Value\":2}", _logger, "test");

        result.HasValue.ShouldBeTrue();
        result.Value.Name.ShouldBe("a");
    }

    [Fact]
    public void DeserializeOption_InvalidJson_ReturnsNone()
    {
        var result = SafeJson.DeserializeOption<TestDto>("bad", _logger, "test");
        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void DeserializeOption_Null_ReturnsNone()
    {
        var result = SafeJson.DeserializeOption<TestDto>(null, _logger, "test");
        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void DeserializeOption_Empty_ReturnsNone()
    {
        var result = SafeJson.DeserializeOption<TestDto>("", _logger, "test");
        result.IsNone.ShouldBeTrue();
    }

    private record TestDto(string Name, int Value);
}
