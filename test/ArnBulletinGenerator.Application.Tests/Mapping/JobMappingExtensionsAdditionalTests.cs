using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Mapping;

public sealed class JobMappingExtensionsAdditionalTests
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
    // ToDetailDto — SynthesizingAudio
    // =====================================================================

    [Fact]
    public void ToDetailDto_SynthesizingAudio_ReturnsSynthesizingAudioJobDto()
    {
        var job = CreateJob(JobStatus.SynthesizingAudio);
        job.ScriptText = Option<string>.Some("Traffic moving along the M2...");

        var dto = job.ToDetailDto("https://audio.test/synth.wav", string.Empty);

        dto.ShouldBeOfType<SynthesizingAudioJobDto>();
        var synth = (SynthesizingAudioJobDto)dto;
        synth.Status.ShouldBe("SYNTHESIZING_AUDIO");
        synth.Script.Data.Text.ShouldBe("Traffic moving along the M2...");
        synth.Audio.Data.Url.ShouldBe("https://audio.test/synth.wav");
    }

    // =====================================================================
    // ToDetailDto — AudioReady
    // =====================================================================

    [Fact]
    public void ToDetailDto_AudioReady_ReturnsAudioReadyJobDto()
    {
        var job = CreateJob(JobStatus.AudioReady, measuredDuration: 29.5);
        job.ScriptText = Option<string>.Some("Script text");

        var dto = job.ToDetailDto("https://audio.test/ready.wav", string.Empty);

        dto.ShouldBeOfType<AudioReadyJobDto>();
        var ar = (AudioReadyJobDto)dto;
        ar.Status.ShouldBe("AUDIO_READY");
        ar.Audio.MeasuredDurationSec.ShouldBe(29.5);
        ar.Audio.DeltaSec.ShouldBe(-0.5, 0.001);
        ar.Audio.Data.Url.ShouldBe("https://audio.test/ready.wav");
    }

    // =====================================================================
    // ComputeBbox
    // =====================================================================

    [Fact]
    public void ToDetailDto_NoGeoData_BboxIsEmpty()
    {
        // Job created via Reconstitute with default lat/lng/radius (all None)
        var job = CreateJob(JobStatus.HereFetched);

        var dto = (TrafficFetchedJobDto)job.ToDetailDto("url", string.Empty);

        dto.Traffic.Bbox.ShouldBeEmpty();
    }

    [Fact]
    public void ToDetailDto_WithGeoData_BboxIsWestSouthEastNorth()
    {
        var job = Job.Reconstitute(
            "geo-1", "2GB", "Sydney", 30,
            new[] { new StatusHistoryEntry(JobStatus.HereFetched, FixedTime, "system") },
            Option<double>.Some(-33.8688),
            Option<double>.Some(151.2093),
            Option<double>.Some(25.0));

        var dto = (TrafficFetchedJobDto)job.ToDetailDto("url", string.Empty);

        dto.Traffic.Bbox.Count().ShouldBe(4);
        // [west, south, east, north]
        double[] bbox = dto.Traffic.Bbox;
        bbox[0].ShouldBeLessThan(151.2093); // west < lng
        bbox[1].ShouldBeLessThan(-33.8688); // south < lat
        bbox[2].ShouldBeGreaterThan(151.2093); // east > lng
        bbox[3].ShouldBeGreaterThan(-33.8688); // north > lat
    }

    // =====================================================================
    // MapFailure — with attempt history
    // =====================================================================

    [Fact]
    public void ToDetailDto_Failed_WithAttemptHistory_MapsEntries()
    {
        var job = CreateJob(JobStatus.Failed);
        job.ErrorMessage = Option<string>.Some("Duration too long");
        job.FailedAtStatus = Option<JobStatus>.Some(JobStatus.AudioReady);
        job.SetAttemptCount(2);
        job.SetAttemptHistory(new[]
        {
            new AttemptResult(1, 3.5, false, FixedTime,
                Option<double>.Some(28.0),
                Option<double>.Some(35.0),
                Option<string>.Some("Over target")),
            new AttemptResult(2, 3.2, true, FixedTime.AddMinutes(5),
                Option<double>.Some(30.0),
                Option<double>.Some(30.5))
        });

        var dto = (FailedJobDto)job.ToDetailDto("url", string.Empty);

        dto.Failure.AttemptHistory.Count().ShouldBe(2);
        dto.Failure.AttemptHistory[0].AttemptNumber.ShouldBe(1);
        dto.Failure.AttemptHistory[0].Succeeded.ShouldBeFalse();
        dto.Failure.AttemptHistory[0].FailureReason.ShouldBe("Over target");
        dto.Failure.AttemptHistory[0].EstimatedDurationSec.ShouldBe(28.0);
        dto.Failure.AttemptHistory[0].ActualDurationSec.ShouldBe(35.0);
        dto.Failure.AttemptHistory[1].Succeeded.ShouldBeTrue();
        dto.Failure.AttemptHistory[1].FailureReason.ShouldBeNull();
    }
    [Fact]
    public void ToDetailDto_Failed_WithCandidateArtifacts_MapsScriptAndAudio()
    {
        var job = CreateJob(JobStatus.Failed);
        job.ScriptText = Option<string>.Some("The strongest available traffic read.");
        job.LastEstimatedDurationSec = Option<double>.Some(27.4);
        job.MeasuredDurationSec = Option<double>.Some(27.2);
        job.AudioUrl = Option<string>.Some("https://audio.test/candidate.wav");

        var dto = (FailedJobDto)job.ToDetailDto("https://audio.test/candidate.wav", string.Empty);

        dto.Script.ShouldNotBeNull();
        dto.Script.Data.Text.ShouldBe("The strongest available traffic read.");
        dto.Script.Data.EstimatedDurationSec.ShouldBe(27.4);
        dto.Audio.ShouldNotBeNull();
        dto.Audio.Data.Url.ShouldBe("https://audio.test/candidate.wav");
        dto.Audio.Data.DurationSec.ShouldBe(27.2);
    }

    [Fact]
    public void ToDetailDto_Failed_WithTrafficData_MapsTrafficAndSelectedIncidents()
    {
        var job = CreateJob(JobStatus.Failed);
        job.SetIncidents([new JobIncident("inc-1", 1, "Crash", "M1 slow")]);
        job.SetCorridors([new JobCorridor("M1", 2, 50.0, "congestion", "Delays on M1", 1, 2.0, 1, true, IncidentKeys: ["inc-1"])]);

        var dto = (FailedJobDto)job.ToDetailDto("https://audio.test/candidate.wav", string.Empty);

        dto.Traffic.ShouldNotBeNull();
        dto.Traffic.Incidents.Count.ShouldBe(1);
        dto.Traffic.Incidents[0].Key.ShouldBe("inc-1");
        dto.SelectedIncidents.ShouldNotBeNull();
        dto.SelectedIncidents.Count.ShouldBe(1);
        dto.SelectedIncidents[0].Key.ShouldBe("inc-1");
    }

    /// <summary>
    ///     The job-detail DTO is what the operator console reads, so the boost fields have to
    ///     survive THIS mapping too. `MapTrafficData` built its FlowRoadDto positionally and passed
    ///     `f.FunctionalClass` into the 8th slot, which is `BaseScore` (double?) — an implicit int
    ///     conversion, so it compiled clean while writing the functional class into BaseScore,
    ///     dropping BoostAmount entirely, and leaving FunctionalClass on its default of 3. That kept
    ///     the review rundown showing a bare "(+)" with no amount even after the persistence-side
    ///     fix in AudioFinalizer, because the API re-introduced the identical defect on the way out
    ///     (dev job 02ce3c71: BaseScore 1, BoostAmount null, FunctionalClass 3 for a boosted FC1
    ///     motorway).
    /// </summary>
    [Fact]
    public void ToDetailDto_BoostedFlowRoad_PreservesBoostAmountBaseScoreAndFunctionalClass()
    {
        var job = CreateJob(JobStatus.Failed);
        job.SetFlowRoads([
            new JobFlowRoad(
                "M5 South Western Motorway", 1.5, 9000, IsWeighted: true, IsSelected: true,
                Score: 112.5, PriorityBoosted: true,
                BaseScore: 62.5, BoostAmount: 50.0, FunctionalClass: 1)
        ]);

        var dto = (FailedJobDto)job.ToDetailDto("https://audio.test/candidate.wav", string.Empty);
        dto.Traffic.ShouldNotBeNull();
        var flow = dto.Traffic!.FlowRoads!.Single();

        flow.PriorityBoosted.ShouldBeTrue();
        flow.BoostAmount.ShouldNotBeNull();
        flow.BoostAmount!.Value.ShouldBe(50.0, 0.001);
        flow.BaseScore.ShouldNotBeNull();
        flow.BaseScore!.Value.ShouldBe(62.5, 0.001);
        flow.FunctionalClass.ShouldBe(1);
    }


    // =====================================================================
    // MapRejection — empty rejection history
    // =====================================================================

    [Fact]
    public void ToDetailDto_Rejected_EmptyRejectionHistory_DefaultsToUnknown()
    {
        var job = CreateJob(JobStatus.Rejected);
        // No rejection entries added — history is empty

        var dto = (RejectedJobDto)job.ToDetailDto("url", string.Empty);

        dto.Rejection.Reason.ShouldBe("unknown");
        dto.Rejection.DecidedBy.ShouldBe("system");
        dto.RejectionHistory.ShouldBeEmpty();
    }

    // =====================================================================
    // MapRejectionHistory — multiple entries
    // =====================================================================

    [Fact]
    public void ToDetailDto_Rejected_MultipleRejections_MapsAll()
    {
        var job = CreateJob(JobStatus.Rejected);
        job.AddRejection(new RejectionEntry("pacing", "too fast", FixedTime, "operator1",
            Option<string>.Some("Script v1")));
        job.AddRejection(new RejectionEntry("tone", "too formal", FixedTime.AddHours(1), "operator2"));

        var dto = (RejectedJobDto)job.ToDetailDto("url", string.Empty);

        dto.RejectionHistory.Count().ShouldBe(2);
        dto.RejectionHistory[0].Reason.ShouldBe("pacing");
        dto.RejectionHistory[0].Notes.ShouldBe("too fast");
        dto.RejectionHistory[0].RejectedScript.ShouldBe("Script v1");
        dto.RejectionHistory[0].By.ShouldBe("operator1");
        dto.RejectionHistory[1].Reason.ShouldBe("tone");
        dto.RejectionHistory[1].RejectedScript.ShouldBeNull();
        dto.Rejection.Reason.ShouldBe("tone"); // latest entry
    }

    // =====================================================================
    // MapAudioData — format and sample rate
    // =====================================================================

    [Fact]
    public void ToDetailDto_AudioData_HasCorrectFormatAndSampleRate()
    {
        var job = CreateJob(JobStatus.WaitingApproval, measuredDuration: 28.0);

        var dto = (WaitingApprovalJobDto)job.ToDetailDto("https://audio.test/file.wav", string.Empty);

        dto.Audio.Data.Format.ShouldBe("wav");
        dto.Audio.Data.SampleRateHz.ShouldBe(44100);
        dto.Audio.Data.DurationSec.ShouldBe(28.0);
        dto.Audio.Data.Url.ShouldBe("https://audio.test/file.wav");
    }

    // =====================================================================
    // MapScriptData — version/text/estimatedDuration
    // =====================================================================

    [Fact]
    public void ToDetailDto_ScriptData_MapsVersionTextAndEstimatedDuration()
    {
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some("Hello Sydney drivers...");
        job.SetAttemptCount(3);
        job.LastEstimatedDurationSec = Option<double>.Some(29.5);

        var dto = (ScriptReadyJobDto)job.ToDetailDto("url", string.Empty);

        dto.Script.Data.Version.ShouldBe(3);
        dto.Script.Data.Text.ShouldBe("Hello Sydney drivers...");
        dto.Script.Data.EstimatedDurationSec.ShouldBe(29.5);
    }

    // =====================================================================
    // MapScriptData — ScriptSentences provenance
    // =====================================================================

    [Fact]
    public void ToDetailDto_ScriptSentences_EmptyWhenNoSentences()
    {
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some("Hello Sydney drivers...");
        // ScriptSentences defaults to empty

        var dto = (ScriptReadyJobDto)job.ToDetailDto("url", string.Empty);

        dto.Script.Data.Sentences.ShouldBeNull();
    }

    [Fact]
    public void ToDetailDto_ScriptSentences_MappedWhenPresent()
    {
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some("Crash on M4. Drive safely.");
        job.SetScriptSentences([
            new JobScriptSentence("Crash on M4.", ["M4"]),
            new JobScriptSentence("Drive safely.", [])
        ]);

        var dto = (ScriptReadyJobDto)job.ToDetailDto("url", string.Empty);

        dto.Script.Data.Sentences.ShouldNotBeNull();
        dto.Script.Data.Sentences.Count.ShouldBe(2);
        dto.Script.Data.Sentences[0].Text.ShouldBe("Crash on M4.");
        dto.Script.Data.Sentences[0].CorridorKeys.ShouldBe(["M4"]);
        dto.Script.Data.Sentences[1].Text.ShouldBe("Drive safely.");
        dto.Script.Data.Sentences[1].CorridorKeys.ShouldBeEmpty();
    }
    [Fact]
    public void ToDetailDto_ScriptData_MapsWholeRoadPhraseSpans()
    {
        const string text = "Traffic is slow on Gillies Highway this afternoon.";
        var job = CreateJob(JobStatus.ScriptReady);
        job.ScriptText = Option<string>.Some(text);
        job.SetCorridors([
            new JobCorridor(
                "Gillies Highway",
                Severity: 2,
                ImpactScore: 100,
                PrimaryType: "congestion",
                Description: "slow",
                IncidentCount: 1,
                TotalLengthKm: 1,
                Rank: 1,
                IsSelected: true),
        ]);

        var dto = (ScriptReadyJobDto)job.ToDetailDto("url", string.Empty);

        dto.Script.Data.RoadPhraseSpans.ShouldHaveSingleItem();
        var span = dto.Script.Data.RoadPhraseSpans[0];
        span.RoadName.ShouldBe("Gillies Highway");
        span.Text.ShouldBe("Gillies Highway");
        text[span.Start..span.End].ShouldBe("Gillies Highway");
    }

}
