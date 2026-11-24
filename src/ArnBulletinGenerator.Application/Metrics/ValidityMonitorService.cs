using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Metrics;

/// <summary>
///     On-demand comparison of every enabled market against the official road-authority feeds — the
///     2026-08-17 landscape study, runnable via POST /api/metrics/validity-probe whenever a rate
///     needs re-measuring (before/after a detector change, or when a job looks wrong). The routine
///     trend series comes from the generation path itself (FetchValidityAccounting in the
///     orchestrator) — sampled by real broadcasts, not a schedule.
///     Best-effort throughout: one market's or one feed's failure never fails the run.
/// </summary>
public sealed class ValidityMonitorService(
    IScheduleRepository scheduleRepository,
    ITrafficProvider trafficProvider,
    IEnumerable<IOfficialTrafficFeed> officialFeeds,
    IBroadcastMetrics metrics,
    TimeProvider timeProvider,
    ILogger<ValidityMonitorService> logger,
    IConfiguration? configuration = null)
{
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<FeedComparisonResult?>>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _singleFlightDisabled =
        bool.TryParse(configuration?["ValidityProbe:SingleFlightDisabled"], out var disabled) && disabled;

    /// <summary>Runs the probe over every enabled market. Returns the per-market results for callers
    ///     that surface them (the on-demand HTTP probe); metrics are emitted as a side effect.</summary>
    public async Task<IReadOnlyDictionary<string, FeedComparisonResult>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, FeedComparisonResult>(StringComparer.OrdinalIgnoreCase);
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(cancellationToken);
        if (schedulesResult.IsFailure)
        {
            logger.LogWarning("[ValidityMonitor] schedule listing failed: {Error}", schedulesResult.Error);
            return results;
        }

        var nowUtc = timeProvider.GetUtcNow();
        foreach (var schedule in schedulesResult.Value.Where(s => s.Enabled))
        {
            try
            {
                var comparison = await GetComparisonAsync(schedule, nowUtc, cancellationToken);
                if (comparison is null)
                    continue;

                results[schedule.MarketId] = comparison;
                Emit(schedule.MarketId, comparison);

                logger.LogInformation(
                    "[ValidityMonitor] {Market}: {Records} records ({Scheduled} scheduled), " +
                    "{Closures} closures ({ScheduledClosures} scheduled, {Uncorroborated} uncorroborated), " +
                    "official {OfficialIncidents} incident-class ({Missing} missing from HERE)",
                    schedule.MarketId, comparison.HereRecords, comparison.Scheduled,
                    comparison.Closures, comparison.ScheduledClosures, comparison.UncorroboratedClosures,
                    comparison.OfficialIncidentClass, comparison.MissingFromHere);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ValidityMonitor] {Market}: probe failed", schedule.MarketId);
            }
        }

        return results;
    }

    private async Task<FeedComparisonResult?> GetComparisonAsync(
        MarketSchedule schedule,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (_singleFlightDisabled)
            return await ProbeMarketAsync(schedule, nowUtc, cancellationToken);

        if (_cache.TryGetValue(schedule.MarketId, out var cached)
            && cached.ExpiresAtUtc > nowUtc)
            return cached.Result;

        var lazy = _inflight.GetOrAdd(
            schedule.MarketId,
            _ => new Lazy<Task<FeedComparisonResult?>>(
                // The shared operation must not inherit one request's cancellation. Each caller
                // cancels only its own wait below; another caller can still receive and cache the
                // result instead of a canceled first waiter poisoning the market flight.
                () => ProbeMarketAsync(schedule, nowUtc, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            // Keep a canceled flight joinable until its shared work settles. Removing it early
            // would allow a second probe for the same market while the first still runs.
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
                _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<FeedComparisonResult?>>>(
                    schedule.MarketId, lazy));
        }
    }

    private async Task<FeedComparisonResult?> ProbeMarketAsync(
        MarketSchedule schedule,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // Deliberately the PIPELINE's view (top-set cap + eligibility transforms), not the raw HERE
        // circle: the monitor measures the data bulletins are actually built from, and the lens is
        // identical every night so the trend is comparable. Consequently, official.missing_from_here
        // counts events absent from the TOP SET — it overstates absolute raw-feed misses but moves
        // the same way.
        var incidentsResult = await trafficProvider.GetIncidentsAsync(schedule.Coverage, cancellationToken);
        if (incidentsResult.IsFailure)
        {
            logger.LogWarning(
                "[ValidityMonitor] {Market}: HERE fetch failed: {Error}",
                schedule.MarketId, incidentsResult.Error);
            return null;
        }

        var official = new List<OfficialTrafficEvent>();
        foreach (var feed in officialFeeds)
        {
            var feedResult = await feed.GetEventsAsync(schedule.Coverage, cancellationToken);
            if (feedResult.IsSuccess)
                official.AddRange(feedResult.Value);
            else
                logger.LogWarning(
                    "[ValidityMonitor] {Market}: {Feed} unavailable: {Error}",
                    schedule.MarketId, feed.SourceId, feedResult.Error);
        }

        var comparison = TrafficFeedComparison.Compare(
            incidentsResult.Value.ToList(), official, nowUtc);
        _cache[schedule.MarketId] = new CacheEntry(
            comparison, timeProvider.GetUtcNow().Add(DefaultCacheDuration));
        return comparison;
    }

    private sealed record CacheEntry(FeedComparisonResult Result, DateTimeOffset ExpiresAtUtc);

    private void Emit(string market, FeedComparisonResult c)
    {
        metrics.TrackDataValidity(market, "here.records", c.HereRecords);
        metrics.TrackDataValidity(market, "here.fresh", c.Fresh);
        metrics.TrackDataValidity(market, "here.ongoing", c.Ongoing);
        metrics.TrackDataValidity(market, "here.scheduled", c.Scheduled);
        metrics.TrackDataValidity(market, "here.closures", c.Closures);
        metrics.TrackDataValidity(market, "here.closures.scheduled", c.ScheduledClosures);
        metrics.TrackDataValidity(market, "here.closures.uncorroborated", c.UncorroboratedClosures);
        metrics.TrackDataValidity(market, "official.events", c.OfficialEvents);
        metrics.TrackDataValidity(market, "official.incident_class", c.OfficialIncidentClass);
        metrics.TrackDataValidity(market, "official.missing_from_here", c.MissingFromHere);
        metrics.TrackDataValidity(market, "here.median_entry_age_hours", c.MedianEntryAgeHours);
    }
}
