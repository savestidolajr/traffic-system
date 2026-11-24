using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Resolves the ON-AIR read order of the trailing supplementary sections (weather and fuel)
///     from the station's configured <see cref="FillSource" /> order. The FillOrder is the
///     sequence fill sources are TRIED in once traffic alone doesn't fill the slot; it is not the
///     on-air order. This type derives the latter: weather and fuel read in the order they appear
///     in the FillOrder, with any source absent from the list falling to the end in canonical
///     weather-then-fuel order, so a legacy or partial list still yields a total order.
/// </summary>
public static class ContentSectionOrder
{
    /// <summary>
    ///     Resolve(fillOrder) -> the trailing supplementary sections in on-air read order, i.e.
    ///     Weather/Fuel in the order they appear in <paramref name="fillOrder" />. Sources absent
    ///     from the list fall to the end in canonical Weather-then-Fuel order, so a legacy or
    ///     partial list still yields a total order. Returns exactly the two supplementary
    ///     sections, deduplicated. MinorTraffic and Filler are NOT trailing sections: minor
    ///     traffic belongs with the traffic body, and filler is appended deterministically after
    ///     generation.
    /// </summary>
    public static IReadOnlyList<FillSource> Resolve(IReadOnlyList<FillSource> fillOrder)
    {
        var resolved = new List<FillSource>(2);
        foreach (var source in fillOrder)
        {
            if (source == FillSource.Weather && !resolved.Contains(FillSource.Weather))
                resolved.Add(FillSource.Weather);
            else if (source == FillSource.Fuel && !resolved.Contains(FillSource.Fuel))
                resolved.Add(FillSource.Fuel);
        }

        if (!resolved.Contains(FillSource.Weather))
            resolved.Add(FillSource.Weather);
        if (!resolved.Contains(FillSource.Fuel))
            resolved.Add(FillSource.Fuel);
        return resolved;
    }

    /// <summary>
    ///     Reads as "weather, then fuel" for the draft prompt. A single section returns just its
    ///     name ("weather"); an empty list returns "".
    /// </summary>
    public static string Describe(IReadOnlyList<FillSource> sections)
    {
        if (sections.Count == 0)
            return string.Empty;
        return string.Join(", then ", sections.Select(Name));
    }

    private static string Name(FillSource source) => source switch
    {
        FillSource.Weather => "weather",
        FillSource.Fuel => "fuel",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Only Weather/Fuel are trailing sections.")
    };
}
