using ArnBulletinGenerator.Domain.Configuration;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
/// One cancellation and attempt budget for a broadcast operation. The budget is created once by
/// the orchestration boundary and passed through generation, synthesis, finalization, and delivery
/// preparation. No nested collaborator may create a fresh deadline or reset an attempt counter.
/// </summary>
internal sealed class BroadcastWorkBudget : IDisposable
{
    public static readonly TimeSpan DeadlineSafetyMargin = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromSeconds(240);
    public const int DefaultMaxGenerationAttempts = 2;
    public const int DefaultMaxSynthesisAttempts = 2;

    private readonly CancellationTokenSource _deadlineCancellation;
    private int _generationAttemptsUsed;
    private int _synthesisAttemptsUsed;
    private int _disposed;

    private BroadcastWorkBudget(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? slotCutoffUtc,
        DateTimeOffset? invocationDeadlineUtc,
        CancellationToken cancellationToken,
        BroadcastWorkBudgetOptions settings)
    {
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        SlotCutoffUtc = slotCutoffUtc?.ToUniversalTime();
        InvocationDeadlineUtc = invocationDeadlineUtc?.ToUniversalTime();

        var maxDuration = settings.MaxDuration > TimeSpan.Zero
            ? settings.MaxDuration
            : DefaultMaxDuration;
        DeadlineUtc = Min(
            StartedAtUtc + maxDuration,
            SlotCutoffUtc?.Subtract(DeadlineSafetyMargin),
            InvocationDeadlineUtc?.Subtract(DeadlineSafetyMargin));

        MaxGenerationAttempts = ResolveAttemptCount(
            settings.UseLegacyAttemptPolicy ? settings.LegacyGenerationAttempts : settings.MaxGenerationAttempts,
            DefaultMaxGenerationAttempts);
        MaxSynthesisAttempts = ResolveAttemptCount(
            settings.UseLegacyAttemptPolicy ? settings.LegacySynthesisAttempts : settings.MaxSynthesisAttempts,
            DefaultMaxSynthesisAttempts);

        _deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan remaining = DeadlineUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            _deadlineCancellation.Cancel();
        else
            _deadlineCancellation.CancelAfter(remaining);
    }

    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? SlotCutoffUtc { get; }
    public DateTimeOffset? InvocationDeadlineUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }
    public int MaxGenerationAttempts { get; }
    public int MaxSynthesisAttempts { get; }
    public int GenerationAttemptsUsed => Volatile.Read(ref _generationAttemptsUsed);
    public int SynthesisAttemptsUsed => Volatile.Read(ref _synthesisAttemptsUsed);
    public CancellationToken Token => _deadlineCancellation.Token;
    public bool IsCancellationRequested => Token.IsCancellationRequested;

    /// <summary>
    /// Creates a budget with the bounded CH-35 policy. Slot and invocation deadlines are optional
    /// for ad-hoc jobs; when absent the 240-second operation ceiling remains authoritative.
    /// </summary>
    public static BroadcastWorkBudget Start(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? slotCutoffUtc = null,
        DateTimeOffset? invocationDeadlineUtc = null,
        CancellationToken cancellationToken = default,
        BroadcastWorkBudgetOptions? settings = null) =>
        new(startedAtUtc, slotCutoffUtc, invocationDeadlineUtc, cancellationToken,
            settings ?? new BroadcastWorkBudgetOptions());

    /// <summary>
    /// Creates a budget while retaining the existing configured attempt counts when the rollout
    /// flag is enabled. The deadline, cancellation, and byte ownership remain new behaviour even
    /// in that compatibility policy.
    /// </summary>
    public static BroadcastWorkBudget Start(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? slotCutoffUtc,
        DateTimeOffset? invocationDeadlineUtc,
        CancellationToken cancellationToken,
        BroadcastOptions broadcastOptions,
        bool useLegacyAttemptPolicy = false) =>
        Start(
            startedAtUtc,
            slotCutoffUtc,
            invocationDeadlineUtc,
            cancellationToken,
            new BroadcastWorkBudgetOptions
            {
                UseLegacyAttemptPolicy = useLegacyAttemptPolicy,
                LegacyGenerationAttempts = 1 + Math.Max(0, broadcastOptions.MaxGenerationRetries),
                LegacySynthesisAttempts = Math.Max(1, broadcastOptions.MaxSynthesisAttempts)
            });

    public bool TryStartGenerationAttempt(out int attemptNumber) =>
        TryConsume(ref _generationAttemptsUsed, MaxGenerationAttempts, out attemptNumber);

    public bool TryStartSynthesisAttempt(out int attemptNumber) =>
        TryConsume(ref _synthesisAttemptsUsed, MaxSynthesisAttempts, out attemptNumber);

    public void ThrowIfCancellationRequested() => Token.ThrowIfCancellationRequested();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _deadlineCancellation.Dispose();
    }


    private static bool TryConsume(ref int used, int maximum, out int attemptNumber)
    {
        while (true)
        {
            int current = Volatile.Read(ref used);
            if (current >= maximum)
            {
                attemptNumber = 0;
                return false;
            }

            if (Interlocked.CompareExchange(ref used, current + 1, current) == current)
            {
                attemptNumber = current + 1;
                return true;
            }
        }
    }

    private static int ResolveAttemptCount(int configured, int fallback) =>
        configured > 0 ? configured : fallback;

    private static DateTimeOffset Min(
        DateTimeOffset operationCeiling,
        DateTimeOffset? slotDeadline,
        DateTimeOffset? invocationDeadline)
    {
        DateTimeOffset result = operationCeiling;
        if (slotDeadline.HasValue && slotDeadline.Value < result)
            result = slotDeadline.Value;
        if (invocationDeadline.HasValue && invocationDeadline.Value < result)
            result = invocationDeadline.Value;
        return result;
    }
}
