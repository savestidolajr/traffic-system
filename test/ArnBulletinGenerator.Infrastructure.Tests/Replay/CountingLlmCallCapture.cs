using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     Counts LLM calls per stage during a replay, so the harness can report what a bulletin
///     actually COSTS.
///     <para>
///         Added 2026-08-25 after a whole session of reporting <c>meanAttempts</c> (1.4-1.8) as
///         evidence the pipeline was efficient. That figure counts OUTER generation attempts, not
///         model calls: measured on Foundry traces the same runs were spending 7.1 to 8.6 LLM calls
///         per bulletin, with review and refine each firing about three times per draft. The metric
///         being quoted was roughly five times optimistic as a churn proxy, and the local harness
///         could not see the difference — so post-approval refine churn stayed invisible locally and
///         looked like a dev-only regression when it was neither dev-only nor new.
///     </para>
///     <para>
///         <see cref="ILlmCallCapture" /> is the right hook because the transport already calls it
///         once per model call by construction, for every agent, on the same path production uses.
///     </para>
/// </summary>
internal sealed class CountingLlmCallCapture : ILlmCallCapture
{
    private readonly ConcurrentDictionary<string, int> _byStage = new(StringComparer.OrdinalIgnoreCase);

    public Task CaptureAsync(LlmCallRecord record, CancellationToken cancellationToken = default)
    {
        _byStage.AddOrUpdate(record.Stage ?? "unknown", 1, (_, n) => n + 1);
        return Task.CompletedTask;
    }

    public int Total => _byStage.Values.Sum();

    public int For(string stage) => _byStage.TryGetValue(stage, out int n) ? n : 0;

    /// <summary>"draft=10 review=31 refine=33 safety=10" — stages in pipeline order, then the rest.</summary>
    public string Describe()
    {
        string[] order = ["Draft", "Review", "RefineSentence", "Safety"];
        var parts = order
            .Where(s => For(s) > 0)
            .Select(s => $"{s.ToLowerInvariant()}={For(s)}")
            .Concat(_byStage.Keys
                .Where(k => !order.Contains(k, StringComparer.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => $"{k.ToLowerInvariant()}={For(k)}"));
        return string.Join(" ", parts);
    }

    public void Reset() => _byStage.Clear();
}
