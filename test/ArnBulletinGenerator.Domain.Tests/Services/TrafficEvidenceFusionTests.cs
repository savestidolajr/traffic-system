using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     Source-neutral evidence fusion (2026-08-18 design review): no feed is "the base". Matched
///     events merge as provenance, official-only events pass the SAME eligibility boundary and
///     impact formula as HERE records, and per-attribute precedence lets the authority's own start
///     date re-window a rolled HERE record.
/// </summary>
public class TrafficEvidenceFusionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);

    private static TrafficCorridor HereCorridor(
        string roadName,
        string primaryType,
        double lat = -33.8688,
        double lng = 151.2093,
        double impactScore = 50.0,
        bool roadClosed = false,
        DateTimeOffset? entryTimeUtc = null)
    {
        var incident = new TrafficIncident(
            Id: $"inc-{roadName}",
            Description: $"{primaryType} on {roadName}",
            Severity: 2,
            Location: roadName,
            Type: primaryType,
            RoadClosed: roadClosed,
            EntryTimeUtc: entryTimeUtc,
            Latitude: Option<double>.Some(lat),
            Longitude: Option<double>.Some(lng),
            Sources: EvidenceSources.HereOnly);

        return new TrafficCorridor(
            RoadName: roadName,
            Severity: 2,
            FunctionalClass: 1,
            TotalLengthMeters: 2000,
            ImpactScore: impactScore,
            PrimaryType: primaryType,
            Description: $"{primaryType} on {roadName}",
            Incidents: [incident],
            Latitude: Option<double>.Some(lat),
            Longitude: Option<double>.Some(lng),
            Sources: EvidenceSources.HereOnly);
    }

    private static OfficialTrafficEvent Official(
        string headline,
        string? roadName = null,
        bool incidentClass = true,
        double lat = -33.8190,
        double lng = 151.0025,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string sourceId = "livetraffic-nsw",
        string? crossStreet = null,
        string? id = null) =>
        new(
            SourceId: sourceId,
            Headline: headline,
            IsIncidentClass: incidentClass,
            Shape: [new CoveragePoint(lat, lng)],
            Id: id,
            RoadName: roadName,
            CrossStreet: crossStreet,
            StartTime: start,
            EndTime: end);

    [Fact]
    public void MatchedEvent_MergesAsProvenance_NotAsAStatusAboutHere()
    {
        var corridors = new List<TrafficCorridor> { HereCorridor("M4", "accident", -33.8188, 151.0022) };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Accident on M4 near Church St", roadName: "M4", crossStreet: "Church St")
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);

        var fused = result.ShouldHaveSingleItem();
        fused.Sources.ShouldBe(["here", "livetraffic-nsw"]);
        fused.CorroborationCount.ShouldBe(2);
        fused.OfficialCrossStreet.Value.ShouldBe("Church St");
        // The receipt: the authority's record verbatim-as-parsed, with the actual match branch.
        fused.OfficialEvidences.ShouldNotBeNull();
        fused.OfficialEvidences.ShouldHaveSingleItem().MatchBasis.ShouldBe("road-name");
        fused.OfficialEvidences[0].SourceId.ShouldBe("livetraffic-nsw");
        fused.OfficialEvidences[0].Headline.ShouldBe("Accident on M4 near Church St");
        fused.OfficialEvidences[0].ShapePointCount.ShouldBe(1);
        // Event-level corroboration: the authority stamps the member records themselves —
        // per-record provenance is what the Event Ledger's trust ladder reads.

        fused.Incidents.ShouldAllBe(i => i.Sources != null && i.Sources.Contains("livetraffic-nsw"));
    }
    [Fact]
    public void MatchedEvent_RetainsOfficialSourceIdentityAlongsideReceipt()
    {
        var corridor = HereCorridor("M4", "accident", -33.8188, 151.0022);
        var official = Official(
            "Accident on M4",
            roadName: "M4",
            id: "nsw-feature-225630");

        var fused = TrafficEvidenceFusion.Fuse([corridor], [official],
                new CorroborationOptions(), Now)
            .ShouldHaveSingleItem();

        fused.OfficialEvidences.ShouldHaveSingleItem().SourceRecordId
            .ShouldBe("nsw-feature-225630");
        var identities = fused.Incidents.ShouldHaveSingleItem().SourceRecordIdentities;
        identities.ShouldNotBeNull();
        identities.ShouldContain(new SourceRecordIdentity("livetraffic-nsw", "nsw-feature-225630"));
    }

    [Fact]
    public void MatchedEvents_AssignEachOfficialIdentityToNearestHereIncident()
    {
        var baseCorridor = HereCorridor("M4", "accident", -33.8188, 151.0022);
        var first = baseCorridor.Incidents[0] with
        {
            Id = "here-first",
            Latitude = Option<double>.Some(-33.8188),
            Longitude = Option<double>.Some(151.0022)
        };
        var second = baseCorridor.Incidents[0] with
        {
            Id = "here-second",
            Latitude = Option<double>.Some(-33.8200),
            Longitude = Option<double>.Some(151.0040)
        };
        var corridor = baseCorridor with { Incidents = [first, second] };
        var firstIdentity = new SourceRecordIdentity("livetraffic-nsw", "official-first");
        var secondIdentity = new SourceRecordIdentity("qldtraffic", "official-second");

        var fused = TrafficEvidenceFusion.Fuse(
                [corridor],
                [
                    Official("First M4 crash", roadName: "M4", lat: -33.8188, lng: 151.0022,
                        sourceId: firstIdentity.SourceId, id: firstIdentity.RecordId),
                    Official("Second M4 crash", roadName: "M4", lat: -33.8200, lng: 151.0040,
                        sourceId: secondIdentity.SourceId, id: secondIdentity.RecordId)
                ],
                new CorroborationOptions(),
                Now)
            .ShouldHaveSingleItem();

        var firstIdentities = fused.Incidents[0].SourceRecordIdentities.ShouldNotBeNull();
        var secondIdentities = fused.Incidents[1].SourceRecordIdentities.ShouldNotBeNull();
        firstIdentities.ShouldContain(firstIdentity);
        firstIdentities.ShouldNotContain(secondIdentity);
        secondIdentities.ShouldContain(secondIdentity);
        secondIdentities.ShouldNotContain(firstIdentity);
        fused.Incidents[0].Sources.ShouldNotBeNull().ShouldBe(["here", "livetraffic-nsw"]);
        fused.Incidents[1].Sources.ShouldNotBeNull().ShouldBe(["here", "qldtraffic"]);
        fused.Sources.ShouldNotBeNull().ShouldBe(["here", "livetraffic-nsw", "qldtraffic"]);
    }

    [Fact]
    public void MatchedEventsFromTwoAuthorities_RetainBothReceiptsAndCorroborationCount()
    {
        var corridors = new List<TrafficCorridor> { HereCorridor("M4", "accident", -33.8188, 151.0022) };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("QLD crash on M4", roadName: "M4", sourceId: "qldtraffic"),
            Official("NSW crash nearby", roadName: null, lat: -33.8188, lng: 151.0022,
                sourceId: "livetraffic-nsw")
        };

        var fused = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now)
            .ShouldHaveSingleItem();

        fused.Sources.ShouldBe(["here", "livetraffic-nsw", "qldtraffic"]);
        fused.CorroborationCount.ShouldBe(3);
        fused.OfficialEvidences.ShouldNotBeNull();
        fused.OfficialEvidences.Count.ShouldBe(2);
        fused.OfficialEvidences.Select(e => e.SourceId)
            .ShouldBe(["livetraffic-nsw", "qldtraffic"]);
        fused.OfficialEvidences.Select(e => e.MatchBasis)
            .ShouldBe(["distance-0m", "road-name"]);
    }

    [Fact]
    public void MatchedPrimary_IsStableWhenOfficialFeedOrderChanges()
    {
        var corridor = HereCorridor("M4", "accident", -33.8188, 151.0022);
        var officials = new[]
        {
            Official("QLD anchor", roadName: "M4", lat: -33.8188, lng: 151.0022,
                sourceId: "qldtraffic"),
            Official("NSW anchor", roadName: null, lat: -33.8188, lng: 151.0022,
                sourceId: "livetraffic-nsw")
        };

        var first = TrafficEvidenceFusion.Fuse([corridor], officials, new CorroborationOptions(), Now)
            .ShouldHaveSingleItem();
        var shuffled = TrafficEvidenceFusion.Fuse(
                [corridor], officials.Reverse().ToArray(), new CorroborationOptions(), Now)
            .ShouldHaveSingleItem();

        first.OfficialHeadline.Value.ShouldBe("NSW anchor");
        shuffled.OfficialHeadline.Value.ShouldBe(first.OfficialHeadline.Value);
        shuffled.OfficialCrossStreet.ShouldBe(first.OfficialCrossStreet);
    }


    [Fact]
    public void MatchedReceipts_AreCappedAtFourAndPrioritised()
    {
        var corridors = new List<TrafficCorridor> { HereCorridor("M4", "accident") };
        var officials = new[]
        {
            Official("SA", roadName: "M4", lat: -33.8688, lng: 151.2093, sourceId: "trafficsa"),
            Official("QLD", roadName: "M4", lat: -33.8688, lng: 151.2093,
                sourceId: "qldtraffic") with { AuthorityPriority = "High" },
            Official("NSW", roadName: "M4", lat: -33.8688, lng: 151.2093,
                sourceId: "livetraffic-nsw") with { AuthorityPriority = "Major" },
            Official("WA", roadName: "M4", lat: -33.8688, lng: 151.2093,
                sourceId: "mainroads-wa") with { AuthorityPriority = "Level 3" },
            Official("Tas", roadName: "M4", lat: -33.8688, lng: 151.2093, sourceId: "tasalert")
        };

        var fused = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now)
            .ShouldHaveSingleItem();

        fused.Sources!.Count.ShouldBe(6);
        fused.OfficialEvidences.ShouldNotBeNull();
        fused.OfficialEvidences.Count.ShouldBe(4);
        fused.OfficialEvidences.Select(e => e.SourceId)
            .ShouldBe(["livetraffic-nsw", "qldtraffic", "mainroads-wa", "tasalert"]);
    }

    [Fact]
    public void SameNamedEventBeyondDistance_DoesNotCorroborate()
    {
        var corridor = HereCorridor("M4", "accident", -33.8188, 151.0022);
        var distant = Official("Crash on M4", roadName: "M4", lat: -33.8190, lng: 151.0600);

        var kept = TrafficEvidenceFusion.Fuse([corridor], [distant],
                new CorroborationOptions(InjectMissingOfficialIncidents: false), Now)
            .ShouldHaveSingleItem();

        kept.Sources.ShouldBe(["here"]);
        kept.OfficialEvidences.ShouldBeNull();
    }

    [Fact]
    public void EndedOfficialEvent_DoesNotCorroborateOrInject()
    {
        var ended = Official("Expired crash on M4", roadName: "M4",
            lat: -33.8188, lng: 151.0022, end: Now.AddHours(-2));
        var corridor = HereCorridor("M4", "accident", -33.8188, 151.0022);

        var matched = TrafficEvidenceFusion.Fuse([corridor], [ended],
                new CorroborationOptions(InjectMissingOfficialIncidents: false), Now)
            .ShouldHaveSingleItem();
        matched.Sources.ShouldBe(["here"]);
        matched.OfficialEvidences.ShouldBeNull();

        TrafficEvidenceFusion.Fuse([], [ended], new CorroborationOptions(), Now)
            .ShouldBeEmpty();
    }

    [Fact]
    public void FutureOfficialEvent_DoesNotCorroborateOrInject()
    {
        var future = Official("Future crash on M4", roadName: "M4",
            lat: -33.8188, lng: 151.0022, start: Now.AddHours(2));
        var corridor = HereCorridor("M4", "accident", -33.8188, 151.0022);

        var matched = TrafficEvidenceFusion.Fuse([corridor], [future],
                new CorroborationOptions(InjectMissingOfficialIncidents: false), Now)
            .ShouldHaveSingleItem();
        matched.Sources.ShouldBe(["here"]);
        matched.OfficialEvidences.ShouldBeNull();

        TrafficEvidenceFusion.Fuse([], [future], new CorroborationOptions(), Now)
            .ShouldBeEmpty();
    }

    [Fact]
    public void FifteenCorridorsWithFourReceipts_FitTableStorageLimits()
    {
        var receipt = new OfficialEvidence(
            SourceId: "livetraffic-nsw",
            Headline: "Emergency roadworks and lane closure on Western Distributor at Miller Street",
            IsIncidentClass: false,
            RoadName: "Western Distributor",
            CrossStreet: "Miller Street",
            Suburb: "Sydney",
            Direction: "northbound",
            Description: "Traffic control on scene; one lane closed for emergency works",
            StartTime: Now.AddHours(-1),
            EndTime: Now.AddDays(1),
            PublishedTime: Now.AddHours(-1),
            ShapePointCount: 12,
            MatchBasis: "road-name",
            AuthorityPriority: "High",
            Category: "CHANGED TRAFFIC CONDITIONS",
            SubCategory: "Emergency roadwork",
            IsStateRoad: true,
            IsPlanned: true,
            AdviceTags: ["lane closure", "traffic control"],
            ImpactType: "Lane blocked",
            ImpactSubtype: "single lane",
            QualitativeDelay: "heavy delays",
            SpeedLimitKph: 60,
            LastUpdated: Now.AddHours(-1));
        var receipts = new[]
        {
            receipt,
            receipt with { SourceId = "qldtraffic" },
            receipt with { SourceId = "mainroads-wa" },
            receipt with { SourceId = "trafficsa" }
        };
        var corridors = Enumerable.Range(0, 15)
            .Select(i => new JobCorridor(
                RoadName: $"Western Distributor {i}",
                Severity: 2,
                ImpactScore: 80,
                PrimaryType: "construction",
                Description: "Emergency roadworks and lane closure",
                IncidentCount: 2,
                TotalLengthKm: 2.4,
                Rank: i + 1,
                IsSelected: i < 5,
                FunctionalClass: 1,
                Direction: "northbound",
                Suburb: "Sydney",
                DriveTimeMinutes: 20,
                NormalDriveTimeMinutes: 8,
                DelayMinutes: 12,
                IncidentKeys: ["inc-1", "inc-2"],
                Sources: ["here", "livetraffic-nsw", "qldtraffic", "mainroads-wa", "trafficsa"],
                OfficialHeadline: "Emergency roadworks",
                OfficialCrossStreet: "Miller Street",
                OfficialEvidences: receipts))
            .ToList();

        string json = JsonSerializer.Serialize(corridors);
        int rawBytes = Encoding.UTF8.GetByteCount(json);
        byte[] compressedBytes;
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(Encoding.UTF8.GetBytes(json));
            compressedBytes = output.ToArray();
        }
        int storedCompressedChars = 3 + ((compressedBytes.Length + 2) / 3 * 4);
        int entityBytes = rawBytes + storedCompressedChars;
        Console.WriteLine(
            $"CorridorsJson: raw={rawBytes} bytes, gzip+base64={storedCompressedChars} chars; entity estimate={entityBytes} bytes");

        rawBytes.ShouldBeLessThan(64 * 1024);
        storedCompressedChars.ShouldBeLessThan(30_000);
        entityBytes.ShouldBeLessThan(1_000_000);
    }

    [Fact]
    public void MatchedPlannedWorks_OfficialStartDate_ReWindowsARolledClosure()
    {
        // The Mitchell Rd class: HERE re-issues a months-old worksite closure with a rolled start,
        // so record-intrinsic freshness reads Fresh and "Still closed" airs. The AUTHORITY'S record
        // carries the true start — per-attribute precedence: the authority wins on the event's age.
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Mitchell Road", "roadClosure", -33.91, 151.19,
                impactScore: 80, roadClosed: true, entryTimeUtc: Now.AddMinutes(-10))
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Roadworks on Mitchell Road at Sydney Park Road", roadName: "Mitchell Road",
                incidentClass: false, lat: -33.91, lng: 151.19, start: Now.AddDays(-90),
                sourceId: "livetraffic-nsw") with { IsPlanned = true }
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);
        var fused = result.ShouldHaveSingleItem();

        fused.Sources.ShouldBe(["here", "livetraffic-nsw"]);
        fused.CorroborationCount.ShouldBe(1);
        fused.Incidents.ShouldAllBe(i => i.Sources != null && i.Sources.Contains("livetraffic-nsw"));
        fused.OfficialHeadline.Value.ShouldBe("Roadworks on Mitchell Road at Sydney Park Road");
        fused.OfficialCrossStreet.HasValue.ShouldBeFalse();
        fused.OfficialEvidences.ShouldNotBeNull();
        fused.OfficialEvidences.ShouldHaveSingleItem().IsPlanned.ShouldBe(true);
        fused.PrimaryType.ShouldBe("construction");
        fused.Incidents.ShouldAllBe(i => !i.RoadClosed);
        fused.Incidents[0].WithheldReason.Value.ShouldBe("demoted:official-start-scheduled");
        fused.ImpactScore.ShouldBe(80);
    }

    [Fact]
    public void MatchedFreshIncident_KeepsItsClosureClaim()
    {
        // An official INCIDENT (crash-caused closure) corroborates the closure — no demotion.
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("M4", "roadClosure", -33.8188, 151.0022,
                roadClosed: true, entryTimeUtc: Now.AddHours(-2))
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Crash on M4 — road closed", roadName: "M4", start: Now.AddHours(-2))
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);

        var fused = result.ShouldHaveSingleItem();
        fused.Incidents.ShouldAllBe(i => i.RoadClosed);
        fused.Sources.ShouldBe(["here", "livetraffic-nsw"]);
    }

    [Fact]
    public void OfficialOnlyCrash_BecomesACorridor_ThroughTheSharedMachineryAndFormula()
    {
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Crash on Victoria Road at Rydalmere", roadName: "Victoria Road", lat: -33.81, lng: 151.03)
        };

        var result = TrafficEvidenceFusion.Fuse([], officials, new CorroborationOptions(), Now);

        var injected = result.ShouldHaveSingleItem();
        injected.Sources.ShouldBe(["livetraffic-nsw"]);
        injected.PrimaryType.ShouldBe("accident");
        // Impact from the SHARED formula on real attributes: severity 1 crash, unknown road
        // class (pipeline default 5 → 20) + severity (80) + no measured length (0) = 100.
        // Never the old fabricated 85 constant.
        injected.ImpactScore.ShouldBe(100);
        var incident = injected.Incidents.ShouldHaveSingleItem();
        incident.Fact.ShouldNotBeNull();
        incident.LengthMeters.ShouldBe(0);
        injected.OfficialEvidences.ShouldNotBeNull();
        injected.OfficialEvidences.ShouldHaveSingleItem().MatchBasis.ShouldBe("official-only");
    }

    [Fact]
    public void ShapelessOfficialEvent_NeverInjects_AtAnyCoverage()
    {
        // TasALERT RSS carries no geocode; the feed used to stamp the requesting market's
        // own centre, which made a Devonport crash sit in Sydney's evidence (live,
        // 2026-08-18). No geometry, no injection.
        var officials = new List<OfficialTrafficEvent>
        {
            new("tasalert", "Call for information for serious two-vehicle crash in Devonport",
                IsIncidentClass: true, Shape: [], Description: "crash"),
        };

        var result = TrafficEvidenceFusion.Fuse(
            [], officials, new CorroborationOptions(Enabled: true, InjectMissingOfficialIncidents: true), Now);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void NearbyEventOnADifferentNamedRoad_NeverCorroborates_HoweverClose()
    {
        // Observed live 2026-08-18: Luddenham Rd paired with the "Mamre Road upgrade" record at
        // McIntyre Avenue, 297 m away. The authority NAMED its road; a different named road a
        // street over is a different claim, not corroboration.
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Luddenham Road", "construction", -33.85, 150.75)
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("SCHEDULED ROADWORK Mamre Road upgrade", roadName: "McIntyre Avenue",
                incidentClass: false, lat: -33.851, lng: 150.752)
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials,
            new CorroborationOptions(InjectMissingOfficialIncidents: false), Now);

        var kept = result.ShouldHaveSingleItem();
        kept.Sources.ShouldBe(["here"]);
        kept.OfficialEvidences.ShouldBeNull();
    }

    [Fact]
    public void AbbreviatedRoadForms_MatchByName_NotByProximity()
    {
        // "Wakehurst Pkwy" (HERE) vs "Wakehurst Parkway" (authority) is one road; the receipt
        // must say road-name, not distance-2m.
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Wakehurst Pkwy", "construction", -33.71, 151.24)
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("SCHEDULED ROADWORK", roadName: "Wakehurst Parkway",
                incidentClass: false, lat: -33.71, lng: 151.24)
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);

        result.ShouldHaveSingleItem().OfficialEvidences!.Single().MatchBasis.ShouldBe("road-name");
    }

    [Fact]
    public void CompoundOfficialRoadNames_EachPartIsARoadClaim()
    {
        // QLD pattern: "Cairns Western Arterial Road (Pease Street)" names both forms; NSW lists
        // "Gore Hill Freeway and Warringah Freeway". Any part matching the corridor corroborates.
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Pease Street", "construction", -16.92, 145.74)
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Roadworks: Reservoir Road and Pease street",
                roadName: "Cairns Western Arterial Road (Pease Street) / Cairns Western Arterial Road (Reservoir Road)",
                incidentClass: false, lat: -16.92, lng: 145.74, sourceId: "qldtraffic")
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);

        var fused = result.ShouldHaveSingleItem();
        fused.Sources.ShouldBe(["here", "qldtraffic"]);
        fused.OfficialEvidences!.Single().MatchBasis.ShouldBe("road-name");
    }

    [Fact]
    public void NonSpecificRoadName_FallsBackToProximity()
    {
        // "Various Roads" identifies nothing — the Western Harbour Tunnel record corroborates
        // The Crescent by geometry, as before.
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("The Crescent", "construction", -33.87, 151.17)
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("SCHEDULED ROADWORK Western Harbour Tunnel", roadName: "Various Roads",
                incidentClass: false, lat: -33.8701, lng: 151.1701)
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);

        var fused = result.ShouldHaveSingleItem();
        fused.Sources.ShouldBe(["here", "livetraffic-nsw"]);
        fused.OfficialEvidences!.Single().MatchBasis.ShouldStartWith("distance-");
    }

    [Fact]
    public void OfficialOnlyNonCrash_IsAHazard_NeverAFabricatedAccident()
    {
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Police operation on Victoria Road", roadName: "Victoria Road", lat: -33.81, lng: 151.03)
        };

        var result = TrafficEvidenceFusion.Fuse([], officials, new CorroborationOptions(), Now);

        result.ShouldHaveSingleItem().PrimaryType.ShouldBe("roadHazard");
    }

    [Fact]
    public void OfficialOnlyEvent_WithNoResolvableRoad_IsWithheldByTheSameBoundary()
    {
        // No road name and no extractable road: an event that cannot say WHERE supports no honest
        // sentence — same rule as any HERE record.
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Emergency services attending an incident", roadName: null, lat: -33.81, lng: 151.03)
        };

        TrafficEvidenceFusion.Fuse([], officials, new CorroborationOptions(), Now).ShouldBeEmpty();
    }

    [Fact]
    public void OfficialOnlyDedupe_SeesThroughDesignationAliases()
    {
        var corridors = new List<TrafficCorridor> { HereCorridor("M4", "accident", -33.8188, 151.0022) };
        var officials = new List<OfficialTrafficEvent>
        {
            // Same road by alias and nearby geometry: corroboration must merge, not duplicate.
            Official("Crash on M4 Western Motorway at Eastern Creek", roadName: "M4 Western Motorway",
                lat: -33.8188, lng: 151.0022)
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials, new CorroborationOptions(), Now);

        result.Count.ShouldBe(1);
        result[0].Sources.ShouldBe(["here", "livetraffic-nsw"]);
    }

    [Fact]
    public void StaleUncorroboratedClosure_DemotesRankAndWording()
    {
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Victoria Road", "roadClosure", -33.81, 151.15,
                impactScore: 80, roadClosed: true, entryTimeUtc: Now.AddHours(-4))
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Crash on Victoria Road", roadName: "Victoria Road", lat: -33.95, lng: 151.10)
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials,
            new CorroborationOptions(InjectMissingOfficialIncidents: false), Now);

        var demoted = result.ShouldHaveSingleItem();
        demoted.PrimaryType.ShouldBe("construction");
        demoted.ImpactScore.ShouldBeLessThanOrEqualTo(10.0);
        demoted.Incidents.ShouldAllBe(i => !i.RoadClosed);
        demoted.Incidents[0].WithheldReason.Value.ShouldBe("demoted:uncorroborated-closure");
        demoted.Sources.ShouldBe(["here"]);
    }

    [Fact]
    public void StaleUncorroboratedClosure_DifferentRoadCoverage_DoesNotDemote()
    {
        var corridor = HereCorridor("Victoria Road", "roadClosure", -33.81, 151.15,
            impactScore: 80, roadClosed: true, entryTimeUtc: Now.AddHours(-4));
        var official = Official("Crash on M5 East", roadName: "M5 East",
            lat: -33.95, lng: 151.10);

        var kept = TrafficEvidenceFusion.Fuse([corridor], [official],
                new CorroborationOptions(InjectMissingOfficialIncidents: false), Now)
            .ShouldHaveSingleItem();

        kept.PrimaryType.ShouldBe("roadClosure");
        kept.ImpactScore.ShouldBe(80);
        kept.Incidents.ShouldHaveSingleItem().RoadClosed.ShouldBeTrue();
        kept.Incidents[0].WithheldReason.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void FreshClosure_IsExemptFromUncorroboratedDemotion_InsideTheGraceWindow()
    {
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Victoria Road", "roadClosure", -33.81, 151.15,
                impactScore: 80, roadClosed: true, entryTimeUtc: Now.AddMinutes(-5))
        };
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Crash on M5 East", roadName: "M5 East", lat: -33.95, lng: 151.10)
        };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials,
            new CorroborationOptions(InjectMissingOfficialIncidents: false), Now);

        var kept = result.ShouldHaveSingleItem();
        kept.Incidents.ShouldHaveSingleItem().RoadClosed.ShouldBeTrue();
        kept.ImpactScore.ShouldBe(80);
    }

    [Fact]
    public void Disabled_PassesEverythingThroughUntouched()
    {
        var corridors = new List<TrafficCorridor>
        {
            HereCorridor("Victoria Road", "roadClosure", impactScore: 80, roadClosed: true)
        };
        var officials = new List<OfficialTrafficEvent> { Official("Crash on M4", roadName: "M4") };

        var result = TrafficEvidenceFusion.Fuse(corridors, officials,
            new CorroborationOptions(Enabled: false), Now);

        result.ShouldHaveSingleItem().ShouldBe(corridors[0]);
    }

    [Fact]
    public void StableEventKey_PrefersStableOriginalBeforeHereCurrentId()
    {
        var key = SourceRecordIdentity.StableEventKey([
            new SourceRecordIdentity(EvidenceSources.Here, "here-current"),
            new SourceRecordIdentity("livetraffic-nsw", "nsw-current", "nsw-original")
        ]);

        key.ShouldBe("livetraffic-nsw:original:nsw-original");
    }

    [Fact]
    public void OfficialOnlyRecordsWithSameProviderIdRemainDistinctAcrossSources()
    {
        var officials = new List<OfficialTrafficEvent>
        {
            Official("Crash on M4", roadName: "M4", sourceId: "livetraffic-nsw", id: "123"),
            Official("Crash on Pacific Motorway", roadName: "Pacific Motorway",
                lat: -27.5000, lng: 153.0000, sourceId: "qldtraffic", id: "123")
        };

        var result = TrafficEvidenceFusion.Fuse([], officials, new CorroborationOptions(), Now);

        result.Count.ShouldBe(2);
        result.SelectMany(c => c.Incidents)
            .SelectMany(i => i.Sources ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ShouldBe(["livetraffic-nsw", "qldtraffic"], ignoreOrder: true);
    }
    [Fact]
    public void StaleAuthorityCoverage_DoesNotDemoteHereClosure_ButFreshCoverageDoes()
    {
        var corridor = HereCorridor("M4", "roadClosure", roadClosed: true);
        var coverageOnly = Official(
            "Roadworks on M4", roadName: "M4", incidentClass: false,
            lat: -33.8190, lng: 151.0025);

        var stale = TrafficEvidenceFusion.Fuse(
            [corridor], [coverageOnly],
            new CorroborationOptions(FreshSourceIds: new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)),
            Now).ShouldHaveSingleItem();
        stale.Incidents.ShouldHaveSingleItem().RoadClosed.ShouldBeTrue();

        var fresh = TrafficEvidenceFusion.Fuse(
            [corridor], [coverageOnly],
            new CorroborationOptions(FreshSourceIds: new HashSet<string>(
                ["livetraffic-nsw"], StringComparer.OrdinalIgnoreCase)),
            Now).ShouldHaveSingleItem();
        fresh.Incidents.ShouldHaveSingleItem().RoadClosed.ShouldBeFalse();
    }

    /// <summary>
    ///     An omitted authority record must be invisible to fusion, not merely absent from the
    ///     corridor list: it may neither become an official-only corridor nor ride along as a
    ///     corroborating receipt on someone else's. Eight of dev's seventeen Desk verdicts are
    ///     authority records (2026-08-24), so this is the common shape, not an edge case.
    /// </summary>
    [Fact]
    public void OmittedAuthorityRecord_NeitherInjectsNorCorroborates()
    {
        var official = Official(
            "SCHEDULED ROADWORK Resurfacing", roadName: "Church Street",
            lat: -33.8190, lng: 151.0025, id: "272218");
        // The authority supplied an id, so the ledger keys it by source-qualified record id.
        var exclusions = new IncidentExclusions(["livetraffic-nsw:record:272218"], []);

        var injected = TrafficEvidenceFusion.Fuse(
            [], [official], new CorroborationOptions(Exclusions: exclusions), Now);
        var corroborated = TrafficEvidenceFusion.Fuse(
            [HereCorridor("Church Street", "construction", -33.8190, 151.0025)],
            [official],
            new CorroborationOptions(Exclusions: exclusions), Now);

        injected.ShouldBeEmpty();
        var corridor = corroborated.ShouldHaveSingleItem();
        (corridor.OfficialEvidences ?? []).ShouldBeEmpty();
        (corridor.Sources ?? []).ShouldNotContain("livetraffic-nsw");
    }

    [Fact]
    public void AuthorityRecordWithNoVerdict_StillCorroboratesAlongsideAnExcludedSibling()
    {
        var omitted = Official("SCHEDULED ROADWORK Resurfacing", roadName: "Church Street",
            lat: -33.8190, lng: 151.0025, id: "272218");
        var kept = Official("Crash on Church Street", roadName: "Church Street",
            lat: -33.8191, lng: 151.0026, id: "999111");

        var result = TrafficEvidenceFusion.Fuse(
            [HereCorridor("Church Street", "construction", -33.8190, 151.0025)],
            [omitted, kept],
            new CorroborationOptions(
                Exclusions: new IncidentExclusions(["livetraffic-nsw:record:272218"], [])),
            Now);

        // Exclusion is per record, never per road or per feed.
        var corridor = result.ShouldHaveSingleItem();
        corridor.OfficialEvidences.ShouldNotBeNull();
        corridor.OfficialEvidences.ShouldHaveSingleItem().SourceRecordId.ShouldBe("999111");
    }
}

