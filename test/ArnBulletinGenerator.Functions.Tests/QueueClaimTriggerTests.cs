using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Functions.Triggers.Queues;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class QueueClaimTriggerTests
{
    [Fact]
    public async Task Broadcast_WhenClaimIsFreshForAnotherWorker_AcknowledgesDuplicate()
    {
        var orchestrator = Substitute.For<IBroadcastOrchestrator>();
        var jobs = NotFoundJobs();
        var claims = Substitute.For<IJobClaimRepository>();
        claims.TryClaimAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result<JobClaim?>.Success(null));
        var sut = new BroadcastQueueTrigger(
            orchestrator,
            jobs,
            Substitute.For<ILogger<BroadcastQueueTrigger>>(),
            claims,
            Options.Create(new QueueClaimOptions { Enabled = true }),
            new NoOpBroadcastMetrics());

        await sut.Run(BroadcastJson("broadcast-duplicate"), Context());

        await orchestrator.DidNotReceive().OrchestrateAsync(
            Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
        await claims.Received(1).TryClaimAsync(
            "broadcast-duplicate", Arg.Any<string>(), TimeSpan.FromMinutes(8), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Broadcast_WhenClaimIsAcquired_ReleasesAfterOrchestration()
    {
        var orchestrator = Substitute.For<IBroadcastOrchestrator>();
        orchestrator.OrchestrateAsync(
                Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("broadcast.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30))));
        var jobs = NotFoundJobs();
        var claims = Substitute.For<IJobClaimRepository>();
        var claim = NewClaim("broadcast-release");
        claims.TryClaimAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result<JobClaim?>.Success(claim));
        claims.ReleaseAsync(Arg.Any<JobClaim>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = new BroadcastQueueTrigger(
            orchestrator,
            jobs,
            Substitute.For<ILogger<BroadcastQueueTrigger>>(),
            claims,
            Options.Create(new QueueClaimOptions { Enabled = true }),
            new NoOpBroadcastMetrics());

        await sut.Run(BroadcastJson("broadcast-release"), Context());

        await claims.Received(1).ReleaseAsync(claim, Arg.Any<CancellationToken>());
        await orchestrator.Received(1).OrchestrateAsync(
            Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regeneration_WhenClaimIsFreshForAnotherWorker_AcknowledgesDuplicate()
    {
        var orchestrator = Substitute.For<IBroadcastOrchestrator>();
        var jobs = NotFoundJobs();
        var claims = Substitute.For<IJobClaimRepository>();
        claims.TryClaimAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result<JobClaim?>.Success(null));
        var sut = new RegenerationQueueTrigger(
            orchestrator,
            jobs,
            Substitute.For<ILogger<RegenerationQueueTrigger>>(),
            claims,
            Options.Create(new QueueClaimOptions { Enabled = true }),
            new NoOpBroadcastMetrics());

        await sut.Run(RegenerationJson("regeneration-duplicate"), Context());

        await orchestrator.DidNotReceive().OrchestrateAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
        await claims.Received(1).TryClaimAsync(
            "regeneration-duplicate", Arg.Any<string>(), TimeSpan.FromMinutes(8), Arg.Any<CancellationToken>());
    }

    private static IJobRepository NotFoundJobs()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure(ErrorMessages.Job.NotFound));
        return jobs;
    }

    private static JobClaim NewClaim(string jobId) =>
        new(jobId, "test-owner", DateTime.UtcNow.AddMinutes(8), 1, DateTime.UtcNow, "etag");

    private static FunctionContext Context() => MockHttpRequestData.CreateFunctionContext();

    private static string BroadcastJson(string jobId) =>
        $$"""
           {
               "JobId": "{{jobId}}",
               "MarketId": "mel",
               "Location": "Melbourne",
               "TargetDurationSeconds": 30,
               "Latitude": -37.8,
               "Longitude": 144.9,
               "RadiusKm": 5.0,
               "VoiceId": "dolly",
               "Model": "eleven_v3",
               "Phase": {{(int)Phase.Generate}}
           }
           """;

    private static string RegenerationJson(string jobId) =>
        $$"""
           {
               "JobId": "{{jobId}}",
               "Location": "Melbourne",
               "TargetDurationSeconds": 30,
               "Traffic": "Light traffic on M1",
               "Incidents": []
           }
           """;
}
