using System.Diagnostics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Fail-soft fetchers for the per-market content that depends only on the coverage area and
///     market/station ids (never the traffic result): weather, fuel, monitored-route drive times,
///     standing guidance, filler statements, and explicit station future lessons.
///     Extracted from <see cref="TrafficEvidenceBuilder" />, which fires these concurrently via
///     <see cref="TrafficEvidenceBuilder.FetchMarketContentAsync" />. Everything here is fail-soft
///     garnish — failures log and degrade to empty, never block the bulletin.
/// </summary>
internal sealed class MarketContentFetcher(
    IFillerStatementsRepository fillerStatementsRepository,
    IFutureLessonRepository futureLessonRepository,
    IGuidanceRepository guidanceRepository,
    IEditorialSettingsRepository editorialSettingsRepository,
    IRoutingProvider routingProvider,
    IOptions<MonitoredRoutesOptions> monitoredRoutesOptions,
    IWeatherProvider weatherProvider,
    IFuelPriceProvider fuelPriceProvider,
    IBroadcastMetrics metrics,
    ILogger<BroadcastOrchestrator> logger,
    JobStatusUpdater jobStatusUpdater,
    TimeProvider timeProvider)

{
    internal sealed record SourceContentResult<T>(T Value, SourceFetchDiagnostic Diagnostic);
    private static string DescribeFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "upstream request failed";
        var match = System.Text.RegularExpressions.Regex.Match(error, @"\b([45]\d{2})\b");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int status))
            return "upstream request failed";
        string reason = status switch
        {
            401 => "unauthorised",
            403 => "forbidden",
            408 => "request timeout",
            429 => "rate limited",
            >= 500 => "server error",
            _ => "request failed"
        };
        return $"HTTP {status} ({reason})";
    }

    internal async Task<IReadOnlyList<FillerStatement>> GetFillerStatementsSafeAsync(
        BroadcastContextBase context, CancellationToken cancellationToken)
    {
        string market = context.MarketId.GetValueOr(context.Location);
        var result = await fillerStatementsRepository.GetStatementsAsync(market, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("[MarketContent] Job {JobId} filler-statements lookup failed (ignored): {Error}",
                context.JobId, result.Error);
            return [];
        }

        // Strict availability filtering at the load boundary, in station-local time: a
        // statement outside its window must never air, in either this path or the fallback
        // padder (the two share FillerAvailabilityFilter). Log exclusions so an all-expired
        // library is distinguishable from an unconfigured one. The pool stays enabled-only
        // (the historical contract — callers gate on its Count for FillSource.Filler); the
        // shared filter keeps disabled rows only so the padder's selector can apply Enabled
        // itself.
        var (localDate, localTime) = TimeZoneHelper.GetLocalDateAndTime(
            timeProvider.GetUtcNow().UtcDateTime, context.TimezoneId);
        var (active, excludedByWindow) = FillerAvailabilityFilter.Apply(result.Value, localDate, localTime);
        if (excludedByWindow > 0)
            logger.LogInformation(
                "[MarketContent] Job {JobId} excluded {Excluded} filler statement(s) by their "
                + "availability window; {Remaining} remain eligible",
                context.JobId, excludedByWindow, active.Count);

        return active.Where(s => s.Enabled).ToList();
    }

    internal async Task<IReadOnlyList<MonitoredRouteStatus>> FetchMonitoredRoutesSafeAsync(
        BroadcastWithTraffic context, CancellationToken cancellationToken)
    {
        try
        {
            string market = context.MarketId.GetValueOr(context.Location);
            var routes = await MonitoredRouteReporter.GetReportsAsync(
                routingProvider, market, monitoredRoutesOptions.Value, cancellationToken);
            if (routes.Count > 0)
                logger.LogInformation("[MarketContent] Job {JobId} monitored routes: {Count}",
                    context.JobId, routes.Count);
            return routes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[MarketContent] Job {JobId} monitored-route lookup failed (ignored)",
                context.JobId);
            return [];
        }
    }

    internal async Task<SourceContentResult<Option<WeatherReport>>> FetchWeatherSafeAsync(
        BroadcastWithTraffic context, CoverageArea coverage, CancellationToken cancellationToken)
    {
        if (!context.Content.Weather)
            return new(
                Option<WeatherReport>.None,
                new SourceFetchDiagnostic("here-weather", SourceFetchOutcome.Skipped,
                    "not selected for this bulletin", null, null));

        var sw = Stopwatch.StartNew();
        var result = await weatherProvider.GetWeatherAsync(coverage, cancellationToken);
        sw.Stop();
        metrics.TrackPipelineStage("weather_fetch", sw.Elapsed.TotalSeconds, context.Location, result.IsSuccess);

        if (result.IsFailure)
        {
            string detail = DescribeFailure(result.Error);
            logger.LogWarning("[MarketContent] Job {JobId} weather unavailable (bulletin continues without): {Error}",
                context.JobId, detail);
            await jobStatusUpdater.LogJobAsync(context.JobId, "Weather data unavailable — bulletin continues without it",
                cancellationToken);
            return new(
                Option<WeatherReport>.None,
                new SourceFetchDiagnostic("here-weather", SourceFetchOutcome.Failed, detail, null, null));
        }

        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Weather fetched: {result.Value.Conditions}, {result.Value.TemperatureC:F0}°C" +
            (result.Value.HasMajorAlert ? $", {result.Value.Alerts.Count} alert(s)" : ""),
            cancellationToken);
        return new(
            Option<WeatherReport>.Some(result.Value),
            new SourceFetchDiagnostic("here-weather", SourceFetchOutcome.Ok, null, timeProvider.GetUtcNow(), null));
    }

    internal async Task<SourceContentResult<Option<FuelPriceReport>>> FetchFuelSafeAsync(
        BroadcastWithTraffic context, CoverageArea coverage, CancellationToken cancellationToken)
    {
        if (!context.Content.Fuel)
            return new(
                Option<FuelPriceReport>.None,
                new SourceFetchDiagnostic("here-fuel", SourceFetchOutcome.Skipped,
                    "not selected for this bulletin", null, null));

        var sw = Stopwatch.StartNew();
        var result = await fuelPriceProvider.GetFuelPricesAsync(coverage, cancellationToken);
        sw.Stop();
        metrics.TrackPipelineStage("fuel_fetch", sw.Elapsed.TotalSeconds, context.Location, result.IsSuccess);

        if (result.IsFailure || !result.Value.HasPrices)
        {
            string detail = result.IsFailure
                ? DescribeFailure(result.Error)
                : "no stations with prices in coverage";
            logger.LogWarning(
                "[MarketContent] Job {JobId} fuel prices unavailable (bulletin continues without): {Reason}",
                context.JobId, detail);
            await jobStatusUpdater.LogJobAsync(context.JobId, "Fuel price data unavailable — bulletin continues without it",
                cancellationToken);
            return new(
                Option<FuelPriceReport>.None,
                new SourceFetchDiagnostic("here-fuel", SourceFetchOutcome.Failed, detail, null, null));
        }

        await jobStatusUpdater.LogJobAsync(context.JobId,
            $"Fuel prices fetched: {result.Value.Summaries.Count} fuel type(s)", cancellationToken);
        return new(
            Option<FuelPriceReport>.Some(result.Value),
            new SourceFetchDiagnostic("here-fuel", SourceFetchOutcome.Ok, null, timeProvider.GetUtcNow(), null));
    }

    internal async Task<IReadOnlyList<FutureLessonEntry>> GetFutureLessonsSafeAsync(
        BroadcastWithTraffic context, CancellationToken cancellationToken)
    {
        if (!context.StationId.HasValue)
            return [];

        // Fetch the full stored page so candidates cannot crowd actives out, then keep only
        // ACTIVE lessons — candidates are stored evidence, not yet writer instructions.
        var result = await futureLessonRepository.GetRecentAsync(
            context.StationId.Value, FutureLessonPolicy.MaxStoredPerStation, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("[MarketContent] Job {JobId} future-lesson lookup failed (ignored): {Error}",
                context.JobId, result.Error);
            return [];
        }

        return result.Value
            .Where(l => l.Status == FutureLessonStatus.Active)
            .Take(FutureLessonPolicy.MaxActivePerStation)
            .ToList();
    }

    /// <summary>
    ///     Composes standing guidance, honouring ARN's directive cap. Takes the settings as a Task so
    ///     the two repository reads still run concurrently: only the cap is needed, and only at the
    ///     moment of composing.
    /// </summary>
    internal async Task<ComposedGuidance> GetComposedGuidanceSafeAsync(
        BroadcastWithTraffic context, Task<EditorialSettings> settingsTask, CancellationToken cancellationToken)
    {
        string market = context.MarketId.GetValueOr(context.Location);
        var result = await guidanceRepository.GetForScopesAsync(market, context.StationId, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("[MarketContent] Job {JobId} guidance lookup failed (ignored): {Error}",
                context.JobId, result.Error);
            return ComposedGuidance.Empty;
        }

        var settings = await settingsTask;
        var composed = GuidanceComposer.Compose(result.Value, market, context.StationId,
            settings.GuidanceMaxDirectives ?? GuidanceComposer.DefaultMaxDirectives);
        if (composed.HasEntries)
            logger.LogInformation(
                "[MarketContent] Job {JobId} standing guidance: {Count} directive(s) in effect, {Dropped} dropped",
                context.JobId, composed.Count, composed.DroppedCount);
        return composed;
    }

    /// <summary>
    ///     Resolves ARN's numeric editorial settings for this bulletin, layering Global → Market →
    ///     Station per field. Fails safe to <see cref="EditorialSettings.Empty" />, which leaves every
    ///     consumer on its shipped default — the same values the constants carried before these became
    ///     configurable.
    /// </summary>
    internal async Task<EditorialSettings> GetEditorialSettingsSafeAsync(
        BroadcastContextBase context, CancellationToken cancellationToken)
    {
        string market = context.MarketId.GetValueOr(context.Location);
        var result = await editorialSettingsRepository.GetApplicableAsync(
            market, context.StationId, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "[MarketContent] Job {JobId} editorial-settings lookup failed; using shipped defaults: {Error}",
                context.JobId, result.Error);
            return EditorialSettings.Empty;
        }

        var resolved = EditorialSettings.Resolve(result.Value, market, context.StationId);
        // Every field, not a chosen five. The previous line named corridors/driveTime/tiers only,
        // so the four continuity caps, the guidance cap and the whole slot-fit envelope had no
        // audit trail at all — an operator could set them and had no way to confirm the bulletin
        // resolved what they saved. `*` marks fields that PERSIST and RESOLVE but are not yet read
        // by any consumer (EditorialSettings.MaxSilencePadSeconds / MaxSafeOverrunTrimSeconds are
        // shadowed by SpeechDurationOptions in AudioPipelineService and AudioDurationFineTuner),
        // so this line can never be read as proof that an inert knob took effect.
        if (result.Value.Count > 0)
            logger.LogInformation(
                "[MarketContent] Job {JobId} editorial settings resolved from {RowCount} row(s): "
                + "corridors={MaxCorridors}, driveTime>={DriveTime}min, tiers={Major}/{Moderate}/{Minor}, "
                + "continuity new/ongoing/cleared/updated={CNew}/{COngoing}/{CCleared}/{CUpdated}, "
                + "guidanceMax={GuidanceMax}, slowDown={SlowDown}, speedUp={SpeedUp}, "
                + "silencePad={SilencePad}*, overrunTrim={OverrunTrim}*, sourcesOff=[{SourcesOff}] "
                + "(null = shipped default; * = stored and resolved but not yet consumed)",
                context.JobId, result.Value.Count,
                resolved.MaxCorridors, resolved.DriveTimeHeadlineThresholdMinutes,
                resolved.MajorTierCap, resolved.ModerateTierCap, resolved.MinorTierCap,
                resolved.ContinuityNewCap, resolved.ContinuityOngoingCap,
                resolved.ContinuityClearedCap, resolved.ContinuityUpdatedCap,
                resolved.GuidanceMaxDirectives, resolved.MaxSlowDownFactor, resolved.MaxSpeedUpFactor,
                resolved.MaxSilencePadSeconds, resolved.MaxSafeOverrunTrimSeconds,
                string.Join(",", resolved.DisabledTrafficSources ?? []));

        return resolved;
    }

}
