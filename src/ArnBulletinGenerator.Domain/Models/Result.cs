using ArnBulletinGenerator.Domain.Errors;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Railway-oriented success/failure outcome used throughout the domain instead of exceptions for expected failure paths, carrying an error message and optional typed failure metadata.</summary>
public record Result
{
    protected Result(bool isSuccess, string error, Option<OperationalFailure> failureMetadata = default)
    {
        IsSuccess = isSuccess;
        Error = error;
        FailureMetadata = failureMetadata;
    }

    public bool IsSuccess { get; init; }
    public string Error { get; init; }
    public Option<OperationalFailure> FailureMetadata { get; init; }
    public bool IsFailure => !IsSuccess;

    public static Result Success()
    {
        return new Result(true, string.Empty);
    }

    public static Result Failure(string error)
    {
        return new Result(false, error);
    }

    public static Result Failure(OperationalFailure failure)
    {
        return new Result(false, failure.Message, Option<OperationalFailure>.Some(failure));
    }

    /// <summary>
    ///     Converts a failed untyped result into a typed failure while preserving structured
    ///     operational metadata. This is the bridge used when an operation with no success value
    ///     sits inside a larger typed workflow.
    /// </summary>
    public Result<TNext> PropagateFailure<TNext>()
    {
        if (IsSuccess)
            throw new InvalidOperationException("Cannot propagate failure from a successful Result.");

        return Result<TNext>.CreateFailed(Error, FailureMetadata);
    }
}

public record Result<T> : Result
{
    private readonly T _value;

    protected Result(bool isSuccess, T value, string error, Option<OperationalFailure> failureMetadata = default)
        : base(isSuccess, error, failureMetadata)
    {
        _value = value;
    }

    public T Value =>
        IsSuccess ? _value : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public static Result<T> Success(T value)
    {
        return new Result<T>(true, value, string.Empty);
    }

    public new static Result<T> Failure(string error)
    {
        return new Result<T>(false, default!, error);
    }

    public new static Result<T> Failure(OperationalFailure failure)
    {
        return new Result<T>(false, default!, failure.Message, Option<OperationalFailure>.Some(failure));
    }

    /// <summary>
    ///     Internal factory used to construct a failed generic Result while preserving typed failure metadata.
    /// </summary>
    internal static Result<T> CreateFailed(string error, Option<OperationalFailure> failureMetadata)
    {
        return new Result<T>(false, default!, error, failureMetadata);
    }

    /// <summary>
    ///     Converts a failed <c>Result&lt;T&gt;</c> into a <c>Result&lt;TNext&gt;</c>, preserving error text
    ///     and typed failure metadata. Throws if this result succeeded.
    /// </summary>
    public new Result<TNext> PropagateFailure<TNext>()
    {
        if (IsSuccess)
            throw new InvalidOperationException("Cannot propagate failure from a successful Result.");

        return Result<TNext>.CreateFailed(Error, FailureMetadata);
    }

    public static implicit operator Result<T>(T value)
    {
        return value is null ? Failure("Value cannot be null") : Success(value);
    }

    public async Task<Result<TNext>> BindAsync<TNext>(Func<T, Task<Result<TNext>>> func)
    {
        return IsFailure ? PropagateFailure<TNext>() : await func(Value);
    }

    public Result<TNext> Map<TNext>(Func<T, TNext> func)
    {
        return IsFailure ? PropagateFailure<TNext>() : Result<TNext>.Success(func(Value));
    }

    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func)
    {
        return IsFailure ? PropagateFailure<TNext>() : func(Value);
    }

    /// <summary>
    ///     Pattern-matches on success/failure, returning the appropriate branch.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(Value) : onFailure(Error);
    }

    /// <summary>
    ///     Executes <paramref name="action" /> on the value if successful; returns the result unchanged.
    /// </summary>
    public Result<T> Tap(Action<T> action)
    {
        if (IsSuccess) action(Value);
        return this;
    }
}

