using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Serves the console's runtime client configuration: the browser-safe HERE Maps JS key,
///     and whether playout delivery is enabled AT ALL in this environment.
///
///     The map key is NEVER baked into the static bundle (that would expose it to anonymous
///     internet callers who can read the public JS). Instead it is fetched from this
///     endpoint, which rides the same Easy Auth + <see cref="AuthorizationMiddleware"/>
///     boundary as every other <c>/api/*</c> call — GET defaults to the Operator role, so
///     only authenticated operators receive it. The value is <see cref="HereOptions.MapApiKey"/>,
///     a SEPARATE, domain-restricted key from the server-side paid REST key.
///
///     Empty key ⇒ the console shows "Map unavailable" in place of every map (no keyless fallback).
///
///     <para>
///         <c>PlayoutEnabled</c> is <see cref="ZettaOptions.Enabled" />, the environment-wide
///         gate. Delivery needs it AND the station's own <c>MarketSchedule.PlayoutEnabled</c>,
///         so the console cannot tell an operator what a station's Send to playout switch will
///         actually do without knowing this. Development has never been able to deliver, and
///         without this the station cards asserted "a published bulletin reaches playout" on
///         14 stations that could not reach it. Not a secret: it is a deployment posture the
///         signed-in operator can already infer from the playout tray.
///     </para>
/// </summary>
public sealed class MapConfigHttpTrigger(
    IOptions<HereOptions> hereOptions,
    IOptions<ZettaOptions> zettaOptions)
{
    private readonly HereOptions _here = hereOptions.Value;
    private readonly ZettaOptions _zetta = zettaOptions.Value;

    [Function("MapConfig")]
    public Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "map-config")]
        HttpRequestData req)
        => HttpResponseHelper.OkJsonAsync(req, new MapConfigResponse(_here.MapApiKey, _zetta.Enabled));

    private sealed record MapConfigResponse(string HereApiKey, bool PlayoutEnabled);
}
