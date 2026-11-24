using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Provides routing and drive time information via HERE Routing API v8.
/// </summary>
public sealed class HereRoutingProvider(
    HttpClient httpClient,
    IOptions<HereOptions> options,
    ILogger<HereRoutingProvider> logger) : IRoutingProvider, IDetourRoutingProvider
{
    private readonly HereOptions _options = options.Value;

    public async Task<Result<DriveTimeResult>> GetDriveTimeAsync(
        (double Lat, double Lng) origin,
        (double Lat, double Lng) destination,
        CancellationToken cancellationToken = default)
    {
        string originStr = FormattableString.Invariant($"{origin.Lat:F6},{origin.Lng:F6}");
        string destStr = FormattableString.Invariant($"{destination.Lat:F6},{destination.Lng:F6}");

        string url = $"{_options.RoutingBaseUrl}/routes" +
                     $"?transportMode=car" +
                     $"&origin={originStr}" +
                     $"&destination={destStr}" +
                     $"&departureTime={DateTimeOffset.UtcNow.ToString("O")}" +
                     $"&return=summary,typicalDuration" +
                     $"&apiKey={_options.ApiKey}";

        using var permit = await HereRequestBudget.AcquireAsync(
            HereRequestKind.Route, cancellationToken);
        var result =
            await HereApiHelper.ExecuteGetAsync<HereRoutingResponse>(httpClient, url, logger, "Routing",
                cancellationToken);
        if (result.IsFailure)
            return Result<DriveTimeResult>.Failure(result.Error);

        var route = result.Value.Routes?.FirstOrDefault();
        var section = route?.Sections?.FirstOrDefault();

        if (section?.Summary is null)
        {
            logger.LogWarning("HERE Routing API returned no route");
            return Result<DriveTimeResult>.Failure("No route found");
        }

        var summary = section.Summary;
        int durationMinutes = (int)Math.Ceiling(summary.Duration / 60.0);
        int typicalMinutes = (int)Math.Ceiling((summary.TypicalDuration ?? summary.Duration) / 60.0);
        double distanceKm = summary.Length / 1000.0;

        return Result<DriveTimeResult>.Success(new DriveTimeResult(
            durationMinutes,
            typicalMinutes,
            distanceKm
        ));
    }

    public async Task<Result<DetourSuggestion>> GetSafeDetourAsync(
        (double Lat, double Lng) origin,
        (double Lat, double Lng) destination,
        IReadOnlyList<string> avoidSegments,
        DriveTimeResult currentRoute,
        CancellationToken cancellationToken = default)
    {
        var segments = avoidSegments
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        if (segments.Count == 0)
            return Result<DetourSuggestion>.Failure("No avoidable HERE segments supplied");

        string originStr = FormattableString.Invariant($"{origin.Lat:F6},{origin.Lng:F6}");
        string destStr = FormattableString.Invariant($"{destination.Lat:F6},{destination.Lng:F6}");
        string avoid = Uri.EscapeDataString(string.Join(",", segments));

        string url = $"{_options.RoutingBaseUrl}/routes" +
                     $"?transportMode=car" +
                     $"&origin={originStr}" +
                     $"&destination={destStr}" +
                     $"&departureTime={DateTimeOffset.UtcNow.ToString("O")}" +
                     // HERE Routing v8 rejects spans=names unless return includes polyline
                     // (E605013 "Spans requested but no polyline requested") — this request
                     // 400'd on every detour poll until polyline was added.
                     $"&return=summary,polyline" +
                     $"&spans=names" +
                     $"&alternatives=2" +
                     $"&avoid[segments]={avoid}" +
                     $"&apiKey={_options.ApiKey}";

        using var permit = await HereRequestBudget.AcquireAsync(
            HereRequestKind.Route, cancellationToken);
        var result = await HereApiHelper.ExecuteGetAsync<HereRoutingResponse>(
            httpClient, url, logger, "Routing detour", cancellationToken);
        if (result.IsFailure)
            return Result<DetourSuggestion>.Failure(result.Error);

        var best = result.Value.Routes?
            .Select(r => r.Sections?.FirstOrDefault()?.Summary)
            .OfType<HereRouteSummary>()
            .Select(s => new
            {
                DurationMinutes = (int)Math.Ceiling(s.Duration / 60.0),
                DistanceKm = s.Length / 1000.0
            })
            .Where(r => r.DurationMinutes < currentRoute.DurationMinutes &&
                        r.DistanceKm <= currentRoute.DistanceKm * 1.5)
            .OrderBy(r => r.DurationMinutes)
            .FirstOrDefault();

        if (best is null)
            return Result<DetourSuggestion>.Failure("No safe faster detour found");

        return Result<DetourSuggestion>.Success(new DetourSuggestion(
            best.DurationMinutes,
            currentRoute.DurationMinutes - best.DurationMinutes,
            best.DistanceKm));
    }

    // Internal DTOs for HERE Routing API v8 Response
    private sealed record HereRoutingResponse(List<HereRoute>? Routes);

    private sealed record HereRoute(List<HereRouteSection>? Sections);

    private sealed record HereRouteSection(HereRouteSummary? Summary);

    private sealed record HereRouteSummary(
        int Duration, // Duration in seconds
        int Length, // Length in meters
        int? TypicalDuration // Typical duration in seconds (traffic-independent)
    );
}
