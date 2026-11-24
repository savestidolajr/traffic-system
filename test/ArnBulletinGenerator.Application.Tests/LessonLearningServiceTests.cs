using ArnBulletinGenerator.Application.Learning;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     The published-edit learning contract: only generalisable, confident verdicts change the
///     store; candidates start inactive, reinforcement merges, supersession never silently kills
///     an active lesson, and three published edits auto-activate a clean candidate.
/// </summary>
public sealed class LessonLearningServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc);

    private readonly IFutureLessonRepository _lessons = Substitute.For<IFutureLessonRepository>();
    private readonly ILessonSummarizer _summarizer = Substitute.For<ILessonSummarizer>();
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly LessonLearningService _sut;

    public LessonLearningServiceTests()
    {
        _lessons.AddAsync(Arg.Any<FutureLessonEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _lessons.DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _lessons.SetStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FutureLessonStatus>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SetupStored([]);
        _jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new LessonLearningService(_lessons, _summarizer, _jobRepo,
            new FixedTimeProvider(Now), Substitute.For<ILogger<LessonLearningService>>());
    }

    private void SetupStored(IReadOnlyList<FutureLessonEntry> stored)
    {
        _lessons.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success(stored));
    }

    private void SetupVerdict(LessonSummaryResult verdict)
    {
        _summarizer.SummarizeAsync(Arg.Any<LessonSummaryRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<LessonSummaryResult>.Success(verdict));
    }

    private static LessonLearningQueueMessage Message(
        string original = "The M4 is experiencing delays approaching Parramatta",
        string published = "The M4 is crawling into Parramatta") =>
        new("job-1", "KIIS 101.1", original, published, Now, "producer.jane@arn.com.au");

    private static FutureLessonEntry StoredLesson(
        string text = "Prefer direct conversational wording over formal phrasing at all times.",
        FutureLessonStatus status = FutureLessonStatus.Candidate,
        FutureLessonOrigin origin = FutureLessonOrigin.Inferred,
        int occurrences = 1,
        string? conflictWith = null,
        string? id = null) =>
        new(
            id ?? FutureLessonPolicy.CreateId("KIIS 101.1", FutureLessonCategory.WordingStyle, text),
            "KIIS 101.1",
            "job-0",
            FutureLessonCategory.WordingStyle,
            text,
            Now.AddDays(-5),
            Now.AddDays(-1),
            occurrences,
            Now.AddDays(85),
            status,
            origin,
            conflictWith);

    private static LessonSummaryResult CreateVerdict(double confidence = 0.82) => new(
        LessonSummaryAction.Create,
        FutureLessonCategory.WordingStyle,
        "Prefer direct, conversational descriptions of significant delays over formal phrases such as 'experiencing delays'.",
        null,
        confidence,
        "The operator replaced formal phrasing with conversational wording.");

    [Fact]
    public async Task Create_PersistsCandidateInferredEntryWithPolicyId()
    {
        SetupVerdict(CreateVerdict());

        await _sut.ProcessAsync(Message());

        string expectedId = FutureLessonPolicy.CreateId("KIIS 101.1", FutureLessonCategory.WordingStyle,
            "Prefer direct, conversational descriptions of significant delays over formal phrases such as 'experiencing delays'.");
        await _lessons.Received(1).AddAsync(
            Arg.Is<FutureLessonEntry>(e =>
                e.Id == expectedId &&
                e.Station == "KIIS 101.1" &&
                e.SourceJobId == "job-1" &&
                e.Status == FutureLessonStatus.Candidate &&
                e.Origin == FutureLessonOrigin.Inferred &&
                e.ConflictWithLessonId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoneVerdict_PersistsNothing()
    {
        SetupVerdict(CreateVerdict() with { Action = LessonSummaryAction.None });

        await _sut.ProcessAsync(Message());

        await _lessons.DidNotReceive()
            .AddAsync(Arg.Any<FutureLessonEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LowConfidenceVerdict_PersistsNothing()
    {
        SetupVerdict(CreateVerdict(confidence: 0.5));

        await _sut.ProcessAsync(Message());

        await _lessons.DidNotReceive()
            .AddAsync(Arg.Any<FutureLessonEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reinforce_AddsWithTheTargetsText()
    {
        var target = StoredLesson();
        SetupStored([target]);
        SetupVerdict(CreateVerdict() with
        {
            Action = LessonSummaryAction.Reinforce,
            TargetLessonId = target.Id,
        });

        await _sut.ProcessAsync(Message());

        await _lessons.Received(1).AddAsync(
            Arg.Is<FutureLessonEntry>(e =>
                e.Id == target.Id &&
                e.Text == target.Text &&
                e.Category == target.Category &&
                e.SourceJobId == "job-1" &&
                e.Origin == FutureLessonOrigin.Inferred),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Supersede_Candidate_DeletesTargetAndCreates()
    {
        var target = StoredLesson(status: FutureLessonStatus.Candidate);
        SetupStored([target]);
        SetupVerdict(CreateVerdict() with
        {
            Action = LessonSummaryAction.Supersede,
            TargetLessonId = target.Id,
        });

        await _sut.ProcessAsync(Message());

        await _lessons.Received(1)
            .DeleteAsync("KIIS 101.1", target.Id, Arg.Any<CancellationToken>());
        await _lessons.Received(1).AddAsync(
            Arg.Is<FutureLessonEntry>(e =>
                e.Status == FutureLessonStatus.Candidate && e.ConflictWithLessonId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Supersede_Active_CreatesConflictedCandidateAndKeepsTarget()
    {
        var target = StoredLesson(status: FutureLessonStatus.Active);
        SetupStored([target]);
        SetupVerdict(CreateVerdict() with
        {
            Action = LessonSummaryAction.Supersede,
            TargetLessonId = target.Id,
        });

        await _sut.ProcessAsync(Message());

        await _lessons.DidNotReceive()
            .DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _lessons.Received(1).AddAsync(
            Arg.Is<FutureLessonEntry>(e => e.ConflictWithLessonId == target.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThirdReinforcement_AutoActivatesTheCandidate()
    {
        var target = StoredLesson(occurrences: 2);
        var reinforced = target with { OccurrenceCount = 3 };
        _lessons.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<IReadOnlyList<FutureLessonEntry>>.Success([target]),
                Result<IReadOnlyList<FutureLessonEntry>>.Success([reinforced]));
        SetupVerdict(CreateVerdict() with
        {
            Action = LessonSummaryAction.Reinforce,
            TargetLessonId = target.Id,
        });

        await _sut.ProcessAsync(Message());

        await _lessons.Received(1).SetStatusAsync(
            "KIIS 101.1", target.Id, FutureLessonStatus.Active, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConflictedCandidate_NeverAutoActivates()
    {
        var target = StoredLesson(occurrences: 2, conflictWith: "some-active-lesson");
        var reinforced = target with { OccurrenceCount = 3 };
        _lessons.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<IReadOnlyList<FutureLessonEntry>>.Success([target]),
                Result<IReadOnlyList<FutureLessonEntry>>.Success([reinforced]));
        SetupVerdict(CreateVerdict() with
        {
            Action = LessonSummaryAction.Reinforce,
            TargetLessonId = target.Id,
        });

        await _sut.ProcessAsync(Message());

        await _lessons.DidNotReceive().SetStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FutureLessonStatus>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreFullOfActiveLessons_NeverEvictsAndNeverCreates()
    {
        var stored = Enumerable.Range(0, FutureLessonPolicy.MaxStoredPerStation)
            .Select(i => StoredLesson(
                text: $"Standing station preference number {i} that editors rely on every day.",
                status: FutureLessonStatus.Active))
            .ToList();
        SetupStored(stored);
        SetupVerdict(CreateVerdict());

        await _sut.ProcessAsync(Message());

        await _lessons.DidNotReceive()
            .DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _lessons.DidNotReceive()
            .AddAsync(Arg.Any<FutureLessonEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreFull_EvictsTheStalestCandidateThenCreates()
    {
        var stale = StoredLesson(
            text: "The stalest candidate wording preference nobody has reinforced lately.")
            with { LastSeenAtUtc = Now.AddDays(-30) };
        var stored = new List<FutureLessonEntry> { stale };
        stored.AddRange(Enumerable.Range(0, FutureLessonPolicy.MaxStoredPerStation - 1)
            .Select(i => StoredLesson(
                text: $"Standing station preference number {i} that editors rely on every day.",
                status: FutureLessonStatus.Active)));
        SetupStored(stored);
        SetupVerdict(CreateVerdict());

        await _sut.ProcessAsync(Message());

        await _lessons.Received(1)
            .DeleteAsync("KIIS 101.1", stale.Id, Arg.Any<CancellationToken>());
        await _lessons.Received(1)
            .AddAsync(Arg.Any<FutureLessonEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdenticalScripts_ShortCircuitBeforeTheSummarizer()
    {
        await _sut.ProcessAsync(Message(
            original: "The M4 is  crawling into Parramatta",
            published: " The M4 is crawling into Parramatta "));

        await _summarizer.DidNotReceive()
            .SummarizeAsync(Arg.Any<LessonSummaryRequest>(), Arg.Any<CancellationToken>());
        await _lessons.DidNotReceive()
            .AddAsync(Arg.Any<FutureLessonEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnresolvedReinforceTarget_DegradesToCreate()
    {
        SetupStored([]);
        SetupVerdict(CreateVerdict() with
        {
            Action = LessonSummaryAction.Reinforce,
            TargetLessonId = "no-such-lesson",
        });

        await _sut.ProcessAsync(Message());

        await _lessons.Received(1).AddAsync(
            Arg.Is<FutureLessonEntry>(e =>
                e.Status == FutureLessonStatus.Candidate &&
                e.Origin == FutureLessonOrigin.Inferred),
            Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
