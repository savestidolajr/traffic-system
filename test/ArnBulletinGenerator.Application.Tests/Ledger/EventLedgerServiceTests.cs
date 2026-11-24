using ArnBulletinGenerator.Application.Ledger;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class EventLedgerServiceTests
{
    [Fact]
    public async Task GetLedger_PagesOnlyTheRecentJobWindow()
    {
        var now = new DateTimeOffset(2026, 8, 20, 1, 0, 0, TimeSpan.Zero);
        var job = Job.Create("job-1", "Test Station", "sydney", 30, now.UtcDateTime).Value;
        var jobs = Substitute.For<IJobRepository>();
        var verdicts = Substitute.For<IEventVerdictRepository>();
        var firstPage = Enumerable.Repeat(JobLedgerProjection.FromJob(job), 500).ToList();
        jobs.ListJobLedgerPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<IReadOnlyList<JobLedgerProjection>>.Success(firstPage),
                Result<IReadOnlyList<JobLedgerProjection>>.Success([]));
        verdicts.GetVerdictsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EventVerdict>>.Success([]));

        var service = new EventLedgerService(
            jobs,
            verdicts,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedTimeProvider(now),
            Substitute.For<ILogger<EventLedgerService>>());

        var result = await service.GetLedgerAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        var calls = jobs.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IJobRepository.ListJobLedgerPageAsync))
            .Select(call => (JobListQuery)call.GetArguments()[0]!)
            .ToList();
        calls.Count.ShouldBe(2);
        calls[0].FromUtc.ShouldBe(now.AddDays(-30));
        calls[0].ToUtc.ShouldBe(now);
        calls[0].Limit.ShouldBe(500);
        calls[0].CursorJobId.ShouldBeNull();
        calls[1].ToUtc.ShouldBe(new DateTimeOffset(job.LastUpdatedAt, TimeSpan.Zero));
        calls[0].Market.ShouldBe("sydney");
        calls[1].CursorJobId.ShouldBe(job.JobId);
        await jobs.DidNotReceive().ListJobsAsync(Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task GetJobDelta_UsesLedgerProjections_InsteadOfFullJobReads()
    {
        var now = new DateTimeOffset(2026, 8, 20, 1, 0, 0, TimeSpan.Zero);
        var previous = Job.Create("previous", "Test Station", "sydney", 30, now.AddHours(-1).UtcDateTime).Value;
        previous.SetAllIncidents([new JobIncident("event-1", 1, "Slow", "Road")]);
        var current = Job.Create("current", "Test Station", "sydney", 30, now.UtcDateTime).Value;
        current.SetAllIncidents([new JobIncident("event-1", 1, "Slow", "Road")]);

        var jobs = Substitute.For<IJobRepository>();
        jobs.GetJobLedgerProjectionAsync("current", Arg.Any<CancellationToken>())
            .Returns(Result<JobLedgerProjection>.Success(JobLedgerProjection.FromJob(current)));
        jobs.ListJobLedgerPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<JobLedgerProjection>>.Success(
                [JobLedgerProjection.FromJob(previous)]));
        var verdicts = Substitute.For<IEventVerdictRepository>();
        verdicts.GetVerdictsAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EventVerdict>>.Success([]));
        var service = new EventLedgerService(
            jobs,
            verdicts,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedTimeProvider(now),
            Substitute.For<ILogger<EventLedgerService>>());

        var result = await service.GetJobDeltaAsync("current");

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreviousJobId.ShouldBe("previous");
        await jobs.DidNotReceive().GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await jobs.DidNotReceive().ListJobsPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void JobLedgerProjection_RoundTripsFoldFields()
    {
        var created = new DateTime(2026, 8, 20, 1, 0, 0, DateTimeKind.Utc);
        var job = Job.Create("job-1", "Test Station", "sydney", 30, created).Value;
        job.SetStatus(JobStatus.Published);
        job.LastUpdatedAt = created.AddMinutes(2);
        job.SetAllIncidents([new JobIncident("event-1", 1, "Slow", "Road")]);

        var roundTrip = JobLedgerProjection.FromJob(job).ToJob();

        roundTrip.JobId.ShouldBe(job.JobId);
        roundTrip.Market.ShouldBe(job.Market);
        roundTrip.Status.ShouldBe(job.Status);
        roundTrip.CreatedAt.ShouldBe(job.CreatedAt);
        roundTrip.LastUpdatedAt.ShouldBe(job.LastUpdatedAt);
        roundTrip.AllIncidents.ShouldBe(job.AllIncidents);
        roundTrip.Corridors.ShouldBe(job.Corridors);
        roundTrip.ScriptSentences.ShouldBe(job.ScriptSentences);
    }
}
