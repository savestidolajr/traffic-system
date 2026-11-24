using System.Threading;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Worker-wide concurrency gates for HERE enrichment. A worker can process more than one bulletin
///     at once, so request-local gates do not protect the process or HERE from aggregate fan-out.
/// </summary>
internal static class HereRequestBudget
{
    internal const int MapPermitCount = 2;
    internal const int RoutePermitCount = 3;
    internal const int ReverseGeocodePermitCount = 3;

    private static readonly SemaphoreSlim MapPermits = new(MapPermitCount, MapPermitCount);
    private static readonly SemaphoreSlim RoutePermits = new(RoutePermitCount, RoutePermitCount);
    private static readonly SemaphoreSlim ReverseGeocodePermits =
        new(ReverseGeocodePermitCount, ReverseGeocodePermitCount);

    internal static async ValueTask<IDisposable> AcquireAsync(
        HereRequestKind kind, CancellationToken cancellationToken)
    {
        var semaphore = kind switch
        {
            HereRequestKind.Map => MapPermits,
            HereRequestKind.Route => RoutePermits,
            HereRequestKind.ReverseGeocode => ReverseGeocodePermits,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown HERE request kind")
        };

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }


    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}

internal enum HereRequestKind
{
    Map,
    Route,
    ReverseGeocode
}
