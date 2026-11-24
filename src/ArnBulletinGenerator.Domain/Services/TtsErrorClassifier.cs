namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Classifies TTS synthesis errors into permanent (non-retryable) vs transient (retryable).
///     Shared between BroadcastOrchestrator and AudioPipelineService to prevent duplication.
/// </summary>
public static partial class TtsErrorClassifier
{
    /// <summary>
    ///     Determines whether a TTS error is permanent and should not be retried.
    public static bool IsPermanentTtsError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        var trimmed = error.Trim();

        return trimmed.StartsWith("bad request", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("badrequest", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("not found", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("method not allowed", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("not acceptable", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("unsupported media type", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("invalid api key", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("account banned", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("subscription expired", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("invalid voice", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("voice not found", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("model not found", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("insufficient quota", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("content blocked", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("character limit reached", StringComparison.OrdinalIgnoreCase);
    }
}
