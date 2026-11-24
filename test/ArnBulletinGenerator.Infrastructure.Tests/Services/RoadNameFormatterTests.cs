using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Services;

public class RoadNameFormatterTests
{
    [Theory]
    // All-caps shouting names are title-cased.
    [InlineData("SMITH STREET MOTORWAY", "Smith Street Motorway")]
    [InlineData("PACIFIC HIGHWAY", "Pacific Highway")]
    // Route codes and acronyms stay upper, even embedded in an all-caps name.
    [InlineData("M4 WESTERN MOTORWAY", "M4 Western Motorway")]
    [InlineData("REID HWY AT TONKIN HWY", "Reid Highway At Tonkin Highway")]
    // A lower-case connector word in an otherwise-shouting phrase is left, caps words fixed.
    [InlineData("near SANDGATE ROAD", "near Sandgate Road")]
    public void ToSpeakable_ShoutingNames_AreTitleCased(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Theory]
    // HERE ramp names carry parenthetical direction qualifiers the writer voices verbatim
    // ("...ramp to M80 Metropolitan Ring Road (Southbound)", fresh-live eval Melbourne). The
    // corridor's Direction field already carries the direction — the parenthetical is residue.
    // Route-code parentheticals ("(M2)") are information and survive.
    [InlineData("M80 Metropolitan Ring Road (Southbound)", "M80 Metropolitan Ring Road")]
    [InlineData("Ring Road (northbound)", "Ring Road")]
    [InlineData("Western Highway (M8)", "Western Highway (M8)")]
    [InlineData("(EASTBOUND)", "")]
    public void ToSpeakable_DirectionParentheticals_AreStripped(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Theory]
    // Observed live: HERE sends "MCEVOY STREET" all-caps; naive title-casing collapsed the
    // internal capital to "Mcevoy" instead of the correct "McEvoy". "Mac" is NOT special-cased
    // (Macquarie, Mackenzie are real lower-case names) — only the unambiguous "Mc" prefix.
    [InlineData("MCEVOY STREET", "McEvoy Street")]
    [InlineData("MCDONALD ROAD", "McDonald Road")]
    [InlineData("MACQUARIE STREET", "Macquarie Street")]
    [InlineData("MC", "Mc")]
    public void ToSpeakable_McPrefixedNames_KeepInternalCapital(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Theory]
    // Correctly-cased names pass through UNTOUCHED — never re-cased into "To"/"Cbd".
    [InlineData("M4 Parramatta to Sydney CBD")]
    [InlineData("Smith Street Motorway")]
    [InlineData("Pacific Motorway to Surfers Paradise")]
    [InlineData("O'Riordan Street")]
    [InlineData("")]
    public void ToSpeakable_AlreadyCasedNames_PassThrough(string input) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(input);

    [Fact]
    public void ToSpeakable_IsIdempotent()
    {
        string once = RoadNameFormatter.ToSpeakable("SMITH STREET MOTORWAY");
        RoadNameFormatter.ToSpeakable(once).ShouldBe(once);
    }

    [Theory]
    // Road-type abbreviations HERE emits in Extent/Location free text are expanded to their
    // spoken form — the writer voices these verbatim, and "shut from Gold Coast Hwy to Pacific
    // Mtwy" aired as a data read-out (frozen-eval baseline, Gold Coast).
    [InlineData("from Gold Coast Hwy to Pacific Mtwy", "from Gold Coast Highway to Pacific Motorway")]
    [InlineData("Helensvale Rd", "Helensvale Road")]
    [InlineData("Kwinana Fwy", "Kwinana Freeway")]
    [InlineData("Grand Pde", "Grand Parade")]
    [InlineData("Monash Mwy", "Monash Motorway")]
    public void ToSpeakable_RoadTypeAbbreviations_AreExpanded(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Theory]
    // HERE's Detail/Extent free text sometimes joins two road names with a literal slash
    // ("Between 8 Mile Bridge and Todd Road / Cook Street") — the writer copies this text
    // into evidence verbatim, and ElevenLabs reads the "/" aloud on air (job
    // dda2471bce5c4a69addeafbf8e475b4b, Melbourne West Gate roadworks). The slash must
    // become the speakable "and" before evidence ever reaches the writer.
    [InlineData("Between 8 Mile Bridge and Todd Road / Cook Street", "Between 8 Mile Bridge and Todd Road and Cook Street")]
    [InlineData("Todd Road/Cook Street", "Todd Road and Cook Street")]
    public void ToSpeakable_SlashSeparatedRoadNames_BecomeSpeakableAnd(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Theory]
    // A digit/digit slash is a fraction or date reading, not a road-pair separator —
    // must NOT become "5 and 8", which would misreport the fraction as two numbers.
    [InlineData("Lanes reduced to 5/8 of the road")]
    public void ToSpeakable_DigitSlashDigit_IsNotTreatedAsRoadSeparator(string input) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(input);

    [Theory]
    // "St" is ambiguous (Street vs Saint) — expanded ONLY in terminal position, where the Saint
    // reading is impossible. Same guard for Dr/Ct/Pl/Ln, which collide with real words mid-name.
    [InlineData("Vincent St", "Vincent Street")]
    [InlineData("St Kilda Road", "St Kilda Road")]
    [InlineData("St Georges Tce", "St Georges Terrace")]
    [InlineData("Punt Dr", "Punt Drive")]
    [InlineData("Dr Lawson Place", "Dr Lawson Place")]
    public void ToSpeakable_AmbiguousAbbreviations_ExpandOnlyTerminal(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Theory]
    // A monitored-route label embeds the corridor's road — whole-word, abbreviation- and
    // direction-insensitive containment. Powers the closure/monitored-route contradiction
    // suppression in TrafficNarrativeSummarizer.
    [InlineData("the Warringah Freeway into the city", "Warringah Freeway citybound", true)]
    [InlineData("M4 Parramatta to Sydney CBD", "M4", true)]
    [InlineData("the Pacific Hwy run", "Pacific Highway", true)]
    [InlineData("the Monash Freeway into town", "Warringah Freeway", false)]
    [InlineData("Epping Road", "Epping Road citybound", true)]
    [InlineData("", "Warringah Freeway", false)]
    public void ContainsRoad_MatchesEmbeddedRoadNames(string containing, string road, bool expected) =>
        RoadNameFormatter.ContainsRoad(containing, road).ShouldBe(expected);

    [Theory]
    // Observed live (Gold Coast): "Helensvale Rd" (reverse-geocoded, abbreviated) and "Helensvale
    // Road citybound" (the corridor's own spelled-out name + direction suffix) name the SAME road,
    // but an exact-string compare treats them as different — which let a self-referential Extent
    // ship as "closed from Helensvale Road to Country Club Drive".
    [InlineData("Helensvale Rd", "Helensvale Road citybound")]
    [InlineData("Church St", "Church Street")]
    [InlineData("Pacific Hwy", "Pacific Highway")]
    [InlineData("M4", "M4")]
    [InlineData("Reid Highway outbound", "Reid Highway")]
    public void IsSameRoad_RecognizesAbbreviationsAndDirectionSuffixes(string a, string b) =>
        RoadNameFormatter.IsSameRoad(a, b).ShouldBeTrue();

    [Theory]
    [InlineData("Helensvale Road", "Country Club Drive")]
    [InlineData("Church Street", "King Street")]
    [InlineData("M4", "M5")]
    public void IsSameRoad_GenuinelyDifferentRoads_ReturnsFalse(string a, string b) =>
        RoadNameFormatter.IsSameRoad(a, b).ShouldBeFalse();

    [Theory]
    [InlineData("", "Church Street")]
    [InlineData("Church Street", "")]
    [InlineData(null, "Church Street")]
    public void IsSameRoad_EmptyOrNullInput_ReturnsFalse(string? a, string b) =>
        RoadNameFormatter.IsSameRoad(a!, b).ShouldBeFalse();
    [Theory]
    // Route codes identified by RouteCodeRegex: 1-2 leading letters + digits, optionally
    // wrapped in parens. Used by geocoding to detect bare route-designation locations
    // (e.g. HERE returns "B56" instead of "Horrocks Pass Road" for Port Augusta).
    [InlineData("A1", true)]
    [InlineData("B56", true)]
    [InlineData("M80", true)]
    [InlineData(" A1 ", true)]
    [InlineData("(M2)", true)]
    [InlineData("Church Street", false)]
    [InlineData("George St", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("99", false)]
    public void IsRouteCodeOnly(string? text, bool expected) =>
        RoadNameFormatter.IsRouteCodeOnly(text!).ShouldBe(expected);

    [Theory]
    // TTS-only speakability pass: route codes get spaced ("A1" → "A 1") so the TTS model
    // produces two distinct spoken tokens instead of a slurred run-on. Non-route-code
    // sentences pass through unchanged; route codes embedded in longer sentences are spaced
    // while the surrounding words stay untouched.
    [InlineData("A1 is closed", "A 1 is closed")]
    [InlineData("Construction on B56 has cleared", "Construction on B 56 has cleared")]
    [InlineData("Church Street is busy today", "Church Street is busy today")]
    [InlineData("M4 Parramatta to Sydney CBD", "M 4 Parramatta to Sydney CBD")]
    [InlineData("(M2) is busy", "(M 2) is busy")]
    public void ExpandRouteCodesForSpeech(string input, string expected) =>
        RoadNameFormatter.ExpandRouteCodesForSpeech(input).ShouldBe(expected);

    [Theory]
    // Regression guard: ToSpeakable (used for script generation/storage/display) must NOT
    // apply route-code spacing — "A1" must stay exactly "A1" in the stored script. Only
    // ExpandRouteCodesForSpeech (TTS-only, applied at synthesis time) spaces route codes.
    [InlineData("A1 is closed")]
    [InlineData("Construction on B56 has cleared")]
    public void ToSpeakable_DoesNotSpaceRouteCodes(string input) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(input);

}
