using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Resolves place names to coordinates via HERE Geocoding, and batch reverse-geocodes many
///     coordinates back to place names in parallel for incident and corridor narration.
/// </summary>
public sealed class HereGeocodingProvider(
    HttpClient httpClient,
    IOptions<HereOptions> options,
    ILogger<HereGeocodingProvider> logger) : IGeocodingProvider
{
    // Keep expensive single-coordinate fallbacks bounded when the premium batch endpoint is unavailable.
    private const int MaxSingleCoordinateFallbacks = 20;
    private readonly HereOptions _options = options.Value;

    public async Task<Result<CoverageArea>> GeocodeAsync(string query, double defaultRadiusKm = 5.0,
        CancellationToken cancellationToken = default)
    {
        string url =
            $"{_options.GeocodingBaseUrl}/{_options.GeocodePath}?q={Uri.EscapeDataString(query)}&apiKey={_options.ApiKey}";

        using var permit = await HereRequestBudget.AcquireAsync(
            HereRequestKind.ReverseGeocode, cancellationToken);
        var result =
            await HereApiHelper.ExecuteGetAsync<HereGeocodeResponse>(httpClient, url, logger, "Geocoding",
                cancellationToken);
        if (result.IsFailure)
        {
            logger.LogError("Geocoding failed for query '{Query}': {Error}", query, result.Error);
            return Result<CoverageArea>.Failure(result.Error);
        }

        var item = result.Value.Items?.FirstOrDefault();
        if (item is null)
        {
            logger.LogWarning("No geocoding results for query: {Query}", query);
            return Result<CoverageArea>.Failure($"Location not found: {query}");
        }

        var coverage = CoverageArea.Create(item.Position.Lat, item.Position.Lng, defaultRadiusKm);
        return coverage.IsSuccess
            ? Result<CoverageArea>.Success(coverage.Value)
            : Result<CoverageArea>.Failure(coverage.Error);
    }

    /// <summary>
    ///     Names the road at a coordinate, for incidents HERE gives no road name for.
    ///     <para>
    ///         This is a PROXIMITY answer and is never authoritative for road identity. Restricting
    ///         the call to distance-ranked streets (<c>types=street</c>) was measured against 173
    ///         live Sydney + Cairns incidents on 2026-08-13 and REJECTED: it changed 33 names and
    ///         roughly half of those moved away from the road HERE Map Attributes confirms for the
    ///         incident's own segment (e.g. a genuine "Ramp to M1 Pacific Highway Southbound"
    ///         closure became "Pennant Hills Rd"). The safe guard against a proximity answer naming
    ///         the wrong road lives in <c>HereTrafficProvider</c>, which refuses a name that
    ///         contradicts HERE's own functional class for that incident.
    ///     </para>
    /// </summary>
    public async Task<Result<string>> ReverseGeocodeAsync(double lat, double lng,
        CancellationToken cancellationToken = default)
    {
        string url =
            $"{_options.GeocodingBaseUrl}/{_options.ReverseGeocodePath}?at={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}&lang=en-US&apiKey={_options.ApiKey}";

        using var permit = await HereRequestBudget.AcquireAsync(
            HereRequestKind.ReverseGeocode, cancellationToken);
        var result =
            await HereApiHelper.ExecuteGetAsync<HereGeocodeResponse>(httpClient, url, logger, "Reverse geocoding",
                cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Reverse geocoding failed for coordinates Lat: {Lat}, Lng: {Lng}: {Error}", lat, lng,
                result.Error);
            return Result<string>.Failure(result.Error);
        }

        var item = result.Value.Items?.FirstOrDefault();
        if (item is null)
            return Result<string>.Failure("No address found");

        // A bare numeric "street" (HERE returns e.g. "99" for a state route) is not a
        // speakable road name — prefer the district (suburb) over the full postal label.
        string address = PreferNamedStreet(item.Address.Street, item.Address.Label, item.Address.District);
        return Result<string>.Success(address);
    }

    /// <inheritdoc />
    public async Task<Result<IDictionary<int, GeocodedPlace>>> BatchReverseGeocodePlacesAsync(
        IEnumerable<(double Lat, double Lng)> coordinates, CancellationToken cancellationToken = default)
    {
        var coordList = coordinates.ToList();
        if (coordList.Count == 0)
            return Result<IDictionary<int, GeocodedPlace>>.Success(new Dictionary<int, GeocodedPlace>());

        var coalesced = CoalesceCoordinates(coordList);
        Result<IDictionary<int, GeocodedPlace>> uniqueResult;
        if (!_options.UseBatchGeocoding)
        {
            uniqueResult = await SequentialReverseGeocodePlacesAsync(
                coalesced.Unique.Take(MaxSingleCoordinateFallbacks).ToList(), cancellationToken);
        }
        else
        {
            uniqueResult = await BatchReverseGeocodePlacesViaApiAsync(
                coalesced.Unique.ToList(), cancellationToken);
            if (uniqueResult.IsFailure)
            {
                logger.LogWarning(
                    "Batch reverse geocoding places failed ({Error}); falling back to sequential calls for at most {Count} coordinates",
                    uniqueResult.Error, MaxSingleCoordinateFallbacks);
                uniqueResult = await SequentialReverseGeocodePlacesAsync(
                    coalesced.Unique.Take(MaxSingleCoordinateFallbacks).ToList(), cancellationToken);
            }
        }

        return uniqueResult.IsFailure
            ? Result<IDictionary<int, GeocodedPlace>>.Failure(uniqueResult.Error)
            : Result<IDictionary<int, GeocodedPlace>>.Success(
                ExpandCoordinates(uniqueResult.Value, coalesced.OriginalToUnique));
    }

    private async Task<Result<IDictionary<int, GeocodedPlace>>> BatchReverseGeocodePlacesViaApiAsync(
        List<(double Lat, double Lng)> coordList, CancellationToken cancellationToken)
    {
        var finalResults = new Dictionary<int, GeocodedPlace>();
        for (int i = 0; i < coordList.Count; i += 100)
        {
            var chunk = coordList.Skip(i).Take(100).ToList();
            // Format: id=0&at=lat,lng\nid=1&at=lat,lng...
            var body = string.Join("\n",
                chunk.Select((c, idx) =>
                    $"id={i + idx}&at={c.Lat.ToString(CultureInfo.InvariantCulture)},{c.Lng.ToString(CultureInfo.InvariantCulture)}"));

            string url = $"{_options.BatchReverseGeocodeUrl}?apiKey={_options.ApiKey}";
            using var content = new StringContent(body, Encoding.UTF8, "text/plain");
            using var permit = await HereRequestBudget.AcquireAsync(
                HereRequestKind.ReverseGeocode, cancellationToken);
            var result = await HereApiHelper.ExecutePostAsync<HereBatchGeocodeResponse>(
                httpClient, url, content, logger, "BatchReverseGeocodePlaces", cancellationToken);

            if (result.IsFailure)
                return Result<IDictionary<int, GeocodedPlace>>.Failure(result.Error);

            if (result.Value.Results is not null)
                foreach (var subResult in result.Value.Results)
                    if (int.TryParse(subResult.Id, out int id) &&
                        subResult.Items is { Count: > 0 } &&
                        subResult.Items[0].Address is { } itemAddress)
                    {
                        string? street = itemAddress.Street;
                        bool speakableRoad = !string.IsNullOrWhiteSpace(street)
                                             && street.Any(char.IsLetter)
                                             && !RoadNameFormatter.IsRouteCodeOnly(street)
                                             && !RoadNameFormatter.LooksLikeRawAddress(street);

                        finalResults[id] = new GeocodedPlace(
                            speakableRoad ? Option<string>.Some(street!) : default,
                            string.IsNullOrWhiteSpace(itemAddress.District)
                                ? default
                                : Option<string>.Some(itemAddress.District));
                    }
        }

        return Result<IDictionary<int, GeocodedPlace>>.Success(finalResults);
    }

    private async Task<Result<IDictionary<int, GeocodedPlace>>> SequentialReverseGeocodePlacesAsync(
        List<(double Lat, double Lng)> coordList, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(Math.Clamp(_options.MaxParallelReverseGeocode, 1, 3));
        var places = new ConcurrentDictionary<int, GeocodedPlace>();

        await Task.WhenAll(coordList.Select(async (coord, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var place = await ReverseGeocodePlaceAsync(coord.Lat, coord.Lng, cancellationToken);
                if (place.IsSuccess)
                    places[index] = place.Value;
            }
            finally
            {
                semaphore.Release();
            }
        }));

        return Result<IDictionary<int, GeocodedPlace>>.Success(
            places.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private async Task<Result<GeocodedPlace>> ReverseGeocodePlaceAsync(
        double lat, double lng, CancellationToken cancellationToken)
    {
        // `types=street` returns the streets nearest the point, ranked by distance. Those are used ONLY
        // to corroborate a road identity resolved from the incident's own segments — naming from this
        // ranking directly was measured and rejected (33 of 173 names changed, about half moving away
        // from the segment-confirmed road).
        string url =
            $"{_options.GeocodingBaseUrl}/{_options.ReverseGeocodePath}?at={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}&lang=en-US&types=street&limit=5&apiKey={_options.ApiKey}";

        // One permit per physical request, never one held across another: RevGeocodePlainAsync
        // below acquires its own, and holding this one while awaiting it deadlocks the worker-wide
        // gate once ReverseGeocodePermitCount callers are all parked in the same place (found by the
        // HereTrafficWireMockTests hang, 2026-09-03: three parallel fallbacks, three permits, zero
        // progress).
        Result<HereGeocodeResponse> streets;
        using (await HereRequestBudget.AcquireAsync(HereRequestKind.ReverseGeocode, cancellationToken))
        {
            streets = await HereApiHelper.ExecuteGetAsync<HereGeocodeResponse>(httpClient, url, logger,
                "Reverse geocoding (streets)", cancellationToken);
        }

        // The unrestricted call remains the NAME source, so this change cannot move any existing name.
        var plain = await RevGeocodePlainAsync(lat, lng, cancellationToken);
        if (plain.IsFailure)
            return Result<GeocodedPlace>.Failure(plain.Error);

        var item = plain.Value;
        string? street = item.Address.Street;
        bool speakableRoad = !string.IsNullOrWhiteSpace(street)
                             && street.Any(char.IsLetter)
                             && !RoadNameFormatter.IsRouteCodeOnly(street)
                             && !RoadNameFormatter.LooksLikeRawAddress(street);

        var nearest = streets.IsSuccess
            ? (streets.Value.Items ?? [])
                .Select(i => i.Address.Street)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList()
            : [];

        return Result<GeocodedPlace>.Success(new GeocodedPlace(
            speakableRoad ? Option<string>.Some(street!) : default,
            string.IsNullOrWhiteSpace(item.Address.District) ? default : Option<string>.Some(item.Address.District),
            nearest));
    }

    /// <summary>The unrestricted reverse-geocode item at a point — the established name source.</summary>
    private async Task<Result<HereGeocodeItem>> RevGeocodePlainAsync(
        double lat, double lng, CancellationToken cancellationToken)
    {
        string url =
            $"{_options.GeocodingBaseUrl}/{_options.ReverseGeocodePath}?at={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}&lang=en-US&apiKey={_options.ApiKey}";

        using var permit = await HereRequestBudget.AcquireAsync(
            HereRequestKind.ReverseGeocode, cancellationToken);
        var result = await HereApiHelper.ExecuteGetAsync<HereGeocodeResponse>(httpClient, url, logger,
            "Reverse geocoding", cancellationToken);
        if (result.IsFailure)
            return Result<HereGeocodeItem>.Failure(result.Error);

        var item = result.Value.Items?.FirstOrDefault();
        return item is null
            ? Result<HereGeocodeItem>.Failure("No address found")
            : Result<HereGeocodeItem>.Success(item);
    }

    public async Task<Result<IDictionary<int, string>>> BatchReverseGeocodeAsync(
        IEnumerable<(double Lat, double Lng)> coordinates, CancellationToken cancellationToken = default)
    {
        var coordList = coordinates.ToList();
        if (coordList.Count == 0)
            return Result<IDictionary<int, string>>.Success(new Dictionary<int, string>());

        var coalesced = CoalesceCoordinates(coordList);
        Result<IDictionary<int, string>> uniqueResult;
        if (!_options.UseBatchGeocoding)
        {
            uniqueResult = await SequentialReverseGeocodeAsync(
                coalesced.Unique.Take(MaxSingleCoordinateFallbacks).ToList(), cancellationToken);
        }
        else
        {
            uniqueResult = await BatchReverseGeocodeViaApiAsync(
                coalesced.Unique.ToList(), cancellationToken);
            if (uniqueResult.IsFailure)
            {
                logger.LogWarning(
                    "Batch reverse geocoding failed ({Error}); falling back to sequential calls for at most {Count} coordinates",
                    uniqueResult.Error, MaxSingleCoordinateFallbacks);
                uniqueResult = await SequentialReverseGeocodeAsync(
                    coalesced.Unique.Take(MaxSingleCoordinateFallbacks).ToList(), cancellationToken);
            }
        }

        return uniqueResult.IsFailure
            ? Result<IDictionary<int, string>>.Failure(uniqueResult.Error)
            : Result<IDictionary<int, string>>.Success(
                ExpandCoordinates(uniqueResult.Value, coalesced.OriginalToUnique));
    }

    /// <summary>
    ///     Calls the HERE multi-reverse-geocode batch endpoint (requires a premium API plan;
    ///     the caller falls back to <see cref="SequentialReverseGeocodeAsync"/> on any failure,
    ///     so this is safe to enable even where the plan tier is unconfirmed).
    ///     Body is plain text, one sub-request per line: `id=1&at=lat,lng`
    /// </summary>
    private async Task<Result<IDictionary<int, string>>> BatchReverseGeocodeViaApiAsync(
        List<(double Lat, double Lng)> coordList, CancellationToken cancellationToken)
    {
        const int batchSize = 100;
        var finalResults = new Dictionary<int, string>();

        for (int i = 0; i < coordList.Count; i += batchSize)
        {
            var chunk = coordList.Skip(i).Take(batchSize).ToList();
            // Format: id=0&at=lat,lng\nid=1&at=lat,lng\n...
            var body = string.Join("\n",
                chunk.Select((c, idx) =>
                    $"id={i + idx}&at={c.Lat.ToString(CultureInfo.InvariantCulture)},{c.Lng.ToString(CultureInfo.InvariantCulture)}"));

            string url = $"{_options.BatchReverseGeocodeUrl}?apiKey={_options.ApiKey}";
            using var content = new StringContent(body, Encoding.UTF8, "text/plain");
            using var permit = await HereRequestBudget.AcquireAsync(
                HereRequestKind.ReverseGeocode, cancellationToken);
            var result = await HereApiHelper.ExecutePostAsync<HereBatchGeocodeResponse>(
                httpClient, url, content, logger, "BatchReverseGeocode", cancellationToken);

            if (result.IsFailure)
                return Result<IDictionary<int, string>>.Failure(result.Error);

            // Response format: { "results": [{ "id": "0", "items": [ ... ] }, ...] }
            if (result.Value.Results is not null)
                foreach (var subResult in result.Value.Results)
                    if (int.TryParse(subResult.Id, out int id) &&
                        subResult.Items is { Count: > 0 } &&
                        subResult.Items[0].Address is { } itemAddress)
                    {
                        string address = PreferNamedStreet(itemAddress.Street, itemAddress.Label, itemAddress.District);
                        if (!string.IsNullOrWhiteSpace(address))
                            finalResults[id] = address;
                    }
        }

        return Result<IDictionary<int, string>>.Success(finalResults);
    }

    /// <summary>
    ///     Falls back to individual reverse geocode calls when batch API is unavailable (e.g., Freemium plan).
    ///     Uses bounded parallelism to stay within rate limits.
    /// </summary>
    private async Task<Result<IDictionary<int, string>>> SequentialReverseGeocodeAsync(
        List<(double Lat, double Lng)> coordList, CancellationToken cancellationToken)
    {
        logger.LogInformation("Using sequential reverse geocoding for {Count} coordinates (batch disabled)",
            coordList.Count);

        using var semaphore = new SemaphoreSlim(Math.Clamp(_options.MaxParallelReverseGeocode, 1, 3));
        var results = new ConcurrentDictionary<int, string>();

        var tasks = coordList.Select((coord, index) =>
            ReverseGeocodeOneAsync(coord, index, semaphore, results, cancellationToken));
        await Task.WhenAll(tasks);

        return Result<IDictionary<int, string>>.Success(results.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private async Task ReverseGeocodeOneAsync(
        (double Lat, double Lng) coord,
        int index,
        SemaphoreSlim semaphore,
        ConcurrentDictionary<int, string> results,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var result = await ReverseGeocodeAsync(coord.Lat, coord.Lng, cancellationToken);
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
                results[index] = result.Value;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private readonly record struct CoordinateBatch(
        IReadOnlyList<(double Lat, double Lng)> Unique,
        IReadOnlyList<int> OriginalToUnique);

    private static CoordinateBatch CoalesceCoordinates(
        IReadOnlyList<(double Lat, double Lng)> coordinates)
    {
        var unique = new List<(double Lat, double Lng)>(coordinates.Count);
        var originalToUnique = new int[coordinates.Count];
        var indexes = new Dictionary<(double Lat, double Lng), int>();

        for (int i = 0; i < coordinates.Count; i++)
        {
            var coordinate = coordinates[i];
            if (!indexes.TryGetValue(coordinate, out int uniqueIndex))
            {
                uniqueIndex = unique.Count;
                indexes.Add(coordinate, uniqueIndex);
                unique.Add(coordinate);
            }

            originalToUnique[i] = uniqueIndex;
        }

        return new CoordinateBatch(unique, originalToUnique);
    }

    private static IDictionary<int, T> ExpandCoordinates<T>(
        IDictionary<int, T> uniqueResults, IReadOnlyList<int> originalToUnique)
    {
        var expanded = new Dictionary<int, T>(originalToUnique.Count);
        for (int i = 0; i < originalToUnique.Count; i++)
            if (uniqueResults.TryGetValue(originalToUnique[i], out var value))
                expanded[i] = value;

        return expanded;
    }

    /// <summary>
    ///     A HERE reverse-geocode "street" is sometimes just a route designation — a bare number
    ///     ("99" for a state route) or, for isolated stretches with no named cross-street, the
    ///     Australian alphanumeric route code itself ("B56"; live feedback, Port Augusta
    ///     2026-07-06, where B56 is locally known as Horrocks Pass Road). Neither is a speakable
    ///     road name a listener would recognize, so a real name must contain a letter (rules out
    ///     "99") AND not be a bare route code (rules out "B56", which has a letter prefix but is
    ///     still just a route number). Fall back to the fuller label instead, which still carries
    ///     the suburb/locality context the corridor namer needs.
    /// </summary>
    /// <summary>
    ///     When the street field is a bare house number (no letters, e.g. "10" instead of
    ///     "Pacific Mwy"), the district/suburb is preferred over the full label — a listener
    ///     needs "Arundel", not "10, Arundel QLD 4214, Australia".
    /// </summary>
    private static string PreferNamedStreet(string? street, string label, string? district) =>
        !string.IsNullOrWhiteSpace(street) && street.Any(char.IsLetter) && !RoadNameFormatter.IsRouteCodeOnly(street)
            ? street
            : district ?? label;

    // Internal DTOs for HERE Response
    private sealed record HereGeocodeResponse(List<HereGeocodeItem> Items);

    private sealed record HereGeocodeItem(HerePosition Position, HereAddress Address);

    private sealed record HerePosition(double Lat, double Lng);

    private sealed record HereAddress(string Label, string? Street, string? District = null);

    // DTOs for the batch response format (different from single-revgeocode response)
    private sealed record HereBatchGeocodeResponse(List<HereBatchSubResult>? Results);
    private sealed record HereBatchSubResult(string Id, List<HereBatchItem>? Items);
    private sealed record HereBatchItem(HereAddress? Address);
}
