using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Converts between a market's free-text coverage query and geographic coordinates, and resolves coordinates back to a readable place name.</summary>
public interface IGeocodingProvider
{
    Task<Result<CoverageArea>> GeocodeAsync(string query, double defaultRadiusKm = 5.0,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ReverseGeocodeAsync(double lat, double lng, CancellationToken cancellationToken = default);

    Task<Result<IDictionary<int, string>>> BatchReverseGeocodeAsync(IEnumerable<(double Lat, double Lng)> coordinates,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reverse-geocodes coordinates into a ROAD NAME and a SUBURB kept apart.
    ///     <para>
    ///         <see cref="BatchReverseGeocodeAsync" /> collapses both into one string and falls back to
    ///         the locality when HERE has no usable street, so a suburb became a road name on air
    ///         ("Arundel", "Greensborough", "Laverton"). A caller that needs to know which it got must
    ///         use this. Default: adapts the single-string result as a road name with no suburb, which
    ///         preserves existing behaviour for providers that do not distinguish them.
    ///     </para>
    /// </summary>
    async Task<Result<IDictionary<int, GeocodedPlace>>> BatchReverseGeocodePlacesAsync(
        IEnumerable<(double Lat, double Lng)> coordinates, CancellationToken cancellationToken = default)
    {
        var result = await BatchReverseGeocodeAsync(coordinates, cancellationToken);
        if (result.IsFailure)
            return Result<IDictionary<int, GeocodedPlace>>.Failure(result.Error);

        IDictionary<int, GeocodedPlace> places = result.Value.ToDictionary(
            kv => kv.Key,
            kv => new GeocodedPlace(Option<string>.Some(kv.Value), default));
        return Result<IDictionary<int, GeocodedPlace>>.Success(places);
    }
}
