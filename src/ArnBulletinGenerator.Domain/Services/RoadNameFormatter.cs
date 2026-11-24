using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Title-cases the SHOUTING (all-caps) road names HERE sometimes emits ("SMITH STREET MOTORWAY"
///     → "Smith Street Motorway") so they never reach the writer — and so never air — as caps.
///     <para>
///     Operates per word: only a word that is entirely upper-case is touched. Any word already
///     carrying a lower-case letter is correctly cased and passes through untouched, so a clean name
///     ("M4 Parramatta to Sydney CBD") is never re-cased into "To"/"Cbd". Route codes (M4, A38) and a
///     small set of state/locality acronyms (CBD, NSW…) stay upper even inside an all-caps name.
///     </para>
///     Idempotent and case-only: a downstream fabrication check that compares case-insensitively is
///     unaffected (the normalized name still matches the raw data it came from).
/// </summary>
public static partial class RoadNameFormatter
{
    [GeneratedRegex(@"^\(?[A-Z]{1,2}[0-9]+[A-Z]?\)?$")]
    private static partial Regex RouteCodeRegex();

    [GeneratedRegex(@"^([A-Z]{1,2})([0-9]+)([A-Z]?)$")]
    private static partial Regex RouteCodeCoreRegex();

    [GeneratedRegex(@"\b\d{4}\b|,\s*Australia\b|^\s*\d+\s*,", RegexOptions.IgnoreCase)]
    private static partial Regex RawAddressRegex();

    [GeneratedRegex(@"(?<left>[\p{L}\d])\s*/\s*(?<right>[\p{L}\d])", RegexOptions.CultureInvariant)]
    private static partial Regex SlashSeparatorRegex();

    /// <summary>
    ///     True when <paramref name="text" /> is a raw reverse-geocoded postal address rather than a
    ///     speakable road name — a house-number-led string, a 4-digit postcode, or a ", Australia"
    ///     country tail. HERE returns these for incidents whose only location is a lat/lng; a cleared
    ///     such item once aired verbatim ("Construction at 10, Arundel QLD 4214, Australia has
    ///     cleared", job e62fffd5), so an address-shaped name must never be voiced as a road. Route
    ///     codes (M80, B4014) are excluded — those are handled by <see cref="IsRouteCodeOnly" />.
    /// </summary>
    public static bool LooksLikeRawAddress(string text) =>
        !string.IsNullOrWhiteSpace(text) && !IsRouteCodeOnly(text) && RawAddressRegex().IsMatch(text);

    /// <summary>Acronyms that must stay upper even inside an all-caps name we title-case.</summary>
    private static readonly FrozenSet<string> KeepUpper =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "CBD", "NSW", "QLD", "SA", "WA", "ACT", "NT", "VIC", "TAS"
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly char[] AffixTrim = ['(', ')', '.', ',', ';', ':', '"', '\''];

    /// <summary>
    ///     Returns <paramref name="text" /> with any all-caps words title-cased; leaves text that is
    ///     already correctly cased (or has no letters) exactly as given.
    /// </summary>
    public static string ToSpeakable(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // HERE ramp/route names can carry a parenthetical direction qualifier — "M80 Metropolitan
        // Ring Road (Southbound)" — which the writer voices verbatim (fresh-live eval, Melbourne:
        // "on the ramp to M80 Metropolitan Ring Road (Southbound)"). The corridor's own Direction
        // field already carries the direction, so the parenthetical is pure data residue. Route-code
        // parentheticals like "(M2)" are untouched.
        string stripped = DirectionParentheticalRegex().Replace(text, "").TrimEnd();

        // HERE sometimes formats alternate/intersection road anchors with a literal slash
        // ("Todd Road / Cook Street"). ElevenLabs reads that punctuation as "slash", and the
        // citation-fix pass treats evidence values as source truth, so normalize it before source
        // text reaches the writer. Pure numeric dates/fractions are left alone.
        stripped = NormalizeSlashSeparators(stripped);

        // HERE also emits glued tokens — "Alfred StreetNorth" (a missing space between the road
        // type and a trailing qualifier), voiced verbatim by TTS as one garbled word (fresh-live
        // eval, Sydney CBD). Split ONLY a road-type word glued to a following capitalized word;
        // genuine compound names ("CityLink") don't start with a full road-type word and pass through.
        stripped = GluedRoadTypeRegex().Replace(stripped, "$1 $2");

        // HERE abbreviates the road type on some names — "Gateway Mwy", "Logan Mwy", "Smith St",
        // "Ferry Rd", "Warrego Hwy". ElevenLabs is free to read "St" as "Saint" and has no reading
        // at all for "Mwy", so these are TTS misreads waiting to happen rather than a style slip.
        // Aired on dev brisbane 2b0d8fc6 and goldcoast 9d3abf9c (2026-08-25). Matched only after a
        // capitalised word, so a sentence starting "St Kilda" or the word "strd" is untouched, and
        // only with a following boundary so "Sty" or "Rda" cannot match.
        stripped = AbbreviatedRoadTypeRegex().Replace(stripped, m =>
            $"{m.Groups[1].Value} {RoadTypeExpansions[m.Groups[2].Value.TrimEnd('.').ToLowerInvariant()]}");

        // HERE emits some hyphenated road names with spaces around the hyphen ("Labrador - Carrara
        // Road"); TTS reads the loose dash as a pause or "dash". Tighten it so the name reads as one.
        stripped = SpacedHyphenRegex().Replace(stripped, "$1-$2");
        bool changed = !string.Equals(stripped, text, StringComparison.Ordinal);

        string[] words = stripped.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            bool lastWord = i == words.Length - 1;
            // An abbreviation can sit either side of a hyphen ("Toll Plaza-W'mooloo"), and the
            // spaced-hyphen tightening above deliberately glues those into ONE space-separated
            // token — so expanding whole words alone would silently miss it. Descend into the
            // hyphen segments; a segment is "terminal" only when it ends the last word, which
            // keeps the St=Street/Saint guard intact.
            string expanded = words[i].Contains('-')
                ? ExpandHyphenSegments(words[i], lastWord)
                : ExpandSpokenAbbreviation(words[i], lastWord);
            string normalized = NormalizeWord(expanded);
            if (!string.Equals(normalized, words[i], StringComparison.Ordinal))
            {
                words[i] = normalized;
                changed = true;
            }
        }

        return changed ? string.Join(' ', words) : text;
    }

    private static string NormalizeSlashSeparators(string text) =>
        SlashSeparatorRegex().Replace(text, match =>
        {
            char left = match.Groups["left"].Value[0];
            char right = match.Groups["right"].Value[0];

            if (char.IsDigit(left) && char.IsDigit(right))
                return match.Value;

            return $"{left} and {right}";
        });

    [GeneratedRegex(@"\s*\((?:north|south|east|west)(?:ern)?(?:bound)?\)", RegexOptions.IgnoreCase)]
    private static partial Regex DirectionParentheticalRegex();

    [GeneratedRegex(@"\b(Street|Road|Avenue|Highway|Freeway|Motorway|Drive|Parade|Terrace|Boulevard|Crescent|Lane|Court|Place)([A-Z][a-z]+)\b")]
    private static partial Regex GluedRoadTypeRegex();

    /// <summary>
    ///     Abbreviated road types HERE emits, and the word a presenter says. Keyed lower-case; the
    ///     matched original supplies the preceding name's casing.
    /// </summary>
    private static readonly Dictionary<string, string> RoadTypeExpansions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["st"] = "Street",
            ["rd"] = "Road",
            ["ave"] = "Avenue",
            ["av"] = "Avenue",
            ["hwy"] = "Highway",
            ["mwy"] = "Motorway",
            ["fwy"] = "Freeway",
            ["dr"] = "Drive",
            ["pde"] = "Parade",
            ["cres"] = "Crescent",
            ["tce"] = "Terrace",
            ["blvd"] = "Boulevard",
            ["ln"] = "Lane",
            ["ct"] = "Court",
            ["pl"] = "Place",
            ["esp"] = "Esplanade",
        };

    // Must follow a capitalised name word, so "St Kilda Road" (where St is a saint, not a street)
    // and mid-sentence prose are both left alone. Optional trailing full stop is consumed.
    [GeneratedRegex(
        @"\b([A-Z][A-Za-z'\-]+)\s+(St|Rd|Ave|Av|Hwy|Mwy|Fwy|Dr|Pde|Cres|Tce|Blvd|Ln|Ct|Pl|Esp)\.?(?=\s|,|$)")]
    private static partial Regex AbbreviatedRoadTypeRegex();

    [GeneratedRegex(@"(\w)\s+-\s+(\w)")]
    private static partial Regex SpacedHyphenRegex();

    /// <summary>
    ///     Road-type abbreviations HERE emits inside Extent/Location free text ("Gold Coast Hwy",
    ///     "Pacific Mtwy", "Vincent St") expanded to their spoken form — the writer voices these
    ///     strings verbatim, and "shut from Gold Coast Hwy to Pacific Mtwy" is a data read-out, not
    ///     a script (frozen-eval baseline, Gold Coast). Word-level and surgical: only a known
    ///     abbreviation token is touched, any other word passes through unchanged.
    /// </summary>
    private static readonly Dictionary<string, string> SpokenExpansions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Rd"] = "Road",
            ["Hwy"] = "Highway",
            ["Fwy"] = "Freeway",
            ["Mwy"] = "Motorway",
            ["Mtwy"] = "Motorway",
            ["Mt"] = "Mount",
            ["Ave"] = "Avenue",
            ["Blvd"] = "Boulevard",
            ["Cres"] = "Crescent",
            ["Pde"] = "Parade",
            ["Tce"] = "Terrace",
            ["Cct"] = "Circuit",
            ["Byp"] = "Bypass",
            ["Ter"] = "Terrace",
            ["Brg"] = "Bridge",
            ["Bdge"] = "Bridge",
            ["Expy"] = "Expressway",
            ["Expwy"] = "Expressway",
            ["Av"] = "Avenue",
            ["Pkwy"] = "Parkway",
            ["Pwy"] = "Parkway",
            ["Cir"] = "Circuit",
            ["Esp"] = "Esplanade",
            // Measured in live dev data (2026-08-03): HERE emitted "Koonung Creek Trl" and this map
            // had no entry, so the source text kept "Trl" while the writer sensibly read "Trail".
            // The grounding reviewer then flagged the expansion as a spelling mismatch and the hard
            // gate binned a finished KIIS 101.1 bulletin for a filler cart. Trl was the only
            // unmapped abbreviation present in the corpus, so this stays surgical by design.
            ["Trl"] = "Trail",
            ["Cwy"] = "Causeway",
            // Same class, found the same way (2026-08-23): HERE emitted "Light Horse Intr" on a live
            // dev Sydney job. The writer correctly read "the Light Horse Interchange", so the bulletin
            // AIRED — but the evidence panel matches spoken text back to the source road name, and
            // "Interchange" could not be matched to an unexpandable "Intr", so ARN saw a voiced
            // incident with no evidence behind it. Cheaper symptom than the Trl case, same cause.
            // Re-measured rather than guessed: across 6,006 road-name strings in every committed
            // corpus plus that job, "Intr" was the ONLY unmapped abbreviation — every other
            // short token was a real word (Kings, Flat, Glen, Port). The measured-list approach
            // holds; it was one token behind live data.
            ["Intr"] = "Interchange",
            // HERE also contracts PLACE names, not just road types, and these are unreadable aloud.
            // Measured across 69k chars of live dev incident/corridor text (34 jobs, 2026-08-03):
            // "Toll Plaza - W'mooloo" and "HT Toll Plaza - North Sydney" were the only two, so this
            // stays a measured list rather than a gazetteer. Both killed a Sydney bulletin — the
            // writer expanded them correctly, as it must, and the grounding gate read the expansion
            // as a NAME_SPELLING violation and binned the script. Expanding at SOURCE keeps the
            // fidelity gate strict: the writer and the reviewer now see the same spoken name.
            ["W'mooloo"] = "Woolloomooloo",
            ["HT"] = "Harbour Tunnel",
            ["Gt"] = "Great",

            // Directional suffixes the feeds append to a road name, mostly on Melbourne freeways:
            // "Westgate Freeway Wbnd", "Mornington Peninsula Freeway Sbnd". Measured 26 occurrences
            // across test/corpus, and confirmed mangled by the live station voice on 2026-08-24 -
            // "Westgate Freeway Wbnd" is read aloud as "West Gate Freeway, WBND", letter by letter.
            // Expanded to the full spoken word so the rest of the pipeline sees the same token it
            // would have seen had the feed spelled it out (the direction handling downstream keys on
            // "westbound", never on "Wbnd").
            ["Nbnd"] = "northbound",
            ["Sbnd"] = "southbound",
            ["Ebnd"] = "eastbound",
            ["Wbnd"] = "westbound"
        };

    private static readonly FrozenSet<string> PlaceholderNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LOCAL ROAD",
            "LOCAL ROAD - CITY OF PERTH",
            "Unnamed Road",
            "Unidentified Road",
            "Unknown Location"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PointOfInterestTokens =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "station", "airport", "interchange", "complex", "centre", "plaza",
            "square", "park", "exit", "roundabout"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> RoadTypeTokens =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "road", "rd", "street", "st", "avenue", "ave", "av", "highway", "hwy",
            "freeway", "fwy", "motorway", "mwy", "mtwy", "drive", "dr", "parade",
            "pde", "terrace", "tce", "boulevard", "blvd", "crescent", "cres", "lane",
            "ln", "court", "ct", "place", "pl", "circuit", "cct", "bypass", "byp",
            "bridge", "br", "brg", "expressway", "expy", "expwy", "parkway", "pkwy",
            "pwy", "close", "cl", "grove", "gr", "esplanade", "esp", "trail", "trl",
            "causeway", "cwy", "way"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> DirectionalTokens =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "northbound", "southbound", "eastbound", "westbound", "citybound",
            "inbound", "outbound"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CompassSuffixTokens =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "n", "s", "e", "w"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Expands each hyphen-separated segment of one space-separated token. Only the FINAL segment
    ///     of the final word counts as terminal, so the ambiguous terminal-only abbreviations
    ///     (St=Street vs Saint, Dr, Ct, Pl, Ln) keep exactly the guard they have elsewhere.
    /// </summary>
    private static string ExpandHyphenSegments(string word, bool isLastWord)
    {
        string[] segments = word.Split('-');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length == 0)
                continue;
            segments[i] = ExpandSpokenAbbreviation(
                segments[i], isLastWord: isLastWord && i == segments.Length - 1);
        }

        return string.Join('-', segments);
    }

    private static string ExpandSpokenAbbreviation(string word, bool isLastWord)
    {
        string core = word.Trim(AffixTrim);
        // "St" is ambiguous — "Vincent St" (Street) vs "St Kilda Road" (Saint). Expand it only in
        // terminal position, where the Saint reading is impossible. "Dr"/"Ct"/"Pl"/"Ln" collide with
        // real words or initials mid-name the same way, so they get the same terminal-only guard.
        // A bare compass LETTER is HERE's suffix for a split road ("Garfield Road E", live dev job
        // 17c0b28e) and TTS reads it as the letter, so it gets the same treatment — terminal only,
        // because mid-name a single letter is an initial.
        bool terminalOnly = core is "St" or "Dr" or "Ct" or "Pl" or "Ln" or "N" or "S" or "E" or "W";
        if (terminalOnly)
        {
            if (!isLastWord) return word;
            string expandedTerminal = core switch
            {
                "St" => "Street",
                "Dr" => "Drive",
                "Ct" => "Court",
                "Pl" => "Place",
                "Ln" => "Lane",
                "N" => "North",
                "S" => "South",
                "E" => "East",
                "W" => "West",
                _ => core
            };
            return ReplaceCore(word, core, expandedTerminal);
        }

        return SpokenExpansions.TryGetValue(core, out string? expanded)
            ? ReplaceCore(word, core, expanded)
            : word;
    }

    /// <summary>Swaps the trimmed core back into the original word, preserving surrounding punctuation.</summary>
    private static string ReplaceCore(string word, string core, string replacement)
    {
        if (core.Length == word.Length)
            return replacement;
        int start = word.IndexOf(core, StringComparison.Ordinal);
        return word[..start] + replacement + word[(start + core.Length)..];
    }

    private static string NormalizeWord(string word)
    {
        bool hasUpper = false;
        bool hasLower = false;
        foreach (char c in word)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
        }

        // Not shouting (already cased, or no letters at all) — leave it exactly as given.
        if (!hasUpper || hasLower)
            return word;
        // Route codes (M4, A38, "(M2)") and state/locality acronyms stay upper.
        if (RouteCodeRegex().IsMatch(word) || KeepUpper.Contains(word.Trim(AffixTrim)))
            return word;

        return TitleCase(word);
    }

    /// <summary>Upper-cases the first letter of each run of letters, lower-cases the rest — except
    /// the unambiguous "Mc" surname prefix (McEvoy, McDonald, McQuarie Street), which keeps its
    /// internal capital ("Mc" + upper) rather than collapsing to "Mcevoy". "Mac" is deliberately
    /// NOT special-cased — it collides with real lower-case names (Macquarie, Mackenzie).</summary>
    private static string TitleCase(string word)
    {
        char[] chars = word.ToCharArray();
        bool wordStart = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                if (wordStart && i + 2 < chars.Length
                    && (chars[i] is 'M' or 'm') && (chars[i + 1] is 'C' or 'c')
                    && char.IsLetter(chars[i + 2]))
                {
                    chars[i] = 'M';
                    chars[i + 1] = 'c';
                    chars[i + 2] = char.ToUpperInvariant(chars[i + 2]);
                    i += 2;
                    wordStart = false;
                    continue;
                }

                chars[i] = wordStart ? char.ToUpperInvariant(chars[i]) : char.ToLowerInvariant(chars[i]);
                wordStart = false;
            }
            else
            {
                wordStart = true;
            }
        }

        return new string(chars);
    }

    /// <summary>Full-word forms for the road-type/direction abbreviations HERE's reverse-geocode
    /// results commonly use ("Rd" vs the corridor's own "Road") — the exact-string self-reference
    /// checks this normalizes for compare unequal despite naming the identical road (observed live:
    /// Extent "from Helensvale Rd to..." vs RoadName "Helensvale Road", both the SAME road).</summary>
    private static readonly Dictionary<string, string> AbbreviationExpansions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["rd"] = "road",
            ["st"] = "street",
            ["av"] = "avenue",
            ["ave"] = "avenue",
            ["dr"] = "drive",
            ["hwy"] = "highway",
            ["fwy"] = "freeway",
            ["mwy"] = "motorway",
            ["mtwy"] = "motorway",
            ["mt"] = "mount",
            ["cres"] = "crescent",
            ["ct"] = "court",
            ["ln"] = "lane",
            ["blvd"] = "boulevard",
            ["cl"] = "close",
            ["gr"] = "grove",
            ["cct"] = "circuit",
            ["byp"] = "bypass",
            ["pde"] = "parade",
            ["tce"] = "terrace",
            ["pl"] = "place",
            ["br"] = "bridge",
            // "Wakehurst Pkwy" (HERE map name) vs "Wakehurst Parkway" (Live Traffic NSW) compared
            // unequal and fell to a distance-2m match receipt (fusion audit, 2026-08-18).
            ["pkwy"] = "parkway",
            ["pwy"] = "parkway",
            ["gt"] = "great",
            ["citybound"] = "",
            ["outbound"] = "",
            ["inbound"] = "",
            ["eastbound"] = "",
            ["westbound"] = "",
            ["northbound"] = "",
            ["southbound"] = ""
        };

    /// <summary>
    ///     Returns true for HERE's non-road placeholders. Empty values are included because they
    ///     cannot be voiced or matched to a real road; these values appeared in the live corpus
    ///     alongside LOCAL ROAD and Unnamed Road.
    /// </summary>
    public static bool IsPlaceholderName(string? name) =>
        string.IsNullOrWhiteSpace(name) || PlaceholderNames.Contains(name.Trim());

    /// <summary>
    ///     Returns true when a HERE location is a point of interest rather than a road name. A
    ///     trailing road designation wins over a token such as Park ("Astoria Park Road"), while
    ///     terminal POI labels and the Exit-number form are rejected. This keeps 128 observed HERE
    ///     POI values out of road matching and voice generation.
    /// </summary>
    public static bool IsPointOfInterestName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || IsPlaceholderName(name))
            return false;

        string[] tokens = ParentheticalRegex().Replace(name, " ")
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(AffixTrim))
            .Where(token => token.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
            return false;

        string last = tokens[^1];
        if (tokens.Length >= 2
            && string.Equals(tokens[^2], "exit", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(last, out _))
        {
            return true;
        }

        return PointOfInterestTokens.Contains(last) && !RoadTypeTokens.Contains(last);
    }
    /// <summary>True when <paramref name="token"/> is a supported road-type word or abbreviation.</summary>
    public static bool IsRoadTypeToken(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && RoadTypeTokens.Contains(token.Trim(AffixTrim));


    /// <summary>
    ///     Splits an authority composite into its candidate names without deciding whether the
    ///     values describe one road or an intersection. The 85 observed slash-composites use slash
    ///     or ampersand separators; unsplit names are returned unchanged.
    /// </summary>
    public static IReadOnlyList<string> SplitComposite(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [name];

        string[] candidates = name.Split(
            ['/', '&'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return candidates.Length > 1 ? candidates : [name];
    }

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalRegex();

    /// <summary>
    ///     True when <paramref name="a" /> and <paramref name="b" /> name the SAME road — comparing
    ///     after expanding common street-type abbreviations ("Rd" → "road") and stripping direction
    ///     suffixes, so "Helensvale Rd" and "Helensvale Road citybound" are recognized as identical
    ///     rather than tripping a naive exact-string comparison into treating them as two places.
    /// </summary>
    public static bool IsSameRoad(string a, string b)
    {
        if (IsPlaceholderName(a) || IsPlaceholderName(b)
            || IsPointOfInterestName(a) || IsPointOfInterestName(b))
        {
            return false;
        }

        string canonicalA = RoadDesignationAliases.Canonicalize(NormalizeForComparison(a));
        string canonicalB = RoadDesignationAliases.Canonicalize(NormalizeForComparison(b));
        return NormalizeForComparison(canonicalA) == NormalizeForComparison(canonicalB);
    }

    /// <summary>
    ///     True when <paramref name="containing" /> names or embeds the road <paramref name="road" /> —
    ///     the same normalization as <see cref="IsSameRoad" /> but matched as a whole-word substring,
    ///     so a monitored-route label ("the Warringah Freeway into the city") is recognized as covering
    ///     the corridor road ("Warringah Freeway citybound").
    /// </summary>
    public static bool ContainsRoad(string containing, string road)
    {
        if (IsPlaceholderName(containing) || IsPlaceholderName(road)
            || IsPointOfInterestName(containing) || IsPointOfInterestName(road))
        {
            return false;
        }

        string normalizedContaining = NormalizeForComparison(containing);
        string normalizedRoad = NormalizeForComparison(road);
        string haystack = $" {NormalizeForComparison(RoadDesignationAliases.Canonicalize(normalizedContaining))} ";
        string needle = $" {NormalizeForComparison(RoadDesignationAliases.Canonicalize(normalizedRoad))} ";
        return needle.Trim().Length > 0 && haystack.Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    ///     True when <paramref name="text" /> is nothing but a bare numbered route code ("A1",
    ///     "B56", "M80") — HERE sometimes returns just the route number as an incident's Location
    ///     with no descriptive road name, which is exactly the shape reverse-geocoding can resolve
    ///     to the road's actual local name (e.g. Port Augusta's B56 → "Horrocks Pass Road"; live
    ///     feedback, 2026-07-06). Lets the geocoding gate treat route-code-only locations the same
    ///     as HERE's own "Unidentified Road"/"Unknown Location" placeholders.
    /// </summary>
    public static bool IsRouteCodeOnly(string text) =>
        !string.IsNullOrWhiteSpace(text) && RouteCodeRegex().IsMatch(text.Trim());

    /// <summary>
    ///     The road name to voice, given HERE's raw map name. HERE returns many roads as
    ///     "&lt;route code&gt; / &lt;name&gt;" ("A28 / Hume Hwy", "1 / Kennedy Hwy"), and voicing that
    ///     verbatim reads as "A28 slash Hume Highway", so the code is normally dropped.
    ///     <para>
    ///         It is NOT dropped when the code is the name in local use. ARN operators rewrote "the
    ///         Pacific Motorway at Cowan" to "the M1 at Cowan" by hand on test (jobs c4544250 and
    ///         591cdaa8, 2026-08-25). Sydney's own priority-roads config already agrees, listing
    ///         "M4 Western Motorway" and "M5 South West Motorway" — so today the map and the config
    ///         disagree with each other about the same road.
    ///     </para>
    ///     <para>
    ///         The decision is delegated, not invented here: the code and name are rejoined and
    ///         offered to <see cref="RoadDesignationAliases" />, which is ARN's curated list of
    ///         gazetted-name-to-designation mappings and already carries "M1 Pacific Mwy". A hit
    ///         means ARN has explicitly decided this road is voiced by its designation; a miss falls
    ///         through to the historic behaviour. That keeps this fail-safe across the eleven markets
    ///         with no entries at all: the worst case is that it does nothing.
    ///     </para>
    /// </summary>
    public static string PreferredRoadName(string? hereName)
    {
        if (string.IsNullOrWhiteSpace(hereName))
            return string.Empty;

        int slash = hereName.IndexOf(" / ", StringComparison.Ordinal);
        if (slash < 0)
            return hereName.Trim();

        string code = hereName[..slash].Trim();
        string name = hereName[(slash + 3)..].Trim();
        if (code.Length == 0)
            return name;

        string rejoined = $"{code} {name}";
        string designation = RoadDesignationAliases.Canonicalize(rejoined);
        return designation.Equals(rejoined, StringComparison.OrdinalIgnoreCase) ? name : designation;
    }

    /// <summary>
    ///     TTS-only speakability pass: a numbered route code ("A1", "B56", "M80") is two spoken
    ///     tokens to a human reader ("A one", "B fifty-six") but one glued alphanumeric token to
    ///     eleven_v3, which reads it as a slurred run-on with no pause between letter and number
    ///     (live feedback, Port Augusta 2026-07-06 — "A1" heard as "AH-ONE"). Splitting the letter
    ///     prefix from the digit run with a space gives the model two distinct tokens to voice.
    ///     <para>
    ///     Deliberately separate from <see cref="ToSpeakable" />: that method feeds script
    ///     generation, job storage, review-refine fact-checking, and on-screen display, where "A1"
    ///     must stay exactly "A1" (<c>ToSpeakable_AlreadyCasedNames_PassThrough</c> asserts this).
    ///     This pass runs ONLY on the text handed to the TTS API, immediately before synthesis, so
    ///     the stored/displayed script is never affected.
    ///     </para>
    /// </summary>
    public static string ExpandRouteCodesForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string normalized = NormalizeSlashSeparators(text);
        string[] words = normalized.Split(' ');
        bool changed = !string.Equals(normalized, text, StringComparison.Ordinal);
        for (int i = 0; i < words.Length; i++)
        {
            string spaced = SpaceOutRouteCode(words[i]);
            if (!ReferenceEquals(spaced, words[i]))
            {
                words[i] = spaced;
                changed = true;
            }
        }

        return changed ? string.Join(' ', words) : text;
    }

    private static string SpaceOutRouteCode(string word)
    {
        string core = word.Trim(AffixTrim);
        var match = RouteCodeCoreRegex().Match(core);
        if (!match.Success)
            return word;

        string letters = match.Groups[1].Value;
        string digits = match.Groups[2].Value;
        string trailingLetter = match.Groups[3].Value;
        string spaced = trailingLetter.Length > 0 ? $"{letters} {digits} {trailingLetter}" : $"{letters} {digits}";
        return ReplaceCore(word, core, spaced);
    }

    private static string NormalizeForComparison(string name)
    {
        string[] tokens = ParentheticalRegex().Replace(name, " ")
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(AffixTrim))
            .Where(token => token.Length > 0)
            .ToArray();
        var words = new List<string>(tokens.Length);

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i].ToLowerInvariant();
            if (words.Count == 0 && token == "the")
                continue;
            if (DirectionalTokens.Contains(token))
                continue;

            bool hasMeaningfulTokenAfter = tokens[(i + 1)..]
                .Select(next => next.Trim(AffixTrim))
                .Any(next => next.Length > 0
                    && !DirectionalTokens.Contains(next)
                    && !CompassSuffixTokens.Contains(next));
            if (CompassSuffixTokens.Contains(token) && !hasMeaningfulTokenAfter)
                continue;

            string expanded;
            if (token == "st" && words.Count == 0 && hasMeaningfulTokenAfter)
            {
                // Leading St is the saint abbreviation (St Kilda), never Street.
                expanded = "saint";
            }
            else
            {
                expanded = AbbreviationExpansions.TryGetValue(token, out string? value)
                    ? value
                    : token;
            }

            if (expanded.Length > 0)
                words.Add(expanded);
        }

        return string.Join(' ', words);
    }
}
