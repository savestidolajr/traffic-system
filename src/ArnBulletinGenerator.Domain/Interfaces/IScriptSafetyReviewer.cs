using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     LLM-based safety review of generated script content prior to human review
///     (SoW: "AI-based pre-review of generated audio and script content prior to human review").
///     Catches nuanced broadcast-safety issues the deterministic banned-word filter cannot:
///     suicide/self-harm references (Mindframe guidelines), graphic injury detail, masked
///     profanity, victim identification or speculation, and discriminatory language.
///     A blocked script fails the generation attempt and is regenerated; the audio is trusted
///     to faithfully render the script, so no audio-level check is performed.
/// </summary>
public interface IScriptSafetyReviewer
{
    Task<Result<ScriptSafetyResult>> ReviewScriptSafetyAsync(
        string script,
        string? jobId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Outcome of the LLM safety review. Violations are operator-readable strings
///     naming the category, the offending text, and the reason.
/// </summary>
public sealed record ScriptSafetyResult(bool IsSafe, IReadOnlyList<string> Violations)
{
    public static ScriptSafetyResult Safe() => new(true, []);
}
