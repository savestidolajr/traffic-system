using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class IncidentClassCrosswalkTests
{
    public static IEnumerable<object[]> HereValues =>
    [
        C("construction", null, IncidentClass.Roadworks),
        C("roadClosure", null, IncidentClass.Closure),
        C("disabledVehicle", null, IncidentClass.Breakdown),
        C("congestion", null, IncidentClass.Congestion),
        C("roadHazard", null, IncidentClass.Hazard),
        C("accident", null, IncidentClass.Crash),
        C("other", null, IncidentClass.Hazard),
        C("plannedEvent", null, IncidentClass.SpecialEvent)
    ];

    public static IEnumerable<object[]> NswValues =>
    [
        C("ADVERSE WEATHER", null, IncidentClass.Hazard),
        C("CHANGED TRAFFIC CONDITIONS", null, IncidentClass.Congestion),
        C("HAZARD", null, IncidentClass.Hazard),
        C("CRASH", null, IncidentClass.Crash),
        C("EMERGENCY ROADWORK", null, IncidentClass.Roadworks),
        C("BREAKDOWN", null, IncidentClass.Breakdown),
        C("SCHEDULED ROADWORK", null, IncidentClass.Roadworks),
        C("FERRY OUT OF SERVICE", null, IncidentClass.Ferry),
        C(null, "Water over road", IncidentClass.Flooding),
        C(null, "Road damage", IncidentClass.Hazard),
        C(null, "Planned Police operation", IncidentClass.SpecialEvent),
        C(null, "Maintenance", IncidentClass.Roadworks),
        C(null, "Resurfacing", IncidentClass.Roadworks),
        C(null, "Bridge work", IncidentClass.Roadworks),
        C(null, "Upgrade", IncidentClass.Roadworks),
        C(null, "Intersection upgrade", IncidentClass.Roadworks),
        C(null, "Utilities", IncidentClass.Roadworks),
        C(null, "Slope stabilisation", IncidentClass.Roadworks),
        C(null, "Line marking", IncidentClass.Roadworks),
        C(null, "Power pole/line repairs", IncidentClass.Roadworks),
        C(null, "Surveying", IncidentClass.Roadworks),
        C(null, "Road/lane widening", IncidentClass.Roadworks),
        C(null, "Routine maintenance", IncidentClass.Roadworks),
        C(null, "Warringah Freeway Upgrade", IncidentClass.Roadworks),
        C(null, "Signage", IncidentClass.Roadworks),
        C("CRASH", "Semi-trailer", IncidentClass.Crash),
        C("CRASH", "2 trucks", IncidentClass.Crash),
        C("CRASH", "2 cars", IncidentClass.Crash),
        C("BREAKDOWN", "Oversize vehicle", IncidentClass.Breakdown),
        C("BREAKDOWN", "Car", IncidentClass.Breakdown),
        C("BREAKDOWN", "Van", IncidentClass.Breakdown)
    ];

    public static IEnumerable<object[]> QueenslandValues =>
    [
        C("Roadworks", null, IncidentClass.Roadworks),
        C("Hazard", null, IncidentClass.Hazard),
        C("Flooding", null, IncidentClass.Flooding),
        C("Special event", null, IncidentClass.SpecialEvent),
        C("Crash", null, IncidentClass.Crash),
        C("Congestion", null, IncidentClass.Congestion),
        C(null, "Planned roadworks", IncidentClass.Roadworks),
        C(null, "Adverse driving conditions", IncidentClass.Hazard),
        C(null, "Road damage", IncidentClass.Hazard),
        C(null, "Bridge or culvert damaged", IncidentClass.Hazard),
        C(null, "Long-term flooding", IncidentClass.Flooding),
        C(null, "Stationary vehicle", IncidentClass.Breakdown),
        C(null, "Multi-vehicle", IncidentClass.Crash),
        C(null, "Single vehicle", IncidentClass.Crash),
        C(null, "Recurring", IncidentClass.Congestion),
        C(null, "Flash flooding", IncidentClass.Flooding),
        C(null, "Fire", IncidentClass.Fire),
        C(null, "Poor visibility", IncidentClass.Hazard),
        C("Crash", "N/A", null)
    ];

    public static IEnumerable<object[]> WesternAustraliaValues =>
    [
        C("Break Down / Tow Away", null, IncidentClass.Breakdown),
        C("Vehicle Fire", null, IncidentClass.Fire),
        C("Pothole / Road Surface Damage", null, IncidentClass.Hazard),
        C("Road Crash", null, IncidentClass.Crash),
        C("Vehicle In Emergency Bay", null, IncidentClass.Breakdown),
        C("Emergency Stopping Bay (SF)", null, IncidentClass.Hazard),
        C(null, "Maintenance", IncidentClass.Roadworks),
        C(null, "Upgrades", IncidentClass.Roadworks),
        C(null, "Utility works", IncidentClass.Roadworks),
        C(null, "Resurfacing", IncidentClass.Roadworks),
        C(null, "PTA Works", IncidentClass.Roadworks),
        C(null, "Left Emergency Lane Blocked", IncidentClass.Hazard),
        C(null, "All Lanes Open", IncidentClass.Hazard),
        C(null, "Lane Closures Unknown", IncidentClass.Hazard)
    ];

    public static IEnumerable<object[]> SouthAustraliaValues =>
    [
        C("INCIDENT", null, IncidentClass.Hazard),
        C("COLLISION", null, IncidentClass.Crash),
        C("FLOODING", null, IncidentClass.Flooding),
        C("EMERGENCY WATERWORKS", null, IncidentClass.Roadworks),
        C(null, "Hazard", IncidentClass.Hazard),
        C(null, "Urgent Dept. For Infrastructure & Transport Works", IncidentClass.Roadworks),
        C(null, "Collision", IncidentClass.Crash),
        C(null, "Flooding Water Over Road", IncidentClass.Flooding),
        C(null, "Ferry Closed", IncidentClass.Ferry),
        C(null, "Debris On Road", IncidentClass.Hazard),
        C(null, "Emergency Waterworks", IncidentClass.Roadworks),
        C(null, "Urgent Waterworks", IncidentClass.Roadworks),
        C(null, "Breakdown", IncidentClass.Breakdown)
    ];


    [Theory]
    [MemberData(nameof(HereValues))]
    public void HereVocabularyMaps(string? category, string? subCategory, IncidentClass? expected) =>
        AssertClassification("here", category, subCategory, expected);

    [Theory]
    [MemberData(nameof(NswValues))]
    public void NewSouthWalesVocabularyMaps(string? category, string? subCategory, IncidentClass? expected) =>
        AssertClassification("livetraffic-nsw", category, subCategory, expected);

    [Theory]
    [MemberData(nameof(QueenslandValues))]
    public void QueenslandVocabularyMaps(string? category, string? subCategory, IncidentClass? expected) =>
        AssertClassification("qldtraffic", category, subCategory, expected);

    [Theory]
    [MemberData(nameof(WesternAustraliaValues))]
    public void WesternAustraliaVocabularyMaps(string? category, string? subCategory, IncidentClass? expected) =>
        AssertClassification("mainroads-wa", category, subCategory, expected);

    [Theory]
    [MemberData(nameof(SouthAustraliaValues))]
    public void SouthAustraliaVocabularyMaps(string? category, string? subCategory, IncidentClass? expected) =>
        AssertClassification("trafficsa", category, subCategory, expected);

    [Fact]
    public void UnknownEmptyAndNaValuesFailClosed()
    {
        IncidentClassCrosswalk.Classify("qldtraffic", "not observed", null).IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("qldtraffic", "", null).IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("qldtraffic", null, "N/A").IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("qldtraffic", "Crash", "N/A").IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("livetraffic-nsw", null, "3 cars").IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("unknown-source", "Crash", null).IsNone.ShouldBeTrue();
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAndWhitespaceTolerant()
    {
        IncidentClassCrosswalk.Classify("QldTraffic", "  fLoOdInG  ", null).Value.ShouldBe(IncidentClass.Flooding);
        IncidentClassCrosswalk.Classify("MAINROADS-WA", null, "  utility WORKS ").Value.ShouldBe(IncidentClass.Roadworks);
    }

    [Fact]
    public void VehicleDetailWithoutParentContextFailsClosed()
    {
        IncidentClassCrosswalk.Classify("livetraffic-nsw", null, "Semi-trailer").IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("livetraffic-nsw", "unrecognised", "Car").IsNone.ShouldBeTrue();
    }

    [Fact]
    public void KnownVocabularyIncludesEveryEncodedValue()
    {
        IncidentClassCrosswalk.KnownVocabulary("qldtraffic").ShouldContain("N/A");
        IncidentClassCrosswalk.KnownVocabulary("mainroads-wa").ShouldContain("Vehicle In Emergency Bay");
        IncidentClassCrosswalk.KnownVocabulary("mainroads-wa").ShouldContain("Emergency Stopping Bay (SF)");
        IncidentClassCrosswalk.KnownVocabulary("unknown-source").ShouldBeEmpty();
    }

    [Fact]
    public void SourceTablesDoNotLeakAcrossAuthorities()
    {
        IncidentClassCrosswalk.Classify("mainroads-wa", "SCHEDULED ROADWORK", null).IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("livetraffic-nsw", "Break Down / Tow Away", null).IsNone.ShouldBeTrue();
        IncidentClassCrosswalk.Classify("some-feed-we-have-not-built", "Crash", null).IsNone.ShouldBeTrue();
    }

    private static object[] C(string? category, string? subCategory, IncidentClass? expected) =>
        [category!, subCategory!, expected!];

    private static void AssertClassification(
        string sourceId,
        string? category,
        string? subCategory,
        IncidentClass? expected)
    {
        var actual = IncidentClassCrosswalk.Classify(sourceId, category, subCategory);
        actual.HasValue.ShouldBe(expected.HasValue);
        if (expected.HasValue)
            actual.Value.ShouldBe(expected.Value);
    }
}
