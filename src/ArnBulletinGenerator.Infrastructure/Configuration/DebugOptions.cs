namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Dev-only debugging switches. Never enable <see cref="CaptureLlmCalls" /> in a shared or
///     production environment — the captured records include the full source traffic/weather/fuel
///     data and the full model output verbatim, which <c>LogsJson</c> deliberately only summarises.
/// </summary>
public sealed class DebugOptions
{
    public const string SectionName = "Debug";

    /// <summary>
    ///     When true, <see cref="ArnBulletinGenerator.Domain.Interfaces.ILlmCallCapture" />
    ///     persists the exact rendered prompt and raw model response for every LLM call
    ///     (draft/review/refine/safety/director/attribute) to blob storage, keyed by job id — so a
    ///     failure can be reproduced and inspected exactly as the model saw it, not reconstructed
    ///     from a narrated summary. Off by default.
    /// </summary>
    public bool CaptureLlmCalls { get; set; }

    /// <summary>
    ///     Rolling cap on how many distinct jobs' captures are kept. Once a capture pushes the
    ///     count of distinct captured jobs past this, the oldest job's blobs are deleted. Bounds
    ///     storage growth for an always-on dev capture without needing a time-based lifecycle rule.
    /// </summary>
    public int MaxCapturedJobs { get; set; } = 20;
}
