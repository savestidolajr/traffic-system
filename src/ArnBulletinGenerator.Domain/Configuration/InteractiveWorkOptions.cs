using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Bounds for interactive work. These defaults keep operator previews independent from the
///     broadcast-generation bulkhead while making every provider call finite. The gate remains
///     responsible for enforcing the deadline even when cache reads are disabled.
/// </summary>
public sealed class InteractiveWorkOptions
{
    public const string SectionName = "InteractiveWork";

    /// <summary>Whether permit admission and the success cache are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Disable successful-result reuse without disabling deadline enforcement.</summary>
    public bool CacheDisabled { get; set; }

    /// <summary>Maximum concurrent interactive LLM operations per worker.</summary>
    public int LlmPoolSize { get; set; } = 2;

    /// <summary>Maximum concurrent interactive TTS operations per worker.</summary>
    public int TtsPoolSize { get; set; } = 2;

    /// <summary>Successful-result cache lifetime. Failures and cancellations are never cached.</summary>
    public int CacheTtlMinutes { get; set; } = 10;

    /// <summary>Preview/direct/retone/rewrite deadline, in seconds.</summary>
    public int LlmDeadlineSeconds { get; set; } = 60;

    /// <summary>Broadcast preflight deadline, in seconds.</summary>
    public int PreflightDeadlineSeconds { get; set; } = 15;

    /// <summary>Synchronous TTS preview deadline, in seconds.</summary>
    public int TtsDeadlineSeconds { get; set; } = 45;

    /// <summary>Interactive delivery-direction deadline, in seconds.</summary>
    public int DeliveryDeadlineSeconds { get; set; } = 30;

    // Compatibility aliases make the intended configuration vocabulary explicit while retaining
    // one source of truth for binders and tests that use either the pool or permit terminology.
    public int LlmPermitCount
    {
        get => LlmPoolSize;
        set => LlmPoolSize = value;
    }

    public int TtsPermitCount
    {
        get => TtsPoolSize;
        set => TtsPoolSize = value;
    }

    public int SuccessCacheMinutes
    {
        get => CacheTtlMinutes;
        set => CacheTtlMinutes = value;
    }

    public int EffectiveLlmPoolSize => Math.Max(1, LlmPoolSize);
    public int EffectiveTtsPoolSize => Math.Max(1, TtsPoolSize);
    public TimeSpan SuccessCacheLifetime => TimeSpan.FromMinutes(Math.Max(0, CacheTtlMinutes));

    public TimeSpan DeadlineFor(InteractiveWorkPool pool, string operation)
    {
        string normalizedOperation = operation.Trim().ToUpperInvariant();
        int seconds = normalizedOperation switch
        {
            "DELIVERY" => DeliveryDeadlineSeconds,
            "PREFLIGHT" when pool != InteractiveWorkPool.Tts => PreflightDeadlineSeconds,
            _ when pool == InteractiveWorkPool.Tts => TtsDeadlineSeconds,
            _ => LlmDeadlineSeconds
        };
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }
}
