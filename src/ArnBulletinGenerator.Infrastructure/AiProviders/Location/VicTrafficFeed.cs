using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Victoria DataVic unplanned disruptions feed (Department of Transport and Planning, CC-BY, keyless).
/// </summary>
public sealed class VicTrafficFeed(
    HttpClient httpClient,
    IOptions<OfficialFeedsOptions> options,
    ILogger<VicTrafficFeed> logger) : IOfficialTrafficFeed
{
    private readonly OfficialFeedsOptions _options = options.Value;

    public string SourceId => "victraffic";
    /// <summary>
    ///     Intentionally empty: <see cref="OfficialFeedsOptions.VicDisruptionsUrl" /> currently
    ///     points at DataVic's OpenAPI 3.0.1 document, while the secured dataset path requires
    ///     an operator-provisioned KeyID. ParseVic therefore sees no features; do not advertise
    ///     aspirational fields until credentials and the endpoint are provisioned.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = new(
        "victraffic",
        "unprovisioned-openapi-3.0.1",
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    public async Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default)
    {
        using var feedBudget = OfficialFeedCallBudget.Begin(OfficialFeedCallBudget.MaxFeedTimeout);
        try
        {
            using var requestCts = OfficialFeedCallBudget.Current!
                .CreateRequestCancellation(cancellationToken);
            using var response = await httpClient.GetAsync(
                _options.VicDisruptionsUrl, requestCts.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(requestCts.Token));
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Success(
                OfficialFeedGeoJson.ParseVic(doc, SourceId, area));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[OfficialFeed] VIC disruptions fetch failed");
            return Result<IReadOnlyList<OfficialTrafficEvent>>.Failure($"VIC feed unavailable: {ex.Message}");
        }
    }
}
