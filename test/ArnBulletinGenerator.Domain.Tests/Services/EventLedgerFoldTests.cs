using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class EventLedgerFoldTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);

    private static Job MakeJob(
        string id, DateTime createdAt, JobStatus status, params JobIncident[] incidents)
    {
        var job = Job.Create(id, "S1", "sydney", 30, createdAt).Value;
        job.SetCreatedAt(createdAt);
        job.SetStatus(status);
        job.SetAllIncidents(incidents);
        job.SetIncidents(incidents);
        job.SetCorridors(incidents.Select(i => new JobCorridor(
            RoadName: $"Road for {i.Key}", Severity: 2, ImpactScore: 50, PrimaryType: "construction",
            Description: i.Headline, IncidentCount: 1, TotalLengthKm: 1, Rank: 1, IsSelected: true,
            IncidentKeys: [i.Key])));
        return job;
    }

    private static JobIncident MakeIncident(
        string key, string headline = "roadworks on M1", bool closed = false,
        DateTimeOffset? end = null, IReadOnlyList<string>? sources = null) =>
        new(key, 1, headline, "M1", RoadClosed: closed, EndTimeUtc: end,
            Sources: sources ?? ["here"]);

    // -- Fold identity and lanes ---------------------------------------------------------------

    [Fact]
    public void Fold_TracksOneEventAcrossJobs_CountingSightings()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-4), JobStatus.Published, MakeIncident("k1")),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published, MakeIncident("k1")),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        var evt = events.ShouldHaveSingleItem();
        evt.SightingCount.ShouldBe(2);
        evt.FirstSeenUtc.ShouldBe(Now.AddHours(-4));
        evt.LastSeenUtc.ShouldBe(Now.AddHours(-1));
        evt.Lane.ShouldBe(LedgerLane.Active);
    }

    [Fact]
    public void Fold_UsesSourceNativeIdentity_WhenProviderRecordKeyChanges()
    {
        var first = MakeIncident("here-record-v1") with
        {
            SourceRecordIdentities =
                [new SourceRecordIdentity("here", "here-record-v1", "here-event-42")]
        };
        var second = MakeIncident("here-record-v2") with
        {
            SourceRecordIdentities =
                [new SourceRecordIdentity("here", "here-record-v2", "here-event-42")]
        };

        var events = EventLedgerFold.Fold(
            [
                MakeJob("j1", Now.UtcDateTime.AddHours(-2), JobStatus.Published, first),
                MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published, second)
            ],
            Now);

        var evt = events.ShouldHaveSingleItem();
        evt.Key.ShouldBe("here:original:here-event-42");
        evt.SightingCount.ShouldBe(2);
        evt.Lane.ShouldBe(LedgerLane.Active);
    }

    [Fact]
    public void Fold_EventAbsentFromNewestFetch_IsCleared()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-4), JobStatus.Published,
                MakeIncident("gone"), MakeIncident("stays")),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published, MakeIncident("stays")),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        events.Single(e => e.Key == "gone").Lane.ShouldBe(LedgerLane.Cleared);
        events.Single(e => e.Key == "stays").Lane.ShouldBe(LedgerLane.Active);
    }

    [Fact]
    public void Fold_FirstSightingInNewestFetch_IsNew_AndFailedJobsNeverClearEvents()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-2), JobStatus.Published, MakeIncident("k1")),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published,
                MakeIncident("k1"), MakeIncident("fresh")),
            // A failed job with no incidents must not define "the newest fetch".
            MakeJob("j3", Now.UtcDateTime.AddMinutes(-5), JobStatus.Failed),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        events.Single(e => e.Key == "fresh").Lane.ShouldBe(LedgerLane.New);
        events.Single(e => e.Key == "k1").Lane.ShouldBe(LedgerLane.Active);
    }

    [Fact]
    public void Fold_ChangeBetweenSightings_RecordsObservation_AndLaneIsChanged()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-4), JobStatus.Published,
                MakeIncident("k1", closed: false)),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published,
                MakeIncident("k1", closed: true)),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        var evt = events.ShouldHaveSingleItem();
        evt.Lane.ShouldBe(LedgerLane.Changed);
        evt.RoadClosed.ShouldBeTrue(); // snapshot reflects the newest sighting
        evt.Observations.ShouldHaveSingleItem().Summary.ShouldBe("now closed");
    }

    [Fact]
    public void Fold_LongLivedUnchangedEvent_IsStale()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddDays(-25), JobStatus.Published, MakeIncident("old")),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published, MakeIncident("old")),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        events.ShouldHaveSingleItem().Lane.ShouldBe(LedgerLane.Stale);
    }

    [Fact]
    public void Fold_RollingReissueNoise_IsNotAChange()
    {
        // HERE re-stamps a worksite's window on every re-issue and schema-era records
        // carry no provenance stamp — neither may register as news.
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-26), JobStatus.Published,
                MakeIncident("k1", end: Now.AddDays(3), sources: null) with { Sources = null }),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published,
                MakeIncident("k1", end: Now.AddDays(3).AddHours(6), sources: ["here"])),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        var evt = events.ShouldHaveSingleItem();
        evt.Observations.ShouldBeEmpty();
        evt.Lane.ShouldBe(LedgerLane.Active);
    }

    [Fact]
    public void Fold_SourceSetGrows_RecordsCorroborationObservation()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-4), JobStatus.Published,
                MakeIncident("k1", sources: ["here"])),
            MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published,
                MakeIncident("k1", sources: ["here", "livetraffic-nsw"])),
        };

        var evt = EventLedgerFold.Fold(jobs, Now).ShouldHaveSingleItem();

        evt.Observations.ShouldHaveSingleItem().Summary.ShouldBe("corroboration gained");
    }

    [Fact]
    public void Fold_OldChange_DecaysBackToActive()
    {
        var jobs = new[]
        {
            MakeJob("j1", Now.UtcDateTime.AddHours(-8), JobStatus.Published,
                MakeIncident("k1", closed: false)),
            MakeJob("j2", Now.UtcDateTime.AddHours(-5), JobStatus.Published,
                MakeIncident("k1", closed: true)),
            MakeJob("j3", Now.UtcDateTime.AddHours(-1), JobStatus.Published,
                MakeIncident("k1", closed: true)),
        };

        var events = EventLedgerFold.Fold(jobs, Now);

        var evt = events.ShouldHaveSingleItem();
        evt.Observations.ShouldHaveSingleItem(); // history kept
        evt.Lane.ShouldBe(LedgerLane.Active); // but the lane is news, not archive
    }

    [Fact]
    public void Fold_PublishedBulletinSentenceNamingTheCorridor_RecordsAiring()
    {
        var job = MakeJob("j1", Now.UtcDateTime.AddHours(-1), JobStatus.Published, MakeIncident("k1"));
        job.SetScriptSentences([new JobScriptSentence("Roadworks on the M1.", ["Road for k1"])]);

        var events = EventLedgerFold.Fold([job], Now);

        var airing = events.ShouldHaveSingleItem().Airings.ShouldHaveSingleItem();
        airing.JobId.ShouldBe("j1");
        airing.Sentence.ShouldBe("Roadworks on the M1.");
    }

    // -- Delta ----------------------------------------------------------------------------------

    [Fact]
    public void ComputeDelta_ClassifiesNewChangedClearedUnchanged()
    {
        var previous = MakeJob("prev", Now.UtcDateTime.AddHours(-4), JobStatus.Published,
            MakeIncident("unchanged"), MakeIncident("reworded", headline: "old words"),
            MakeIncident("gone"));
        var current = MakeJob("cur", Now.UtcDateTime.AddHours(-1), JobStatus.WaitingApproval,
            MakeIncident("unchanged"), MakeIncident("reworded", headline: "new words"),
            MakeIncident("brand-new"));

        var deltas = EventLedgerFold.ComputeDelta(current, previous);

        deltas.Single(d => d.Key == "brand-new").Status.ShouldBe(DeltaStatus.New);
        deltas.Single(d => d.Key == "gone").Status.ShouldBe(DeltaStatus.Cleared);
        var changed = deltas.Single(d => d.Key == "reworded");
        changed.Status.ShouldBe(DeltaStatus.Changed);
        changed.ChangeSummary.ShouldBe("reworded");
        deltas.Single(d => d.Key == "unchanged").Status.ShouldBe(DeltaStatus.Unchanged);
    }

    [Fact]
    public void ComputeDelta_NoPreviousBulletin_EverythingIsNew()
    {
        var current = MakeJob("cur", Now.UtcDateTime, JobStatus.WaitingApproval, MakeIncident("k1"));

        var deltas = EventLedgerFold.ComputeDelta(current, null);

        deltas.ShouldHaveSingleItem().Status.ShouldBe(DeltaStatus.New);
    }

    // -- Feed health ------------------------------------------------------------------------------

    [Fact]
    public void FoldFeedHealth_ReportsPerSourceContribution()
    {
        var j1 = MakeJob("j1", Now.UtcDateTime.AddHours(-4), JobStatus.Published, MakeIncident("k1"));
        j1.SetCorridors([new JobCorridor(
            "M1", 2, 50, "construction", "d", 1, 1, 1, true,
            IncidentKeys: ["k1"], Sources: ["here", "livetraffic-nsw"])]);
        var j2 = MakeJob("j2", Now.UtcDateTime.AddHours(-1), JobStatus.Published, MakeIncident("k1"));
        j2.SetCorridors([new JobCorridor(
            "M1", 2, 50, "construction", "d", 1, 1, 1, true,
            IncidentKeys: ["k1"], Sources: ["here"])]);

        var health = EventLedgerFold.FoldFeedHealth([j1, j2]);

        health.Single(h => h.SourceId == "here").JobsWithSource.ShouldBe(2);
        var nsw = health.Single(h => h.SourceId == "livetraffic-nsw");
        nsw.JobsWithSource.ShouldBe(1);
        nsw.LastContributedUtc.ShouldBe(Now.AddHours(-4));
    }

    private static LedgerEvent Fold1(JobIncident incident) =>
        EventLedgerFold.Fold(
            [MakeJob("j1", Now.UtcDateTime.AddHours(-1), JobStatus.Published, incident)], Now)
        .Single();
}
