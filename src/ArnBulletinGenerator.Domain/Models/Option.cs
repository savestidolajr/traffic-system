namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Non-generic factory methods for creating <see cref="Option{T}" /> from nullable/whitespace values.
/// </summary>
public static class Option
{
    public static Option<string> OfWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Option<string>.None : value!;
    }

    public static Option<T> OfNullable<T>(T? value) where T : struct
    {
        return value.HasValue ? Option<T>.Some(value.Value) : Option<T>.None;
    }

    /// <summary>Lifts a nullable reference type into an Option, treating null as None.</summary>
    public static Option<T> OfNullable<T>(T? value) where T : class
    {
        return value is not null ? Option<T>.Some(value) : Option<T>.None;
    }
}

public readonly record struct Option<T>
{
    private readonly T _value;

    private Option(T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        _value = value;
        HasValue = true;
    }

    public bool HasValue { get; }
    public bool IsNone => !HasValue;
    public T Value => HasValue ? _value : throw new InvalidOperationException("Option has no value.");

    public bool IsSome => HasValue;

    public static Option<T> None => default;

    public static Option<T> Some(T value)
    {
        return new Option<T>(value);
    }

    public T GetValueOr(T fallback)
    {
        return HasValue ? _value : fallback;
    }

    /// <summary>
    ///     Returns this option if it has a value, otherwise returns <paramref name="fallback" />.
    /// </summary>
    public Option<T> Or(Option<T> fallback)
    {
        return HasValue ? this : fallback;
    }

    /// <summary>
    ///     Transforms the contained value using <paramref name="map" />, or returns None.
    /// </summary>
    public Option<TResult> Map<TResult>(Func<T, TResult> map)
    {
        if (!HasValue) return Option<TResult>.None;
        var result = map(_value);
        return result is null ? Option<TResult>.None : Option<TResult>.Some(result);
    }

    /// <summary>
    ///     Flat-maps the contained value using <paramref name="bind" />, or returns None.
    /// </summary>
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bind)
    {
        return HasValue ? bind(_value) : Option<TResult>.None;
    }

    /// <summary>
    ///     Pattern-matches on the option, returning <paramref name="some" /> if present, otherwise <paramref name="none" />.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
    {
        return HasValue ? some(_value) : none();
    }

    /// <summary>
    ///     Executes <paramref name="action" /> if a value is present; returns the option unchanged.
    /// </summary>
    public Option<T> Tap(Action<T> action)
    {
        if (HasValue) action(_value);
        return this;
    }

    public static implicit operator Option<T>(T value)
    {
        return value is not null ? Some(value) : None;
    }
}
