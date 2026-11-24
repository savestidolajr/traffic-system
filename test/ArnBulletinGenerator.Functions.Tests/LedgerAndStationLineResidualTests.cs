using System.Net;
using ArnBulletinGenerator.Application.Ledger;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class LedgerAndStationLineResidualTests
{
    [Fact]
    public async Task SpotChecks_DefaultPath_PreservesLegacyShapeAndRepositoryCall()
    {
        var ledger = Substitute.For<IEventLedgerService>();
        var access = Substitute.For<IMarketAccessService>();
        var jobs = Substitute.For<IJobRepository>();
        var logger = Substitute.For<ILogger<LedgerHttpTrigger>>();
        ledger.GetSpotChecksAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SpotCheckReviewDto>>.Success([]));
        var trigger = new LedgerHttpTrigger(ledger, access, jobs, logger,
            new ConfigurationBuilder().Build());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await trigger.GetSpotChecks(
                MockHttpRequestData.CreateGet("http://localhost/api/spot-checks"),
                MockHttpRequestData.CreateFunctionContext()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("{\"Items\":[]}");
        await ledger.Received(1).GetSpotChecksAsync(Arg.Any<CancellationToken>());
        await ledger.DidNotReceive().GetSpotChecksPageAsync(
            Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpotChecks_OptInPaging_ClampsLimitAndReturnsContinuationMetadata()
    {
        var ledger = Substitute.For<IEventLedgerService>();
        var access = Substitute.For<IMarketAccessService>();
        var jobs = Substitute.For<IJobRepository>();
        var logger = Substitute.For<ILogger<LedgerHttpTrigger>>();
        ledger.GetSpotChecksPageAsync(
                Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<VersionedLibraryListPage<SpotCheckReviewDto>>.Success(
                new VersionedLibraryListPage<SpotCheckReviewDto>(
                    [new SpotCheckReviewDto("job-1", "operator", DateTimeOffset.UtcNow)], 501, "next")));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpotChecks:PagingDisabled"] = "false"
            })
            .Build();
        var trigger = new LedgerHttpTrigger(ledger, access, jobs, logger, config);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await trigger.GetSpotChecks(
                MockHttpRequestData.CreateGet("http://localhost/api/spot-checks?limit=5000&cursor=prior"),
                MockHttpRequestData.CreateFunctionContext()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":501");
        body.ShouldContain("\"HasMore\":true");
        body.ShouldContain("\"NextCursor\":\"next\"");
        await ledger.Received(1).GetSpotChecksPageAsync(
            Arg.Is<VersionedLibraryListQuery>(q => q.PageSize == 500 && q.Cursor == "prior"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpotChecks_PagingKillSwitch_IgnoresOptInQuery()
    {
        var ledger = Substitute.For<IEventLedgerService>();
        var access = Substitute.For<IMarketAccessService>();
        var jobs = Substitute.For<IJobRepository>();
        var logger = Substitute.For<ILogger<LedgerHttpTrigger>>();
        ledger.GetSpotChecksAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SpotCheckReviewDto>>.Success([]));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpotChecks:PagingDisabled"] = "true"
            })
            .Build();
        var trigger = new LedgerHttpTrigger(ledger, access, jobs, logger, config);

        var response = await trigger.GetSpotChecks(
            MockHttpRequestData.CreateGet("http://localhost/api/spot-checks?limit=2"),
            MockHttpRequestData.CreateFunctionContext());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await ledger.Received(1).GetSpotChecksAsync(Arg.Any<CancellationToken>());
        await ledger.DidNotReceive().GetSpotChecksPageAsync(
            Arg.Any<VersionedLibraryListQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StationLines_UsesExactAiredBookendVersionAndKeepsDisabledWindowedFillers()
    {
        var fillers = Substitute.For<IFillerStatementsRepository>();
        var bookends = Substitute.For<IBookendRepository>();
        var logger = Substitute.For<ILogger>();
        fillers.GetStatementsAsync("market", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([
                new FillerStatement("disabled", "disabled line", Enabled: false),
                new FillerStatement("windowed", "windowed line", Availability:
                    new FillerAvailability(new DateOnly(2099, 1, 1), null, [], null, null))]));
        var bookend = Bookend.Create("name", null,
            new BookendTemplate("old intro", "old outro"), null, null, DateTime.UtcNow, "bookend").Value;
        bookend.ApplyEdit("name", null, new BookendTemplate("new intro", "new outro"), null, null,
            DateTime.UtcNow.AddMinutes(1));
        bookends.GetAsync("bookend", Arg.Any<CancellationToken>())
            .Returns(Result<Option<Bookend>>.Success(Option<Bookend>.Some(bookend)));
        var job = Job.Create("job", "station", "market", 30, DateTime.UtcNow).Value;
        job.BookendId = Option<string>.Some("bookend");
        job.BookendVersion = Option<int>.Some(1);

        var lines = await new JobStationLineResolver(fillers, bookends, logger)
            .ResolveAsync(job, CancellationToken.None);

        lines.ShouldBe(["disabled line", "windowed line", "old intro", "old outro"]);
    }

    [Fact]
    public async Task StationLines_StartsIndependentFillerAndBookendReadsConcurrently()
    {
        var fillers = Substitute.For<IFillerStatementsRepository>();
        var bookends = Substitute.For<IBookendRepository>();
        var logger = Substitute.For<ILogger>();
        var fillerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bookendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fillers.GetStatementsAsync("market", Arg.Any<CancellationToken>())
            .Returns(_ => WaitForReadAsync(fillerStarted, release,
                Result<IReadOnlyList<FillerStatement>>.Success([])));
        bookends.GetAsync("bookend", Arg.Any<CancellationToken>())
            .Returns(_ => WaitForReadAsync(bookendStarted, release,
                Result<Option<Bookend>>.Success(Option<Bookend>.None)));
        var job = Job.Create("job", "station", "market", 30, DateTime.UtcNow).Value;
        job.BookendId = Option<string>.Some("bookend");

        var pending = new JobStationLineResolver(fillers, bookends, logger)
            .ResolveAsync(job, CancellationToken.None);
        await Task.WhenAll(fillerStarted.Task, bookendStarted.Task);
        release.SetResult(true);
        (await pending).ShouldBeEmpty();
    }

    private static async Task<Result<T>> WaitForReadAsync<T>(
        TaskCompletionSource<bool> started,
        TaskCompletionSource<bool> release,
        Result<T> result)
    {
        started.SetResult(true);
        await release.Task;
        return result;
    }
}
