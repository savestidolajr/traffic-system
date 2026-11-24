using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public sealed class JobProgressDocumentTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Append_IncrementsVersionExactlyOncePerEvent()
    {
        var document = new JobProgressDocument("job-1", "sydney", JobStatus.Requested, Start);

        document.Append(Start, 1, ProgressStage.Evidence, ProgressEventKind.StageStarted, "Traffic fetch started.");
        document.Version.ShouldBe(1);

        document.Append(Start.AddSeconds(1), 1, ProgressStage.Evidence, ProgressEventKind.SubStep, "Traffic fetched.");
        document.Version.ShouldBe(2);

        document.Append(Start.AddSeconds(2), 1, ProgressStage.Evidence, ProgressEventKind.StageCompleted,
            "Traffic fetch completed.", durationMs: 2_000);
        document.Version.ShouldBe(3);
        document.Events.Count.ShouldBe(3);
    }

    [Fact]
    public void Append_KeepsEventsChronologicalAndDropsOldestAtTheTwoHundredEventCap()
    {
        var document = new JobProgressDocument("job-1", "sydney", JobStatus.Requested, Start);

        for (int i = 0; i < JobProgressDocument.MaxEventCount + 3; i++)
        {
            document.Append(Start.AddSeconds(i), 1, ProgressStage.Evidence, ProgressEventKind.SubStep,
                $"Event {i}.");
        }

        document.Version.ShouldBe(JobProgressDocument.MaxEventCount + 3);
        document.Events.Count.ShouldBe(JobProgressDocument.MaxEventCount);
        document.Events[0].Detail.ShouldBe("Event 3.");
        document.Events[^1].Detail.ShouldBe("Event 202.");
        document.Events.Select(e => DateTimeOffset.Parse(e.At)).ShouldBeInOrder();
    }

    [Fact]
    public void Append_StageCompletedCarriesDurationFromStageStartToCompletion()
    {
        var startedAt = Start.AddMinutes(1);
        var completedAt = startedAt.AddMilliseconds(1_250);
        var document = new JobProgressDocument("job-1", "sydney", JobStatus.Requested, Start);

        document.Append(startedAt, 1, ProgressStage.Write, ProgressEventKind.StageStarted, "Writing started.");
        document.Append(completedAt, 1, ProgressStage.Write, ProgressEventKind.StageCompleted,
            "Writing completed.", durationMs: (int)(completedAt - startedAt).TotalMilliseconds);

        var started = document.Events[^2];
        var completed = document.Events[^1];
        completed.Kind.ShouldBe(nameof(ProgressEventKind.StageCompleted));
        completed.DurationMs.ShouldNotBeNull();
        (DateTimeOffset.Parse(completed.At) - DateTimeOffset.Parse(started.At))
            .TotalMilliseconds.ShouldBe(completed.DurationMs.Value);
        DateTimeOffset.Parse(completed.At).ShouldBe(completedAt);
    }

    [Fact]
    public void SetStatus_UpdatesStatusAndAppendsAStatusNote()
    {
        var document = new JobProgressDocument("job-1", "sydney", JobStatus.Requested, Start);

        document.SetStatus(Start.AddSeconds(1), JobStatus.HereFetched);
        // SetStatus intentionally appends a Note so the status transition also invalidates the ETag.
        // Statuses are stamped at phase START, so the note names the activity beginning.

        document.Status.ShouldBe(nameof(JobStatus.HereFetched));
        document.Version.ShouldBe(1);
        document.Events.Count.ShouldBe(1);
        document.Events[0].Kind.ShouldBe(nameof(ProgressEventKind.Note));
        document.Events[0].Detail.ShouldBe("Fetching traffic.");
    }
}
