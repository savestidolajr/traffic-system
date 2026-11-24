namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Which content sources a bulletin draws from (Workshop 1: traffic + weather + fuel in
///     scope, per-market source selection, traffic-first). Each source toggles independently;
///     the default is the classic traffic-only bulletin. A source is only used when BOTH the
///     selection enables it AND the platform-level provider options are configured/enabled.
///     <para>
///         Selecting a source makes it ELIGIBLE: it may fill the bulletin, at the writer's
///         discretion. <c>WeatherRequired</c>/<c>FuelRequired</c> promote a selected source to a
///         FIXTURE that must air whenever its data was fetched; the provider fetch stays
///         fail-soft, so a required source whose fetch failed is logged and the bulletin still
///         airs without it.
///     </para>
/// </summary>
public sealed record BulletinContentSelection(
    bool Traffic = true,
    bool Weather = false,
    bool Fuel = false,
    bool WeatherRequired = false,
    bool FuelRequired = false,
    IReadOnlyList<BulletinSegment>? SegmentOrder = null,
    TrafficRunningOrder TrafficOrder = TrafficRunningOrder.WorstFirst)
{
    public static readonly BulletinContentSelection Default = new();

    /// <summary>
    ///     The configured order after disabled sources are removed. Legacy rows with no configured
    ///     order use the classic traffic, weather, fuel sequence.
    /// </summary>
    public IReadOnlyList<BulletinSegment> EffectiveSegmentOrder
    {
        get
        {
            IReadOnlyList<BulletinSegment> configured = SegmentOrder ?? DefaultSegmentOrder;
            return configured.Where(IsEnabled).ToList();
        }
    }

    private IReadOnlyList<BulletinSegment> DefaultSegmentOrder =>
        [BulletinSegment.Traffic, BulletinSegment.Weather, BulletinSegment.Fuel];

    private bool IsEnabled(BulletinSegment segment) => segment switch
    {
        BulletinSegment.Traffic => Traffic,
        BulletinSegment.Weather => Weather,
        BulletinSegment.Fuel => Fuel,
        _ => false
    };

    /// <summary>Whether every configured segment is a unique enabled source.</summary>
    public bool HasValidSegmentOrder
    {
        get
        {
            if (SegmentOrder is null)
                return true;

            var enabled = new HashSet<BulletinSegment>();
            if (Traffic) enabled.Add(BulletinSegment.Traffic);
            if (Weather) enabled.Add(BulletinSegment.Weather);
            if (Fuel) enabled.Add(BulletinSegment.Fuel);
            return SegmentOrder.Count == SegmentOrder.Distinct().Count()
                && SegmentOrder.All(segment => Enum.IsDefined(segment) && enabled.Contains(segment))
                && SegmentOrder.Count == enabled.Count;
        }
    }
    /// <summary>Whether the traffic order is one of the supported deterministic values.</summary>
    public bool HasValidTrafficOrder => Enum.IsDefined(TrafficOrder);

    /// <summary>A bulletin with every source disabled cannot be generated.</summary>
    public bool HasAnySource => Traffic || Weather || Fuel;

    /// <summary>Weather is a fixture for this station: selected AND flagged required.</summary>
    public bool RequiresWeather => Weather && WeatherRequired;

    /// <summary>Fuel is a fixture for this station: selected AND flagged required.</summary>
    public bool RequiresFuel => Fuel && FuelRequired;
}

public enum BulletinSegment
{
    Traffic,
    Weather,
    Fuel
}

public enum TrafficRunningOrder
{
    WorstFirst = 0,
    WorstLast = 1
}


/// <summary>
///     Current weather for a market's coverage centre, shaped for bulletin narration.
///     Alerts come from the provider's global forecast-alert model (not official BoM
///     warnings) — they justify a "major weather" lead, never fabricated detail.
/// </summary>
public sealed record WeatherReport(
    string Conditions,
    double TemperatureC,
    Option<string> WindDescription,
    int PrecipitationProbabilityPercent,
    IReadOnlyList<string> Alerts,
    Option<string> Outlook = default,
    bool IsForecast = false)
{
    /// <summary>Workshop 1: "major weather overrides" — an active alert promotes weather to the lead.</summary>
    public bool HasMajorAlert => Alerts.Count > 0;
}

/// <summary>
///     Per-fuel-type price summary across stations in the coverage area.
///     Prices are per litre in the provider's reported currency (AUD in AU markets).
///     <see cref="CheapestStationSuburb" /> is the suburb/district of the specific station
///     reporting <see cref="MinPrice" /> (from HERE's <c>address.district</c>), so the script
///     can say WHERE the cheapest price is, not just what it is.
/// </summary>
public sealed record FuelPriceSummary(
    string FuelType,
    double MinPrice,
    double AveragePrice,
    int StationCount,
    DateTimeOffset? LatestModifiedUtc = null,
    string? CheapestStationSuburb = null);

public sealed record FuelPriceReport(IReadOnlyList<FuelPriceSummary> Summaries)
{
    public bool HasPrices => Summaries.Count > 0;
}
