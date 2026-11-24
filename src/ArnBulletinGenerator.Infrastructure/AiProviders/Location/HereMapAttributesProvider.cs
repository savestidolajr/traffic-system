using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;
///     Reads HERE's Map Attributes API for the specific segments an incident references, giving the
///     road identity that belongs to the incident's OWN geometry rather than whatever is near its
///     coordinates.
///     <para>
///         This is the only HERE source that can settle a road name: at the Sydney closure of
///         2026-08-13 it returns <c>NAME: "Wallgrove Rd"</c>, <c>FUNCTIONAL_CLASS: "5"</c> for segment
///         909906003 while reverse geocoding returned "Westlink M7" — the motorway 20 m away.
///     </para>
///     Fail-soft by contract: any failure returns a failed <see cref="Result{T}" /> and callers keep
///     the name they already had. Segment references must carry their tile (HERE's full
///     <c>hrn:…::&lt;tile&gt;:here:cm:segment:&lt;id&gt;#…</c> form); the Routing-API form has the tile
///     stripped and the API rejects it.
/// </summary>
public sealed class HereMapAttributesProvider : IMapAttributesProvider
{
    private readonly HttpClient _httpClient;
    private readonly HereOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HereMapAttributesProvider> _logger;
    private readonly IMapAttributesCacheRepository? _cacheRepository;
    private readonly MapAttributesCacheOptions _cacheOptions;
    private static readonly ConditionalWeakTable<IMemoryCache, MapAttributesL1State> L1ByCache = new();
    private readonly MapAttributesL1State _l1;

    public HereMapAttributesProvider(
        HttpClient httpClient,
        IOptions<HereOptions> options,
        IMemoryCache cache,
        ILogger<HereMapAttributesProvider> logger,
        IMapAttributesCacheRepository? cacheRepository = null,
        IOptions<MapAttributesCacheOptions>? cacheOptions = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _cacheRepository = cacheRepository;
        _cacheOptions = cacheOptions?.Value ?? new MapAttributesCacheOptions();
        _l1 = L1ByCache.GetValue(
            _cache, _ => new MapAttributesL1State(_cache, _cacheOptions));
    }

    /// <summary>Attributes requested: the road's name plus its class/ramp flags.</summary>
    private const string Attributes = "LINK_ATTRIBUTE_FCN(*),ROAD_GEOM_FCN(*)";

    /// <summary>
    ///     Segments per request. HERE separates them with ';' (a comma is rejected outright) and caps a
    ///     request at 64 references — measured against the live API: 64 refs → 200 OK, 100 refs → 400
    ///     "Maximum of 64 segment references can be requested". Requests are chunked to that ceiling and
    ///     run concurrently.
    /// </summary>
    private const int BatchSize = 64;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<string, SegmentRoad>>> GetSegmentRoadsAsync(
        IEnumerable<string> segmentRefs, CancellationToken cancellationToken = default)
    {
        var requestedRefs = segmentRefs
            .Where(r => !string.IsNullOrWhiteSpace(r) && r.Contains("here:cm:segment:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var refs = requestedRefs
            .DistinctBy(HereSegmentReference.CacheIdentity, StringComparer.Ordinal)
            .ToList();

        if (refs.Count == 0)
            return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad>());

        var resolved = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal);
        var uncachedRefs = new List<string>();

        foreach (string reference in refs)
        {
            string cacheKey = CacheKey(reference);
            if (_cache.TryGetValue(cacheKey, out SegmentRoad road))
            {
                resolved[reference] = road;
                _l1.Touch(cacheKey);
            }
            else if (!_cache.TryGetValue(NegativeCacheKey(reference), out _))
            {
                uncachedRefs.Add(reference);
            }
        }

        if (uncachedRefs.Count == 0)
            return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                ExpandAliases(requestedRefs, resolved));

        int l2Resolved = 0;
        if (_cacheRepository is not null)
        {
            try
            {
                var persisted = await _cacheRepository.GetBatchAsync(uncachedRefs, cancellationToken);
                foreach ((string reference, var road) in persisted)
                {
                    if (!resolved.ContainsKey(reference))
                    {
                        resolved[reference] = road;
                        l2Resolved++;
                    }

                    CachePositive(reference, road);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "[MapAttributes] Persistent cache read failed; falling back to HERE for {Count} references",
                    uncachedRefs.Count);
            }
        }

        var apiRefs = uncachedRefs
            .Where(reference => !resolved.ContainsKey(reference))
            .ToList();
        if (apiRefs.Count == 0)
        {
            _logger.LogInformation(
                "[MapAttributes] Resolved {Resolved} of {Requested} segments ({Cached} from cache)",
                resolved.Count, refs.Count, refs.Count - apiRefs.Count);
            return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                ExpandAliases(requestedRefs, resolved));
        }
        var batches = apiRefs.Chunk(BatchSize).Select(batch => batch.ToList()).ToList();
        var results = await Task.WhenAll(batches.Select(async batch =>
            (Refs: batch, Result: await FetchBatchCoalescedAsync(batch, cancellationToken))));

        int failed = 0;
        var successfulRefs = new HashSet<string>(StringComparer.Ordinal);
        var fetched = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal);
        foreach (var batch in results)
        {
            if (batch.Result.IsFailure)
            {
                failed++;
                continue;
            }

            foreach (string reference in batch.Refs)
                successfulRefs.Add(reference);

            foreach ((string key, var road) in batch.Result.Value)
            {
                resolved.TryAdd(key, road);
                fetched[key] = road;
                CachePositive(key, road);
            }
        }

        // A successful API response that omits a requested segment confirms absence. Failed batches
        // are deliberately not negatively cached, so a transient HERE outage can recover next time.
        foreach (string reference in successfulRefs.Where(reference => !resolved.ContainsKey(reference)))
            CacheNegative(reference);

        if (_cacheRepository is not null && fetched.Count > 0)
        {
            try
            {
                await _cacheRepository.SaveBatchAsync(fetched, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[MapAttributes] Persistent cache write failed (ignored)");
            }
        }

        if (failed == batches.Count && resolved.Count == 0)
            return Result<IReadOnlyDictionary<string, SegmentRoad>>.Failure(
                "Every map-attributes request failed");

        if (failed > 0)
            _logger.LogWarning("[MapAttributes] {Failed} of {Total} segment batches failed", failed, batches.Count);

        _logger.LogInformation(
            "[MapAttributes] Resolved {Resolved} of {Requested} segments ({Cached} from cache)",
            resolved.Count, refs.Count, refs.Count - uncachedRefs.Count + l2Resolved);
        return Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
            ExpandAliases(requestedRefs, resolved));
    }
    private async Task<Result<Dictionary<string, SegmentRoad>>> FetchBatchAsync(
        List<string> refs, CancellationToken cancellationToken)
    {
        // HERE requires the ';' separator and rejects an encoded one, so the query is composed directly
        // rather than through a form encoder. The '#' inside a segment reference MUST be
        // percent-encoded: left literal it starts a URI fragment, so everything after it — including
        // apiKey — is dropped and the call fails 401 "No credentials found" (verified against the live
        // API, 2026-08-13: literal '#' → 401, '%23' → 200 carrying the segment's name).
        string refsJoined = string.Join(';', refs).Replace("#", "%23", StringComparison.Ordinal);
        string url = $"{_options.MapAttributesBaseUrl}/segments" +
                     $"?attributes={Attributes}" +
                     $"&segmentRefs={refsJoined}" +
                     $"&apiKey={_options.ApiKey}";

        using var permit = await HereRequestBudget.AcquireAsync(
            HereRequestKind.Map, cancellationToken);
        var result = await HereApiHelper.ExecuteGetAsync<HereMapAttributesResponse>(
            _httpClient, url, _logger, "MapAttributes", cancellationToken);
        if (result.IsFailure)
            return Result<Dictionary<string, SegmentRoad>>.Failure(result.Error);

        var byId = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal);
        foreach (var segment in result.Value.Segments ?? [])
        {
            string? id = HereSegmentReference.SegmentId(segment.SegmentRef);
            if (id is null)
                continue;

            var geom = segment.Attributes?.RoadGeometry?.FirstOrDefault();
            var link = segment.Attributes?.LinkAttributes?.FirstOrDefault();
            var road = new SegmentRoad(
                StripRouteCode(geom?.Name),
                int.TryParse(link?.FunctionalClass, out int fc) ? fc : 5,
                string.Equals(link?.Ramp, "Y", StringComparison.OrdinalIgnoreCase));
            byId[id] = road;
        }

        // Key the result by the caller's own reference: HERE echoes segments back in its compact
        // form with its own offsets, which never string-matches what we asked for.
        var resolved = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal);
        foreach (string reference in refs)
        {
            string? id = HereSegmentReference.SegmentId(reference);
            if (id is not null && byId.TryGetValue(id, out var road))
                resolved[reference] = road;
        }

        return Result<Dictionary<string, SegmentRoad>>.Success(resolved);
    }
    private async Task<Result<Dictionary<string, SegmentRoad>>> FetchBatchCoalescedAsync(
        List<string> refs, CancellationToken cancellationToken)
    {
        string key = string.Join('\u001F', refs.OrderBy(reference => reference, StringComparer.Ordinal));
        var pending = _l1.GetOrAddFetch(key, () => FetchBatchAsync(refs, cancellationToken));
        try
        {
            return await pending.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            _l1.RemoveFetch(key, pending);
        }
    }

    private static IReadOnlyDictionary<string, SegmentRoad> ExpandAliases(
        IReadOnlyList<string> requestedRefs,
        IReadOnlyDictionary<string, SegmentRoad> resolved)
    {
        if (requestedRefs.Count == resolved.Count)
            return resolved;

        var roadsBySegment = resolved.ToDictionary(
            pair => HereSegmentReference.CacheIdentity(pair.Key),
            pair => pair.Value,
            StringComparer.Ordinal);
        var expanded = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal);
        foreach (string reference in requestedRefs)
        {
            if (roadsBySegment.TryGetValue(HereSegmentReference.CacheIdentity(reference), out var road))
                expanded[reference] = road;
        }
        return expanded;
    }

    /// <summary>
    ///     The name to voice for a segment. Delegates to <see cref="RoadNameFormatter.PreferredRoadName" />
    ///     so the route-code rule has one home: it drops the code HERE prefixes onto many names
    ///     ("A28 / Hume Hwy" → "Hume Hwy"; 31 of 173 live segment names carried one, and voicing it
    ///     verbatim would read as "A28 slash Hume Highway"), but keeps it when ARN's curated
    ///     designation list says that code is the name listeners use ("M1 / Pacific Mwy" → "M1").
    ///     This copy previously dropped the code unconditionally, which discarded the exact token
    ///     the designation list matches on.
    /// </summary>
    private static string? StripRouteCode(string? name)
    {
        string preferred = RoadNameFormatter.PreferredRoadName(name);
        return string.IsNullOrWhiteSpace(preferred) ? null : preferred;
    }

    private void CachePositive(string reference, SegmentRoad road)
    {
        int evicted = _l1.Set(
            CacheKey(reference),
            road,
            TimeSpan.FromHours(Math.Max(1, _cacheOptions.PositiveTtlHours)),
            ApproximateSize(reference, road));
        if (evicted > 0)
            _logger.LogInformation("[MapAttributes] L1 evicted {Count} entries to stay within bounds", evicted);
    }

    private void CacheNegative(string reference)
    {
        int evicted = _l1.Set(
            NegativeCacheKey(reference),
            true,
            TimeSpan.FromMinutes(Math.Max(1, _cacheOptions.NegativeTtlMinutes)),
            Math.Max(1, reference.Length + 16));
        if (evicted > 0)
            _logger.LogInformation("[MapAttributes] L1 evicted {Count} entries to stay within bounds", evicted);
    }

    private static int ApproximateSize(string reference, SegmentRoad road) =>
        Math.Max(1, reference.Length + (road.Name?.Length ?? 0) +
            (road.TravelDirection?.Length ?? 0) + 32);

    private static string CacheKey(string reference) =>
        $"MapAttr:{HereSegmentReference.CacheIdentity(reference)}";
    private static string NegativeCacheKey(string reference) =>
        $"MapAttr:negative:{HereSegmentReference.CacheIdentity(reference)}";


    private sealed class MapAttributesL1State
    {
        private readonly IMemoryCache _cache;
        private readonly int _entryLimit;
        private readonly long _byteLimit;
        private readonly object _gate = new();
        private readonly Dictionary<string, L1Entry> _entries = new(StringComparer.Ordinal);
        private readonly SortedSet<L1Entry> _order = new(L1EntryComparer.Instance);
        private long _bytes;
        private long _sequence;
        private readonly ConcurrentDictionary<string, Lazy<Task<Result<Dictionary<string, SegmentRoad>>>>> _inflight =
            new(StringComparer.Ordinal);

        internal MapAttributesL1State(IMemoryCache cache, MapAttributesCacheOptions options)
        {
            _cache = cache;
            _entryLimit = Math.Max(1, options.L1SizeLimit);
            _byteLimit = Math.Max(1, options.L1ByteLimit);
        }

        internal void Touch(string key)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(key, out var entry))
                    return;
                _order.Remove(entry);
                entry = entry with { Sequence = ++_sequence };
                _entries[key] = entry;
                _order.Add(entry);
            }
        }

        internal int Set<T>(string key, T value, TimeSpan ttl, int bytes)
        {
            lock (_gate)
            {
                _cache.Set(key, value, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl,
                    Size = 1
                });

                if (_entries.TryGetValue(key, out var old))
                {
                    _order.Remove(old);
                    _bytes -= old.Bytes;
                }

                var entry = new L1Entry(key, ++_sequence, Math.Max(1, bytes));
                _entries[key] = entry;
                _order.Add(entry);
                _bytes += entry.Bytes;

                int evicted = 0;
                while (_entries.Count > _entryLimit || _bytes > _byteLimit)
                {
                    var oldest = _order.Min;
                    if (oldest is null)
                        break;
                    _order.Remove(oldest);
                    _entries.Remove(oldest.Key);
                    _bytes -= oldest.Bytes;
                    _cache.Remove(oldest.Key);
                    evicted++;
                }
                return evicted;
            }
        }
        internal Lazy<Task<Result<Dictionary<string, SegmentRoad>>>> GetOrAddFetch(
            string key, Func<Task<Result<Dictionary<string, SegmentRoad>>>> factory) =>
            _inflight.GetOrAdd(
                key,
                _ => new Lazy<Task<Result<Dictionary<string, SegmentRoad>>>>(
                    factory, LazyThreadSafetyMode.ExecutionAndPublication));

        internal void RemoveFetch(
            string key, Lazy<Task<Result<Dictionary<string, SegmentRoad>>>> fetch) =>
            ((ICollection<KeyValuePair<string, Lazy<Task<Result<Dictionary<string, SegmentRoad>>>>>>)_inflight)
                .Remove(new KeyValuePair<string, Lazy<Task<Result<Dictionary<string, SegmentRoad>>>>>(key, fetch));

        private sealed record L1Entry(string Key, long Sequence, int Bytes);

        private sealed class L1EntryComparer : IComparer<L1Entry>
        {
            internal static readonly L1EntryComparer Instance = new();

            public int Compare(L1Entry? x, L1Entry? y)
            {
                if (ReferenceEquals(x, y))
                    return 0;
                if (x is null)
                    return -1;
                if (y is null)
                    return 1;
                int sequence = x.Sequence.CompareTo(y.Sequence);
                return sequence != 0 ? sequence : StringComparer.Ordinal.Compare(x.Key, y.Key);
            }
        }
    }

    private static string EscapeOData(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record HereMapAttributesResponse(List<HereSegmentAttributes>? Segments);

    private sealed record HereSegmentAttributes(
        [property: JsonPropertyName("segmentRef")] string? SegmentRef,
        [property: JsonPropertyName("attributes")] HereSegmentAttributeSets? Attributes);

    private sealed record HereSegmentAttributeSets(
        [property: JsonPropertyName("LINK_ATTRIBUTE_FCN")] List<HereLinkAttributes>? LinkAttributes,
        [property: JsonPropertyName("ROAD_GEOM_FCN")] List<HereRoadGeometry>? RoadGeometry);

    private sealed record HereLinkAttributes(
        [property: JsonPropertyName("FUNCTIONAL_CLASS")] string? FunctionalClass,
        [property: JsonPropertyName("RAMP")] string? Ramp);

    private sealed record HereRoadGeometry(
        [property: JsonPropertyName("NAME")] string? Name);
}

internal static class HereSegmentReference
{
    internal static string CacheIdentity(string reference) => SegmentId(reference) ?? reference;

    /// <summary>The bare segment id from any of HERE's reference forms, or null when there is none.</summary>
    internal static string? SegmentId(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        const string marker = "here:cm:segment:";
        int start = reference.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            int compact = reference.IndexOf("$1:", StringComparison.Ordinal);
            if (compact < 0)
                return null;
            start = compact + 3;
        }
        else
        {
            start += marker.Length;
        }

        int end = start;
        while (end < reference.Length && char.IsAsciiDigit(reference[end]))
            end++;

        return end > start ? reference[start..end] : null;
    }
}
