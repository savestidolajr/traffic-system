using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public class IncidentMappingExtensionsTests
{
    // =====================================================================
    // ToJobIncident
    // =====================================================================

    [Fact]
    public void ToJobIncident_MapsAllFields()
    {
        var incident = new TrafficIncident(
            "inc-1",
            "Multi-vehicle accident",
            3,
            "M4 Motorway near Parramatta",
            "accident",
            1200,
            1,
            Option<double>.Some(-33.81),
            Option<double>.Some(151.0),
            SourceRecordIdentities: [new SourceRecordIdentity("here", "inc-1", "orig-1")]);

        var result = incident.ToJobIncident();

        result.Key.ShouldBe("inc-1");
        result.Priority.ShouldBe(3);
        result.Headline.ShouldBe("accident on M4 Motorway near Parramatta");
        result.Location.ShouldBe("M4 Motorway near Parramatta");
        result.FunctionalClass.ShouldBe(1);
        result.LengthMeters.ShouldBe(1200);
        result.Detail.HasValue.ShouldBeTrue();
        result.Latitude.Value.ShouldBe(-33.81);
        result.Longitude.Value.ShouldBe(151.0);
        result.SourceRecordIdentities.ShouldHaveSingleItem()
            .ShouldBe(new SourceRecordIdentity("here", "inc-1", "orig-1"));
    }

    [Fact]
    public void ToJobIncident_CarriesDirectionLaneDetailCommentAndClosureThrough()
    {
        var incident = new TrafficIncident(
            "inc-closure",
            "Lane blocked",
            2,
            "M5 Motorway near Bankstown",
            "accident",
            800,
            1,
            Option<double>.Some(-33.88),
            Option<double>.Some(151.02),
            Direction: Option<string>.Some("southbound"),
            Summary: Option<string>.Some("Left lane blocked"),
            Comment: Option<string>.Some("Serious multi-vehicle crash"),
            RoadClosed: true,
            EntryTimeUtc: new DateTimeOffset(2026, 6, 5, 2, 22, 46, TimeSpan.Zero));

        var result = incident.ToJobIncident();

        result.Direction.Value.ShouldBe("southbound");
        result.LaneDetail.Value.ShouldBe("Left lane blocked");
        result.Comment.Value.ShouldBe("Serious multi-vehicle crash");
        result.RoadClosed.ShouldBeTrue();
        result.EntryTimeUtc.ShouldBe(new DateTimeOffset(2026, 6, 5, 2, 22, 46, TimeSpan.Zero));
    }

    [Fact]
    public void ToTrafficIncident_RetainsTimingAndSourceRevisionIdentity()
    {
        var entry = new DateTimeOffset(2026, 6, 5, 2, 22, 46, TimeSpan.Zero);
        var start = entry.AddMinutes(5);
        var end = entry.AddHours(2);
        var incident = new JobIncident(
            Key: "here-current-2",
            Priority: 2,
            Headline: "accident on M5",
            Location: "M5",
            EntryTimeUtc: entry,
            StartTimeUtc: start,
            EndTimeUtc: end,
            SourceRecordIdentities:
                [new SourceRecordIdentity("here", "here-current-2", "here-original-1")]);

        var result = incident.ToTrafficIncident();

        result.EntryTimeUtc.ShouldBe(entry);
        result.StartTimeUtc.ShouldBe(start);
        result.EndTimeUtc.ShouldBe(end);
        result.OriginalId.ShouldBe("here-original-1");
        result.SourceRecordIdentities.ShouldHaveSingleItem()
            .ShouldBe(new SourceRecordIdentity("here", "here-current-2", "here-original-1"));
    }


    [Fact]
    public void ToJobIncident_LatLon_None_WhenNotProvided()
    {
        var incident = new TrafficIncident("i", "desc", 1, "loc", "type");

        var result = incident.ToJobIncident();

        result.Latitude.HasValue.ShouldBeFalse();
        result.Longitude.HasValue.ShouldBeFalse();
    }

    // =====================================================================
    // ToJobCorridor
    // =====================================================================

    [Fact]
    public void ToJobCorridor_MapsFieldsAndConvertsMetersToKm()
    {
        var corridor = new TrafficCorridor(
            "M4",
            2,
            1,
            5500,
            8.5,
            "accident",
            "Delays on M4",
            new List<TrafficIncident>
            {
                new("i1", "d1", 2, "M4", "accident"),
                new("i2", "d2", 3, "M4", "construction")
            },
            Direction: Option<string>.Some("citybound"),
            Suburb: Option<string>.Some("Parramatta"));

        var result = corridor.ToJobCorridor(1, true);

        result.RoadName.ShouldBe("M4");
        result.Severity.ShouldBe(2);
        result.ImpactScore.ShouldBe(8.5);
        result.PrimaryType.ShouldBe("accident");
        result.Description.ShouldBe("Delays on M4");
        result.IncidentCount.ShouldBe(2);
        result.TotalLengthKm.ShouldBe(5.5); // 5500 / 1000 rounded to 1dp
        result.Rank.ShouldBe(1);
        result.IsSelected.ShouldBeTrue();
        result.FunctionalClass.ShouldBe(1);
        result.Direction.ShouldBe("citybound");
        result.Suburb.ShouldBe("Parramatta");
    }

    [Fact]
    public void ToJobCorridor_OptionalFields_NullWhenNone()
    {
        var corridor = new TrafficCorridor(
            "Road", 1, 3, 1000, 5, "type", "desc",
            new List<TrafficIncident>());

        var result = corridor.ToJobCorridor(0, false);

        result.Direction.ShouldBeNull();
        result.Suburb.ShouldBeNull();
        result.DriveTimeMinutes.ShouldBeNull();
        result.NormalDriveTimeMinutes.ShouldBeNull();
        result.DelayMinutes.ShouldBeNull();
    }
    [Fact]
    public void CorridorMapping_RoundTripsPlannedProvenanceWithoutInflatingCorroboration()
    {
        var incident = new TrafficIncident(
            "i1", "Roadworks on M4", 2, "M4", "construction",
            Sources: ["here", "livetraffic-nsw"]);
        var corridor = new TrafficCorridor(
            "M4", 2, 1, 1000, 10, "construction", "Roadworks on M4", [incident],
            Sources: ["here", "livetraffic-nsw"])
        {
            CorroboratingSourceCount = 1
        };

        var persisted = corridor.ToJobCorridor(1, true);
        var restored = persisted.ToTrafficCorridor(
            new Dictionary<string, JobIncident> { ["i1"] = incident.ToJobIncident() });

        persisted.Sources.ShouldBe(["here", "livetraffic-nsw"]);
        persisted.CorroboratingSourceCount.ShouldBe(1);
        restored.Sources.ShouldBe(["here", "livetraffic-nsw"]);
        restored.CorroborationCount.ShouldBe(1);
        restored.ToCorridorDto().CorroborationCount.ShouldBe(1);
    }


    // =====================================================================
    // ToCorridorDto (from TrafficCorridor)
    // =====================================================================

    [Fact]
    public void ToCorridorDto_FromTrafficCorridor_MapsAllFields()
    {
        var corridor = new TrafficCorridor(
            "Punt Road",
            1,
            2,
            3200,
            6.0,
            "congestion",
            "Heavy traffic",
            new List<TrafficIncident> { new("i1", "d", 1, "loc", "t") },
            Direction: Option<string>.Some("southbound"),
            Suburb: Option<string>.Some("Richmond"),
            DriveTimeMinutes: Option<int>.Some(15),
            NormalDriveTimeMinutes: Option<int>.Some(8),
            Latitude: Option<double>.Some(-37.8),
            Longitude: Option<double>.Some(145.0));

        var dto = corridor.ToCorridorDto(2, true);

        dto.RoadName.ShouldBe("Punt Road");
        dto.TotalLengthKm.ShouldBe(3.2);
        dto.IncidentCount.ShouldBe(1);
        dto.Rank.ShouldBe(2);
        dto.IsSelected.ShouldBeTrue();
        dto.Direction.ShouldBe("southbound");
        dto.Suburb.ShouldBe("Richmond");
        dto.DriveTimeMinutes.ShouldBe(15);
        dto.NormalDriveTimeMinutes.ShouldBe(8);
        dto.DelayMinutes.ShouldBe(7); // 15 - 8
        dto.Latitude.ShouldBe(-37.8);
        dto.Longitude.ShouldBe(145.0);
        // Congestion (Minor floor) promoted to Major by a >3km queue — the commuter magnitude,
        // not the flat HERE severity (1), surfaced for the UI.
        dto.Magnitude.ShouldBe("Major");
    }

    // =====================================================================
    // ToCorridorDto (from JobCorridor)
    // =====================================================================

    [Fact]
    public void ToCorridorDto_FromJobCorridor_PreservesFields()
    {
        var jc = new JobCorridor(
            "M1",
            2,
            7.0,
            "accident",
            "Crash M1",
            3,
            4.5,
            1,
            true,
            1,
            "eastbound",
            "Wahroonga",
            20,
            10,
            10);

        var dto = jc.ToCorridorDto();

        dto.RoadName.ShouldBe("M1");
        dto.Direction.ShouldBe("eastbound");
        dto.Suburb.ShouldBe("Wahroonga");
        dto.DriveTimeMinutes.ShouldBe(20);
        dto.NormalDriveTimeMinutes.ShouldBe(10);
        dto.DelayMinutes.ShouldBe(10);
        // Computed from the persisted JobCorridor's own fields (accident on a freeway, 4.5km).
        dto.Magnitude.ShouldBe("Major");
    }

    // =====================================================================
    // ToChangeSummary
    // =====================================================================

    [Fact]
    public void ToChangeSummary_CreatesCorrectSummary()
    {
        var delta = new IncidentDeltaContext(
            new List<IncidentDeltaItem>
            {
                new("i1", "accident", 2, Option<string>.Some("M4"), Option<string>.None, Option<string>.None, 500,
                    Option<string>.Some("crash"))
            },
            new List<IncidentDeltaItem>(),
            new List<IncidentDeltaItem>
            {
                new("i2", "construction", 3, Option<string>.None, Option<string>.None, Option<string>.None, 0,
                    Option<string>.None)
            },
            new List<IncidentDeltaItem>());

        var timeProvider = TimeProvider.System;

        var result = delta.ToChangeSummary(timeProvider);

        result.HasValue.ShouldBeTrue();
        var summary = result.Value;
        summary.NewCount.ShouldBe(1);
        summary.UpdatedCount.ShouldBe(0);
        summary.ClearedCount.ShouldBe(1);
        summary.GeneratedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ToChangeSummary_EmptyDelta_StillReturnsSome()
    {
        var delta = new IncidentDeltaContext(
            new List<IncidentDeltaItem>(),
            new List<IncidentDeltaItem>(),
            new List<IncidentDeltaItem>(),
            new List<IncidentDeltaItem>());

        var result = delta.ToChangeSummary(TimeProvider.System);

        result.HasValue.ShouldBeTrue();
        result.Value.NewCount.ShouldBe(0);
    }

    // =====================================================================
    // Operator visibility: facts recovered from prose must be inspectable
    // =====================================================================

    /// <summary>
    ///     The curation window exists so an operator can review the fetched data before the draft
    ///     agent sees it, which is only possible if the recovered facts reach the wire. Measured on
    ///     live dev data: a Sydney fetch recovered 4 directions and 5 other-assets from prose across
    ///     171 authority records, and none of it was visible to the operator until these fields were
    ///     mapped. ProseFields is what distinguishes an LLM-extracted value from a parsed one.
    /// </summary>
    [Fact]
    public void ToJobIncident_CarriesRecoveredFactsAndMarksProseDerivedOnes()
    {
        var official = new OfficialTrafficEvent(
            "livetraffic-nsw",
            "Reduced to a single lane in each direction",
            false,
            [],
            RoadName: "Warringah Freeway",
            Category: "SCHEDULED ROADWORK",
            LanesClosed: 1,
            LanesTotal: 2,
            OtherAssets: ["Sydney Harbour Tunnel"],
            CanonicalClass: IncidentClass.Roadworks);
        var incident = OfficialIncidentNormalizer.ToIncident(official, DateTimeOffset.UtcNow)
            with
        { ProseFields = ["lanesClosed", "lanesTotal", "otherAssets"] };

        var dto = incident.ToJobIncident();

        dto.LanesClosed.ShouldBe(1);
        dto.LanesTotal.ShouldBe(2);
        dto.OtherAssets.ShouldNotBeNull().ShouldContain("Sydney Harbour Tunnel");
        dto.CanonicalClass.ShouldBe(nameof(IncidentClass.Roadworks));
        dto.ProseFields.ShouldNotBeNull().ShouldContain("lanesClosed");
    }

    /// <summary>
    ///     Absent stays absent: a zero lane count or an empty asset list would read to an operator
    ///     as "no lanes closed" rather than "not reported", and non-positive numerics are how three
    ///     of the authorities encode unknown.
    /// </summary>
    [Fact]
    public void ToJobIncident_AbsentRecoveredFacts_EmitNothing()
    {
        var official = new OfficialTrafficEvent(
            "livetraffic-nsw", "Breakdown on the M4", true, [], RoadName: "M4");
        var incident = OfficialIncidentNormalizer.ToIncident(official, DateTimeOffset.UtcNow);

        var dto = incident.ToJobIncident();

        dto.LanesClosed.ShouldBeNull();
        dto.LanesTotal.ShouldBeNull();
        dto.OtherAssets.ShouldBeNull();
        dto.CanonicalClass.ShouldBeNull();
    }
}
