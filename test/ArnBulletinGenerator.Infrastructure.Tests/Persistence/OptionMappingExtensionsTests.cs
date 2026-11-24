using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class OptionMappingExtensionsTests
{
    // ─── String ToOption ─────────────────────────────────────────

    [Fact]
    public void StringToOption_NonEmpty_ReturnsSome()
    {
        string? value = "hello";
        var result = value.ToOption();
        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void StringToOption_Null_ReturnsNone()
    {
        string? value = null;
        value.ToOption().IsNone.ShouldBeTrue();
    }

    [Fact]
    public void StringToOption_Whitespace_ReturnsNone()
    {
        string? value = "   ";
        value.ToOption().IsNone.ShouldBeTrue();
    }

    [Fact]
    public void StringToOption_Empty_ReturnsNone()
    {
        string? value = "";
        value.ToOption().IsNone.ShouldBeTrue();
    }

    // ─── Double ToOption ─────────────────────────────────────────

    [Fact]
    public void DoubleToOption_HasValue_ReturnsSome()
    {
        double? value = 3.14;
        var result = value.ToOption();
        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe(3.14);
    }

    [Fact]
    public void DoubleToOption_Null_ReturnsNone()
    {
        double? value = null;
        value.ToOption().IsNone.ShouldBeTrue();
    }

    // ─── DateTimeOffset ToUtcOption ──────────────────────────────

    [Fact]
    public void ToUtcOption_HasValue_ReturnsSomeUtc()
    {
        var dto = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.FromHours(10));
        DateTimeOffset? nullable = dto;

        var result = nullable.ToUtcOption();

        result.HasValue.ShouldBeTrue();
        result.Value.Kind.ShouldBe(DateTimeKind.Utc);
        result.Value.ShouldBe(dto.UtcDateTime);
    }

    [Fact]
    public void ToUtcOption_Null_ReturnsNone()
    {
        DateTimeOffset? nullable = null;
        nullable.ToUtcOption().IsNone.ShouldBeTrue();
    }

    // ─── Option<string> ToNullable ───────────────────────────────

    [Fact]
    public void StringOptionToNullable_Some_ReturnsValue()
    {
        var option = Option<string>.Some("test");
        option.ToNullable().ShouldBe("test");
    }

    [Fact]
    public void StringOptionToNullable_None_ReturnsNull()
    {
        var option = Option<string>.None;
        option.ToNullable().ShouldBeNull();
    }

    // ─── Option<double> ToNullable ───────────────────────────────

    [Fact]
    public void DoubleOptionToNullable_Some_ReturnsValue()
    {
        var option = Option<double>.Some(2.71);
        option.ToNullable().ShouldBe(2.71);
    }

    [Fact]
    public void DoubleOptionToNullable_None_ReturnsNull()
    {
        var option = Option<double>.None;
        option.ToNullable().ShouldBeNull();
    }

    // ─── Option<DateTime> ToNullableOffset ───────────────────────

    [Fact]
    public void ToNullableOffset_Some_ReturnsDateTimeOffset()
    {
        var dt = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var option = Option<DateTime>.Some(dt);

        var result = option.ToNullableOffset();

        result.ShouldNotBeNull();
        result!.Value.UtcDateTime.ShouldBe(dt);
        result.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ToNullableOffset_None_ReturnsNull()
    {
        var option = Option<DateTime>.None;
        option.ToNullableOffset().ShouldBeNull();
    }

    // ─── Roundtrip ───────────────────────────────────────────────

    [Fact]
    public void StringRoundtrip_SomeToNullableToOption()
    {
        var original = Option<string>.Some("roundtrip");
        string? nullable = original.ToNullable();
        var back = nullable.ToOption();

        back.HasValue.ShouldBeTrue();
        back.Value.ShouldBe("roundtrip");
    }

    [Fact]
    public void StringRoundtrip_NoneToNullableToOption()
    {
        var original = Option<string>.None;
        string? nullable = original.ToNullable();
        var back = nullable.ToOption();

        back.IsNone.ShouldBeTrue();
    }
}
