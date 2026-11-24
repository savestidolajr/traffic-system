using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The landscape study's arithmetic (docs/reference/here-staleness-landscape-…): each measure is
///     pinned on tiny synthetic feeds so the nightly monitor's numbers stay trustworthy as inputs.
/// </summary>
public sealed class TrafficFeedComparisonTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 6, 0, 0, TimeSpan.Zero);

    private static TrafficIncident Incident(
        string id, double lat, double lng, bool closed = false, int windowDays = 1,
        double startedHoursAgo = 200, double enteredHoursAgo = 100) =>
        new(id, "Closed", 1, "Test Road", closed ? "roadClosure" : "construction",
            RoadClosed: closed,
            Shape: [new CoveragePoint(lat, lng)],
            StartTimeUtc: Now.AddHours(-startedHoursAgo),
            EndTimeUtc: Now.AddHours(-startedHoursAgo).AddDays(windowDays),
            EntryTimeUtc: Now.AddHours(-enteredHoursAgo));

    private static OfficialTrafficEvent Official(double lat, double lng, bool incidentClass = true) =>
        new("test-feed", "CRASH", incidentClass, [new CoveragePoint(lat, lng)]);

    [Fact]
    public void ScheduledClosures_AreCountedFromTheWindow_NotTheType()
    {
        var result = TrafficFeedComparison.Compare(
            [
                Incident("long", -33.87, 151.21, closed: true, windowDays: 90),
                Incident("short", -33.88, 151.22, closed: true, windowDays: 2)
            ],
            [], Now);

        result.Closures.ShouldBe(2);
        result.ScheduledClosures.ShouldBe(1);
        result.Scheduled.ShouldBe(1);
        result.Ongoing.ShouldBe(1);
    }

    [Fact]
    public void UncorroboratedClosures_RequireAnOfficialPictureToJudgeAgainst()
    {
        var closure = Incident("c1", -33.87, 151.21, closed: true);

        // No official events at all: nothing is indicted.
        TrafficFeedComparison.Compare([closure], [], Now).UncorroboratedClosures.ShouldBe(0);

        // An official event 100 m away corroborates; one 5 km away does not.
        TrafficFeedComparison.Compare([closure], [Official(-33.8709, 151.21)], Now)
            .UncorroboratedClosures.ShouldBe(0);
        TrafficFeedComparison.Compare([closure], [Official(-33.915, 151.21)], Now)
            .UncorroboratedClosures.ShouldBe(1);
    }

    [Fact]
    public void MissingFromHere_CountsOnlyIncidentClassEvents()
    {
        var here = new[] { Incident("h1", -33.87, 151.21) };
        var officials = new[]
        {
            Official(-33.8701, 151.2101),               // crash, matched -> not missing
            Official(-33.95, 151.10),                   // crash, unmatched -> missing
            Official(-33.96, 151.05, incidentClass: false) // roadworks, unmatched -> ignored
        };

        var result = TrafficFeedComparison.Compare(here, officials, Now);

        result.OfficialIncidentClass.ShouldBe(2);
        result.MissingFromHere.ShouldBe(1);
        result.OfficialEvents.ShouldBe(3);
    }

    [Fact]
    public void MedianEntryAge_IsReported()
    {
        var result = TrafficFeedComparison.Compare(
            [
                Incident("a", -33.8, 151.2, enteredHoursAgo: 10),
                Incident("b", -33.8, 151.2, enteredHoursAgo: 100),
                Incident("c", -33.8, 151.2, enteredHoursAgo: 300)
            ],
            [], Now);

        result.MedianEntryAgeHours.ShouldBe(100, tolerance: 0.01);
    }
}
