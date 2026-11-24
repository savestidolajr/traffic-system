using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

/// <summary>
///     Proves the production job repository against Azurite Table Storage. The 3,003-row
///     composite-cursor case was deleted on 2026-09-03: it asserted exact counts over a shared
///     emulator table selected by time window alone, so emulator throughput and any other run's
///     rows decided the result (CI 3409 and 3410 saw 2,925 of 2,997). What remains is
///     deterministic and scoped to its own market.
/// </summary>
[Trait("Category", "E2E")]
public sealed class TableStorageJobRepositoryAzuriteTests
{

    private readonly TableStorageJobRepository _repository = new(
        Options.Create(new StorageOptions
        {
            AccountName = StorageOptions.LocalEmulatorAccount,
            ContainerName = "broadcasts",
            MaxRetries = 0
        }),
        new AzuriteTokenCredential(),
        TimeProvider.System,
        NullLogger<TableStorageJobRepository>.Instance);

    /// <summary>
    ///     Pins the test-environment incident of 2026-09-03: code whose read-model flags defaulted
    ///     ON met an environment with no backfill, so the index held rows only for jobs written
    ///     after the deploy and a windowed page read from it reduced a 776-job history to 15 rows.
    ///     History is not the index's to lose, so a short index page must yield to the scan.
    /// </summary>
    [Fact(Timeout = 300_000)]
    public async Task PartiallyBackfilledReadIndex_StillReturnsTheJobsItHasNeverSeen()
    {
        var storage = Options.Create(new StorageOptions
        {
            AccountName = StorageOptions.LocalEmulatorAccount,
            ContainerName = "broadcasts",
            MaxRetries = 0
        });
        var credential = new AzuriteTokenCredential();
        var readIndex = new TableStorageJobReadIndexRepository(
            storage, credential, NullLogger<TableStorageJobReadIndexRepository>.Instance);

        // The history: written while the read models were off, so the index knows nothing of it.
        string runId = Guid.NewGuid().ToString("N");
        string market = $"partial-index-{runId}";
        var history = new List<string>();
        DateTime baseTime = DateTime.UtcNow.AddHours(-2);
        for (int index = 0; index < 5; index++)
        {
            var created = Job.Create($"{runId}-history-{index:D2}", "E2E Partial", market, 30,
                baseTime.AddMinutes(index));
            created.IsSuccess.ShouldBeTrue(created.Error);
            created.Value.LastUpdatedAt = baseTime.AddMinutes(index);
            (await _repository.SaveJobAsync(created.Value)).IsSuccess.ShouldBeTrue();
            history.Add(created.Value.JobId);
        }

        // The deploy: reads and writes now go through the index, which starts at this job.
        var indexed = new TableStorageJobRepository(
            storage, credential, TimeProvider.System,
            NullLogger<TableStorageJobRepository>.Instance,
            persistenceOptions: Options.Create(new JobPersistenceOptions
            {
                ReadIndexWriteEnabled = true,
                ReadIndexReadEnabled = true
            }),
            readIndexRepository: readIndex);
        var fresh = Job.Create($"{runId}-fresh", "E2E Partial", market, 30, baseTime.AddMinutes(10));
        fresh.IsSuccess.ShouldBeTrue(fresh.Error);
        fresh.Value.LastUpdatedAt = baseTime.AddMinutes(10);
        (await indexed.SaveJobAsync(fresh.Value)).IsSuccess.ShouldBeTrue();

        var page = await indexed.ListJobSummariesPageAsync(new JobListQuery(
            new DateTimeOffset(baseTime.AddMinutes(-1), TimeSpan.Zero),
            new DateTimeOffset(baseTime.AddMinutes(11), TimeSpan.Zero),
            50, null, null));

        page.IsSuccess.ShouldBeTrue(page.Error);
        var returned = page.Value
            .Where(item => string.Equals(item.Market, market, StringComparison.Ordinal))
            .Select(item => item.JobId)
            .ToHashSet(StringComparer.Ordinal);
        returned.ShouldContain(fresh.Value.JobId);
        foreach (string jobId in history)
            returned.ShouldContain(jobId, "the index has no row for this job, so the scan must supply it");
    }

    private sealed class AzuriteTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("azurite-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
