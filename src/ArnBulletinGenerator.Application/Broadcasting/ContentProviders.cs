using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;

namespace ArnBulletinGenerator.Application.Broadcasting;
/// <summary>
///     The external evidence feeds a bulletin is generated from: traffic, weather, fuel prices,
///     routing/drive-time lookups, and optional official-feed prose extraction plus its parse
///     coverage sink and incident priority/landscape repository. Bundled because <see cref="BroadcastOrchestrator" />
///     only ever threads them through as a group into <c>TrafficEvidenceBuilder</c>.
/// </summary>
public sealed record ContentProviders(
    ITrafficProvider TrafficProvider,
    IWeatherProvider WeatherProvider,
    IFuelPriceProvider FuelPriceProvider,
    IRoutingProvider RoutingProvider,
    IEnumerable<IOfficialTrafficFeed>? OfficialFeeds = null,
    ISourceParseReportRepository? SourceParseReportRepository = null,
    IIncidentPriorityRepository? IncidentPriorityRepository = null,
    IEmergencyIncidentFeed? EmergencyIncidentFeed = null);
