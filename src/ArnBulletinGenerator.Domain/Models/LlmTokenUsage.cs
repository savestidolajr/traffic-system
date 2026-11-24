namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Token accounting for one LLM call, lifted out of the Responses API <c>usage</c> object.
///     <para>
///         <see cref="CachedInputTokens" /> is the part that pays for itself. Azure serves a
///         repeated prompt prefix from its prompt cache at a discount, but only when the prefix is
///         byte-identical AND still warm — in-memory caches clear within 5-10 minutes of
///         inactivity. From <c>gpt-5.6</c> onward cache WRITES are billed on top of discounted
///         reads, so a workload that keeps re-writing a prefix it never reads back (Arn's
///         drive-time slots sit minutes-to-hours apart) pays the write premium and collects
///         nothing. Measured 2026-07-27: every captured production call reported
///         <c>cached_tokens: 0</c> while the same agent, called back-to-back, reaches ~83%.
///         Recording it per call is what turns that from a guess into a number.
///     </para>
/// </summary>
/// <param name="InputTokens">Prompt tokens billed for this call, cached and uncached together.</param>
/// <param name="CachedInputTokens">The subset of <paramref name="InputTokens" /> served from the
///     prompt cache. Quantised by the service to 128-token blocks.</param>
/// <param name="OutputTokens">Completion tokens, including <paramref name="ReasoningTokens" />.</param>
/// <param name="ReasoningTokens">Reasoning tokens inside <paramref name="OutputTokens" />; 0 at
///     reasoning effort "none".</param>
public sealed record LlmTokenUsage(
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens,
    int ReasoningTokens)
{
    /// <summary>
    ///     Share of the prompt served from cache, 0.0-1.0. Zero when nothing was cached, which is
    ///     the signal worth alerting on for a prompt whose prefix should be stable.
    /// </summary>
    public double CacheHitRatio => InputTokens > 0 ? (double)CachedInputTokens / InputTokens : 0d;
}
