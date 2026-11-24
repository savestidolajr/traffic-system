using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Captures one physical LLM attempt. Operational metadata is always recorded; the full
///     rendered prompt and raw response are optional diagnostics controlled by the capture
///     implementation.
/// </summary>
public interface ILlmCallCapture
{
    /// <summary>
    ///     Records metadata without waiting for an exporter. Implementations should keep this
    ///     operation allocation-light and fail-safe because it runs on the model-call path.
    /// </summary>
    void RecordMetadata(in LlmCallMetadata metadata) { }

    /// <summary>
    ///     Queues the full prompt/response payload when enabled. The returned task represents only
    ///     bounded in-memory admission; implementations must not make callers wait for blob I/O.
    /// </summary>
    Task CaptureAsync(LlmCallRecord record, CancellationToken cancellationToken = default);
}
