using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Errors;

/// <summary>
///     Lifts a failed <see cref="Result" /> into a typed <see cref="Result{T}" />,
///     preserving the typed <see cref="OperationalFailure" /> metadata when the failure carried
///     one. The typed disposition is what HTTP and queue boundaries map to a status/routing
///     decision (<see cref="FailureDisposition" />); dropping it at a service boundary is how a
///     client-input error becomes a 500 and a conflict becomes a generic failure, so every
///     Application service that wraps a repository/provider failure uses this one helper instead
///     of re-deriving the two-line ternary.
/// </summary>
public static class ResultFailure
{
    /// <summary>
    ///     The failed <paramref name="failure" /> as a <c>Result&lt;T&gt;</c> failure, carrying its
    ///     typed metadata when present (and only the message when not). Callers invoke this only
    ///     on a failed result.
    /// </summary>
    public static Result<T> Propagate<T>(Result failure) =>
        failure.FailureMetadata.HasValue
            ? Result<T>.Failure(failure.FailureMetadata.Value)
            : Result<T>.Failure(failure.Error);
}
