using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Ledger;

/// <summary>Serves the Event Ledger: the fold, omission verdicts, and per-job deltas.</summary>
public interface IEventLedgerService
{
    Task<Result<LedgerDto>> GetLedgerAsync(string market, CancellationToken cancellationToken = default);

    Task<Result> SetVerdictAsync(
        string market, string eventKey, string? verdict, string? notes, string by,
        CancellationToken cancellationToken = default);

    Task<Result<JobDeltaDto>> GetJobDeltaAsync(string jobId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SpotCheckReviewDto>>> GetSpotChecksAsync(
        CancellationToken cancellationToken = default);

    Task<Result<VersionedLibraryListPage<SpotCheckReviewDto>>> GetSpotChecksPageAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default);

    Task<Result> MarkSpotCheckAsync(string jobId, string by, CancellationToken cancellationToken = default);
}

/// <summary>
///     The ledger is a derived read-model: folded on demand from stored jobs (bounded window),
///     cached briefly, rebuildable at any time. Verdicts are the only authored state.
/// </summary>
public sealed class EventLedgerService(
    IJobRepository jobRepository,
    IEventVerdictRepository verdictRepository,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<EventLedgerService> logger,
    IOptions<LedgerReadModelOptions>? readModelOptions = null,
    ILedgerBucketRepository? ledgerBucketRepository = null) : IEventLedgerService
{
    private readonly LedgerReadModelOptions _readModelOptions =
        readModelOptions?.Value ?? new();
    private readonly ILedgerBucketRepository? _ledgerBucketRepository = ledgerBucketRepository;
    /// <summary>Maximum number of projections read per legacy fold page.</summary>
    private const int FoldPageSize = 500;
    /// <summary>Fold window: how far back the ledger reads.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan VerdictCacheTtl = TimeSpan.FromMinutes(30);

    public async Task<Result<LedgerDto>> GetLedgerAsync(
        string market, CancellationToken cancellationToken = default)
    {
        var jobsResult = await GetMarketJobsAsync(market, cancellationToken);
        if (jobsResult.IsFailure)
            return Result<LedgerDto>.Failure(jobsResult.Error);

        var verdictsResult = await GetVerdictsCachedAsync(market, cancellationToken);
        if (verdictsResult.IsFailure)
            return Result<LedgerDto>.Failure(verdictsResult.Error);

        var now = timeProvider.GetUtcNow();
        var events = EventLedgerFold.Fold(jobsResult.Value, now);
        var verdictsByKey = ToVerdictMap(verdictsResult.Value);
        var feedHealth = EventLedgerFold.FoldFeedHealth(jobsResult.Value)
            .Select(f => new FeedHealthDto(f.SourceId, f.LastContributedUtc, f.JobsWithSource, f.JobsInWindow))
            .ToList();

        var eventDtos = events.Select(e =>
        {
            var verdict = verdictsByKey.GetValueOrDefault(e.Key);
            return new LedgerEventDto(
                e.Key, e.RoadName, e.Headline, e.Detail, e.Sources,
                e.FirstSeenUtc, e.LastSeenUtc, e.StartTimeUtc, e.EndTimeUtc, e.RoadClosed,
                e.Lane.ToString(), e.SightingCount,
                e.Observations.Select(o => new LedgerObservationDto(o.AtUtc, o.JobId, o.Summary)).ToList(),
                e.Airings.Select(a => new LedgerAiringDto(a.JobId, a.AtUtc, a.Sentence)).ToList(),
                verdict?.Verdict.ToString(), verdict?.Notes, verdict?.By, verdict?.AtUtc,
                e.Latest.ToDto(), e.OfficialEvidences);
        }).ToList();

        return Result<LedgerDto>.Success(new LedgerDto(market, eventDtos, feedHealth));
    }

    public async Task<Result> SetVerdictAsync(
        string market, string eventKey, string? verdict, string? notes, string by,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
            return Result.Failure("Event key is required.");

        if (string.IsNullOrWhiteSpace(verdict))
        {
            var cleared = await verdictRepository.ClearVerdictAsync(market, eventKey, cancellationToken);
            if (cleared.IsSuccess)
            {
                InvalidateMarketCaches(market);
                logger.LogInformation("Verdict cleared for {EventKey} in {Market} by {By}", eventKey, market, by);
            }
            return cleared;
        }

        if (!Enum.TryParse<VerdictKind>(verdict, ignoreCase: true, out var kind))
            return Result.Failure($"Unknown verdict '{verdict}'. Expected Omitted, or null to clear.");

        var result = await verdictRepository.SetVerdictAsync(
            new EventVerdict(market, eventKey, kind, notes, by, timeProvider.GetUtcNow()),
            cancellationToken);
        if (result.IsSuccess)
        {
            InvalidateMarketCaches(market);
            logger.LogInformation(
                "Verdict {Verdict} recorded for {EventKey} in {Market} by {By}", kind, eventKey, market, by);
        }
        return result;
    }

    public async Task<Result<JobDeltaDto>> GetJobDeltaAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var jobResult = await jobRepository.GetJobLedgerProjectionAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result<JobDeltaDto>.Failure(jobResult.Error);
        var job = jobResult.Value.ToJob();

        var previousResult = await FindPredecessorAsync(job, cancellationToken);
        if (previousResult.IsFailure)
            return Result<JobDeltaDto>.Failure(previousResult.Error);
        Job? previous = previousResult.Value.Match<Job?>(value => value, () => null);

        var deltas = EventLedgerFold.ComputeDelta(job, previous);

        return Result<JobDeltaDto>.Success(new JobDeltaDto(
            job.JobId,
            previous?.JobId,
            previous is null ? null : new DateTimeOffset(previous.CreatedAt, TimeSpan.Zero),
            deltas.Select(d => new EventDeltaDto(
                d.Key, d.RoadName, d.Headline, d.Status.ToString(), d.ChangeSummary)).ToList()));
    }

    public async Task<Result<IReadOnlyList<SpotCheckReviewDto>>> GetSpotChecksAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await verdictRepository.GetSpotChecksAsync(cancellationToken);
        if (result.IsFailure)
            return Result<IReadOnlyList<SpotCheckReviewDto>>.Failure(result.Error);
        return Result<IReadOnlyList<SpotCheckReviewDto>>.Success(
            result.Value.Select(s => new SpotCheckReviewDto(s.JobId, s.By, s.AtUtc)).ToList());
    }

    public async Task<Result<VersionedLibraryListPage<SpotCheckReviewDto>>> GetSpotChecksPageAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var result = await verdictRepository.GetSpotChecksPageAsync(query, cancellationToken);
        if (result.IsFailure)
            return Result<VersionedLibraryListPage<SpotCheckReviewDto>>.Failure(result.Error);

        return Result<VersionedLibraryListPage<SpotCheckReviewDto>>.Success(
            new VersionedLibraryListPage<SpotCheckReviewDto>(
                result.Value.Items
                    .Select(s => new SpotCheckReviewDto(s.JobId, s.By, s.AtUtc))
                    .ToList(),
                result.Value.Total,
                result.Value.NextCursor));
    }

    public Task<Result> MarkSpotCheckAsync(
        string jobId, string by, CancellationToken cancellationToken = default)
        => verdictRepository.MarkSpotCheckAsync(
            new SpotCheckReview(jobId, by, timeProvider.GetUtcNow()), cancellationToken);

    private async Task<Result<Option<Job>>> FindPredecessorAsync(
        Job current, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? predecessorId = current.PreviousJobId.Match<string?>(value => value, () => null);
        for (int depth = 0; depth < 64 && !string.IsNullOrWhiteSpace(predecessorId); depth++)
        {
            if (!visited.Add(predecessorId))
                break;

            var predecessor = await jobRepository.GetJobLedgerProjectionAsync(
                predecessorId, cancellationToken);
            if (predecessor.IsFailure)
                break;

            var candidate = predecessor.Value.ToJob();
            if (candidate.JobId != current.JobId
                && string.Equals(candidate.Market, current.Market, StringComparison.OrdinalIgnoreCase)
                && candidate.CreatedAt < current.CreatedAt
                && candidate.AllIncidents.Count > 0)
                return Result<Option<Job>>.Success(Option<Job>.Some(candidate));
            predecessorId = candidate.PreviousJobId.Match<string?>(value => value, () => null);
        }

        // Rows written before PreviousJobId was introduced (or a partially backfilled chain)
        // retain a bounded, projection-only fallback. It is deliberately outside the pointer
        // chain's hot path.
        var jobsResult = await GetMarketJobsAsync(current.Market, cancellationToken);
        if (jobsResult.IsFailure)
            return Result<Option<Job>>.Failure(jobsResult.Error);
        var fallback = jobsResult.Value
            .Where(j => j.JobId != current.JobId
                && j.CreatedAt < current.CreatedAt
                && j.AllIncidents.Count > 0)
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
            .FirstOrDefault();
        return Result<Option<Job>>.Success(Option.OfNullable(fallback));
    }

    private async Task<Result<IReadOnlyList<Job>>> GetMarketJobsAsync(
        string market, CancellationToken cancellationToken)
    {
        var all = await ListRecentJobsAsync(market, cancellationToken);
        if (all.IsFailure)
            return Result<IReadOnlyList<Job>>.Failure(all.Error);
        return Result<IReadOnlyList<Job>>.Success(all.Value
            .Where(j => string.Equals(j.Market, market, StringComparison.OrdinalIgnoreCase))
            .ToList());
    }

    /// <summary>
    ///     One bounded, briefly-cached market read serves every ledger surface. It prefers the
    ///     scalar Ledger_{market}_{yyyyMMdd} buckets and retains a bounded projection fallback
    ///     while those rows are being backfilled.
    /// </summary>
    private async Task<Result<IReadOnlyList<Job>>> ListRecentJobsAsync(
        string market, CancellationToken cancellationToken)
    {
        string key = JobsCacheKey(market);
        if (cache.TryGetValue<IReadOnlyList<Job>>(key, out var cached) && cached is not null)
            return Result<IReadOnlyList<Job>>.Success(cached);

        if (_ledgerBucketRepository is not null
            && _readModelOptions.ReadEnabled
            && !_readModelOptions.KillSwitch)
        {
            var bucketRead = await ReadLedgerBucketsAsync(market, cancellationToken);
            if (bucketRead.IsSuccess && bucketRead.Value.Count > 0)
            {
                cache.Set(key, bucketRead.Value, CacheTtl);
                return bucketRead;
            }
            if (bucketRead.IsFailure)
                logger.LogWarning(
                    "Incremental ledger read failed for {Market}; using bounded legacy projections: {Error}",
                    market, bucketRead.Error);
        }

        var now = timeProvider.GetUtcNow();
        var cutoff = now.AddDays(-Math.Max(1, _readModelOptions.FoldWindowDays));
        var from = cutoff;
        var to = now;
        string? cursorJobId = null;
        var recent = new List<Job>();

        while (true)
        {
            var pageResult = await jobRepository.ListJobLedgerPageAsync(
                new JobListQuery(from, to, FoldPageSize, CursorJobId: cursorJobId, Market: market),
                cancellationToken);
            if (pageResult.IsFailure)
                return Result<IReadOnlyList<Job>>.Failure(pageResult.Error);

            var page = pageResult.Value;
            recent.AddRange(page
                .Select(projection => projection.ToJob())
                .Where(j => new DateTimeOffset(j.CreatedAt, TimeSpan.Zero) >= cutoff));
            if (page.Count < FoldPageSize)
                break;

            var boundary = page[^1];
            to = new DateTimeOffset(boundary.LastUpdatedAt, TimeSpan.Zero);
            cursorJobId = boundary.JobId;
        }

        cache.Set(key, recent, CacheTtl);
        return Result<IReadOnlyList<Job>>.Success(recent);
    }

    private async Task<Result<IReadOnlyList<Job>>> ReadLedgerBucketsAsync(
        string market, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = now.AddDays(-Math.Max(1, _readModelOptions.FoldWindowDays));
        var to = now;
        var jobs = new List<Job>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var page = await _ledgerBucketRepository!.ReadPageAsync(
                market, from, to, token,
                Math.Clamp(_readModelOptions.PageSize, 1, FoldPageSize), cancellationToken);
            if (page.IsFailure)
                return Result<IReadOnlyList<Job>>.Failure(page.Error);
            foreach (var entry in page.Value.Items)
            {
                if (!seen.Add(entry.JobId))
                    continue;
                var projection = await jobRepository.GetJobLedgerProjectionAsync(
                    entry.JobId, cancellationToken);
                if (projection.IsFailure)
                    return Result<IReadOnlyList<Job>>.Failure(projection.Error);
                jobs.Add(projection.Value.ToJob());
            }
            token = page.Value.ContinuationToken;
        } while (token is not null);
        return Result<IReadOnlyList<Job>>.Success(jobs);
    }

    private async Task<Result<IReadOnlyList<EventVerdict>>> GetVerdictsCachedAsync(
        string market, CancellationToken cancellationToken)
    {
        string key = VerdictCacheKey(market);
        if (cache.TryGetValue<IReadOnlyList<EventVerdict>>(key, out var cached) && cached is not null)
            return Result<IReadOnlyList<EventVerdict>>.Success(cached);

        var result = await verdictRepository.GetVerdictsAsync(market, cancellationToken);
        if (result.IsSuccess)
            cache.Set(key, result.Value, VerdictCacheTtl);
        return result;
    }

    private void InvalidateMarketCaches(string market)
    {
        cache.Remove(JobsCacheKey(market));
        cache.Remove(VerdictCacheKey(market));
    }

    private static string JobsCacheKey(string market) =>
        $"event-ledger:jobs:{market.Trim().ToLowerInvariant()}";

    private static string VerdictCacheKey(string market) =>
        $"event-ledger:verdicts:{market.Trim().ToLowerInvariant()}";

    private static Dictionary<string, EventVerdict> ToVerdictMap(IReadOnlyList<EventVerdict> verdicts)
    {
        var map = new Dictionary<string, EventVerdict>(StringComparer.Ordinal);
        foreach (var v in verdicts)
            map[v.EventKey] = v;
        return map;
    }
}
