using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Extension methods that reduce boilerplate when converting between
///     nullable storage types and the domain <see cref="Option{T}" /> type.
/// </summary>
internal static class OptionMappingExtensions
{
    // ─── Nullable → Option (read from entity) ───────────────────────

    /// <summary>Converts a nullable string to <see cref="Option{T}" />, treating null/whitespace as None.</summary>
    internal static Option<string> ToOption(this string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Option<string>.None : Option<string>.Some(value);
    }

    /// <summary>Converts a nullable double to <see cref="Option{T}" />.</summary>
    internal static Option<double> ToOption(this double? value)
    {
        return value.HasValue ? Option<double>.Some(value.Value) : Option<double>.None;
    }

    /// <summary>Converts a nullable int to <see cref="Option{T}" />.</summary>
    internal static Option<int> ToOption(this int? value)
    {
        return value.HasValue ? Option<int>.Some(value.Value) : Option<int>.None;
    }

    /// <summary>Converts a nullable <see cref="DateTimeOffset" /> to <see cref="Option{T}" /> of UTC DateTime.</summary>
    internal static Option<DateTime> ToUtcOption(this DateTimeOffset? value)
    {
        return value.HasValue ? Option<DateTime>.Some(value.Value.UtcDateTime) : Option<DateTime>.None;
    }

    // ─── Option → Nullable (write to entity) ────────────────────────

    /// <summary>Extracts the string value or returns null.</summary>
    internal static string? ToNullable(this Option<string> option)
    {
        return option.Match(v => (string?)v, () => null);
    }

    /// <summary>Extracts the double value or returns null.</summary>
    internal static double? ToNullable(this Option<double> option)
    {
        return option.Match(v => (double?)v, () => null);
    }

    /// <summary>Extracts the int value or returns null.</summary>
    internal static int? ToNullable(this Option<int> option)
    {
        return option.Match(v => (int?)v, () => null);
    }

    /// <summary>Wraps the DateTime value in a <see cref="DateTimeOffset" /> or returns null.</summary>
    internal static DateTimeOffset? ToNullableOffset(this Option<DateTime> option)
    {
        return option.Match(v => (DateTimeOffset?)new DateTimeOffset(v, TimeSpan.Zero), () => null);
    }
}
