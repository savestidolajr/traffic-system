namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Always-on facts for one physical LLM attempt. Full prompt and response payloads are optional
///     diagnostics; these bounded metadata fields remain available for operational telemetry even
///     when payload capture is disabled or its queue is under pressure.
/// </summary>
public readonly record struct LlmCallMetadata(
    string? JobId,
    string Stage,
    int Attempt,
    string Model,
    string? ResponseId,
    int? StatusCode,
    bool Success,
    bool ViaFallback,
    long ElapsedMs,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset DeadlineUtc,
    LlmTokenUsage? Usage,
    string? Error = null);
