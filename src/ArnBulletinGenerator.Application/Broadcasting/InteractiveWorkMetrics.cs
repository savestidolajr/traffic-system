using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Broadcasting;


/// <summary>Default no-op sink for local runs and tests without an App Insights adapter.</summary>
public sealed class NoOpInteractiveWorkMetrics : IInteractiveWorkMetrics
{
    public void TrackCacheHit(InteractiveWorkPool pool, string operation) { }
    public void TrackSingleFlight(InteractiveWorkPool pool, string operation) { }
    public void TrackWait(InteractiveWorkPool pool, string operation, double durationMilliseconds,
        bool acquired, bool cancelled) { }
    public void TrackExecution(InteractiveWorkPool pool, string operation, double durationMilliseconds,
        bool success, bool cancelled, bool timedOut) { }
}
