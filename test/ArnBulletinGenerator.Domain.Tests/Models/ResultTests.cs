using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class ResultTests
{
    [Fact]
    public void Success_SetsCorrectState()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void Failure_SetsErrorAndState()
    {
        var result = Result.Failure("Test error");

        (result.IsSuccess, result.IsFailure, result.Error)
            .ShouldBe((false, true, "Test error"));
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var r = Result<string>.Success("val");
        r.IsSuccess.ShouldBeTrue();
        r.Value.ShouldBe("val");
        r.Error.ShouldBe(string.Empty);
    }

    [Fact]
    public void GenericFailure_ThrowsOnValueAccess()
    {
        var result = Result<int>.Failure("err");

        result.IsFailure.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task BindAsync_PropagatesFailure()
    {
        var bound = await Result<string>.Failure("err")
            .BindAsync(_ => Task.FromResult(Result<int>.Success(123)));

        (bound.IsFailure, bound.Error).ShouldBe((true, "err"));
    }

    [Fact]
    public async Task BindAsync_ExecutesOnSuccess()
    {
        var bound = await Result<string>.Success("input")
            .BindAsync(v => Task.FromResult(Result<int>.Success(v.Length)));

        (bound.IsSuccess, bound.Value).ShouldBe((true, 5));
    }

    // --- Typed failure: string failures remain unclassified ---

    [Fact]
    public void StringFailure_HasNoMetadata()
    {
        var result = Result.Failure("something broke");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("something broke");
        result.FailureMetadata.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void GenericStringFailure_HasNoMetadata()
    {
        var result = Result<int>.Failure("nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("nope");
        result.FailureMetadata.IsNone.ShouldBeTrue();
    }

    // --- Typed failure: OperationalFailure preserves Error/message/code/disposition ---

    [Fact]
    public void TypedFailure_PreservesErrorAndMetadata()
    {
        var failure = OperationalFailure.Retryable("TTS_TIMEOUT", "TTS timed out after 30s");
        var result = Result.Failure(failure);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("TTS timed out after 30s");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Retryable);
        result.FailureMetadata.Value.Code.ShouldBe("TTS_TIMEOUT");
        result.FailureMetadata.Value.Message.ShouldBe("TTS timed out after 30s");
    }

    [Fact]
    public void GenericTypedFailure_PreservesErrorAndMetadata()
    {
        var failure = OperationalFailure.Permanent("SAFETY_VIOLATION", "Content rejected");
        var result = Result<string>.Failure(failure);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Content rejected");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Permanent);
        result.FailureMetadata.Value.Code.ShouldBe("SAFETY_VIOLATION");
    }

    [Theory]
    [InlineData(FailureDisposition.Retryable)]
    [InlineData(FailureDisposition.Permanent)]
    [InlineData(FailureDisposition.ContentLimited)]
    [InlineData(FailureDisposition.SafetyRejected)]
    [InlineData(FailureDisposition.Conflict)]
    [InlineData(FailureDisposition.InvalidInput)]
    [InlineData(FailureDisposition.Cancelled)]
    public void TypedFailure_AllDispositions_RoundTrip(FailureDisposition disposition)
    {
        var failure = new OperationalFailure(disposition, "CODE", "msg");
        var result = Result.Failure(failure);

        result.FailureMetadata.Value.Disposition.ShouldBe(disposition);
    }

    // --- Metadata survives Map ---

    [Fact]
    public void Map_PropagatesFailureMetadata()
    {
        var failure = OperationalFailure.Retryable("E1", "err");
        var result = Result<int>.Failure(failure);

        var mapped = result.Map(v => v.ToString());

        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe("err");
        mapped.FailureMetadata.HasValue.ShouldBeTrue();
        mapped.FailureMetadata.Value.Code.ShouldBe("E1");
    }

    [Fact]
    public void Map_MapsSuccess_NoMetadata()
    {
        var result = Result<int>.Success(42);

        var mapped = result.Map(v => v * 2);

        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(84);
        mapped.FailureMetadata.IsNone.ShouldBeTrue();
    }

    // --- Metadata survives Bind ---

    [Fact]
    public void Bind_PropagatesFailureMetadata()
    {
        var failure = OperationalFailure.InvalidInput("BAD", "invalid");
        var result = Result<string>.Failure(failure);

        var bound = result.Bind(v => Result<int>.Success(v.Length));

        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBe("invalid");
        bound.FailureMetadata.HasValue.ShouldBeTrue();
        bound.FailureMetadata.Value.Code.ShouldBe("BAD");
        bound.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
    }

    // --- Metadata survives BindAsync ---

    [Fact]
    public async Task BindAsync_PropagatesFailureMetadata()
    {
        var failure = OperationalFailure.Conflict("DUP", "duplicate job");
        var result = Result<string>.Failure(failure);

        var bound = await result.BindAsync(v => Task.FromResult(Result<int>.Success(v.Length)));

        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBe("duplicate job");
        bound.FailureMetadata.HasValue.ShouldBeTrue();
        bound.FailureMetadata.Value.Code.ShouldBe("DUP");
    }

    // --- Cross-type propagation (string -> int) preserves metadata ---

    [Fact]
    public void Map_CrossType_PreservesMetadata()
    {
        var failure = OperationalFailure.Permanent("HDR", "header failed");
        var result = Result<string>.Failure(failure);

        var mapped = result.Map(s => s.Length);

        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe("header failed");
        mapped.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Permanent);
        mapped.FailureMetadata.Value.Code.ShouldBe("HDR");
    }

    // --- PropagateFailure: public instance API ---

    [Fact]
    public void PropagateFailure_FromFailedResult_PreservesErrorAndMetadata()
    {
        var failure = OperationalFailure.Retryable("NET", "network timeout");
        var source = Result<string>.Failure(failure);

        var propagated = source.PropagateFailure<int>();

        propagated.IsFailure.ShouldBeTrue();
        propagated.Error.ShouldBe("network timeout");
        propagated.FailureMetadata.HasValue.ShouldBeTrue();
        propagated.FailureMetadata.Value.Code.ShouldBe("NET");
        propagated.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Retryable);
    }

    [Fact]
    public void PropagateFailure_FromStringFailure_NoMetadata()
    {
        var source = Result<string>.Failure("plain error");

        var propagated = source.PropagateFailure<int>();

        propagated.IsFailure.ShouldBeTrue();
        propagated.Error.ShouldBe("plain error");
        propagated.FailureMetadata.IsNone.ShouldBeTrue();
    }

    [Fact]
    public void PropagateFailure_FromSuccess_Throws()
    {
        var source = Result<string>.Success("ok");

        Should.Throw<InvalidOperationException>(() => source.PropagateFailure<int>());
    }

    [Fact]
    public void PropagateFailure_CrossType_PreservesAllMetadata()
    {
        var failure = new OperationalFailure(FailureDisposition.SafetyRejected, "BAN", "content banned");
        var source = Result<string>.Failure(failure);

        var propagated = source.PropagateFailure<IReadOnlyList<int>>();

        propagated.IsFailure.ShouldBeTrue();
        propagated.Error.ShouldBe("content banned");
        propagated.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.SafetyRejected);
        propagated.FailureMetadata.Value.Code.ShouldBe("BAN");
    }
}
