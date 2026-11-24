using System.Diagnostics;
using System.Threading;

using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;

/// <summary>
/// Process-wide bulkhead and absolute operation deadline for ElevenLabs generation calls.
/// Every physical TTS request, including calibration and filler renders, consumes one of the
/// configured provider permits. The lease owns the linked deadline token so a permit is always
/// returned on success, provider failure, caller cancellation, or timeout.
/// </summary>
public sealed class ElevenLabsTtsBudget
{
    internal const int DefaultPermitCount = 2;
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _permits;
    private readonly TimeSpan _operationTimeout;
    private readonly ILogger<ElevenLabsTtsBudget>? _logger;

    public int PermitCount { get; }

    public ElevenLabsTtsBudget(
        IOptions<ElevenLabsOptions>? options = null,
        TimeSpan? operationTimeout = null,
        ILogger<ElevenLabsTtsBudget>? logger = null)
    {
        PermitCount = Math.Clamp(options?.Value.MaxConcurrentRequests ?? DefaultPermitCount, 1, 64);
        _permits = new(PermitCount, PermitCount);
        _operationTimeout = operationTimeout is { } value && value > TimeSpan.Zero
            ? value
            : DefaultOperationTimeout;
        _logger = logger;
    }

    internal async ValueTask<Lease> AcquireAsync(CancellationToken callerCancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        timeout.CancelAfter(_operationTimeout);
        var wait = Stopwatch.StartNew();

        try
        {
            await _permits.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            timeout.Dispose();
            throw new TimeoutException(
                $"ElevenLabs TTS operation exceeded its {_operationTimeout.TotalSeconds:0} second budget.");
        }
        catch
        {
            timeout.Dispose();
            throw;
        }
        finally
        {
            wait.Stop();
            if (wait.ElapsedMilliseconds > 500 && _logger is not null)
            {
                _logger.LogInformation(
                    "[TtsBudget] Waited {WaitMs}ms for one of {Permits} ElevenLabs permits",
                    wait.ElapsedMilliseconds,
                    PermitCount);
            }
        }

        return new Lease(timeout, _permits);
    }

    internal sealed class Lease : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly SemaphoreSlim _permits;
        private int _released;

        internal Lease(CancellationTokenSource timeout, SemaphoreSlim permits)
        {
            _timeout = timeout;
            _permits = permits;
        }

        internal CancellationToken Token => _timeout.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            _permits.Release();
            _timeout.Dispose();
        }
    }
}
