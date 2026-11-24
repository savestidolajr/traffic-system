using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class ResultBehaviorTests
{

    // ── Instance Tap ─────────────────────────────────────────────

    [Fact]
    public void Tap_OnSuccess_RunsAction()
    {
        int captured = 0;

        var result = Result<int>.Success(42).Tap(v => captured = v);

        captured.ShouldBe(42);
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Tap_OnFailure_DoesNotRun()
    {
        bool ran = false;

        var result = Result<int>.Failure("fail").Tap(_ => ran = true);

        ran.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
    }



    // ── Instance BindAsync ───────────────────────────────────────

    [Fact]
    public async Task InstanceBindAsync_OnSuccess_Chains()
    {
        var result = await Result<int>.Success(5)
            .BindAsync(v => Task.FromResult(Result<string>.Success($"val:{v}")));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("val:5");
    }

    [Fact]
    public async Task InstanceBindAsync_OnFailure_ShortCircuits()
    {
        var result = await Result<int>.Failure("err")
            .BindAsync(v => Task.FromResult(Result<string>.Success($"val:{v}")));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("err");
    }

    // ── Match ────────────────────────────────────────────────────

    [Fact]
    public void Match_OnSuccess_ReturnsOnSuccess()
    {
        int result = Result<int>.Success(5).Match(v => v * 2, _ => -1);

        result.ShouldBe(10);
    }

    [Fact]
    public void Match_OnFailure_ReturnsOnFailure()
    {
        int result = Result<int>.Failure("err").Match(v => v * 2, e => e.Length);

        result.ShouldBe(3);
    }

    // ── Implicit operator ────────────────────────────────────────

    [Fact]
    public void ImplicitOperator_NonNull_CreatesSuccess()
    {
        Result<string> result = "hello";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void ImplicitOperator_Null_CreatesFailure()
    {
        Result<string> result = (string)null!;

        result.IsFailure.ShouldBeTrue();
    }

    // ── Map (sync) ───────────────────────────────────────────────

    [Fact]
    public void Map_OnSuccess_Transforms()
    {
        var result = Result<int>.Success(3).Map(v => v.ToString());

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("3");
    }

    [Fact]
    public void Map_OnFailure_ShortCircuits()
    {
        var result = Result<int>.Failure("bad").Map(v => v.ToString());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("bad");
    }

    // ── Bind (sync) ──────────────────────────────────────────────

    [Fact]
    public void Bind_OnSuccess_Chains()
    {
        var result = Result<int>.Success(5).Bind(v => Result<string>.Success($"v={v}"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("v=5");
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuits()
    {
        var result = Result<int>.Failure("err").Bind(v => Result<string>.Success($"v={v}"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("err");
    }

    // ── Value access on failure throws ───────────────────────────

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result<int>.Failure("err");

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
