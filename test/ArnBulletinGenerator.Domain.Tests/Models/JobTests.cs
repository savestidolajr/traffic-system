using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class JobTests
{
    private static readonly DateTime FixedTime = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Job CreateJob(string id = "j1", string station = "S1", string market = "M1", double dur = 30)
    {
        return Job.Create(id, station, market, dur, FixedTime).Value;
    }

    private static Job CreateJobInStatus(JobStatus status)
    {
        return Job.Reconstitute("j1", "S1", "M1", 30, [new StatusHistoryEntry(status, DateTime.UtcNow, "test")]);
    }

    // ── Constructor ──────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsInitialState()
    {
        var job = CreateJob();

        job.JobId.ShouldBe("j1");
        job.Station.ShouldBe("S1");
        job.Market.ShouldBe("M1");
        job.TargetDurationSeconds.ShouldBe(30);
        job.Status.ShouldBe(JobStatus.Requested);
        job.StatusHistory.ShouldHaveSingleItem()
            .Status.ShouldBe(JobStatus.Requested);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyJobId_Fails(string? id)
    {
        Job.Create(id!, "S1", "M1", 30, DateTime.UtcNow).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyStation_Fails(string? station)
    {
        Job.Create("j1", station!, "M1", 30, DateTime.UtcNow).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyMarket_Fails(string? market)
    {
        Job.Create("j1", "S1", market!, 30, DateTime.UtcNow).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_CreatedAt_UsesInjectedTime()
    {
        var job = Job.Create("j1", "S1", "M1", 30, FixedTime).Value;

        job.CreatedAt.ShouldBe(FixedTime);
        job.LastUpdatedAt.ShouldBe(FixedTime);
    }

    // ── Approve ──────────────────────────────────────────────────

    [Fact]
    public void Approve_FromWaitingApproval_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.WaitingApproval);

        var result = job.Approve(FixedTime, "op-1");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
        job.ApprovedBy.ShouldBe("op-1");
        job.ApprovedAt.ShouldBe(new DateTimeOffset(FixedTime, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(JobStatus.Requested)]
    [InlineData(JobStatus.HereFetched)]
    [InlineData(JobStatus.IncidentsSelected)]
    [InlineData(JobStatus.ScriptReady)]
    [InlineData(JobStatus.SynthesizingAudio)]
    [InlineData(JobStatus.AudioReady)]
    public void Approve_FromInFlight_Fails(JobStatus inFlight)
    {
        // Until 2026-07-29 the domain permitted approval from EVERY in-flight state, so a job
        // with no script and no audio could be approved by any caller reaching the model
        // directly; only JobService's separate check stood in the way. The old test asserted
        // exactly that (Approve_FromRequested_Succeeds). Approval means "authorised for air",
        // which cannot be true before the bulletin has reached review.
        var job = CreateJobInStatus(inFlight);

        var result = job.Approve(FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(inFlight);
    }

    [Theory]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.Failed)]
    public void Approve_FromTerminal_Fails(JobStatus terminal)
    {
        var job = CreateJobInStatus(terminal);

        var result = job.Approve(FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(terminal);
    }

    [Fact]
    public void Approve_FromFallbackUsed_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.FallbackUsed);

        var result = job.Approve(FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
    }

    [Fact]
    public void Approve_FromRejected_Fails()
    {
        var job = CreateJobInStatus(JobStatus.Rejected);

        var result = job.Approve(FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Rejected);
    }

    [Fact]
    public void Approve_FromApproved_Fails()
    {
        // Re-approving an already-approved job would overwrite the recorded approver identity
        // and timestamp — the audit/compliance evidence for who authorised air. Must be rejected.
        var job = CreateJobInStatus(JobStatus.Approved);

        var result = job.Approve(FixedTime, "second-operator");

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
    }

    // ── Reject ───────────────────────────────────────────────────

    [Fact]
    public void Reject_FromWaitingApproval_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.WaitingApproval);
        var entry = new RejectionEntry("bad", "notes", DateTime.UtcNow, "op");

        var result = job.Reject(entry, FixedTime, "op");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Rejected);
        job.RejectionHistory.ShouldHaveSingleItem().Reason.ShouldBe("bad");
        job.ErrorMessage.HasValue.ShouldBeTrue();
    }

    [Fact]
    public void Reject_FromRequested_Fails()
    {
        var job = CreateJob();
        var entry = new RejectionEntry("bad", "notes", DateTime.UtcNow, "op");

        var result = job.Reject(entry, FixedTime, "op");

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Requested);
    }

    // ── Publish ──────────────────────────────────────────────────

    [Fact]
    public void Publish_FromApproved_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.Approved);

        var result = job.Publish(FixedTime, "op-2");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        job.PublishedBy.ShouldBe("op-2");
        job.PublishedAt.ShouldBe(new DateTimeOffset(FixedTime, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(JobStatus.WaitingApproval)]
    [InlineData(JobStatus.Requested)]
    public void Publish_FromNonApproved_Fails(JobStatus status)
    {
        var job = CreateJobInStatus(status);

        var result = job.Publish(FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(status);
    }

    // ── MarkFailed ───────────────────────────────────────────────

    [Fact]
    public void MarkFailed_FromRequested_Succeeds()
    {
        var job = CreateJob();

        var result = job.MarkFailed("oops", FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Failed);
        job.FailedAtStatus.HasValue.ShouldBeTrue();
        job.FailedAtStatus.Value.ShouldBe(JobStatus.Requested);
        job.ErrorMessage.Value.ShouldBe("oops");
    }

    [Theory]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.FallbackUsed)]
    public void MarkFailed_FromTerminal_Fails(JobStatus terminal)
    {
        var job = CreateJobInStatus(terminal);

        var result = job.MarkFailed("err", FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(terminal);
    }

    // ── RequestRegeneration ──────────────────────────────────────

    [Fact]
    public void RequestRegeneration_FromRejected_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("old script");
        job.AudioUrl = Option<string>.Some("http://old");
        job.ErrorMessage = Option<string>.Some("was rejected");
        job.FallbackDeadlineUtc = Option<DateTime>.Some(DateTime.UtcNow.AddMinutes(-5));
        job.SetAttemptCount(3);

        var result = job.RequestRegeneration(FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Requested);
        job.AttemptCount.ShouldBe(0);
        job.ScriptText.HasValue.ShouldBeFalse();
        job.AudioUrl.HasValue.ShouldBeFalse();
        job.ErrorMessage.HasValue.ShouldBeFalse();
        // The slot's delivery deadline SURVIVES regeneration: it belongs to the airtime, not to
        // the attempt. Clearing it took the regenerated job out of ListJobsNeedingFallbackAsync
        // (whose filter a null never matches), so a rejected-then-regenerated scheduled bulletin
        // silently lost auto-delivery, fallback coverage and its no-fallback alert.
        job.FallbackDeadlineUtc.HasValue.ShouldBeTrue();
    }

    [Fact]
    public void RequestRegeneration_FromRequested_Fails()
    {
        var job = CreateJob();

        var result = job.RequestRegeneration(FixedTime);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RequestRegeneration_FromFallbackUsed_Succeeds()
    {
        // The standby covered the slot, but the operator can still retry a fresh bulletin.
        var job = CreateJobInStatus(JobStatus.FallbackUsed);
        job.ScriptText = Option<string>.Some("standby script");
        job.AudioUrl = Option<string>.Some("http://standby");

        var result = job.RequestRegeneration(FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Requested);
        job.ScriptText.HasValue.ShouldBeFalse();
        job.AudioUrl.HasValue.ShouldBeFalse();
    }

    // ── ApplyFallback ────────────────────────────────────────────

    [Fact]
    public void ApplyFallback_FromNonTerminal_Succeeds()
    {
        var job = CreateJob();
        var source = CreateJob("src");
        source.ScriptText = Option<string>.Some("fallback script");
        source.AudioUrl = Option<string>.Some("http://fb");

        var result = job.ApplyFallback(source, FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.ScriptText.Value.ShouldBe("fallback script");
        job.AudioUrl.Value.ShouldBe("http://fb");
        job.FallbackSourceJobId.Value.ShouldBe("src");
        job.FallbackAppliedAt.HasValue.ShouldBeTrue();
        job.FallbackStreak.ShouldBe(1);
    }

    [Theory]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.FallbackUsed)]
    public void ApplyFallback_FromTerminal_Fails(JobStatus terminal)
    {
        var job = CreateJobInStatus(terminal);
        var source = CreateJob("src");

        var result = job.ApplyFallback(source, FixedTime);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ApplyFallback_FromFailed_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.Failed);
        var source = CreateJob("src");
        source.ScriptText = Option<string>.Some("fallback script");
        source.AudioUrl = Option<string>.Some("http://fb");

        var result = job.ApplyFallback(source, FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.ScriptText.Value.ShouldBe("fallback script");
        job.AudioUrl.Value.ShouldBe("http://fb");
        job.FallbackSourceJobId.Value.ShouldBe("src");
        job.FallbackAppliedAt.HasValue.ShouldBeTrue();
    }

    [Fact]
    public void ApplyFallback_FromFallbackUsedSource_IncrementsStreak()
    {
        var upstream = CreateJob("upstream");
        upstream.ApplyFillerFallback("http://filler/cart.wav", FixedTime);
        upstream.FallbackStreak.ShouldBe(1);

        var mid = CreateJob("mid");
        mid.ApplyFallback(upstream, FixedTime);
        mid.FallbackStreak.ShouldBe(2);

        var job = CreateJob();
        var result = job.ApplyFallback(mid, FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.FallbackStreak.ShouldBe(3);
    }

    [Fact]
    public void ApplyFallback_FromNonFallbackSource_SetsStreakToOne()
    {
        var job = CreateJob();
        var source = CreateJobInStatus(JobStatus.Approved);

        var result = job.ApplyFallback(source, FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.FallbackStreak.ShouldBe(1);
    }

    [Fact]
    public void ApplyFallback_CopiesWordTimingsFromSource()
    {
        var job = CreateJob();
        var source = CreateJob("src");
        source.WordTimings = Option<IReadOnlyList<WordTiming>>.Some(
            [new WordTiming("hello", 0, 0.5)]);

        var result = job.ApplyFallback(source, FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.WordTimings.ShouldBe(source.WordTimings);
    }

    // ── ApplyFillerFallback ──────────────────────────────────────

    [Fact]
    public void ApplyFillerFallback_FromNonTerminal_SetsFillerAudioAndFallbackUsed()
    {
        var job = CreateJob();

        var result = job.ApplyFillerFallback("http://filler/cart.wav", FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("http://filler/cart.wav");
        job.FallbackSourceJobId.HasValue.ShouldBeFalse();
        job.FallbackAppliedAt.HasValue.ShouldBeTrue();
        job.FallbackStreak.ShouldBe(1);
    }

    [Fact]
    public void ApplyFillerFallback_WithMeasuredDuration_SurfacesItOnTheJob()
    {
        // Bug-bash 2026-08-05 finding 32: a fallback that under-covers its slot must carry its
        // measured duration so an operator sees the hole before approving — a silent 14s gap in
        // a 30s slot was the defect.
        var job = CreateJob();

        var result = job.ApplyFillerFallback("http://filler/cart.wav", FixedTime, measuredDurationSec: 15.98);

        result.IsSuccess.ShouldBeTrue();
        job.MeasuredDurationSec.Value.ShouldBe(15.98);
    }

    [Fact]
    public void ApplyFillerFallback_WithoutMeasuredDuration_LeavesItUnset()
    {
        // The evergreen cart's duration is not known to the caller — MeasuredDurationSec stays
        // None rather than claiming a value that was never measured.
        var job = CreateJob();

        var result = job.ApplyFillerFallback("http://filler/cart.wav", FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.MeasuredDurationSec.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ApplyFillerFallback_ClearsWordTimings()
    {
        var job = CreateJob();
        job.WordTimings = Option<IReadOnlyList<WordTiming>>.Some(
            [new WordTiming("hello", 0, 0.5)]);

        var result = job.ApplyFillerFallback("http://filler/cart.wav", FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.WordTimings.HasValue.ShouldBeFalse();
    }

    // ── SupersededTake (the bulletin a fallback replaced) ────────

    [Fact]
    public void ApplyFillerFallback_RetainsTheBulletinItReplaced()
    {
        // The filler cart clears ScriptText outright, so before this the generated words were
        // unrecoverable from the job record — measured on dev + test 2026-08-24: 40 of 49 jobs
        // in a 24-hour window had an empty script and zero sentences.
        var job = CreateJob();
        job.ScriptText = Option<string>.Some("The M4 is slow citybound.");
        job.AudioUrl = Option<string>.Some("broadcast_20260824_014619_abc.wav");
        job.SetScriptSentences([new JobScriptSentence("The M4 is slow citybound.", ["M4"])]);

        var result = job.ApplyFillerFallback("filler.wav", FixedTime, reason: "missed-window");

        result.IsSuccess.ShouldBeTrue();
        job.ScriptText.HasValue.ShouldBeFalse();
        job.ScriptSentences.ShouldBeEmpty();
        var take = job.SupersededTake.Value;
        take.ScriptText.ShouldBe("The M4 is slow citybound.");
        take.AudioUrl.Value.ShouldBe("broadcast_20260824_014619_abc.wav");
        take.ScriptSentences.ShouldHaveSingleItem().Text.ShouldBe("The M4 is slow citybound.");
        take.Reason.ShouldBe("missed-window");
        take.CapturedAt.ShouldBe(FixedTime);
    }

    [Fact]
    public void ApplyFallback_RetainsThisJobsBulletin_NotTheSources()
    {
        // ApplyFallback overwrites this job's script with the re-aired source bulletin's, so the
        // retained take must be THIS slot's own generated read, not the one that covered it.
        var job = CreateJob();
        job.ScriptText = Option<string>.Some("The M4 is slow citybound.");
        job.AudioUrl = Option<string>.Some("broadcast_20260824_014619_abc.wav");
        var source = CreateJob("src");
        source.ScriptText = Option<string>.Some("An earlier read that aired instead.");
        source.AudioUrl = Option<string>.Some("broadcast_20260824_011503_src.wav");

        var result = job.ApplyFallback(source, FixedTime, reason: "operator-rejected");

        result.IsSuccess.ShouldBeTrue();
        job.ScriptText.Value.ShouldBe("An earlier read that aired instead.");
        var take = job.SupersededTake.Value;
        take.ScriptText.ShouldBe("The M4 is slow citybound.");
        take.AudioUrl.Value.ShouldBe("broadcast_20260824_014619_abc.wav");
        take.Reason.ShouldBe("operator-rejected");
    }

    [Fact]
    public void ApplyFillerFallback_WithNoScript_RetainsNothing()
    {
        // A job that fell back before ever drafting has no take to keep; None must not become an
        // empty-string take the console would render as a blank "bulletin that did not air".
        var job = CreateJob();

        var result = job.ApplyFillerFallback("filler.wav", FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.SupersededTake.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void RequestRegeneration_KeepsTheRetainedTake()
    {
        // The take is a historical fact about a fallback that DID happen (like FallbackDeadlineUtc,
        // which the reset also keeps deliberately) — a retry must not erase the evidence.
        var job = CreateJob();
        job.ScriptText = Option<string>.Some("The M4 is slow citybound.");
        job.ApplyFillerFallback("filler.wav", FixedTime);

        var result = job.RequestRegeneration(FixedTime.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        job.ScriptText.HasValue.ShouldBeFalse();
        job.SupersededTake.Value.ScriptText.ShouldBe("The M4 is slow citybound.");
    }

    [Fact]
    public void ApplyFillerFallback_EmptyUrl_Fails()
    {
        var job = CreateJob();

        var result = job.ApplyFillerFallback("   ", FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldNotBe(JobStatus.FallbackUsed);
    }

    [Theory]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.FallbackUsed)]
    public void ApplyFillerFallback_FromTerminal_Fails(JobStatus terminal)
    {
        var job = CreateJobInStatus(terminal);

        var result = job.ApplyFillerFallback("http://filler/cart.wav", FixedTime);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ApplyFillerFallback_FromFailed_Succeeds()
    {
        var job = CreateJobInStatus(JobStatus.Failed);

        var result = job.ApplyFillerFallback("http://filler/cart.wav", FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("http://filler/cart.wav");
        job.FallbackSourceJobId.HasValue.ShouldBeFalse();
        job.FallbackAppliedAt.HasValue.ShouldBeTrue();
    }

    // ── Reconstitute ─────────────────────────────────────────────

    [Fact]
    public void Reconstitute_SetsStateFromHistory()
    {
        var t1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var history = new[]
        {
            new StatusHistoryEntry(JobStatus.Requested, t1, "sys"),
            new StatusHistoryEntry(JobStatus.Approved, t2, "op")
        };

        var job = Job.Reconstitute("j1", "S1", "M1", 30, history);

        job.Status.ShouldBe(JobStatus.Approved);
        job.CreatedAt.ShouldBe(t1);
        job.LastUpdatedAt.ShouldBe(t2);
        job.StatusHistory.Count().ShouldBe(2);
    }

    [Fact]
    public void Reconstitute_DoesNotAddDuplicateRequestedEntry()
    {
        var history = new[] { new StatusHistoryEntry(JobStatus.Requested, DateTime.UtcNow, "sys") };

        var job = Job.Reconstitute("j1", "S1", "M1", 30, history);

        job.StatusHistory.ShouldHaveSingleItem();
    }

    [Fact]
    public void Reconstitute_AcceptsCorridors()
    {
        var corridor = new JobCorridor("M1", 3, 1.0, "CONSTRUCTION", "desc", 2, 5.0, 1, true);
        var history = new[] { new StatusHistoryEntry(JobStatus.Requested, DateTime.UtcNow, "sys") };

        var job = Job.Reconstitute("j1", "S1", "M1", 30, history, corridors: [corridor]);

        job.Corridors.ShouldHaveSingleItem().RoadName.ShouldBe("M1");
    }

    [Fact]
    public void ETag_DefaultsNull()
    {
        var created = Job.Create("j1", "S1", "M1", 30, FixedTime).Value;
        created.ETag.ShouldBeNull();

        var reconstituted = Job.Reconstitute("j1", "S1", "M1", 30,
            [new StatusHistoryEntry(JobStatus.Requested, DateTime.UtcNow, "sys")]);
        reconstituted.ETag.ShouldBeNull();
    }

    // ── TransitionTo ─────────────────────────────────────────────

    [Fact]
    public void TransitionTo_UpdatesStatusAndHistory()
    {
        var job = CreateJob();

        job.TransitionTo(JobStatus.HereFetched, "sys", utcNow: FixedTime);

        job.Status.ShouldBe(JobStatus.HereFetched);
        job.LastUpdatedAt.ShouldBe(FixedTime);
        job.StatusHistory.Count().ShouldBe(2);
        job.StatusHistory.Last().Status.ShouldBe(JobStatus.HereFetched);
    }

    // ── Collection mutations ─────────────────────────────────────

    [Fact]
    public void AddLog_AppendsToLogs()
    {
        var job = CreateJob();
        job.AddLog(new JobLog(DateTime.UtcNow, JobLogLevel.Info, "test"));
        job.Logs.ShouldHaveSingleItem();
    }

    [Fact]
    public void AddIncident_AppendsToIncidents()
    {
        var job = CreateJob();
        job.AddIncident(new JobIncident("k1", 1, "h", "loc"));
        job.Incidents.ShouldHaveSingleItem();
    }

    [Fact]
    public void AddAttempt_AppendsToAttemptHistory()
    {
        var job = CreateJob();
        job.AddAttempt(new AttemptResult(1, 4.5, true, DateTime.UtcNow));
        job.AttemptHistory.ShouldHaveSingleItem();
    }

    [Fact]
    public void SetLogs_ReplacesExisting()
    {
        var job = CreateJob();
        job.AddLog(new JobLog(DateTime.UtcNow, JobLogLevel.Info, "old"));
        job.SetLogs([new JobLog(DateTime.UtcNow, JobLogLevel.Warn, "new")]);
        job.Logs.ShouldHaveSingleItem().Message.ShouldBe("new");
    }

    [Fact]
    public void SetCorridors_ReplacesExisting()
    {
        var job = CreateJob();
        job.SetCorridors([new JobCorridor("Rd1", 1, 1.0, "T", "d", 1, 1.0, 1, true)]);
        job.Corridors.ShouldHaveSingleItem();
    }

    [Fact]
    public void SetIncidents_ReplacesExisting()
    {
        var job = CreateJob();
        job.AddIncident(new JobIncident("k1", 1, "h", "loc"));
        job.SetIncidents([new JobIncident("k2", 2, "h2", "loc2")]);
        job.Incidents.ShouldHaveSingleItem().Key.ShouldBe("k2");
    }

    [Fact]
    public void SetAttemptHistory_ReplacesExisting()
    {
        var job = CreateJob();
        job.AddAttempt(new AttemptResult(1, 4.0, true, DateTime.UtcNow));
        job.SetAttemptHistory([new AttemptResult(2, 5.0, false, DateTime.UtcNow)]);
        job.AttemptHistory.ShouldHaveSingleItem().AttemptNumber.ShouldBe(2);
    }

    [Fact]
    public void AddStatusEntry_AppendsToStatusHistory()
    {
        var job = CreateJob();
        job.AddStatusEntry(new StatusHistoryEntry(JobStatus.HereFetched, DateTime.UtcNow, "sys"));
        job.StatusHistory.Count().ShouldBe(2);
    }

    [Fact]
    public void AddRejection_AppendsToRejectionHistory()
    {
        var job = CreateJob();
        job.AddRejection(new RejectionEntry("r", "n", DateTime.UtcNow, "op"));
        job.RejectionHistory.ShouldHaveSingleItem();
    }

    [Fact]
    public void SetStatusHistory_ReplacesExisting()
    {
        var job = CreateJob();
        var entry = new StatusHistoryEntry(JobStatus.Approved, DateTime.UtcNow, "op");
        job.SetStatusHistory([entry]);
        job.StatusHistory.ShouldHaveSingleItem().Status.ShouldBe(JobStatus.Approved);
    }

    [Fact]
    public void SetRejectionHistory_ReplacesExisting()
    {
        var job = CreateJob();
        job.AddRejection(new RejectionEntry("old", "n", DateTime.UtcNow, "op"));
        job.SetRejectionHistory([new RejectionEntry("new", "n2", DateTime.UtcNow, "op2")]);
        job.RejectionHistory.ShouldHaveSingleItem().Reason.ShouldBe("new");
    }

    [Theory]
    [InlineData(JobStatus.WaitingApproval)]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Failed)]
    public void MarkMissed_FromUnpublishedStatus_Succeeds(JobStatus status)
    {
        var job = CreateJobInStatus(status);
        var deadline = FixedTime.AddMinutes(-10);

        var result = job.MarkMissed(deadline, FixedTime);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Missed);
        job.MissedDeadlineUtc.ShouldBe(deadline);
        job.RejectionHistory.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(JobStatus.Rejected)]
    [InlineData(JobStatus.Published)]
    [InlineData(JobStatus.FallbackUsed)]
    [InlineData(JobStatus.Missed)]
    public void MarkMissed_FromSettledStatus_Fails(JobStatus status)
    {
        var job = CreateJobInStatus(status);

        var result = job.MarkMissed(FixedTime.AddMinutes(-10), FixedTime);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(status);
    }

    // =====================================================================
    // ScriptSentences — lifecycle
    // =====================================================================

    [Fact]
    public void SetScriptSentences_ReplacesExisting()
    {
        var job = CreateJob();
        job.SetScriptSentences([new JobScriptSentence("Test.", ["M1"])]);
        job.ScriptSentences.ShouldHaveSingleItem();
        job.ScriptSentences[0].Text.ShouldBe("Test.");
        job.ScriptSentences[0].CorridorKeys.ShouldBe(["M1"]);
    }

    [Fact]
    public void RequestRegeneration_ClearsScriptSentences()
    {
        var job = CreateJobInStatus(JobStatus.Rejected);
        job.SetScriptSentences([new JobScriptSentence("Keep?", ["M1"])]);

        var result = job.RequestRegeneration(DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        job.ScriptSentences.ShouldBeEmpty();
    }

    [Fact]
    public void ApplyFallback_CopiesScriptSentences()
    {
        var job = CreateJob();
        var source = CreateJob("src");
        source.SetScriptSentences([new JobScriptSentence("Fallback", ["M4"])]);

        var result = job.ApplyFallback(source, DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        job.ScriptSentences.ShouldHaveSingleItem();
        job.ScriptSentences[0].Text.ShouldBe("Fallback");
        job.ScriptSentences[0].CorridorKeys.ShouldBe(["M4"]);
    }

    [Fact]
    public void ApplyFillerFallback_ClearsScriptSentences()
    {
        var job = CreateJob();
        job.SetScriptSentences([new JobScriptSentence("Delete me.", ["A1"])]);

        var result = job.ApplyFillerFallback("http://filler/cart.wav", DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        job.ScriptSentences.ShouldBeEmpty();
    }

    [Fact]
    public void ScriptSentences_DefaultIsEmpty()
    {
        var job = CreateJob();

        job.ScriptSentences.ShouldBeEmpty();
    }

    /// <summary>
    ///     <c>StatusHistory</c> persists <see cref="JobStatus" /> as its INTEGER ordinal, so these
    ///     values are a wire contract against every history row already in Table Storage. Inserting
    ///     a member renumbers every later one and silently rewrites the meaning of stored history:
    ///     on 2026-08-19 adding AwaitingCuration after IncidentsSelected made a stored 3
    ///     (ScriptReady) render as AwaitingCuration and a stored 9 (Published) render as Rejected,
    ///     so healthy historical jobs read as failures in the operator console. New members MUST be
    ///     appended. This test is the guard that would have caught it.
    /// </summary>
    [Fact]
    public void JobStatus_OrdinalsAreAStableWireContract()
    {
        ((int)JobStatus.Requested).ShouldBe(0);
        ((int)JobStatus.HereFetched).ShouldBe(1);
        ((int)JobStatus.IncidentsSelected).ShouldBe(2);
        ((int)JobStatus.ScriptReady).ShouldBe(3);
        ((int)JobStatus.SynthesizingAudio).ShouldBe(4);
        ((int)JobStatus.AudioReady).ShouldBe(5);
        ((int)JobStatus.WaitingApproval).ShouldBe(6);
        ((int)JobStatus.Approved).ShouldBe(7);
        ((int)JobStatus.Rejected).ShouldBe(8);
        ((int)JobStatus.Published).ShouldBe(9);
        ((int)JobStatus.FallbackUsed).ShouldBe(10);
        ((int)JobStatus.Failed).ShouldBe(11);
        ((int)JobStatus.Missed).ShouldBe(12);
        ((int)JobStatus.AwaitingCuration).ShouldBe(13);
    }
}
