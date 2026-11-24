using System.Globalization;
using System.Text;

namespace ArnBulletinGenerator.Workflow.Prompts;

/// <summary>
///     Selects a small, deterministic ATN ground-truth bank for the composer. The bank is a
///     compiled asset rather than a per-job file read: the selector stays pure and the stable
///     system-prefix cache is not invalidated by a rotating network fetch.
///     <para>
///         Every read here is BODY COPY only. The production wrapper an aircheck carries — sponsor
///         and hotline labels, the station ident, the presenter's opener and sign-off, a branded
///         weather prefix, driver advice — is removed, because the composer writes the body and
///         nothing else: <c>BulletinBookendComposer</c> adds the station's intro and sign-off
///         around it afterwards. An exemplar that carries a wrapper DEMONSTRATES the output one
///         prompt line forbids, and demonstration beats instruction: dev goldcoast job
///         <c>de2d1b60</c> (2026-09-03) opened "It's Hot Tomato traffic:" — the exemplars' own
///         sign-off shape with the station name from the brief header substituted — and then the
///         configured intro was prepended in front of it, airing two openers.
///     </para>
///     <para>
///         <see cref="AtnExemplar.WordCount" /> is the body count, and it is load-bearing twice:
///         <see cref="Render(int,string,string?,string,DateOnly)" /> ranks by distance from the
///         composer's word target AND prints it in the exemplar label. Wrapper words inflated it
///         by roughly a quarter, so a 67-word body target was shown, and told to model, reads of
///         90 to 112 words.
///     </para>
/// </summary>
public static class ExemplarSelector
{
    /// <summary>One ATN read's body copy and its selection metadata.</summary>
    public sealed record AtnExemplar(
        string Id,
        string Text,
        int WordCount,
        string RegionClass,
        string Daypart,
        string Density);

    /// <summary>All 21 metro and regional reads in the runtime bank, body copy only.</summary>
    public static IReadOnlyList<AtnExemplar> Bank { get; } = new AtnExemplar[]
    {
        new(
            "gold1043-0700",
            """
            The West Gate Bridge chockers citybound with a crash with two centre
            lanes out of action and delays back out onto the Princes Freeway.
            There's also a breakdown in the West Gate Tunnel citybound before the
            Dynon Road exit. And a crash on the Deer Park Bypass citybound after
            Christies Road has traffic slow going back to Leakes Road. And a power
            outage has lights out through Ringwood along the Ringwood Bypass,
            Maroondah Highway and Ringwood Street.
            """,
            77,
            "metro",
            "morning",
            "busy"),
        new(
            "gold1043-0939",
            """
            Absolutely chockers on the West Gate citybound from the Princes
            Freeway over the bridge into the Burnley Tunnel — it's desperately
            trying to recover following peak and an earlier crash on the bridge.
            Delays also pushing onto the Bolte Bridge just for good measure. As
            well, it's patchy for the Monash citybound between EastLink and
            Warrigal Road, the usual hotspot. Works on the Tulla outbound at
            Mickleham Road, speed and lane restrictions set up — no dramas
            citybound till you get to the Bolte.
            """,
            82,
            "metro",
            "morning",
            "busy"),
        new(
            "kiis1011-0829",
            """
            It is awful on the West Gate citybound — delays from the Princes
            Freeway to the Burnley Tunnel following an earlier crash. The Tulla
            hosting debris citybound after Racecourse Road, lanes closed, speeds
            down, delays back to the Bulla Road exit. A bit of morning smoke,
            partly cloudy afternoon, a top of 24 degrees on the menu.
            """,
            56,
            "metro",
            "morning",
            "busy"),
        new(
            "kiis1011-1158",
            """
            Works affecting your run outbound on the Tullamarine Freeway at
            Mickleham Road — speed and lane restrictions are in place with some
            minor delays. Also delays citybound on the West Gate Freeway: you'll
            find it slow from the West Gate Bridge through to the Burnley Tunnel.
            To give you an idea how long it'll take you to drive from Forsyth Road
            on the Princes to the Burnley — 22 minutes. Partly cloudy today with a
            top of 24.
            """,
            76,
            "metro",
            "midday",
            "light"),
        new(
            "96fm-0759",
            """
            The freeway is looking a bit more full now: so, Freeway southbound at
            Burns Beach Road, again Whitfords Avenue through to Reid Highway and
            Hutton Street through to the Powis. Kwinana Freeway northbound Thomas
            Road through to Rowley Road, then busy Farrington Road through to
            Cranford Avenue as well. Tonkin Highway slow southbound at Reid
            Highway and in both directions at Welshpool Road East. Also Roe
            Highway slow southbound at the Great Eastern Highway Bypass at Tonkin
            Highway, and northbound at Nicholson Road too.
            """,
            84,
            "metro",
            "morning",
            "busy"),
        new(
            "96fm-0814",
            """
            Mitchell Freeway southbound slow Burns Beach Road, Whitfords Avenue to
            Reid Highway, and Hutton Street to the Powis. Kwinana Freeway
            northbound Thomas Road to Rowley Road, Farrington Road to Cranford
            Avenue as well. And Roe Highway slow southbound at the Great Eastern
            Highway Bypass at Tonkin Highway, and northbound at Nicholson Road.
            """,
            52,
            "metro",
            "morning",
            "light"),
        new(
            "cada-0750",
            """
            Mays Hill: M4 medical emergency eastbound in the breakdown lane at
            Coleman Street. Traffic is fairly busy for the Ryde Bridge from Rhodes
            to Top Ryde citybound, slowing up to North Ryde, Lane Cove Road or
            Quarry Road.
            """,
            38,
            "metro",
            "morning",
            "light"),
        new(
            "cada-0822",
            """
            It's very busy for the Ryde Bridge from Rhodes to Top Ryde citybound,
            following a breakdown that's cleared off North Ryde at Lane Cove Road.
            The M2 chewing up citybound North Ryde to the Lane Cove Tunnel. Very
            heavy M4 motorway citybound at Eastern Creek — stacks of traffic at
            the M7/M2 merger, Bella Vista. We've got slow traffic as well at
            Oatlands, Pennant Hills Road, coming towards the school zones. Busy
            for the M5 citybound through Moorebank into Revesby.
            """,
            80,
            "metro",
            "morning",
            "busy"),
        new(
            "wsfm-0814",
            """
            At Ryde, a crash at Cressy Road and down on Lane Cove Road it's
            northbound in the right-hand lane approaching Hancock Street; also
            slow north- and southbound over the Ryde Bridge at the moment. On the
            M4 motorway at Mays Hill there's a medical emergency, cars in the
            breakdown lane citybound just before Coleman Street — emergency crews
            are on site and traffic is slow past the site as well. Built up now
            from Frenchs Forest as far as Warringah Road heading south from the
            underpass to Starkey Street, and slowing down eastbound into the Lane
            Cove Tunnel on the M2.
            """,
            100,
            "metro",
            "morning",
            "busy"),
        new(
            "wsfm-1058",
            """
            In Macquarie Park a car has broken down on Lane Cove Road northbound
            approaching Waterloo Road — the left lane is blocked off. At
            Haberfield a truck breakdown eastbound on City West Link approaching
            James Street. Burwood: an over-height truck stuck on Burwood Road at
            the railway bridge, both ways affected. And Centenary Drive under pace
            in Strathfield approaching Arthur Street. Drummoyne: slight congestion
            on Victoria Road coming up to Lyons Road, and General Holmes Drive
            near the airport approaching Foreshore Road also struggling. The M4
            eastbound from Mulgoa Road to Church Street Parramatta, a 20-minute
            run.
            """,
            96,
            "metro",
            "morning",
            "busy"),
        new(
            "kiis1065-0739",
            """
            The M7 building eastbound from Richmond Road to the M2 at Windsor
            Road, taking just under 20 minutes. A crash in Cecil Hills, Cowpasture
            Road northbound approaching Elizabeth Drive. A breakdown in Rose Hill
            at the motorway, James Ruse Drive northbound past Grand Avenue.
            Sunshine, 29 in the city, 35 in the west.
            """,
            53,
            "metro",
            "morning",
            "busy"),
        new(
            "kiis1065-0749",
            """
            We're seeing a 36-minute run westbound on the M4, Woodville Road to
            Church Street, caught up in this medical emergency with a reduced 40k
            speed — it's in the breakdown lane citybound approaching Coleman
            Street, Mays Hill. A crawl through the Rozelle Interchange on
            citybound approaches to the Anzac Bridge. A warm sunny day, 29 in the
            city, 35 in the west.
            """,
            61,
            "metro",
            "morning",
            "busy"),
        new(
            "gympie-morning",
            """
            Looking good between the Coast and Gympie. Got delays down through
            North Lakes — that's around 10 minutes on top of your trip — and
            getting heavier through Mountain Creek and the Sunshine Motorway.
            Beckmans Road (?). Noosaville State School (?) — school drop-offs
            underway, school-zone speed limits in place. Bit of traffic on Channon
            Street (?) around Louisa Street and around the KFC. Also Exhibition
            Road, building on the south side now around the school.
            """,
            70,
            "regional",
            "morning",
            "busy"),
        new(
            "ipswich-morning",
            """
            Traffic on the Centenary Highway is a heavy run northbound through
            Sumner, and it looks good for the Western Freeway — no delays through
            Indooroopilly, Fig Tree Pocket. The Ipswich Motorway is busy between
            the Centenary Highway and Oxley Road. East Street is also queuing up a
            little at times at the Brisbane and Limestone Street traffic lights.
            """,
            57,
            "regional",
            "morning",
            "busy"),
        new(
            "darwin-morning",
            """
            No major delays across Darwin this morning, with the drive into the
            city looking pretty good — traffic moving well on McMinn Street,
            Cavanagh Street and along the Esplanade. Around the airport, Bagot
            Road and McMillans Road are also running smoothly. Watch for crews
            packing up night works on Berrimah Road at Export Drive, while
            roadworks continue on the Stuart Highway near Bilroy Road (?).
            """,
            63,
            "regional",
            "morning",
            "light"),
        new(
            "nowra-morning",
            """
            You'll have a good run this morning on the Princes Highway, north- and
            southbound, going past Turpentine Road. Also Falls Creek, Tourist Bay
            Road (?) clear to the Princes Highway. Cambewarra Mountain. Steady-
            flowing traffic on Moss Vale Road going eastbound. And from the
            Southern Highlands, no delays — the Motorway going north- and
            southbound.
            """,
            52,
            "regional",
            "morning",
            "all-clear"),
        new(
            "murray-bridge-morning",
            """
            A smooth run across the regions this morning, with traffic flowing
            nicely throughout the Murraylands, South Coast and Hills areas —
            including Adelaide Road around Mannum Road and through Mount Barker.
            Just watch for roadworks on the South Eastern Freeway near Callington,
            and Inman Valley Road near Torrens Vale Road (?) at Bald Hills —
            that's where speed restrictions are in place around ongoing roadworks.
            """,
            62,
            "regional",
            "morning",
            "light"),
        new(
            "ballarat-morning",
            """
            Having a look around town, there's no delays or collisions — it's a
            nice run for your Thursday morning. Otherwise, have a look at your run
            into Melbourne: it's flying through on the Western Freeway, you get to
            Ferris Road and delays start up through to Hopkins Road. As for the
            Deer Park Bypass, a slow run from Robinsons Road on the Ring Road,
            where it's banked up all the way through to the West Gate.
            """,
            75,
            "regional",
            "morning",
            "light"),
        new(
            "wollongong-morning",
            """
            In Coniston, Spring Hill Road's seeing a bit of congestion in both
            directions just past Masters Road. The Princes Highway's moving well
            for both north- and southbounders — much the same for the M1, with a
            bit of activity at the on- and off-ramps to Five Islands Road.
            Cataract to Heathcote (?) via the Princes Highway northbound — a
            22-minute run.
            """,
            58,
            "regional",
            "morning",
            "light"),
        new(
            "sunshine-coast-morning",
            """
            I've got a breakdown on the Bruce Highway northbound through Murrumba
            Downs, not causing any delays though. It is slow southbound through
            North Lakes, but easing — it's a couple of minutes. The Sunshine
            Motorway's also a couple of minutes through Mountain Creek. And a bit
            of traffic through Kunda Park, Maroochydore Road, the Nicklin Way
            through Bokarina, and Caloundra Road, Kawana Way, the Link Road
            roundabout, Meridan Plains.
            """,
            68,
            "regional",
            "morning",
            "light"),
        new(
            "townsville-morning",
            """
            Well, traffic is settling down on Woolcock Street between Cosgrove and
            Mount Louisa. It's been heavier at times on Dalrymple Road through
            Mount Louisa, around Bamford Lane, and also around Nathan Street at
            Heatley. Steady on Sturt Street around the CBD, and also Ross River
            Road through Cranbrook.
            """,
            48,
            "regional",
            "morning",
            "light")
    };

    private static readonly string[] AntiAtnPhrases =
    [
        "give it a miss",
        "avoid [ROAD]",
        "on record",
        "in the system",
        "the listed roads",
        "cut that line",
        "drop that line",
        "the source",
        "not stated",
        "monitored routes"
    ];

    /// <summary>
    ///     Renders three style exemplars. The explicit date overload is deterministic and has no
    ///     I/O; callers without a supplied local date use the current UTC date only to select the
    ///     daily cache variant.
    /// </summary>
    public static string Render(int targetWords, string marketClass, string? daypart, string stationId, DateOnly localDate)
    {
        string region = string.Equals(marketClass, "regional", StringComparison.OrdinalIgnoreCase)
            ? "regional" : "metro";
        string? requestedDaypart = NormalizeDaypart(daypart);
        var candidates = Bank.Where(e => e.RegionClass == region).ToList();
        int seed = StableHash($"{stationId}|{localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{requestedDaypart ?? "unknown"}");
        var matching = candidates.Where(e => requestedDaypart is not null && e.Daypart == requestedDaypart)
            .OrderBy(e => Math.Abs(e.WordCount - targetWords)).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        var other = candidates.Where(e => !matching.Contains(e))
            .OrderBy(e => Math.Abs(e.WordCount - targetWords)).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        Rotate(matching, seed);
        Rotate(other, seed / 31);
        var ordered = matching.Concat(other).ToList();
        var selected = new List<AtnExemplar>(3);
        foreach (AtnExemplar candidate in ordered)
        {
            if (selected.Count == 3)
                break;
            if (selected.Count == 1 && candidate.Density == selected[0].Density
                && ordered.Any(e => e.Density != selected[0].Density))
                continue;
            selected.Add(candidate);
        }
        foreach (AtnExemplar candidate in ordered)
        {
            if (selected.Count == 3)
                break;
            if (!selected.Contains(candidate))
                selected.Add(candidate);
        }
        var output = new StringBuilder();
        output.AppendLine("Use these ATN reads as style reference only; use none of their facts.");
        for (int index = 0; index < selected.Count; index++)
        {
            AtnExemplar exemplar = selected[index];
            output.Append("[ATN exemplar ").Append(index + 1).Append(" | ")
                .Append(exemplar.RegionClass).Append(" | ").Append(exemplar.Daypart).Append(" | ")
                .Append(exemplar.Density).Append(" | ").Append(exemplar.WordCount).AppendLine(" words]");
            output.AppendLine(exemplar.Text);
        }
        output.AppendLine("ANTI-ATN PHRASES — never voice these production or source terms:");
        foreach (string phrase in AntiAtnPhrases)
            output.Append("- ").AppendLine(phrase);
        return output.ToString().TrimEnd();
    }

    /// <summary>Uses the current UTC date when the workflow has no clock/date boundary to pass in.</summary>
    public static string Render(int targetWords, string marketClass, string? daypart, string stationId) =>
        Render(targetWords, marketClass, daypart, stationId, DateOnly.FromDateTime(DateTime.UtcNow));

    private static string? NormalizeDaypart(string? daypart) => daypart?.Trim().ToLowerInvariant() switch
    {
        "morning" => "morning",
        "midday" => "midday",
        "afternoon" => "afternoon",
        "evening" => "evening",
        _ => null
    };

    private static void Rotate(List<AtnExemplar> items, int seed)
    {
        if (items.Count < 2)
            return;
        int offset = (int)((uint)seed % (uint)Math.Min(items.Count, 5));
        if (offset == 0)
            return;
        var rotated = items.Skip(offset).Concat(items.Take(offset)).ToList();
        items.Clear();
        items.AddRange(rotated);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }
}
