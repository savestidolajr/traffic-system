using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class RoadNameFormatterTests
{
    [Theory]
    // A house-number-led reverse-geocoded address, a ", Australia" country tail, or an
    // embedded 4-digit postcode all mark text as a raw address rather than a speakable road
    // name (job e62fffd5: "Construction at 10, Arundel QLD 4214, Australia has cleared" aired
    // verbatim).
    [InlineData("10, Arundel QLD 4214, Australia")] // trips all three branches at once
    [InlineData("3, Green Street")] // house-number-led only: ^\d+\s*, branch, no postcode/country
    [InlineData("Foo Road, Australia")] // country tail only, no digits at all
    [InlineData("12 Smith Street, Southport QLD 4215")] // 4-digit postcode only, not house-number-led
    public void LooksLikeRawAddress_RawAddressShapes_ReturnsTrue(string text) =>
        RoadNameFormatter.LooksLikeRawAddress(text).ShouldBeTrue();

    [Theory]
    [InlineData("Smith Street")]
    [InlineData("Smith Street Motorway")]
    [InlineData("Pacific Motorway")]
    [InlineData("M80 Metropolitan Ring Road")]
    [InlineData("")]
    public void LooksLikeRawAddress_SpeakableRoadNames_ReturnsFalse(string text) =>
        RoadNameFormatter.LooksLikeRawAddress(text).ShouldBeFalse();

    [Theory]
    // Bare route codes must never be voiced as addresses — "B4014" is the two-letter-prefix,
    // 4-digit-body equivalence class ("4014" is glued to "B" with no word boundary, so it
    // never trips the postcode branch on its own, but the digit run is postcode-length and a
    // future regex change reintroducing that boundary would need the IsRouteCodeOnly gate to
    // still exclude it), distinct from the 1-letter/2-digit class "M80" already covers.
    [InlineData("M80")]
    [InlineData("B4014")]
    public void LooksLikeRawAddress_BareRouteCodes_ReturnsFalse(string text) =>
        RoadNameFormatter.LooksLikeRawAddress(text).ShouldBeFalse();

    [Theory]
    // HERE's abbreviated "Gt" and "Av" forms are compared and voiced using their full words.
    [InlineData("Railway Ter", "Railway Terrace")]
    [InlineData("Captain Cook Brg", "Captain Cook Bridge")]
    [InlineData("Cahill Expy", "Cahill Expressway")]
    [InlineData("Mirrabooka Av", "Mirrabooka Avenue")]
    [InlineData("Marmion Av", "Marmion Avenue")]
    [InlineData("Kessels Pkwy", "Kessels Parkway")]
    [InlineData("Gt Western Hwy", "Great Western Highway")]
    [InlineData("Labrador - Carrara Road", "Labrador-Carrara Road")]
    [InlineData("SOUTHERN CROSS WAY (NORTHBOUND)", "Southern Cross Way")]
    // Measured 26x in test/corpus and confirmed letter-spelled by the live voice on 2026-08-24:
    // "Westgate Freeway Wbnd" was read aloud as "West Gate Freeway, WBND".
    [InlineData("Westgate Freeway Wbnd", "Westgate Freeway westbound")]
    [InlineData("Mornington Peninsula Freeway Sbnd", "Mornington Peninsula Freeway southbound")]
    [InlineData("Westgate Freeway Ebnd", "Westgate Freeway eastbound")]
    public void ToSpeakable_expandsAbbreviationsAndCleansArtifacts(string raw, string expected) =>
        RoadNameFormatter.ToSpeakable(raw).ShouldBe(expected);

    [Theory]
    // HERE suffixes a split road with a bare compass LETTER ("Garfield Road E", live dev job
    // 17c0b28e). TTS reads that as the letter "E", and the provenance mapper needs the expanded
    // form to recognise the read's "Garfield Road East" as this corridor.
    [InlineData("Garfield Road E", "Garfield Road East")]
    [InlineData("KINGSWAY S", "Kingsway South")]
    // Terminal only: mid-name a single letter is an initial, not a direction.
    [InlineData("E Boundary Road", "E Boundary Road")]
    public void ToSpeakable_ExpandsTerminalCompassLetter(string raw, string expected) =>
        RoadNameFormatter.ToSpeakable(raw).ShouldBe(expected);

    // ---------------------------------------------------------------------------
    // Trl. HERE emitted "Koonung Creek Trl" on 2026-08-03 with no entry in the
    // expansion map, so source text handed to the writer kept the abbreviation.
    // The writer read it aloud as "Trail" - correct for radio - and the grounding
    // reviewer flagged the expansion as a NAME_SPELLING mismatch, whose hard gate
    // binned a finished KIIS 101.1 bulletin in favour of a filler cart.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Koonung Creek Trl", "Koonung Creek Trail")]
    [InlineData("Yarra Trl", "Yarra Trail")]
    [InlineData("Federal Cwy", "Federal Causeway")]
    public void ToSpeakable_ExpandsTrailAndCauseway(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Fact]
    public void ToSpeakable_TrailExpansion_LeavesTheBaseNameUntouched()
    {
        // Only the road-type token may change: the base name is what the grounding gate
        // legitimately compares, so expanding a suffix must never rewrite it.
        RoadNameFormatter.ToSpeakable("Koonung Creek Trl").ShouldStartWith("Koonung Creek");
        RoadNameFormatter.ToSpeakable("Trlbeck Road").ShouldBe("Trlbeck Road");
    }

    // ---------------------------------------------------------------------------
    // HERE contracts PLACE names too. "Toll Plaza - W'mooloo" and "HT Toll Plaza -
    // North Sydney" (both live, 2026-08-03) are unreadable aloud, so the writer
    // expanded them - and the grounding gate read the expansion as a NAME_SPELLING
    // violation and binned a whole Sydney bulletin for a filler cart. Expanding at
    // source keeps the fidelity gate strict by making both sides agree.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Toll Plaza - W'mooloo", "Toll Plaza-Woolloomooloo")]
    [InlineData("HT Toll Plaza - North Sydney", "Harbour Tunnel Toll Plaza-North Sydney")]
    public void ToSpeakable_ExpandsHerePlaceContractions(string input, string expected) =>
        RoadNameFormatter.ToSpeakable(input).ShouldBe(expected);

    [Fact]
    public void ToSpeakable_PlaceContraction_OnlyMatchesTheWholeToken()
    {
        // Substring safety: the expansion is word-level, so a real name that merely CONTAINS
        // the abbreviation must pass through untouched.
        RoadNameFormatter.ToSpeakable("Hthaway Street").ShouldBe("Hthaway Street");
        RoadNameFormatter.ToSpeakable("Heathmont Road").ShouldBe("Heathmont Road");
    }

    [Theory]
    [InlineData("St Albans Road", "Saint Albans Road")]
    [InlineData("St Georges Terrace", "Saint Georges Terrace")]
    [InlineData("St Kilda Avenue", "Saint Kilda Avenue")]
    public void IsSameRoad_LeadingStMeansSaintNotStreet(string abbreviated, string expanded) =>
        RoadNameFormatter.IsSameRoad(abbreviated, expanded).ShouldBeTrue();

    [Fact]
    public void IsSameRoad_StKildaNeverBecomesStreetKilda()
    {
        RoadNameFormatter.ToSpeakable("St Kilda Avenue").ShouldBe("St Kilda Avenue");
        RoadNameFormatter.IsSameRoad("St Kilda Avenue", "Street Kilda Avenue").ShouldBeFalse();
        RoadNameFormatter.IsSameRoad("St Kilda Avenue", "Saint Kilda Avenue").ShouldBeTrue();
    }

    [Theory]
    [InlineData("Marmion Av", "Marmion Avenue")]
    [InlineData("The Parade W", "Parade")]
    [InlineData("Churchill Road N", "Churchill Road")]
    [InlineData("Barton Terrace E", "Barton Terrace")]
    public void IsSameRoad_ExpandsAvenueAndStripsCompassSuffix(string abbreviated, string expanded) =>
        RoadNameFormatter.IsSameRoad(abbreviated, expanded).ShouldBeTrue();

    [Theory]
    [InlineData("SOUTH ROAD NORTHBOUND (REGENCY PARK)", "South Road")]
    [InlineData("PACIFIC MOTORWAY (NORTHBOUND)", "Pacific Motorway")]
    [InlineData("Bruce Hwy (Cairns)", "Bruce Highway")]
    public void IsSameRoad_StripsDirectionalAndParentheticalQualifiers(string qualified, string road) =>
        RoadNameFormatter.IsSameRoad(qualified, road).ShouldBeTrue();

    [Theory]
    [InlineData("LOCAL ROAD")]
    [InlineData("LOCAL ROAD - CITY OF PERTH")]
    [InlineData("Unnamed Road")]
    [InlineData("Unidentified Road")]
    [InlineData("Unknown Location")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPlaceholderName_LivePlaceholderValuesAreRejected(string name) =>
        RoadNameFormatter.IsPlaceholderName(name).ShouldBeTrue();

    [Fact]
    public void IsSameRoad_PlaceholdersNeverMatch()
    {
        RoadNameFormatter.IsSameRoad("LOCAL ROAD", "LOCAL ROAD").ShouldBeFalse();
        RoadNameFormatter.IsSameRoad("Unnamed Road", "Smith Road").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Epping Train Station")]
    [InlineData("Brisbane Airport")]
    [InlineData("Abbotsford Road Interchange")]
    [InlineData("Diamond Sporting Complex")]
    [InlineData("Exit 85")]
    [InlineData("Blunder Road Exit")]
    [InlineData("Austlink Corporate Park")]
    [InlineData("Attenborough Park")]
    public void IsPointOfInterestName_LiveHerePoiValuesAreRejected(string name) =>
        RoadNameFormatter.IsPointOfInterestName(name).ShouldBeTrue();

    [Fact]
    public void IsPointOfInterestName_RoadSuffixDisambiguatesAstoriaParkRoad()
    {
        RoadNameFormatter.IsPointOfInterestName("Astoria Park Road").ShouldBeFalse();
        RoadNameFormatter.IsPointOfInterestName("Attenborough Park").ShouldBeTrue();
    }

    [Theory]
    [InlineData("Milo Road / Unnamed Road", "Milo Road", "Unnamed Road")]
    [InlineData("Kidman Way & Silo Road", "Kidman Way", "Silo Road")]
    [InlineData("Horrocks Pass Rd / Horrocks Hwy (Main North Rd)", "Horrocks Pass Rd", "Horrocks Hwy (Main North Rd)")]
    [InlineData("RIVERSIDE DRIVE / STREETIRLING HIGHWAY", "RIVERSIDE DRIVE", "STREETIRLING HIGHWAY")]
    public void SplitComposite_LiveCompositeValuesYieldCandidates(
        string composite, string first, string second)
    {
        RoadNameFormatter.SplitComposite(composite).ShouldBe([first, second]);
    }

    [Fact]
    public void SplitComposite_NonCompositeReturnsOriginal()
    {
        RoadNameFormatter.SplitComposite("Bruce Hwy").ShouldBe(["Bruce Hwy"]);
    }

    [Theory]
    [InlineData("The Parade", "Parade")]
    [InlineData("The Boulevarde", "Boulevarde")]
    [InlineData("The Shelf Road", "Shelf Road")]
    [InlineData("Gt Western Hwy", "Great Western Highway")]
    public void IsSameRoad_StripsLeadingArticleAndExpandsGreat(string first, string second) =>
        RoadNameFormatter.IsSameRoad(first, second).ShouldBeTrue();

    [Theory]
    [InlineData("Great Western Hwy", "Great Western Highway")]
    [InlineData("Wakehurst Pkwy", "Wakehurst Parkway")]
    [InlineData("M4 Western Motorway", "M4")]
    public void IsSameRoad_ExistingRoadAliasesRemainEquivalent(string first, string second) =>
        RoadNameFormatter.IsSameRoad(first, second).ShouldBeTrue();
}
