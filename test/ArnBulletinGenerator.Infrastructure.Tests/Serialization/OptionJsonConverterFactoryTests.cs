using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Serialization;

public class OptionJsonConverterFactoryTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new OptionJsonConverterFactory() }
    };

    // ─── CanConvert ──────────────────────────────────────────────

    [Fact]
    public void CanConvert_OptionType_ReturnsTrue()
    {
        var factory = new OptionJsonConverterFactory();
        factory.CanConvert(typeof(Option<string>)).ShouldBeTrue();
    }

    [Fact]
    public void CanConvert_NonOptionType_ReturnsFalse()
    {
        var factory = new OptionJsonConverterFactory();
        factory.CanConvert(typeof(string)).ShouldBeFalse();
    }

    [Fact]
    public void CanConvert_OptionInt_ReturnsTrue()
    {
        var factory = new OptionJsonConverterFactory();
        factory.CanConvert(typeof(Option<int>)).ShouldBeTrue();
    }

    // ─── Serialize ───────────────────────────────────────────────

    [Fact]
    public void Serialize_SomeString_WritesValue()
    {
        var option = Option<string>.Some("hello");
        string json = JsonSerializer.Serialize(option, _options);

        json.ShouldBe("\"hello\"");
    }

    [Fact]
    public void Serialize_NoneString_WritesNull()
    {
        var option = Option<string>.None;
        string json = JsonSerializer.Serialize(option, _options);

        json.ShouldBe("null");
    }

    [Fact]
    public void Serialize_SomeInt_WritesValue()
    {
        var option = Option<int>.Some(42);
        string json = JsonSerializer.Serialize(option, _options);

        json.ShouldBe("42");
    }

    [Fact]
    public void Serialize_NoneInt_WritesNull()
    {
        var option = Option<int>.None;
        string json = JsonSerializer.Serialize(option, _options);

        json.ShouldBe("null");
    }

    // ─── Deserialize ─────────────────────────────────────────────

    [Fact]
    public void Deserialize_StringValue_ReturnsSome()
    {
        var result = JsonSerializer.Deserialize<Option<string>>("\"world\"", _options);

        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe("world");
    }

    [Fact]
    public void Deserialize_Null_ReturnsNone()
    {
        var result = JsonSerializer.Deserialize<Option<string>>("null", _options);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Deserialize_IntValue_ReturnsSome()
    {
        var result = JsonSerializer.Deserialize<Option<int>>("99", _options);

        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe(99);
    }

    // ─── Roundtrip ───────────────────────────────────────────────

    [Fact]
    public void Roundtrip_SomeString()
    {
        var original = Option<string>.Some("roundtrip");
        string json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Option<string>>(json, _options);

        deserialized.HasValue.ShouldBeTrue();
        deserialized.Value.ShouldBe("roundtrip");
    }

    [Fact]
    public void Roundtrip_NoneString()
    {
        var original = Option<string>.None;
        string json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Option<string>>(json, _options);

        deserialized.IsNone.ShouldBeTrue();
    }

    // ─── Complex object ──────────────────────────────────────────

    [Fact]
    public void Roundtrip_ComplexObject_AllSome()
    {
        var wrapper = new Wrapper(Option<string>.Some("test"), Option<int>.Some(5));
        string json = JsonSerializer.Serialize(wrapper, _options);
        var result = JsonSerializer.Deserialize<Wrapper>(json, _options);

        result.ShouldNotBeNull();
        result!.Name.Value.ShouldBe("test");
        result.Count.Value.ShouldBe(5);
    }

    [Fact]
    public void Roundtrip_ComplexObject_MixedSomeAndNone()
    {
        var wrapper = new Wrapper(Option<string>.None, Option<int>.Some(7));
        string json = JsonSerializer.Serialize(wrapper, _options);
        var result = JsonSerializer.Deserialize<Wrapper>(json, _options);

        result.ShouldNotBeNull();
        result!.Name.IsNone.ShouldBeTrue();
        result.Count.Value.ShouldBe(7);
    }

    [Fact]
    public void Serialize_SomeObject_WritesObjectJson()
    {
        var obj = new { Foo = "bar" };
        var option = Option<object>.Some(obj);
        string json = JsonSerializer.Serialize(option, _options);

        json.ShouldContain("\"Foo\"");
        json.ShouldContain("\"bar\"");
    }

    private record Wrapper(Option<string> Name, Option<int> Count);
}
