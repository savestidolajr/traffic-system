using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public class JobMappingExtensionsTests
{
    private static readonly DateTime FixedTime = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Job CreateJob(
        JobStatus status = JobStatus.Requested,
        double targetDuration = 30,
        double? measuredDuration = null)
    {
        var job = Job.Reconstitute(
            "job-1", "2GB", "Sydney", targetDuration,
            new[] { new StatusHistoryEntry(status, FixedTime, "system") });
        if (measuredDuration.HasValue)
            job.MeasuredDurationSec = Option<double>.Some(measuredDuration.Value);
        return job;
    }

    // =====================================================================
    // ToSummaryDto
    // =====================================================================

    [Fact]
    public void ToSummaryDto_WithoutMeasuredDuration_LeavesDurationMeasurementsNull()
    {
        var job = CreateJob();

        var dto = job.ToSummaryDto();

        dto.JobId.ShouldBe("job-1");
        dto.Station.ShouldBe("2GB");
        dto.Market.ShouldBe("Sydney");
        dto.TargetDurationSec.ShouldBe(30);
        dto.Status.ShouldBe("REQUESTED");
        dto.MeasuredDurationSec.ShouldBeNull();
        dto.DeltaSec.ShouldBeNull();
    }

    [Fact]
    public void ToSummaryDto_MapsEtag_FromJobETag()
    {
        var job = CreateJob();
        job.ETag = "etag-summary-1";

        var dto = job.ToSummaryDto();

        dto.Etag.ShouldBe("etag-summary-1");
    }

    [Fact]
    public void ToSummaryDto_WithMeasuredDuration_CarriesMeasurementAndDelta()
    {
        var job = CreateJob(measuredDuration: 28.5);

        var dto = job.ToSummaryDto();

        dto.MeasuredDurationSec.ShouldBe(28.5);
        dto.DeltaSec.ShouldBe(28.5 - 30); // -1.5
    }

    [Fact]
    public void ToSummaryDto_FromListItem_PreservesScalarListContract()
    {
        var item = new JobListItem(
            "summary-1",
            "KIIS 90s",
            "Sydney",
            22,
            JobStatus.WaitingApproval,
            FixedTime,
            FixedTime.AddMinutes(1),
            "etag-1",
            21.5);

        var dto = item.ToSummaryDto();

        dto.JobId.ShouldBe("summary-1");
        dto.Status.ShouldBe("WAITING_APPROVAL");
        dto.TargetDurationSec.ShouldBe(22);
        dto.MeasuredDurationSec.ShouldBe(21.5);
        dto.DeltaSec.ShouldBe(-0.5);
        dto.Etag.ShouldBe("etag-1");
    }

    // =====================================================================
    // Status string mapping (tested via ToSummaryDto)
    // =====================================================================

    [Theory]
    [InlineData(JobStatus.Requested, "REQUESTED")]
    [InlineData(JobStatus.HereFetched, "HERE_FETCHED")]
    [InlineData(JobStatus.IncidentsSelected, "INCIDENTS_SELECTED")]
    [InlineData(JobStatus.ScriptReady, "SCRIPT_READY")]
    [InlineData(JobStatus.SynthesizingAudio, "SYNTHESIZING_AUDIO")]
    [InlineData(JobStatus.AudioReady, "AUDIO_READY")]
    [InlineData(JobStatus.WaitingApproval, "WAITING_APPROVAL")]
    [InlineData(JobStatus.Approved, "APPROVED")]
    [InlineData(JobStatus.Rejected, "REJECTED")]
    [InlineData(JobStatus.Published, "PUBLISHED")]
    [InlineData(JobStatus.FallbackUsed, "FALLBACK_USED")]
    [InlineData(JobStatus.Failed, "FAILED")]
    [InlineData(JobStatus.Missed, "MISSED")]
    public void ToSummaryDto_StatusStringMapping_IsCorrect(JobStatus status, string expected)
    {
        var job = CreateJob(status);

        var dto = job.ToSummaryDto();

        dto.Status.ShouldBe(expected);
    }

    // =====================================================================
    // ToDetailDto — one test per status
    // =====================================================================

    [Fact]
    public void ToDetailDto_Requested_ReturnsRequestedJobDto()
    {
        var job = CreateJob();

        var dto = job.ToDetailDto("https://audio.test/a.wav", string.Empty);

        dto.ShouldBeOfType<RequestedJobDto>();
        dto.Status.ShouldBe("REQUESTED");
        dto.JobId.ShouldBe("job-1");
        dto.Station.ShouldBe("2GB");
        dto.Market.ShouldBe("Sydney");
    }

    [Fact]
    public void ToDetailDto_HereFetched_ReturnsTrafficFetchedJobDto()
    {
        var job = CreateJob(JobStatus.HereFetched);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<TrafficFetchedJobDto>();
        ((TrafficFetchedJobDto)dto).Traffic.ShouldNotBeNull();
    }

    [Fact]
    public void ToDetailDto_IncidentsSelected_ReturnsIncidentsSelectedJobDto()
    {
        var job = CreateJob(JobStatus.IncidentsSelected);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<IncidentsSelectedJobDto>();
    }

    [Fact]
    public void ToDetailDto_ScriptReady_ReturnsScriptReadyJobDto()
    {
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some("Traffic on the M4...");
        job.SetAttemptCount(2);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<ScriptReadyJobDto>();
        var scriptDto = (ScriptReadyJobDto)dto;
        scriptDto.Script.Data.Text.ShouldBe("Traffic on the M4...");
        scriptDto.Script.Data.Version.ShouldBe(2);
    }

    [Fact]
    public void ToDetailDto_MonitoredRoutes_ReachTheOperatorConsoleAsTrafficEvidence()
    {
        // Job cb5eadd4 (test, 2026-08-23): the drive-time run the read quoted was measured, stored
        // and correct, but no DTO carried it, so the console had nothing to check the claim
        // against and flagged a grounded sentence "No source found".
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some("The M4 from Parramatta to the city is a slower run, taking 33 minutes.");
        job.MonitoredRoutes =
        [
            new MonitoredRouteStatus(
                "the M4 from Parramatta to the city",
                new CoveragePoint(-33.8148, 151.0017),
                new CoveragePoint(-33.8688, 151.2093),
                33, 26, 25.36)
        ];

        var traffic = ((ScriptReadyJobDto)job.ToDetailDto("url", string.Empty)).Traffic;

        traffic.MonitoredRoutes.ShouldNotBeNull();
        var route = traffic.MonitoredRoutes.ShouldHaveSingleItem();
        route.Name.ShouldBe("the M4 from Parramatta to the city");
        route.DriveTimeMinutes.ShouldBe(33);
        route.NormalDriveTimeMinutes.ShouldBe(26);
        route.DelayMinutes.ShouldBe(7);
        route.DistanceKm.ShouldBe(25.4);
    }

    [Fact]
    public void ToDetailDto_SentenceRouteLineage_IsCarriedThroughToTheScriptDto()
    {
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some("The M4 from Parramatta to the city is a slower run.");
        job.SetScriptSentences(
        [
            new JobScriptSentence(
                "The M4 from Parramatta to the city is a slower run.",
                [],
                RouteKeys: ["the M4 from Parramatta to the city"])
        ]);

        var script = ((ScriptReadyJobDto)job.ToDetailDto("url", string.Empty)).Script;

        var sentence = script.Data.Sentences.ShouldNotBeNull().ShouldHaveSingleItem();
        sentence.RouteKeys.ShouldBe(["the M4 from Parramatta to the city"]);
        sentence.CorridorKeys.ShouldBeEmpty();
        sentence.EvidenceIds.ShouldBeEmpty();
    }

    [Fact]
    public void ToDetailDto_WaitingApproval_ReturnsWaitingApprovalJobDto()
    {
        var job = CreateJob(JobStatus.WaitingApproval);
        job.MeasuredDurationSec = Option<double>.Some(28.0);

        var dto = job.ToDetailDto("https://audio.test/a.wav", string.Empty);

        dto.ShouldBeOfType<WaitingApprovalJobDto>();
        var wa = (WaitingApprovalJobDto)dto;
        wa.Audio.MeasuredDurationSec.ShouldBe(28.0);
        wa.Audio.Data.Url.ShouldBe("https://audio.test/a.wav");
    }

    [Fact]
    public void ToDetailDto_Approved_ReturnsApprovedJobDto()
    {
        var job = CreateJob(JobStatus.Approved);
        job.SetStatusHistory(new[]
        {
            new StatusHistoryEntry(JobStatus.Requested, FixedTime, "system"),
            new StatusHistoryEntry(JobStatus.Approved, FixedTime.AddHours(1), "operator")
        });

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<ApprovedJobDto>();
        var approved = (ApprovedJobDto)dto;
        approved.Approval.DecidedBy.ShouldBe("operator");
    }

    [Fact]
    public void ToDetailDto_Rejected_ReturnsRejectedJobDto()
    {
        var job = CreateJob(JobStatus.Rejected);
        job.AddRejection(new RejectionEntry("bad", "notes", FixedTime, "operator"));

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<RejectedJobDto>();
        var rejected = (RejectedJobDto)dto;
        rejected.Rejection.Reason.ShouldBe("bad");
        rejected.RejectionHistory.Count().ShouldBe(1);
        rejected.RejectionHistory[0].Notes.ShouldBe("notes");
    }

    [Fact]
    public void ToDetailDto_Published_ReturnsPublishedJobDto()
    {
        var job = CreateJob(JobStatus.Published);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<PublishedJobDto>();
        var pub = (PublishedJobDto)dto;
        pub.Publish.Destination.ShouldBe("broadcast");
    }

    /// <summary>
    ///     Since coverage started delivering (2026-08-11), a covered slot ends at Published,
    ///     so provenance has to survive the transition: without it a published job looks
    ///     identical whether it aired its own read or a repeat of one from three hours ago.
    /// </summary>
    [Fact]
    public void ToDetailDto_PublishedAfterARepeat_KeepsTheProvenance()
    {
        var job = CreateJob(JobStatus.Published);
        job.FallbackSourceJobId = Option<string>.Some("job-source");
        job.FallbackAppliedAt = Option<DateTime>.Some(FixedTime);
        job.FallbackReason = Option<string>.Some("operator chose a repeat");
        job.SetFallbackStreak(2);

        var pub = (PublishedJobDto)job.ToDetailDto("url", string.Empty);

        pub.Fallback.ShouldNotBeNull();
        pub.Fallback!.SourceJobId.ShouldBe("job-source");
        pub.Fallback.Reason.ShouldBe("operator chose a repeat");
        pub.Fallback.Streak.ShouldBe(2);
    }

    /// <summary>
    ///     The standby cart has no source bulletin to point at, so keying provenance off the
    ///     source job id hid cart coverage entirely (caught on dev, 2026-08-11).
    /// </summary>
    [Fact]
    public void ToDetailDto_PublishedAfterTheCart_KeepsTheProvenanceWithNoSourceJob()
    {
        var job = CreateJob(JobStatus.Published);
        // No source job: Job.ApplyFillerFallback leaves it None for the cart.
        job.FallbackAppliedAt = Option<DateTime>.Some(FixedTime);
        job.FallbackReason = Option<string>.Some("operator chose the standby cart");

        var pub = (PublishedJobDto)job.ToDetailDto("url", string.Empty);

        pub.Fallback.ShouldNotBeNull();
        pub.Fallback!.SourceJobId.ShouldBe(string.Empty);
        pub.Fallback.Reason.ShouldBe("operator chose the standby cart");
    }

    [Fact]
    public void ToDetailDto_PublishedOrdinarily_HasNoFallbackBlock()
    {
        var pub = (PublishedJobDto)CreateJob(JobStatus.Published).ToDetailDto("url", string.Empty);

        pub.Fallback.ShouldBeNull();
    }

    [Fact]
    public void ToDetailDto_Published_PrefersPublishedAtAndPublishedByOverAudit()
    {
        var job = CreateJob(JobStatus.Published);
        var publishedAt = new DateTimeOffset(2025, 6, 1, 12, 30, 0, TimeSpan.Zero);
        job.PublishedAt = publishedAt;
        job.PublishedBy = "operator-1";

        var dto = job.ToDetailDto("url", string.Empty);

        var pub = ((PublishedJobDto)dto).Publish;
        pub.PublishedAt.ShouldBe(publishedAt.ToString("o"));
        pub.PublishedBy.ShouldBe("operator-1");
    }

    [Fact]
    public void ToDetailDto_Published_FallsBackToLastUpdatedAtAndUnknownWhenAuditMissing()
    {
        var job = CreateJob(JobStatus.Published);
        job.PublishedAt = null;
        job.PublishedBy = null;

        var dto = job.ToDetailDto("url", string.Empty);

        var pub = ((PublishedJobDto)dto).Publish;
        pub.PublishedAt.ShouldBe(job.LastUpdatedAt.ToString("o"));
        pub.PublishedBy.ShouldBe("unknown");
    }

    [Fact]
    public void ToDetailDto_FallbackUsed_ReturnsFallbackJobDto()
    {
        var job = CreateJob(JobStatus.FallbackUsed);
        job.FallbackSourceJobId = Option<string>.Some("source-job");
        job.FallbackAppliedAt = Option<DateTime>.Some(FixedTime);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<FallbackJobDto>();
        var fb = (FallbackJobDto)dto;
        fb.Fallback.SourceJobId.ShouldBe("source-job");
    }

    [Fact]
    public void ToDetailDto_FallbackUsed_MapsStreakAndReason()
    {
        var job = CreateJob(JobStatus.FallbackUsed);
        job.FallbackSourceJobId = Option<string>.Some("source-job");
        job.FallbackAppliedAt = Option<DateTime>.Some(FixedTime);
        job.FallbackReason = Option<string>.Some("content too short");
        job.SetFallbackStreak(7);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<FallbackJobDto>();
        var fb = (FallbackJobDto)dto;
        fb.Fallback.SourceJobId.ShouldBe("source-job");
        fb.Fallback.Reason.ShouldBe("content too short");
        fb.Fallback.Streak.ShouldBe(7);
    }

    [Fact]
    public void ToDetailDto_Failed_ReturnsFailedJobDto()
    {
        var job = CreateJob(JobStatus.Failed);
        job.ErrorMessage = Option<string>.Some("Something broke");
        job.FailedAtStatus = Option<JobStatus>.Some(JobStatus.ScriptReady);
        job.SetAttemptCount(3);

        var dto = job.ToDetailDto("url", string.Empty);

        dto.ShouldBeOfType<FailedJobDto>();
        var failed = (FailedJobDto)dto;
        // Statuses are stamped at phase start: failing at ScriptReady means
        // synthesis was the work in flight (activity-stage vocabulary).
        failed.Failure.Stage.ShouldBe("SYNTHESIZING_AUDIO");
        failed.Failure.Message.ShouldBe("Something broke");
        failed.Failure.AttemptCount.ShouldBe(3);
    }

    [Fact]
    public void ToDetailDto_Missed_ReturnsMissedJobDtoWithDeadlineAndNoRejectionData()
    {
        var job = CreateJob(JobStatus.Missed);
        var deadline = new DateTime(2025, 6, 1, 9, 50, 0, DateTimeKind.Utc);
        job.MissedDeadlineUtc = Option<DateTime>.Some(deadline);

        var dto = job.ToDetailDto("url", string.Empty);

        // Must not fall through to the RequestedJobDto default (that would emit a
        // REQUESTED-shaped payload under a MISSED discriminator, which the console
        // zod union rejects).
        dto.ShouldBeOfType<MissedJobDto>();
        var missed = (MissedJobDto)dto;
        missed.Status.ShouldBe("MISSED");
        missed.Missed.DeadlineAt.ShouldBe(deadline.ToString("o"));
        missed.Missed.At.ShouldBe(job.LastUpdatedAt.ToString("o"));
        // An expiry is not a rejection: no rejection payload, ever.
        missed.Traffic.ShouldNotBeNull();
        missed.Script.ShouldNotBeNull();
        missed.Audio.ShouldNotBeNull();
    }

    [Fact]
    public void ToDetailDto_SetsBaseProperties_ForAllStatuses()
    {
        var job = CreateJob();

        var dto = job.ToDetailDto("url", string.Empty);

        dto.JobId.ShouldBe("job-1");
        dto.TargetDurationSec.ShouldBe(30);
        dto.Input.ShouldNotBeNull();
        dto.Input.BulletinType.ShouldBe("traffic");
        dto.Logs.ShouldNotBeNull();
        dto.StatusHistory.ShouldNotBeNull();
    }

    [Fact]
    public void ToDetailDto_MapsPronunciationProvenance_WhenDictionaryAndVersionArePresent()
    {
        var job = CreateJob();
        job.PronunciationDictionaryId = Option<string>.Some("dict-sydney");
        job.PronunciationVersionId = Option<string>.Some("version-2025-06-01");

        var dto = job.ToDetailDto("url", string.Empty);

        dto.PronunciationDictionaryId.ShouldBe("dict-sydney");
        dto.PronunciationVersionId.ShouldBe("version-2025-06-01");
    }

    [Fact]
    public void ToDetailDto_MapsPronunciationProvenance_AsNullForLegacyJob()
    {
        var job = CreateJob();

        var dto = job.ToDetailDto("url", string.Empty);

        dto.PronunciationDictionaryId.ShouldBeNull();
        dto.PronunciationVersionId.ShouldBeNull();
    }

    [Fact]
    public void ToDetailDto_MapsEtag_FromJobETag()
    {
        var job = CreateJob();
        job.ETag = "etag-detail-1";

        var dto = job.ToDetailDto("url", string.Empty);

        dto.Etag.ShouldBe("etag-detail-1");
    }

    [Fact]
    public void ToDetailDto_VoiceConfig_MappedCorrectly()
    {
        var job = CreateJob();
        job.VoiceConfig = Option<JobVoiceConfig>.Some(
            new JobVoiceConfig("elevenlabs", "v-1", "Aussie", "eleven_turbo_v2"));

        var dto = job.ToDetailDto("url", string.Empty);

        dto.Input.Voice.Provider.ShouldBe("elevenlabs");
        dto.Input.Voice.VoiceId.ShouldBe("v-1");
        dto.Input.Voice.VoiceName.ShouldBe("Aussie");
        dto.Input.Voice.Model.ShouldBe("eleven_turbo_v2");
    }

    [Fact]
    public void ToDetailDto_Logs_MappedCorrectly()
    {
        var job = CreateJob();
        job.AddLog(new JobLog(FixedTime, JobLogLevel.Info, "Started"));

        var dto = job.ToDetailDto("url", string.Empty);

        dto.Logs.Count().ShouldBe(1);
        dto.Logs[0].Message.ShouldBe("Started");
        dto.Logs[0].Level.ShouldBe(JobLogLevel.Info);
    }

    [Fact]
    public void ToDetailDto_StatusHistory_MappedWithNote()
    {
        var job = Job.Reconstitute(
            "j", "s", "m", 30,
            new[] { new StatusHistoryEntry(JobStatus.Requested, FixedTime, "sys", Option<string>.Some("init")) });

        var dto = job.ToDetailDto("url", string.Empty);

        dto.StatusHistory.Count().ShouldBe(1);
        dto.StatusHistory[0].Note.ShouldBe("init");
    }

    // =====================================================================
    // Evidence pool: Traffic.Incidents = full fetched pool, SelectedIncidents = subset
    // =====================================================================

    [Fact]
    public void ToDetailDto_TrafficIncidents_AreFullPool_WhileSelectedIncidents_AreSubset()
    {
        var job = CreateJob(JobStatus.IncidentsSelected);
        // The selected-corridor subset that backs the bulletin (regeneration seed).
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });
        // The full fetched pool: the selected one plus a non-selected incident.
        job.SetAllIncidents(new[]
        {
            new JobIncident("sel-1", 1, "crash on M4", "M4"),
            new JobIncident("omit-1", 1, "roadworks on M5", "M5"),
        });

        var dto = (IncidentsSelectedJobDto)job.ToDetailDto("url", string.Empty);

        // Traffic.Incidents is the evidence pool — both the selected and the omitted key.
        dto.Traffic.Incidents.Select(i => i.Key).ShouldBe(new[] { "sel-1", "omit-1" }, ignoreOrder: true);
        // SelectedIncidents stays the bulletin subset so the review UI can mark the rest omitted.
        dto.SelectedIncidents.Select(i => i.Key).ShouldBe(new[] { "sel-1" });
    }

    [Fact]
    public void ToDetailDto_TrafficIncidents_FallBackToSelected_WhenAllIncidentsEmpty()
    {
        // Jobs persisted before AllIncidents existed have an empty pool; the pool must
        // degrade to the selected set rather than render no evidence at all.
        var job = CreateJob(JobStatus.IncidentsSelected);
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });

        var dto = (IncidentsSelectedJobDto)job.ToDetailDto("url", string.Empty);

        dto.Traffic.Incidents.Select(i => i.Key).ShouldBe(new[] { "sel-1" });
        dto.SelectedIncidents.Select(i => i.Key).ShouldBe(new[] { "sel-1" });
    }

    // =====================================================================
    // ToDetailDto — coverage snapshot (point-in-time shape)
    // =====================================================================

    [Fact]
    public void ToDetailDto_WithCoverageSnapshot_ExposesShape()
    {
        var polygon = CoverageArea.CreatePolygon(new[]
        {
            new CoveragePoint(-33.86, 151.20),
            new CoveragePoint(-33.86, 151.22),
            new CoveragePoint(-33.88, 151.21),
        }).Value;

        var job = Job.Create(
            "cov-1", "2GB", "Sydney", 30, FixedTime,
            Option<double>.Some(polygon.Latitude),
            Option<double>.Some(polygon.Longitude),
            Option<double>.Some(polygon.RadiusKm),
            coverage: Option<CoverageArea>.Some(polygon)).Value;

        var dto = job.ToDetailDto("url", string.Empty);

        dto.Coverage.ShouldNotBeNull();
        dto.Coverage!.ShapeType.ShouldBe("polygon");
        dto.Coverage.PolygonPoints!.Count.ShouldBe(3);
    }

    [Fact]
    public void ToDetailDto_NoCoverageSnapshot_CoverageIsNull()
    {
        var dto = CreateJob().ToDetailDto("url", string.Empty);

        dto.Coverage.ShouldBeNull();
    }

    // =====================================================================
    // ToDetailDto — incident shape (road polyline for the map)
    // =====================================================================

    [Fact]
    public void ToDetailDto_IncidentShape_ExposedOnTrafficAndSelectedIncidents()
    {
        var job = CreateJob(JobStatus.IncidentsSelected);
        job.SetIncidents(
            new[]
            {
                new JobIncident(
                    "sh-1",
                    1,
                    "construction on M1",
                    "M1",
                    Shape: new List<CoveragePoint>
                    {
                        new(-37.81, 144.96),
                        new(-37.82, 144.97),
                        new(-37.83, 144.98),
                    }
                ),
            }
        );

        var dto = (IncidentsSelectedJobDto)job.ToDetailDto("url", string.Empty);

        var traffic = dto.Traffic.Incidents.Single(i => i.Key == "sh-1");
        traffic.Shape.ShouldNotBeNull();
        traffic.Shape!.Count.ShouldBe(3);
        traffic.Shape[0].Lat.ShouldBe(-37.81);
        traffic.Shape[2].Lng.ShouldBe(144.98);
        dto.SelectedIncidents.Single(i => i.Key == "sh-1").Shape!.Count.ShouldBe(3);
    }

    [Fact]
    public void ToDetailDto_NoIncidentShape_ShapeIsNull()
    {
        var job = CreateJob(JobStatus.IncidentsSelected);
        job.SetIncidents(new[] { new JobIncident("ns-1", 1, "crash on M4", "M4") });

        var dto = (IncidentsSelectedJobDto)job.ToDetailDto("url", string.Empty);

        dto.Traffic.Incidents.Single(i => i.Key == "ns-1").Shape.ShouldBeNull();
    }

    // =====================================================================
    // ToDetailDto — incident field plumbing (Direction, LaneDetail, Comment,
    //               RoadClosed, EntryTimeUtc)
    // =====================================================================

    [Fact]
    public void ToDetailDto_IncidentFields_DirectionLaneDetailCommentAndClosureFlowToDto()
    {
        var job = CreateJob(JobStatus.IncidentsSelected);
        job.SetIncidents(new[]
        {
            new JobIncident(
                "f-1",
                2,
                "crash on M4",
                "M4 Motorway",
                Direction: Option<string>.Some("eastbound"),
                LaneDetail: Option<string>.Some("Both lanes blocked"),
                Comment: Option<string>.Some("Multi-vehicle pileup"),
                RoadClosed: true,
                EntryTimeUtc: new DateTimeOffset(2026, 6, 5, 2, 22, 46, TimeSpan.Zero))
        });

        var dto = (IncidentsSelectedJobDto)job.ToDetailDto("url", string.Empty);
        var incident = dto.SelectedIncidents.Single(i => i.Key == "f-1");

        incident.Direction.ShouldBe("eastbound");
        incident.LaneDetail.ShouldBe("Both lanes blocked");
        incident.Comment.ShouldBe("Multi-vehicle pileup");
        incident.RoadClosed.ShouldBeTrue();
        incident.EntryTimeUtc.ShouldBe(new DateTimeOffset(2026, 6, 5, 2, 22, 46, TimeSpan.Zero));
    }

    [Fact]
    public void ToDetailDto_IncidentFields_OptionNoneFieldsMapToNull()
    {
        var job = CreateJob(JobStatus.IncidentsSelected);
        job.SetIncidents(new[]
        {
            new JobIncident("n-1", 1, "roadworks on M5", "M5")
        });

        var dto = (IncidentsSelectedJobDto)job.ToDetailDto("url", string.Empty);
        var incident = dto.SelectedIncidents.Single(i => i.Key == "n-1");

        incident.Direction.ShouldBeNull();
        incident.LaneDetail.ShouldBeNull();
        incident.Comment.ShouldBeNull();
        incident.RoadClosed.ShouldBeFalse();
        incident.EntryTimeUtc.ShouldBeNull();
    }

    // =====================================================================
    // ParseStatusList
    // =====================================================================

    [Fact]
    public void ParseStatusList_Null_ReturnsEmpty()
    {
        var result = JobMappingExtensions.ParseStatusList(null);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public void ParseStatusList_MapsWireStatusesToEnum()
    {
        var result = JobMappingExtensions.ParseStatusList("WAITING_APPROVAL,FAILED");
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new[] { JobStatus.WaitingApproval, JobStatus.Failed });
    }

    [Fact]
    public void ParseStatusList_IgnoresBlanksAndDuplicates()
    {
        var result = JobMappingExtensions.ParseStatusList(" WAITING_APPROVAL , , waiting_approval ");
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new[] { JobStatus.WaitingApproval });
    }

    [Fact]
    public void ParseStatusList_UnrecognizedToken_ReturnsFailure()
    {
        // A typo'd/unknown status must fail the parse, not silently degrade to "no filter"
        // (which would leak the full unfiltered dataset to the caller).
        var result = JobMappingExtensions.ParseStatusList(" WAITING_APPROVAL , NONSENSE ");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ToDetailDto_TimeWindow_IsAValidIntervalWithHonestBounds()
    {
        // Regression for bug-bash 2026-08-05 #48: the window mapped From = ScheduledForUtc and
        // To = FallbackDeadlineUtc, so every poller job carried From > To (e.g. 12:47 → 12:44)
        // with both labels describing the wrong field. The window now runs from the fallback
        // deadline (when standby coverage would kick in) to the scheduled air slot.
        var job = CreateJob();
        job.ScheduledForUtc = Option<DateTime>.Some(new DateTime(2026, 8, 5, 12, 47, 0, DateTimeKind.Utc));
        job.FallbackDeadlineUtc = Option<DateTime>.Some(new DateTime(2026, 8, 5, 12, 44, 0, DateTimeKind.Utc));

        var dto = job.ToDetailDto(string.Empty, string.Empty);

        var from = DateTimeOffset.Parse(dto.Input.TimeWindow.From);
        var to = DateTimeOffset.Parse(dto.Input.TimeWindow.To);
        from.ShouldBeLessThan(to);
        from.UtcDateTime.ShouldBe(job.FallbackDeadlineUtc.Value);
        to.UtcDateTime.ShouldBe(job.ScheduledForUtc.Value);
    }
}
