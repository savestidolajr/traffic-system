namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Controls optional full-payload LLM diagnostics. Metadata is always emitted; full prompt and
///     response bodies remain off by default because they may contain source data and model output.
/// </summary>
public sealed class LlmCaptureOptions
{
    public const string SectionName = "LlmCapture";

    /// <summary>
    ///     Enables bounded asynchronous persistence of full prompt/request/response payloads. Keep
    ///     false in shared and production environments unless a replay investigation explicitly
    ///     enables it.
    /// </summary>
    public bool FullPayloads { get; set; }

    /// <summary>Fixed queue capacity for full-payload persistence.</summary>
    public const int QueueCapacity = 100;
}
