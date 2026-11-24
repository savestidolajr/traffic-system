using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public sealed class JobProgressReporterTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RepositoryUpsertThrow_IsSwallowedByEveryReporterMethod()
    {
        var repository = RepositoryWithNoDocument();
        repository.UpsertAsync(Arg.Any<JobProgressDocument>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("storage unavailable"));
        var reporter = CreateReporter(repository);

        await EmitEveryMethodAsync(reporter);
    }

    [Fact]
    public async Task RepositoryUpsertFailure_IsSwallowedByEveryReporterMethod()
    {
        var repository = RepositoryWithNoDocument();
        repository.UpsertAsync(Arg.Any<JobProgressDocument>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("storage unavailable"));
        var reporter = CreateReporter(repository);

        await EmitEveryMethodAsync(reporter);
    }

    [Fact]
    public async Task Ready_UpdatesMarketTypicalDurationsWithCompletedStageSeconds()
    {
        var repository = RepositoryWithNoDocument();
        var ema = new Dictionary<string, double>
        {
            [nameof(ProgressStage.Evidence)] = 2.5,
            [nameof(ProgressStage.Write)] = 1.25
        };
        IReadOnlyDictionary<string, double>? actualSample = null;
        repository.UpdateTypicalAsync(
                Arg.Any<string>(),
                Arg.Do<IReadOnlyDictionary<string, double>>(sample => actualSample = sample),
                Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, double>>.Success(ema));
        var persisted = new List<JobProgressDocument>();
        repository.UpsertAsync(Arg.Do<JobProgressDocument>(document => persisted.Add(document)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var time = new MutableTimeProvider(Start);
        var reporter = CreateReporter(repository, time);

        await reporter.StageStarted(ProgressStage.Evidence, "Evidence started.");
        time.Set(Start.AddSeconds(2.5));
        await reporter.StageCompleted(ProgressStage.Evidence, "Evidence completed.");
        await reporter.StageStarted(ProgressStage.Write, "Writing started.");
        time.Set(Start.AddSeconds(3.75));
        await reporter.StageCompleted(ProgressStage.Write, "Writing completed.");
        await reporter.Ready("Ready for approval.");

        await repository.Received(1).UpdateTypicalAsync("sydney",
            Arg.Any<IReadOnlyDictionary<string, double>>(), Arg.Any<CancellationToken>());
        actualSample.ShouldNotBeNull();
        actualSample![nameof(ProgressStage.Evidence)].ShouldBe(2.5, 0.001);
        actualSample![nameof(ProgressStage.Write)].ShouldBe(1.25, 0.001);
        persisted[^1].TypicalSeconds.ShouldNotBeNull();
        persisted[^1].TypicalSeconds![nameof(ProgressStage.Evidence)].ShouldBe(2.5);
        persisted[^1].TypicalSeconds![nameof(ProgressStage.Write)].ShouldBe(1.25);
        persisted[^1].Events
            .Where(progressEvent => progressEvent.Kind == nameof(ProgressEventKind.StageCompleted))
            .Select(progressEvent => progressEvent.DurationMs)
            .ShouldBe([2_500, 1_250]);
    }

    [Fact]
    public async Task StageStarted_OnNewAttempt_RecordsTheNewAttemptNumber()
    {
        var repository = RepositoryWithNoDocument();
        var persisted = new List<JobProgressDocument>();
        repository.UpsertAsync(Arg.Do<JobProgressDocument>(document => persisted.Add(document)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var reporter = CreateReporter(repository);

        await reporter.AttemptStarted(1, 3, "First attempt.");
        await reporter.StageStarted(ProgressStage.Write, "Writing attempt one.");
        await reporter.AttemptStarted(2, 3, "Retrying after a short read.");
        await reporter.StageStarted(ProgressStage.Write, "Writing attempt two.");

        var starts = persisted[^1].Events
            .Where(progressEvent => progressEvent.Kind == nameof(ProgressEventKind.StageStarted))
            .ToList();
        starts.Count.ShouldBe(2);
        starts.Select(progressEvent => progressEvent.Attempt).ShouldBe([1, 2]);
    }

    private static async Task EmitEveryMethodAsync(IGenerationProgressReporter reporter)
    {
        await reporter.StatusChanged(JobStatus.HereFetched);
        await reporter.StageStarted(ProgressStage.Evidence, "Evidence started.");
        await reporter.StageCompleted(ProgressStage.Evidence, "Evidence completed.", durationMs: 100);
        await reporter.SubStep(ProgressStage.Write, "Writer sub-step.");
        await reporter.AttemptStarted(1, 2, "First attempt.");
        await reporter.AttemptFailed("Attempt failed.");
        await reporter.LlmCall(ProgressStage.Write, "writer", "model-a", 100);
        await reporter.Ready("Ready.");
        await reporter.Failed("Failed.");
    }

    private static IJobProgressRepository RepositoryWithNoDocument()
    {
        var repository = Substitute.For<IJobProgressRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<JobProgressSnapshot>>.Success(Option<JobProgressSnapshot>.None));
        return repository;
    }

    private static IGenerationProgressReporter CreateReporter(
        IJobProgressRepository repository,
        TimeProvider? timeProvider = null) =>
        new JobProgressReporter(
            "job-1", "sydney", JobStatus.Requested, repository,
            timeProvider ?? new MutableTimeProvider(Start),
            NullLogger<JobProgressReporter>.Instance);

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public void Set(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
