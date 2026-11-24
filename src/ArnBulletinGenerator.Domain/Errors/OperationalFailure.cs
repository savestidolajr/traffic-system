namespace ArnBulletinGenerator.Domain.Errors;

/// <summary>
///     Classifies the operational disposition of a typed failure so queue boundaries
///     can make routing decisions without parsing error-message prefixes.
/// </summary>
public enum FailureDisposition
{
    Retryable,
    Permanent,
    ContentLimited,
    SafetyRejected,
    Conflict,
    InvalidInput,
    Cancelled
}

/// <summary>
///     Immutable, typed failure value that can flow through <c>Result</c> alongside
///     the existing unclassified <c>Failure(string)</c> path.
/// </summary>
public record OperationalFailure(
    FailureDisposition Disposition,
    string Code,
    string Message)
{
    public static OperationalFailure Retryable(string code, string message) =>
        new(FailureDisposition.Retryable, code, message);

    public static OperationalFailure Permanent(string code, string message) =>
        new(FailureDisposition.Permanent, code, message);

    public static OperationalFailure ContentLimited(string code, string message) =>
        new(FailureDisposition.ContentLimited, code, message);

    public static OperationalFailure SafetyRejected(string code, string message) =>
        new(FailureDisposition.SafetyRejected, code, message);

    public static OperationalFailure Conflict(string code, string message) =>
        new(FailureDisposition.Conflict, code, message);

    public static OperationalFailure InvalidInput(string code, string message) =>
        new(FailureDisposition.InvalidInput, code, message);

    public static OperationalFailure Cancelled(string code, string message) =>
        new(FailureDisposition.Cancelled, code, message);
}
