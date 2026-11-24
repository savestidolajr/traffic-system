using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Conversational;

public sealed class PlaybookPreviewServiceTests
{
    [Fact]
    public async Task Uses_latest_replayable_snapshot_substitutes_overlay_and_returns_baseline()
    {
        Job job = JobWithSnapshot("job-preview", "baseline read");
        Job newerWithoutSnapshot = Job.Create(
            "job-newer-no-snapshot", "2GB", "sydney", 30, DateTime.UtcNow.AddMinutes(1)).Value;
        var repository = Substitute.For<IJobRepository>();
        repository.ListJobsPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Job>>.Success([newerWithoutSnapshot, job]));
        var generator = Substitute.For<IPlaybookPreviewGenerator>();
        BroadcastWithTraffic? captured = null;
        generator.GenerateAsync(
                Arg.Do<BroadcastWithTraffic>(context => captured = context),
                Arg.Any<CancellationToken>())
            .Returns(Result<PlaybookPreviewGeneration>.Success(
                new PlaybookPreviewGeneration("proposed read", 12.5, [])));
        var shipped = ShippedProvider();
        var sut = new PlaybookPreviewService(repository, generator, shipped, TimeProvider.System,
            NullLogger<PlaybookPreviewService>.Instance);
        var overlay = new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role.standard"] = "Use a friendly radio voice."
            }
        };

        var result = await sut.PreviewAsync(new PlaybookPreviewRequest("compose2", "sydney", overlay));

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Script.ShouldBe("proposed read");
        result.Value.BaselineScript.ShouldBe("baseline read");
        captured.ShouldNotBeNull();
        captured!.Playbook.ShouldBeSameAs(overlay);
        captured.JobId.ShouldBe("job-preview-preview");
    }

    [Fact]
    public async Task Returns_not_found_when_no_job_has_a_generator_snapshot()
    {
        Job job = Job.Create("job-no-snapshot", "2GB", "sydney", 30, DateTime.UtcNow).Value;
        var repository = Substitute.For<IJobRepository>();
        repository.ListJobsPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Job>>.Success([job]));
        var shipped = ShippedProvider();
        var generator = Substitute.For<IPlaybookPreviewGenerator>();
        var sut = new PlaybookPreviewService(repository, generator, shipped, TimeProvider.System,
            NullLogger<PlaybookPreviewService>.Instance);

        var result = await sut.PreviewAsync(new PlaybookPreviewRequest("compose2", "sydney", new PlaybookOverlay()));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("No replayable job exists");
        await generator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default);
    }

    [Fact]
    public async Task Surfaces_overlay_refusals_while_still_generating()
    {
        Job job = JobWithSnapshot("job-refused");
        var repository = Substitute.For<IJobRepository>();
        repository.ListJobsPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Job>>.Success([job]));
        var generator = Substitute.For<IPlaybookPreviewGenerator>();
        generator.GenerateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<PlaybookPreviewGeneration>.Success(
                new PlaybookPreviewGeneration("read", null, [])));
        var shipped = ShippedProvider();
        var sut = new PlaybookPreviewService(repository, generator, shipped, TimeProvider.System,
            NullLogger<PlaybookPreviewService>.Instance);
        var overlay = new PlaybookOverlay
        {
            Rewrite = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["missing.block"] = "This cannot apply."
            }
        };

        var result = await sut.PreviewAsync(new PlaybookPreviewRequest("compose2", "sydney", overlay));

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Refused.Count.ShouldBe(1);
        result.Value.Refused[0].ShouldContain("missing.block");
        await generator.Received(1).GenerateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SamePrincipalPreviewSharesOneWorkflowAndCachesSuccessfulResult()
    {
        Job job = JobWithSnapshot("job-preview");
        var repository = Substitute.For<IJobRepository>();
        repository.ListJobsPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Job>>.Success([job]));
        var generator = Substitute.For<IPlaybookPreviewGenerator>();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Result<PlaybookPreviewGeneration>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        generator.GenerateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                entered.TrySetResult(true);
                return release.Task;
            });
        var sut = new PlaybookPreviewService(repository, generator, ShippedProvider(), TimeProvider.System,
            NullLogger<PlaybookPreviewService>.Instance,
            new InteractiveWorkGate(Options.Create(new InteractiveWorkOptions())));
        var request = new PlaybookPreviewRequest(
            "compose2", "sydney", new PlaybookOverlay(), "operator-a", "revision-1");

        Task<Result<PlaybookPreview>> first = sut.PreviewAsync(request);
        await entered.Task;
        Task<Result<PlaybookPreview>> duplicate = sut.PreviewAsync(request);
        release.SetResult(Result<PlaybookPreviewGeneration>.Success(
            new PlaybookPreviewGeneration("read", 10, [])));

        (await Task.WhenAll(first, duplicate)).All(result => result.IsSuccess).ShouldBeTrue();
        (await sut.PreviewAsync(request)).IsSuccess.ShouldBeTrue();
        await generator.Received(1).GenerateAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    private static Job JobWithSnapshot(string id, string? script = null)
    {
        Job job = Job.Create(id, "2GB", "sydney", 30, DateTime.UtcNow).Value;
        job.GeneratorInput = new GeneratorInputSnapshot
        {
            TargetDurationSeconds = 30,
            Location = "Sydney"
        };
        if (script is not null)
            job.ScriptText = Option<string>.Some(script);
        return job;
    }
    private static IShippedPlaybookProvider ShippedProvider()
    {
        var provider = Substitute.For<IShippedPlaybookProvider>();
        provider.GetDefault("compose2").Returns(Result<Playbook>.Success(new Playbook(
            [new PlaybookBlock("role.standard", PlaybookSection.Role, "Default role.", 0)])));
        return provider;
    }
}
