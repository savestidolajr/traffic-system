using System.Diagnostics;
using System.Threading;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The per-source budget shared by every HTTP call made while an official feed is read. NSW and
///     WA each expose two layers, so the layer loops must share their deadline and retry backoff
///     rather than each starting a fresh retry policy.
/// </summary>
public sealed class OfficialFeedCallBudget
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxFeedTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan TotalBackoffCap = TimeSpan.FromSeconds(2);

    private static readonly AsyncLocal<OfficialFeedCallBudget?> Ambient = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan _feedTimeout;
    private readonly object _gate = new();
    private TimeSpan _backoff;

    private OfficialFeedCallBudget(TimeSpan feedTimeout) => _feedTimeout = feedTimeout;

    public static OfficialFeedCallBudget? Current => Ambient.Value;

    public static Scope Begin(TimeSpan configuredTimeout)
    {
        var current = Ambient.Value;
        if (current is not null)
            return new Scope(current, current);

        var bounded = configuredTimeout <= TimeSpan.Zero
            ? MaxFeedTimeout
            : configuredTimeout > MaxFeedTimeout ? MaxFeedTimeout : configuredTimeout;
        var budget = new OfficialFeedCallBudget(bounded);
        Ambient.Value = budget;
        return new Scope(budget, null);
    }

    public TimeSpan Remaining
    {
        get
        {
            var remaining = _feedTimeout - _clock.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool TryReserveBackoff(TimeSpan requested, out TimeSpan granted)
    {
        lock (_gate)
        {
            var remaining = TotalBackoffCap - _backoff;
            if (remaining <= TimeSpan.Zero)
            {
                granted = TimeSpan.Zero;
                return false;
            }

            granted = requested <= remaining ? requested : remaining;
            _backoff += granted;
            return granted > TimeSpan.Zero;
        }
    }

    public CancellationTokenSource CreateRequestCancellation(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = Remaining;
        if (remaining <= TimeSpan.Zero) cts.Cancel();
        else cts.CancelAfter(remaining);
        return cts;
    }

    public sealed class Scope : IDisposable
    {
        private readonly OfficialFeedCallBudget _current;
        private readonly OfficialFeedCallBudget? _previous;
        private int _disposed;

        internal Scope(OfficialFeedCallBudget current, OfficialFeedCallBudget? previous)
        {
            _current = current;
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && ReferenceEquals(Ambient.Value, _current))
                Ambient.Value = _previous;
        }
    }
}
