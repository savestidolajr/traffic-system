using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Application.Tests;

public class JobMessageBuilderTests
{
    // =====================================================================
    // BuildRejectionFeedback
    // =====================================================================

    [Fact]
    public void BuildRejectionFeedback_EmptyHistory_NoContext_ReturnsDefault()
    {
        string result = JobMessageBuilder.BuildRejectionFeedback(
            [], Option<string>.None);

        result.ShouldBe("No specific feedback provided.");
    }

    [Fact]
    public void BuildRejectionFeedback_WithRejectionReason_IncludesReason()
    {
        string result = JobMessageBuilder.BuildRejectionFeedback(
            [], Option<string>.None,
            "Too long");

        result.ShouldContain("Rejection reason: Too long");
    }

    [Fact]
    public void BuildRejectionFeedback_WithRejectionDescription_IncludesDescription()
    {
        string result = JobMessageBuilder.BuildRejectionFeedback(
            [], Option<string>.None,
            rejectionDescription: "Script exceeded 40 seconds");

        result.ShouldContain("Detailed feedback: Script exceeded 40 seconds");
    }

    [Fact]
    public void BuildRejectionFeedback_SingleRejection_UsesPreviousVersionWording()
    {
        var history = new List<RejectionEntry>
        {
            new("bad", "needs improvement", DateTime.UtcNow, "operator")
        };

        string result = JobMessageBuilder.BuildRejectionFeedback(
            history, Option<string>.None);

        result.ShouldContain("Previous version was rejected: needs improvement");
        result.ShouldNotContain("Attempt 1");
    }

    [Fact]
    public void BuildRejectionFeedback_MultipleRejections_UsesAttemptNumbering()
    {
        var history = new List<RejectionEntry>
        {
            new("bad", "first attempt notes", DateTime.UtcNow, "op"),
            new("bad", "second attempt notes", DateTime.UtcNow, "op")
        };

        string result = JobMessageBuilder.BuildRejectionFeedback(
            history, Option<string>.None);

        result.ShouldContain("Attempt 1 was rejected: first attempt notes");
        result.ShouldContain("Attempt 2 was rejected: second attempt notes");
    }

    [Fact]
    public void BuildRejectionFeedback_WithAdditionalContext_AppendsContext()
    {
        var history = new List<RejectionEntry>
        {
            new("bad", "notes", DateTime.UtcNow, "op")
        };

        string result = JobMessageBuilder.BuildRejectionFeedback(
            history, Option<string>.Some("focus on brevity"));

        result.ShouldContain("Previous version was rejected: notes");
        result.ShouldContain("\n\nAdditional context: focus on brevity");
    }

    [Fact]
    public void BuildRejectionFeedback_EmptyAdditionalContext_Ignored()
    {
        var history = new List<RejectionEntry>
        {
            new("bad", "notes", DateTime.UtcNow, "op")
        };

        string result = JobMessageBuilder.BuildRejectionFeedback(
            history, Option<string>.Some("   "));

        result.ShouldNotContain("Additional context:");
    }

    [Fact]
    public void BuildRejectionFeedback_EmptyHistoryWithAdditionalContextOnly_ReturnsContext()
    {
        string result = JobMessageBuilder.BuildRejectionFeedback(
            [], Option<string>.Some("just context"));

        result.ShouldBe("just context");
    }

    [Fact]
    public void BuildRejectionFeedback_ReasonAndDescription_BothIncluded()
    {
        string result = JobMessageBuilder.BuildRejectionFeedback(
            [], Option<string>.None,
            "Too long",
            "Over 40 seconds");

        result.ShouldContain("Rejection reason: Too long");
        result.ShouldContain("Detailed feedback: Over 40 seconds");
    }

    // =====================================================================
    // BuildRegenerationQueueMessage
    // =====================================================================

    [Fact]
    public void BuildRegenerationQueueMessage_MapsJobFieldsCorrectly()
    {
        var job = Job.Create("job-1", "2GB", "Sydney", 30, DateTime.UtcNow).Value;

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "fix it");

        msg.JobId.ShouldBe("job-1");
        msg.Location.ShouldBe("Sydney");
        msg.TargetDurationSeconds.ShouldBe(30);
        msg.Traffic.ShouldBe("Sydney");
        msg.RejectionFeedback.ShouldBe("fix it");
        msg.Incidents.ShouldBeEmpty();
    }

    [Fact]
    public void BuildRegenerationQueueMessage_MapsIncidentsWithAllFields()
    {
        var job = Job.Create("j-2", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.AddIncident(new JobIncident(
            "inc-1",
            2,
            "Accident on M4",
            "M4 Motorway",
            1,
            500,
            Latitude: Option<double>.Some(-33.8),
            Longitude: Option<double>.Some(151.2)));

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "feedback");

        msg.Incidents.Count().ShouldBe(1);
        var inc = msg.Incidents[0];
        inc.Id.ShouldBe("inc-1");
        inc.Description.ShouldBe("Accident on M4");
        inc.Severity.ShouldBe(2);
        inc.Location.ShouldBe("M4 Motorway");
        inc.Type.ShouldBe("incident");
        inc.LengthMeters.ShouldBe(500);
        inc.FunctionalClass.ShouldBe(1);
        inc.Latitude.ShouldBe(-33.8);
        inc.Longitude.ShouldBe(151.2);
    }

    [Fact]
    public void BuildRegenerationQueueMessage_NullableLatLon_WhenNone()
    {
        var job = Job.Create("j-3", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.AddIncident(new JobIncident("k", 1, "H", "L"));

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "fb");

        msg.Incidents[0].Latitude.ShouldBeNull();
        msg.Incidents[0].Longitude.ShouldBeNull();
    }

    [Fact]
    public void BuildRegenerationQueueMessage_UsesMarketAsLocationAndTraffic()
    {
        var job = Job.Create("j-4", "STATION", "MARKET", 25, DateTime.UtcNow).Value;

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "");

        msg.Location.ShouldBe("MARKET");
        msg.Traffic.ShouldBe("MARKET");
    }

    [Fact]
    public void BuildRegenerationQueueMessage_IncludesVoiceConfig()
    {
        var job = Job.Create("j-5", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "ElevenLabs",
            "voice-123",
            "Radio Host V2",
            "eleven_multilingual_v2",
            Option<VoiceSettings>.Some(VoiceSettings.Create(0.5, 0.75, 0.0, false, 1.0).Value)));

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "fix it");

        msg.VoiceId.ShouldBe("voice-123");
        msg.Model.ShouldBe("eleven_multilingual_v2");
        msg.VoiceSettings.ShouldNotBeNull();
        msg.VoiceSettings!.Stability.ShouldBe(0.5);
    }

    [Fact]
    public void BuildRegenerationQueueMessage_NoVoiceConfig_NullVoiceFields()
    {
        var job = Job.Create("j-6", "2GB", "Sydney", 30, DateTime.UtcNow).Value;

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "fb");

        msg.VoiceId.ShouldBeNull();
        msg.Model.ShouldBeNull();
        msg.VoiceSettings.ShouldBeNull();
    }

    [Fact]
    public void BuildRegenerationQueueMessage_WithEditedScript_UsesItAsPreviousScript()
    {
        var job = Job.Create("j-edit", "2GB", "Sydney", 30, DateTime.UtcNow).Value;

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(
            job, "fb", Option<string>.Some("operator edited script"));

        msg.PreviousScript.ShouldBe("operator edited script");
    }

    [Fact]
    public void BuildRegenerationQueueMessage_CarriesExcludedIncidentsAndRevoice()
    {
        var job = Job.Create("j-x", "2GB", "Sydney", 30, DateTime.UtcNow).Value;

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(
            job, "fb",
            editedScript: default,
            selectedCorridors: default,
            deliveryIntent: default,
            selectedGoodRuns: default,
            excludedIncidentKeys: Option<IReadOnlyList<string>>.Some(["inc-9"]),
            revoiceOnly: true);

        msg.ExcludedIncidentKeys.ShouldNotBeNull();
        msg.ExcludedIncidentKeys!.ShouldBe(["inc-9"]);
        msg.RevoiceOnly.ShouldBeTrue();
    }

    [Fact]
    public void BuildRegenerationQueueMessage_DefaultsExcludedNullAndRevoiceFalse()
    {
        var job = Job.Create("j-y", "2GB", "Sydney", 30, DateTime.UtcNow).Value;

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "fb");

        msg.ExcludedIncidentKeys.ShouldBeNull();
        msg.RevoiceOnly.ShouldBeFalse();
    }

    [Fact]
    public void BuildRegenerationQueueMessage_PreservesExactPronunciationLocator()
    {
        var job = Job.Create("j-pron", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.PronunciationDictionaryId = Option<string>.Some("dictionary-original");
        job.PronunciationVersionId = Option<string>.Some("version-original");

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "revoice", revoiceOnly: true);

        msg.PronunciationLocator.ShouldBe(new PronunciationLocator("dictionary-original", "version-original"));
    }

    [Fact]
    public void BuildRegenerationQueueMessage_IncompletePronunciationLocator_IsNull()
    {
        var job = Job.Create("j-pron-legacy", "2GB", "Sydney", 30, DateTime.UtcNow).Value;
        job.PronunciationDictionaryId = Option<string>.Some("dictionary-original");

        var msg = JobMessageBuilder.BuildRegenerationQueueMessage(job, "revoice", revoiceOnly: true);

        msg.PronunciationLocator.ShouldBeNull();
    }
}
