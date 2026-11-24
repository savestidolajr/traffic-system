using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class OptionTests
{
    [Fact]
    public void None_HasNoValue()
    {
        var none = Option<string>.None;

        none.HasValue.ShouldBeFalse();
        none.IsNone.ShouldBeTrue();
        none.GetValueOr("fallback").ShouldBe("fallback");
    }

    [Fact]
    public void Some_ExposesValue()
    {
        var some = Option<int>.Some(42);

        (some.HasValue, some.IsNone, some.Value, some.GetValueOr(10))
            .ShouldBe((true, false, 42, 42));
    }

    [Fact]
    public void Value_Throws_WhenNone()
    {
        var o = Option<Guid>.None;

        Should.Throw<InvalidOperationException>(() => _ = o.Value)
            .Message.ShouldBe("Option has no value.");
    }

    [Fact]
    public void IsSome_ReturnsTrue_WhenHasValue()
    {
        Option<int>.Some(1).IsSome.ShouldBeTrue();
        Option<int>.None.IsSome.ShouldBeFalse();
    }

    [Fact]
    public void Some_WithNull_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Option<string>.Some(null!));
    }

    [Fact]
    public void Map_Some_TransformsValue()
    {
        var result = Option<int>.Some(5).Map(x => x * 2);

        result.HasValue.ShouldBeTrue();
        result.Value.ShouldBe(10);
    }

    [Fact]
    public void Map_None_ReturnsNone()
    {
        var result = Option<int>.None.Map(x => x * 2);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Map_ReturningNull_ReturnsNone()
    {
        var result = Option<string>.Some("hello").Map<string?>(_ => null);

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Bind_Some_ReturnsBoundOption()
    {
        var result = Option<int>.Some(10).Bind(x => Option<string>.Some(x.ToString()));

        result.Value.ShouldBe("10");
    }

    [Fact]
    public void Bind_None_ReturnsNone()
    {
        var result = Option<int>.None.Bind(x => Option<string>.Some(x.ToString()));

        result.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void Match_Some_ReturnsSomeBranch()
    {
        int result = Option<int>.Some(7).Match(v => v + 1, () => -1);

        result.ShouldBe(8);
    }

    [Fact]
    public void Match_None_ReturnsNoneBranch()
    {
        int result = Option<int>.None.Match(v => v + 1, () => -1);

        result.ShouldBe(-1);
    }

    [Fact]
    public void Or_Some_ReturnsOriginal()
    {
        var original = Option<int>.Some(1);
        var fallback = Option<int>.Some(2);

        original.Or(fallback).Value.ShouldBe(1);
    }

    [Fact]
    public void Or_None_ReturnsFallback()
    {
        var fallback = Option<int>.Some(99);

        Option<int>.None.Or(fallback).Value.ShouldBe(99);
    }

    [Fact]
    public void Tap_Some_ExecutesAction()
    {
        int captured = 0;
        var option = Option<int>.Some(42);

        var returned = option.Tap(v => captured = v);

        captured.ShouldBe(42);
        returned.Value.ShouldBe(42);
    }

    [Fact]
    public void Tap_None_DoesNotExecuteAction()
    {
        bool executed = false;

        Option<int>.None.Tap(_ => executed = true);

        executed.ShouldBeFalse();
    }

    [Fact]
    public void ImplicitConversion_NonNull_CreatesSome()
    {
        Option<string> option = "hello";

        option.HasValue.ShouldBeTrue();
        option.Value.ShouldBe("hello");
    }

    [Fact]
    public void ImplicitConversion_Null_CreatesNone()
    {
        Option<string> option = null!;

        option.IsNone.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OfWhitespace_BlankOrNull_ReturnsNone(string? input)
    {
        Option.OfWhitespace(input).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void OfWhitespace_NonBlank_ReturnsSome()
    {
        var result = Option.OfWhitespace("abc");

        result.Value.ShouldBe("abc");
    }

    [Fact]
    public void OfNullable_Struct_WithValue_ReturnsSome()
    {
        int? value = 42;
        var result = Option.OfNullable(value);

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void OfNullable_Struct_Null_ReturnsNone()
    {
        int? value = null;

        Option.OfNullable(value).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void OfNullable_Class_WithValue_ReturnsSome()
    {
        string? value = "test";
        var result = Option.OfNullable(value);

        result.Value.ShouldBe("test");
    }

    [Fact]
    public void OfNullable_Class_Null_ReturnsNone()
    {
        string? value = null;

        Option.OfNullable(value).IsNone.ShouldBeTrue();
    }
}
