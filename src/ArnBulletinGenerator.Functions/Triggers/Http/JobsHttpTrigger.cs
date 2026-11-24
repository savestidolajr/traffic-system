using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Read-side HTTP trigger for jobs: paginated list/detail scoped to the caller's markets,
///     plus a Range-aware audio proxy for playback.
/// </summary>
public sealed class JobsHttpTrigger(
    IJobRepository jobRepository,
    IAudioUrlProvider audioUrlProvider,
    IMarketAccessService marketAccess,
    IVoiceCalibrationService voiceCalibration,
    IOptions<BroadcastOptions> broadcastOptions,
    IOptions<SpeechDurationOptions> speechOptions,
    TimeProvider timeProvider,
    ILogger<JobsHttpTrigger> logger,
    IMemoryCache? cache = null)
{
    private const int DefaultLimit = 100;
    private const int MinLimit = 1;
    private const int MaxLimit = 500;
    private static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(180);
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);
    private static readonly TimeSpan CalibrationCacheTtl = TimeSpan.FromMinutes(1);
    private const int CalibrationCacheSizeLimit = 256;
    private static readonly ConditionalWeakTable<IMemoryCache, CalibrationCacheState> CalibrationCaches = new();
    private readonly CalibrationCacheState _calibrationCache = cache is null
        ? new CalibrationCacheState(CalibrationCacheSizeLimit)
        : CalibrationCaches.GetValue(cache, _ => new CalibrationCacheState(CalibrationCacheSizeLimit));

    // A market-scoped operator's page can come back under-filled — the raw page is capped to
    // Limit *before* filtering to their markets. This caps how many extra look-further-back
    // rounds we'll spend trying to top it back up, so a heavily-filtered operator still gets a
    // bounded number of Table Storage round-trips per request rather than an unbounded scan.
    private const int MaxBackfillRounds = 4;

    [Function("ListJobs")]
    public async Task<HttpResponseData> ListJobs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        logger.LogInformation("Listing jobs");
        var statusesResult = JobMappingExtensions.ParseStatusList(MarketScopeRequest.Query(req, "status"));
        if (statusesResult.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, statusesResult.Error);
        var statuses = statusesResult.Value;

        // Opt-in paging (History): only when the caller explicitly asks for a bounded page via
        // ?limit=. Every other caller (On-air board, dashboards, "earlier today") needs the
        // complete unbounded set for correctness (e.g. a stuck pipeline job must stay visible
        // regardless of age) and keeps the pre-existing behaviour untouched.
        string? limitRaw = MarketScopeRequest.Query(req, "limit");
        if (limitRaw is not null)
            return await ListJobsPagedAsync(req, ctx, statuses, limitRaw);

        var result = statuses.Count > 0
            ? await jobRepository.ListJobSummariesAsync(statuses, ctx.CancellationToken)
            : await jobRepository.ListJobSummariesAsync(ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var allowed = await ResolveAllowedMarketsAsync(req, ctx.CancellationToken);
        var items = FilterToAllowedMarkets(result.Value.ToList(), allowed)
            .Select(j => j.ToSummaryDto())
            .ToList();
        return await HttpResponseHelper.OkJsonAsync(req, new JobsListResponseDto(items, items.Count));
    }

    /// <summary>
    ///     The bounded, newest-first page path: pages backward in time by <c>LastUpdatedAt</c>
    ///     (composite cursor: timestamp + row key, so jobs whose <c>LastUpdatedAt</c> ties with
    ///     the page boundary are not silently skipped — see <see cref="JobListQuery.CursorJobId" />),
    ///     backfilling extra rounds only when market scoping is actually thinning the page out.
    /// </summary>
    private async Task<HttpResponseData> ListJobsPagedAsync(
        HttpRequestData req, FunctionContext ctx, IReadOnlyList<JobStatus> statuses, string limitRaw)
    {
        var toCursor = ParseCursor(MarketScopeRequest.Query(req, "to"), timeProvider.GetUtcNow());
        var from = MarketScopeRequest.ParseDate(MarketScopeRequest.Query(req, "from"))
            ?? toCursor.Timestamp - DefaultLookback;

        if (toCursor.Timestamp < from)
            return await HttpResponseHelper.BadRequestAsync(req, "to must not be before from");
        if (toCursor.Timestamp - from > MaxRange)
            return await HttpResponseHelper.BadRequestAsync(req, "date range too wide — max 366 days");

        int limit = int.TryParse(limitRaw, out int parsedLimit) ? parsedLimit : DefaultLimit;
        limit = Math.Clamp(limit, MinLimit, MaxLimit);

        var allowed = await ResolveAllowedMarketsAsync(req, ctx.CancellationToken);
        var statusFilter = statuses.Count > 0 ? statuses : null;

        var collected = new List<JobListItem>();
        var cursor = toCursor;
        bool exhausted;
        int rounds = 0;
        do
        {
            var pageResult = await jobRepository.ListJobSummariesPageAsync(
                new JobListQuery(
                    from,
                    cursor.Timestamp,
                    limit,
                    statusFilter,
                    cursor.JobId,
                    SingleAllowedMarket(allowed)),
                ctx.CancellationToken);
            if (pageResult.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, pageResult.Error);

            var page = pageResult.Value;
            exhausted = page.Count < limit; // fewer than asked for ⇒ nothing older exists in [from, cursor)
            if (page.Count == 0) break;

            collected.AddRange(FilterToAllowedMarkets(page, allowed));
            // The next boundary is the raw page's OLDEST item in the stable total order
            // (LastUpdatedAt desc, row key desc) — the row the composite cursor pins, so the
            // next page continues strictly after it and cannot skip tied rows.
            var boundary = page
                .OrderByDescending(j => j.LastUpdatedAt)
                .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
                .Last();
            cursor = new JobCursor(new DateTimeOffset(boundary.LastUpdatedAt, TimeSpan.Zero), boundary.JobId);
            rounds++;
        }
        while (!exhausted && collected.Count < limit && allowed.HasValue && rounds < MaxBackfillRounds);

        var final = collected
            .OrderByDescending(j => j.LastUpdatedAt)
            .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
            .Take(limit)
            .Select(j => j.ToSummaryDto())
            .ToList();
        return await HttpResponseHelper.OkJsonAsync(req, new JobsListResponseDto(final, collected.Count, !exhausted));
    }

    /// <summary>
    ///     Parses the optional <c>to</c> cursor into a page boundary. Backward-compatible: a bare
    ///     ISO-8601 timestamp is a timestamp-only cursor (legacy clients keep working); the
    ///     composite form <c>{ISO timestamp}|{row key}</c> additionally pins the boundary to a
    ///     specific row so jobs sharing the boundary timestamp are not silently skipped by the
    ///     next page's strict <c>lt</c> filter (see <see cref="JobListQuery.CursorJobId" />).
    ///     Anything unparseable falls back to <paramref name="fallback" /> (the pre-existing
    ///     lenient behaviour for junk values).
    /// </summary>
    private static JobCursor ParseCursor(string? raw, DateTimeOffset fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new JobCursor(fallback, null);

        int separator = raw.IndexOf('|');
        if (separator >= 0)
        {
            string timestampPart = raw[..separator];
            string jobIdPart = raw[(separator + 1)..];
            if (DateTimeOffset.TryParse(timestampPart, out var compositeTimestamp)
                && !string.IsNullOrWhiteSpace(jobIdPart))
                return new JobCursor(compositeTimestamp, jobIdPart);
        }

        return MarketScopeRequest.ParseDate(raw) is { } parsed
            ? new JobCursor(parsed, null)
            : new JobCursor(fallback, null);
    }

    /// <summary>A page boundary in the stable total order: timestamp, plus the row key tiebreaker.</summary>
    private sealed record JobCursor(DateTimeOffset Timestamp, string? JobId);

    private async Task<Option<IReadOnlyList<string>>> ResolveAllowedMarketsAsync(
        HttpRequestData req, CancellationToken cancellationToken)
    {
        var principal = ClientPrincipal.Parse(req);
        if (principal is null) return Option<IReadOnlyList<string>>.None;
        return await marketAccess.GetAllowedMarketsAsync(principal.ObjectId, principal.IsAdmin, cancellationToken);
    }

    private static string? SingleAllowedMarket(Option<IReadOnlyList<string>> allowed) =>
        allowed.HasValue && allowed.Value.Count == 1
            ? allowed.Value[0]
            : null;

    /// <summary>Market scoping (WS2): <see cref="Option{T}.None" /> is unrestricted; <see cref="Option{T}.Some(T)" /> contains only non-empty assigned markets.</summary>
    private static IReadOnlyList<Job> FilterToAllowedMarkets(
        IReadOnlyList<Job> jobs, Option<IReadOnlyList<string>> allowed)
    {
        if (allowed.IsNone) return jobs;
        var markets = new HashSet<string>(allowed.Value.Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
        return jobs.Where(j => markets.Contains((j.Market ?? j.Station).Trim())).ToList();
    }

    private static IReadOnlyList<JobListItem> FilterToAllowedMarkets(
        IReadOnlyList<JobListItem> items, Option<IReadOnlyList<string>> allowed)
    {
        if (allowed.IsNone) return items;
        var markets = new HashSet<string>(allowed.Value.Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
        return items.Where(item => markets.Contains((item.Market ?? item.Station).Trim())).ToList();
    }

    [Function("GetJob")]
    public async Task<HttpResponseData> GetJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        logger.LogInformation("Getting job {JobId}", jobId);
        var loaded = await LoadAuthorizedJobAsync(req, jobId, ctx);
        if (loaded.Response is { } response)
            return response;

        var job = loaded.Job!;
        var audioUrl = await ResolveAudioUrlAsync(job, ctx.CancellationToken);
        var takeAudioUrl = await ResolveStoredAudioUrlAsync(
            job.SupersededTake.Match(t => t.AudioUrl, () => Option<string>.None), ctx.CancellationToken);
        var dto = job.ToDetailDto(audioUrl.GetValueOr(string.Empty), takeAudioUrl.GetValueOr(string.Empty));
        if (IsEditable(job.Status) && job.ScriptText.HasValue && job.VoiceConfig.HasValue)
            dto = dto with { EditingGuidance = await BuildEditingGuidanceAsync(job, ctx.CancellationToken) };
        return await HttpResponseHelper.OkJsonAsync<object>(req, dto);
    }

    /// <summary>
    ///     Resolves the cheap JobIndex routing row and authorizes its market before the aggregate
    ///     row (and its compressed JSON snapshots) is materialized. The loaded-job guard is only
    ///     needed when a stale index points at an entity whose market changed; it validates the
    ///     already-loaded aggregate and never performs a second repository read.
    /// </summary>
    private async Task<(Job? Job, HttpResponseData? Response)> LoadAuthorizedJobAsync(
        HttpRequestData req, string jobId, FunctionContext ctx)
    {
        var indexResult = await jobRepository.GetJobIndexAsync(jobId, ctx.CancellationToken);
        if (indexResult.IsFailure)
            return (null, await HttpResponseHelper.NotFoundAsync(req, indexResult.Error));

        var index = indexResult.Value;
        if (await marketAccess.EnsureMarketAccessAsync(
                req, index.Market, logger, ctx.CancellationToken) is { } forbidden)
            return (null, forbidden);

        var result = await jobRepository.GetJobAsync(index, ctx.CancellationToken);
        if (result.IsFailure)
            return (null, await HttpResponseHelper.NotFoundAsync(req, result.Error));

        var job = result.Value;
        // Normally the index and aggregate carry the same immutable market. Keep the loaded-job
        // guard as a stale-pointer defense without paying a second access lookup on every request.
        if (!string.Equals(job.Market, index.Market, StringComparison.OrdinalIgnoreCase) &&
            await marketAccess.EnsureMarketAccessAsync(
                req, job.Market, logger, ctx.CancellationToken) is { } staleIndexForbidden)
            return (null, staleIndexForbidden);

        return (job, null);
    }

    /// <summary>
    ///     Streams the job's CURRENT audio. The optional trailing <c>render</c> segment is a cache
    ///     key, never a selector: it carries the blob filename of the render the DTO advertised, so
    ///     a re-voice changes the URL the console binds to its &lt;audio&gt; element. Without it the
    ///     path is constant across re-voices, the element never reloads, and the operator keeps
    ///     hearing the previous take — which was observed in the review console.
    /// </summary>
    [Function("GetJobAudio")]
    public async Task<HttpResponseData> GetJobAudio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}/audio/{render?}")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        var loaded = await LoadAuthorizedJobAsync(req, jobId, ctx);
        if (loaded.Response is { } authorizationResponse)
            return authorizationResponse;

        var job = loaded.Job!;
        if (job.AudioUrl.IsNone)
            return await HttpResponseHelper.NotFoundAsync(req, "Job has no audio");

        // External audio (e.g. a filler cart hosted outside our container) cannot be streamed
        // from our storage — redirect the client to the source.
        string rawUrl = job.AudioUrl.Value;
        if (audioUrlProvider.ResolveOwnBlobName(rawUrl).IsNone)
        {
            var redirect = req.CreateResponse(HttpStatusCode.Redirect);
            redirect.Headers.Add("Location", rawUrl);
            return redirect;
        }

        var range = ParseRangeHeader(
            req.Headers.TryGetValues("Range", out var rangeValues) ? rangeValues.FirstOrDefault() : null);

        var streamResult = await audioUrlProvider.OpenAudioStreamAsync(rawUrl, range, ctx.CancellationToken);
        if (streamResult.IsFailure)
        {
            if (streamResult.Error.Contains("not satisfiable", StringComparison.OrdinalIgnoreCase))
            {
                var unsatisfiable = req.CreateResponse(HttpStatusCode.RequestedRangeNotSatisfiable);
                return unsatisfiable;
            }
            logger.LogWarning("Failed to open audio stream for job {JobId}: {Error}", jobId, streamResult.Error);
            return await HttpResponseHelper.NotFoundAsync(req, streamResult.Error);
        }

        await using var handle = streamResult.Value;
        var response = req.CreateResponse(handle.IsPartial ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
        response.Headers.Add("Content-Type", handle.ContentType);
        response.Headers.Add("Accept-Ranges", "bytes");
        if (handle.IsPartial)
            response.Headers.Add(
                "Content-Range",
                $"bytes {handle.RangeStart}-{handle.RangeStart + handle.ContentLength - 1}/{handle.TotalLength}");
        await handle.Content.CopyToAsync(response.Body, ctx.CancellationToken);
        return response;
    }

    /// <summary>
    ///     Parses a single-range <c>Range: bytes=a-b</c> header. Open-ended requests
    ///     ("bytes=200-") are honoured; suffix ("bytes=-500"), multi-range, and malformed
    ///     headers fall back to null (full-body 200) rather than erroring.
    /// </summary>
    internal static AudioByteRange? ParseRangeHeader(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return null;
        const string prefix = "bytes=";
        if (!headerValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var spec = headerValue[prefix.Length..].Trim();
        if (spec.Contains(',')) return null; // multi-range unsupported

        int dash = spec.IndexOf('-');
        if (dash <= 0) return null; // suffix form or malformed

        if (!long.TryParse(spec[..dash], out long from) || from < 0) return null;

        var toPart = spec[(dash + 1)..];
        if (toPart.Length == 0) return new AudioByteRange(from, null);

        if (!long.TryParse(toPart, out long to) || to < from) return null;
        return new AudioByteRange(from, to);
    }

    // Mirrors the web EDITABLE_STATES: only these statuses expose the verbatim re-voice editor.
    private static bool IsEditable(JobStatus s) =>
        s is JobStatus.WaitingApproval or JobStatus.AudioReady or JobStatus.AwaitingCuration or JobStatus.Failed;

    private async Task<BulletinEditingGuidanceDto> BuildEditingGuidanceAsync(Job job, CancellationToken ct)
    {
        var vc = job.VoiceConfig.Value;
        var settings = vc.Settings.GetValueOr(VoiceSettings.Default);
        var metrics = await GetCalibrationMetricsCachedAsync(vc, settings, ct);
        double wpm = metrics.WordsPerMinute > 0 ? metrics.WordsPerMinute : broadcastOptions.Value.WordsPerMinute;
        var band = BulletinEditingBand.Compute(
            wpm, job.TargetDurationSeconds, settings.Speed,
            speechOptions.Value.MaxSlowDownFactor, speechOptions.Value.MaxSpeedUpFactor, vc.PaceBoostWpm,
            speechOptions.Value.AbsolutePaceBoostSpeedUpCeiling,
            speechOptions.Value.AbsolutePaceBoostSlowDownCeiling);
        return new BulletinEditingGuidanceDto(
            band.TargetWordCount, band.MinWordCount, band.MaxWordCount, Math.Round(wpm, 1), job.TargetDurationSeconds);
    }

    private async Task<CalibrationMetrics> GetCalibrationMetricsCachedAsync(
        JobVoiceConfig voiceConfig, VoiceSettings settings, CancellationToken ct)
    {
        string key = settings.GetCalibrationConfigurationKey(voiceConfig.VoiceId, voiceConfig.Model);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (_calibrationCache.TryGet(key, now, out var cached))
            return cached;

        var flight = _calibrationCache.InFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<CalibrationMetrics>>(
                () => voiceCalibration.GetCalibrationMetricsAsync(
                    voiceConfig.VoiceId, Option<VoiceSettings>.Some(settings), false, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var metrics = await flight.Value;
            _calibrationCache.Set(key, metrics, now + CalibrationCacheTtl);
            return metrics;
        }
        finally
        {
            _calibrationCache.InFlight.TryRemove(
                new KeyValuePair<string, Lazy<Task<CalibrationMetrics>>>(key, flight));
        }
    }

    private async Task<Option<string>> ResolveAudioUrlAsync(Job job, CancellationToken ct)
    {
        if (job.AudioUrl.IsNone) return Option<string>.None;
        string raw = job.AudioUrl.Value;

        // Audio proxy mode: own-container audio rides the authenticated server-side proxy
        // (relative path; the SWA linked backend forwards it with the auth cookie, so it works
        // off-network when the storage data plane is private). External absolute URLs (filler
        // cart) still pass through unchanged.
        //
        // The blob filename is appended as a render token so the path CHANGES when a re-voice
        // writes a new take. The console's pinMediaSrc only rebinds <audio src> on a path change
        // (it deliberately ignores SAS-token rotation), so a constant proxy path meant the player
        // silently kept the first render forever.
        if (broadcastOptions.Value.UseAudioProxy)
            return Option<string>.Some(
                audioUrlProvider.ResolveOwnBlobName(raw).Match(
                    blob => $"jobs/{job.JobId}/audio/{RenderToken(blob)}",
                    () => raw));

        var result = await audioUrlProvider.GetAudioUrlAsync(raw, ct);
        if (result.IsFailure)
        {
            logger.LogWarning("Failed to generate SAS URL for {FileName}: {Error}", raw, result.Error);
            return Option<string>.None;
        }

        return result.Value;
    }

    /// <summary>
    ///     Resolves an arbitrary stored audio reference — one that is NOT the job's current audio,
    ///     i.e. the bulletin a fallback superseded. Deliberately does NOT take the proxy path:
    ///     <c>jobs/{jobId}/audio/{render?}</c> always streams <see cref="Job.AudioUrl" /> and
    ///     treats the trailing segment as a cache key, never a selector, so routing the take
    ///     through it would serve the CART audio under the take's label. Fails soft to None (an
    ///     empty URL downstream), exactly like <see cref="ResolveAudioUrlAsync" />; the take's
    ///     script still renders without a player.
    /// </summary>
    private async Task<Option<string>> ResolveStoredAudioUrlAsync(Option<string> stored, CancellationToken ct)
    {
        if (stored.IsNone || string.IsNullOrWhiteSpace(stored.Value)) return Option<string>.None;
        string raw = stored.Value;

        var result = await audioUrlProvider.GetAudioUrlAsync(raw, ct);
        if (result.IsFailure)
        {
            logger.LogWarning("Failed to generate SAS URL for superseded take {FileName}: {Error}",
                raw, result.Error);
            return Option<string>.None;
        }

        return result.Value;
    }

    /// <summary>
    ///     A route-safe token identifying one render. Blob names are flat filenames
    ///     (<c>broadcast_{ts}_{jobId}.wav</c>), but a nested name would still yield a single
    ///     segment: only the leaf is kept, then escaped.
    /// </summary>
    internal static string RenderToken(string blobName)
    {
        int slash = blobName.LastIndexOf('/');
        string leaf = slash >= 0 ? blobName[(slash + 1)..] : blobName;
        return Uri.EscapeDataString(leaf);
    }
    /// <summary>Bounded, TTL-based calibration metadata cache with per-key request coalescing.</summary>
    private sealed class CalibrationCacheState(int sizeLimit)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, TimedValue<CalibrationMetrics>> _values = new(StringComparer.Ordinal);
        private readonly int _sizeLimit = Math.Max(1, sizeLimit);
        private long _sequence;

        internal ConcurrentDictionary<string, Lazy<Task<CalibrationMetrics>>> InFlight { get; } = new(StringComparer.Ordinal);

        internal bool TryGet(string key, DateTimeOffset now, out CalibrationMetrics value)
        {
            lock (_gate)
            {
                if (_values.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
                {
                    _values[key] = entry with { Sequence = ++_sequence };
                    value = entry.Value;
                    return true;
                }

                _values.Remove(key);
                value = null!;
                return false;
            }
        }

        internal void Set(string key, CalibrationMetrics value, DateTimeOffset expiresAt)
        {
            lock (_gate)
            {
                _values[key] = new TimedValue<CalibrationMetrics>(value, expiresAt, ++_sequence);
                while (_values.Count > _sizeLimit)
                {
                    string? oldest = _values.MinBy(pair => pair.Value.Sequence).Key;
                    if (oldest is null)
                        break;
                    _values.Remove(oldest);
                }
            }
        }

        private readonly record struct TimedValue<T>(T Value, DateTimeOffset ExpiresAt, long Sequence);
    }
}
