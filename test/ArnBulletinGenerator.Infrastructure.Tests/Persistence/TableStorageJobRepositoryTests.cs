using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.JobEntityMapper;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageJobRepositoryTests
{
    private readonly ILogger<TableStorageJobRepository> _logger = Substitute.For<ILogger<TableStorageJobRepository>>();
    private readonly TableStorageJobRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageJobRepositoryTests()
    {
        _sut = new TableStorageJobRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            TimeProvider.System,
            _logger);

        // Azure returns a non-null response for every successful write. Keep the default
        // substitute behavior equivalent so tests that only care about repository control flow
        // don't accidentally turn an otherwise successful save into a null-response failure.
        _tableClient.UpsertEntityAsync(
                Arg.Any<JobIndexEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
        _tableClient.UpsertEntityAsync(
                Arg.Any<JobEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
        _tableClient.UpdateEntityAsync(
                Arg.Any<JobEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
        _tableClient.AddEntityAsync(
                Arg.Any<LastSuccessfulJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static JobEntity CreateEntity(string jobId = "job-1", string market = "Sydney",
        string station = "2GB", string status = "Requested", DateTimeOffset? createdAt = null)
    {
        return new JobEntity
        {
            PartitionKey = "Job",
            RowKey = jobId,
            Market = market,
            Station = station,
            Status = status,
            TargetDurationSeconds = 90,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            StatusHistoryJson = "[]"
        };
    }

    private static TableEntity CreateTableEntity(
        string jobId = "job-1",
        string market = "Sydney",
        string station = "2GB",
        string status = "Requested",
        DateTimeOffset? createdAt = null)
    {
        return new TableEntity("Job_" + market.ToLowerInvariant(), jobId)
        {
            ["Station"] = station,
            ["Market"] = market,
            ["TargetDurationSeconds"] = 90d,
            ["CreatedAt"] = createdAt ?? DateTimeOffset.UtcNow,
            ["LastUpdatedAt"] = DateTimeOffset.UtcNow,
            ["Status"] = status
        };
    }

    private void SeedTableQuery(params TableEntity[] entities)
    {
        _tableClient.QueryAsync<TableEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    private void SeedQuery(params JobEntity[] entities)
    {
        _tableClient.QueryAsync<JobEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    private void SeedGet(JobEntity entity)
    {
        var indexEntity = new JobIndexEntity
        {
            PartitionKey = "JobIndex",
            RowKey = entity.RowKey,
            Market = entity.Market
        };
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(indexEntity));
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }


    private void SeedTableGet(TableEntity entity)
    {
        var indexEntity = new JobIndexEntity
        {
            PartitionKey = "JobIndex",
            RowKey = entity.RowKey,
            Market = entity.GetString("Market") ?? string.Empty
        };
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(indexEntity));
        _tableClient.GetEntityAsync<TableEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }
    private void SeedGetNotFound(string rowKey)
    {
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    // ── ListJobsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ListJobsAsync_ReturnsJobs_OrderedByCreatedAtDescending()
    {
        var older = CreateEntity("j-1", createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = CreateEntity("j-2", createdAt: DateTimeOffset.UtcNow.AddHours(-1));
        SeedQuery(older, newer);

        var result = await _sut.ListJobsAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.First().JobId.ShouldBe("j-2");
    }

    [Fact]
    public async Task ListJobsAsync_EmptyTable_ReturnsEmptyList()
    {
        SeedQuery();

        var result = await _sut.ListJobsAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListJobsAsync_StorageError_ReturnsFailure()
    {
        _tableClient.QueryAsync<JobEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(500, "Internal Server Error"));

        var result = await _sut.ListJobsAsync();

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task ListJobSummariesAsync_SelectsOnlyListFields_AndSkipsFullJsonMapping()
    {
        var entity = CreateTableEntity("summary-1", market: "Sydney", station: "KIIS 90s");
        entity["MeasuredDurationSec"] = 22.4;
        IEnumerable<string>? capturedSelect = null;
        int? capturedPageSize = null;
        _tableClient.QueryAsync<TableEntity>(
                Arg.Any<string?>(),
                Arg.Do<int?>(pageSize => capturedPageSize = pageSize),
                Arg.Do<IEnumerable<string>?>(select => capturedSelect = select),
                Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(new[] { entity }));

        var result = await _sut.ListJobSummariesAsync();

        result.IsSuccess.ShouldBeTrue();
        var item = result.Value.Single();
        item.JobId.ShouldBe("summary-1");
        item.Market.ShouldBe("Sydney");
        item.MeasuredDurationSec.ShouldBe(22.4);
        capturedSelect.ShouldNotBeNull();
        capturedSelect.ShouldContain("RowKey");
        capturedPageSize.ShouldBe(1000);
        capturedSelect.ShouldContain("Status");
        capturedSelect.ShouldContain("MeasuredDurationSec");
        capturedSelect.ShouldNotContain("IncidentsJson");
        capturedSelect.ShouldNotContain("LogsJson");
    }

    [Fact]
    public async Task GetJobLedgerProjectionAsync_SelectsOnlyLedgerFields()
    {
        var entity = CreateTableEntity("ledger-projection-1", market: "Sydney");
        entity["AllIncidentsJson"] = "[]";
        entity["CorridorsJson"] = "[]";
        entity["ScriptSentencesJson"] = "[]";
        entity["LogsJson"] = "{ this is intentionally not valid JSON";
        SeedTableGet(entity);
        IEnumerable<string>? capturedSelect = null;
        _tableClient.GetEntityAsync<TableEntity>(
                Arg.Any<string>(),
                Arg.Is("ledger-projection-1"),
                Arg.Do<IEnumerable<string>?>(select => capturedSelect = select),
                Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        var result = await _sut.GetJobLedgerProjectionAsync("ledger-projection-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Market.ShouldBe("Sydney");
        result.Value.AllIncidents.ShouldBeEmpty();
        capturedSelect.ShouldNotBeNull();
        capturedSelect.ShouldContain("RowKey");
        capturedSelect.ShouldContain("AllIncidentsJson");
        capturedSelect.ShouldContain("CorridorsJson");
        capturedSelect.ShouldNotContain("LogsJson");
        capturedSelect.ShouldNotContain("WeatherJson");
    }

    [Fact]
    public async Task ListJobLedgerPageAsync_PreservesLastUpdatedOrdering()
    {
        var older = CreateTableEntity("ledger-old", market: "Sydney");
        older["LastUpdatedAt"] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = CreateTableEntity("ledger-new", market: "Sydney");
        newer["LastUpdatedAt"] = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        SeedTableQuery(older, newer);

        var result = await _sut.ListJobLedgerPageAsync(new JobListQuery(
            new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            2,
            Market: "Sydney"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(item => item.JobId).ShouldBe(new[] { "ledger-new", "ledger-old" });
    }

    // ── ListJobsPageAsync ────────────────────────────────────────────

    [Fact]
    public async Task ListJobsPageAsync_BuildsFilter_WithInclusiveFromAndExclusiveToBounds()
    {
        SeedQuery(); // filter content is what's under test; the mock never applies it itself
        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<JobEntity>());

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var result = await _sut.ListJobsPageAsync(new JobListQuery(from, to, 50));
        capturedFilter!.ShouldContain("PartitionKey ge 'Job_' and PartitionKey lt 'Job`'");
        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        // FromUtc is the inclusive lower bound (ge); ToUtc is the exclusive upper bound (lt) —
        // the cursor-exclusivity invariant the whole backward-paging scheme depends on.
        capturedFilter.ShouldContain("LastUpdatedAt ge datetime'2026-01-01T00:00:00Z'");
        capturedFilter.ShouldContain("LastUpdatedAt lt datetime'2026-01-02T00:00:00Z'");
    }

    [Fact]
    public async Task ListJobsPageAsync_WithStatuses_AppendsOrClause()
    {
        SeedQuery();
        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<JobEntity>());

        var result = await _sut.ListJobsPageAsync(new JobListQuery(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 50,
            new[] { JobStatus.WaitingApproval, JobStatus.Failed }));

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldContain("(Status eq 'WaitingApproval' or Status eq 'Failed')");
    }

    [Fact]
    public async Task ListJobsPageAsync_NoStatuses_OmitsStatusClause()
    {
        SeedQuery();
        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<JobEntity>());

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 50));

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldNotContain("Status eq");
    }

    [Fact]
    public async Task ListJobsPageAsync_WithMarket_UsesExactMarketPartition()
    {
        SeedQuery();
        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<JobEntity>());

        await _sut.ListJobsPageAsync(new JobListQuery(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            50,
            Market: "Sydney"));

        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldContain("PartitionKey eq 'Job_sydney'");
        capturedFilter.ShouldNotContain("PartitionKey ge 'Job_'");
    }

    [Fact]
    public async Task ListJobsPageAsync_ReturnsJobs_OrderedByLastUpdatedAtDescendingAndCappedAtLimit()
    {
        var oldest = CreateEntity("p-old");
        oldest.LastUpdatedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var mid = CreateEntity("p-mid");
        mid.LastUpdatedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var newest = CreateEntity("p-new");
        newest.LastUpdatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        // Seeded out of chronological order to prove the repo re-sorts before capping.
        SeedQuery(oldest, newest, mid);

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 2));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Select(j => j.JobId).ShouldBe(new[] { "p-new", "p-mid" });
    }

    [Fact]
    public async Task ListJobsPageAsync_EmptyTable_ReturnsEmptyList()
    {
        SeedQuery();

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 50));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListJobsPageAsync_WithCompositeCursor_EmitsTiebreakerFilter()
    {
        // A composite cursor (timestamp + row key) pins the page boundary to a specific row so
        // jobs sharing the boundary timestamp are not silently skipped by the next page's strict
        // `lt` filter (bug-bash 2026-08-05 #11: 4 of 6 tied jobs never appeared across pages).
        SeedQuery();
        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<JobEntity>());

        var boundary = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), boundary, 50,
                CursorJobId: "abc123"));

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        // Strictly after the boundary row in the stable total order: older timestamps, or the
        // boundary timestamp with a lower row key.
        capturedFilter.ShouldContain("LastUpdatedAt eq datetime'2026-01-02T00:00:00Z'");
        capturedFilter.ShouldContain("RowKey lt 'abc123'");
    }

    [Fact]
    public async Task ListJobsPageAsync_BareTimestampCursor_OmitsRowKeyTiebreaker()
    {
        // Backward compatibility: a bare timestamp cursor (legacy clients) keeps the plain
        // `lt` filter — no tiebreaker clause.
        SeedQuery();
        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<JobEntity>());

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 50));

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldNotContain("RowKey");
    }

    [Fact]
    public async Task ListJobsPageAsync_TiedTimestamps_TruncatedDeterministicallyByRowKey()
    {
        // Bulk writes stamp identical timestamps, so a page truncated at a tie must pick rows
        // deterministically — by row key descending — or the composite cursor cannot continue
        // exactly where the page stopped and rows fall between pages (bug-bash 2026-08-05 #11).
        var tieA = CreateEntity("tie-a");
        var tieB = CreateEntity("tie-b");
        var tieC = CreateEntity("tie-c");
        var shared = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        tieA.LastUpdatedAt = shared;
        tieB.LastUpdatedAt = shared;
        tieC.LastUpdatedAt = shared;
        SeedQuery(tieC, tieA, tieB); // deliberately out of order

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                shared.AddDays(1), 2));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(j => j.JobId).ShouldBe(new[] { "tie-c", "tie-b" });
    }

    [Fact]
    public async Task ListJobsPageAsync_StorageError_ReturnsFailure()
    {
        _tableClient.QueryAsync<JobEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(500, "Internal Server Error"));

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 50));

        result.IsFailure.ShouldBeTrue();
    }

    // ── DeleteJobAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteJobAsync_MatchingEtag_DeletesJobAndIndex()
    {
        var entity = CreateEntity("del-1");
        SeedGet(entity);
        _tableClient.DeleteEntityAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
        _tableClient.DeleteEntityAsync(
                "JobIndex", "del-1", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.DeleteJobAsync("del-1", "etag-v1");

        result.IsSuccess.ShouldBeTrue();
        // The delete carries the caller's revision as a storage-level precondition.
        await _tableClient.Received(1).DeleteEntityAsync(
            JobPartition(entity.Market),
            "del-1", new ETag("etag-v1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJobAsync_StaleEtag_RaceSurfacesConflict()
    {
        // A concurrent lifecycle write (e.g. an approve landing between the trigger's load and
        // this delete) bumps the stored revision, so the storage-level conditional delete fails
        // with 412 instead of destroying the changed bulletin blind (bug-bash 2026-08-05 #12).
        var entity = CreateEntity("del-2");
        SeedGet(entity);
        _tableClient.DeleteEntityAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(412, "Precondition Failed"));

        var result = await _sut.DeleteJobAsync("del-2", "stale-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.Conflict);
    }

    [Fact]
    public async Task DeleteJobAsync_ConcurrentDeletion_ReturnsNotFound()
    {
        var entity = CreateEntity("del-3");
        SeedGet(entity);
        _tableClient.DeleteEntityAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(404, "Not Found"));

        var result = await _sut.DeleteJobAsync("del-3", "etag-v1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    // ── GetJobAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetJobAsync_ExistingJob_ReturnsSuccess()
    {
        var entity = CreateEntity("job-42");
        SeedGet(entity);

        var result = await _sut.GetJobAsync("job-42");

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobId.ShouldBe("job-42");
        result.Value.Market.ShouldBe("Sydney");
    }

    [Fact]
    public async Task GetJobAsync_NonExistent_ReturnsFailure()
    {
        SeedGetNotFound("missing");

        var result = await _sut.GetJobAsync("missing");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }
    [Fact]
    public async Task SaveJobAsync_ValidJob_ReturnsSuccess()
    {
        var job = Job.Create("save-1", "2GB", "Sydney", 90, DateTime.UtcNow).Value;

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<JobIndexEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<JobEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveJobAsync_StorageError_ReturnsFailure()
    {
        _tableClient.UpsertEntityAsync(
                Arg.Any<JobEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var job = Job.Create("err-1", "2GB", "Sydney", 90, DateTime.UtcNow).Value;
        var result = await _sut.SaveJobAsync(job);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveJobAsync_WithETag_UpdatesEntity()
    {
        var job = Job.Create("save-2", "2GB", "Sydney", 90, DateTime.UtcNow).Value;
        job.ETag = "etag-v1";

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<JobEntity>(),
            Arg.Is<ETag>(e => e.ToString() == "etag-v1"),
            TableUpdateMode.Replace,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveJobAsync_WithStaleETag_RetriesExhausted_ReturnsConflict()
    {
        // Every reload reports the same material fingerprint as the job carries (i.e. only
        // benign concurrent log appends, never a real state change), so the loop keeps
        // retrying until attempts are exhausted rather than bailing out on the first 412.
        var job = Job.Create("save-3", "2GB", "Sydney", 90, DateTime.UtcNow).Value;
        job.ETag = "stale-etag";

        var refreshed = CreateEntity("save-3", "Sydney");
        refreshed.ETag = new ETag("refreshed-etag");
        job.ConcurrencyFingerprint = ComputeFingerprint(refreshed);

        _tableClient.UpdateEntityAsync(
                Arg.Any<JobEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"));

        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Is("Job_sydney"), Arg.Is("save-3"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(refreshed));

        var result = await _sut.SaveJobAsync(job);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.Conflict);
        await _tableClient.Received(3).UpdateEntityAsync(
            Arg.Any<JobEntity>(), Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>());
        await _tableClient.Received(2).GetEntityAsync<JobEntity>(
            Arg.Any<string>(), Arg.Is("save-3"), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveJobAsync_ETagConflict_ConcurrentStatusChange_ReturnsConflictWithoutOverwriting()
    {
        // A concurrent writer changed job state (e.g. a reject landed while this save was
        // approving) between load and save, so the reloaded row's material fingerprint no
        // longer matches the one this job captured at load. That must surface as a conflict
        // immediately: replaying the caller's stale row would silently lose the concurrent
        // state change (the C1 lost-update bug), so no further UpdateEntityAsync may happen.
        var job = Job.Create("save-5", "2GB", "Sydney", 90, DateTime.UtcNow).Value;
        job.ETag = "v1";
        job.ConcurrencyFingerprint = "fingerprint-captured-at-load";

        var refreshed = CreateEntity("save-5", "Sydney", status: "Rejected");
        refreshed.ETag = new ETag("v2");
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Is("Job_sydney"), Arg.Is("save-5"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(refreshed));

        _tableClient.UpdateEntityAsync(
                Arg.Any<JobEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"));

        var result = await _sut.SaveJobAsync(job);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.Conflict);
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<JobEntity>(), Arg.Is<ETag>(e => e.ToString() == "v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        await _tableClient.Received(1).GetEntityAsync<JobEntity>(
            Arg.Is("Job_sydney"), Arg.Is("save-5"), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveJobAsync_ETagConflict_ConcurrentLogAppend_RetriesAndMergesBothWriters()
    {
        // Models the real scenario: PublishJobAsync fetches a job (ETag "v1"), a concurrent
        // AddLogAsync write appends a delivery-audit log entry and bumps the stored row to
        // "v2" without touching any material column. The retry loop must recognise the append
        // as benign, merge the concurrently-appended entry into this job's own logs, and retry
        // with the fresh ETag instead of permanently 409'ing or dropping either writer's entry.
        var job = Job.Create("save-6", "2GB", "Sydney", 90, DateTime.UtcNow).Value;
        job.ETag = "v1";
        var ourLog = new JobLog(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), JobLogLevel.Info,
            "Our own log entry");
        job.SetLogs(new[] { ourLog });

        var refreshed = CreateEntity("save-6", "Sydney");
        refreshed.ETag = new ETag("v2");
        var concurrentLog = new JobLog(new DateTime(2026, 1, 1, 10, 0, 5, DateTimeKind.Utc), JobLogLevel.Info,
            "Concurrently appended log entry");
        refreshed.LogsJson = JsonSerializer.Serialize(new[] { concurrentLog });
        job.ConcurrencyFingerprint = ComputeFingerprint(refreshed);

        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Is("Job_sydney"), Arg.Is("save-6"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(refreshed));

        JobEntity? writtenEntity = null;
        _tableClient.UpdateEntityAsync(
                Arg.Do<JobEntity>(e => writtenEntity = e), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new RequestFailedException(412, "Precondition Failed"),
                _ => Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<JobEntity>(), Arg.Is<ETag>(e => e.ToString() == "v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<JobEntity>(), Arg.Is<ETag>(e => e.ToString() == "v2"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());

        writtenEntity.ShouldNotBeNull();
        writtenEntity!.LogsJson.ShouldNotBeNull();
        writtenEntity.LogsJson!.ShouldContain("Our own log entry");
        writtenEntity.LogsJson.ShouldContain("Concurrently appended log entry");
    }

    [Fact]
    public async Task SaveJobAsync_WithNullETag_CreatesEntity()
    {
        var job = Job.Create("save-4", "2GB", "Sydney", 90, DateTime.UtcNow).Value;
        job.ETag = null;

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<JobIndexEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<JobEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    // ── AddLogAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task AddLogAsync_ExistingJob_AppendsLogAndUpdates()
    {
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is("log-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(
                new JobIndexEntity { PartitionKey = "JobIndex", RowKey = "log-1", Market = "Sydney" }));

        var entity = CreateEntity("log-1");
        entity.LogsJson = "[]";
        entity.ETag = new ETag("etag-1");

        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Any<string>(), Arg.Is("log-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        var result = await _sut.AddLogAsync("log-1", "test message");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Any<JobEntity>(), Arg.Any<ETag>(),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }
    [Fact]

    public async Task AddLogAsync_NonExistentJob_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is("gone"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.AddLogAsync("gone", "nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task AddLogAsync_ETagConflict_RetriesAndSucceeds()
    {
        var entity = CreateEntity("retry-1");
        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is("retry-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(
                new JobIndexEntity { PartitionKey = "JobIndex", RowKey = "retry-1", Market = "Sydney" }));

        entity.LogsJson = "[]";
        entity.ETag = new ETag("v1");

        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Any<string>(), Arg.Is("retry-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        // First update throws 412, second succeeds
        _tableClient.UpdateEntityAsync(
                Arg.Any<JobEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new RequestFailedException(412, "Precondition Failed"),
                _ => Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.AddLogAsync("retry-1", "retry msg");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AddLogAsync_WhenLogsExceedCeiling_TrimsOldest_AndAppendStillSucceeds()
    {
        // VC-1: AddLogAsync re-serialises the ENTIRE log list on every append. A row whose
        // LogsJson already exceeds the 30,000-char storage threshold (incompressible payload)
        // must NOT wedge on the append — the oldest entries are trimmed, the freshly appended
        // entry survives, and the update succeeds.
        var entity = CreateEntity("log-ceiling");
        var logs = Enumerable.Range(0, 700)
            .Select(i => new JobLog(DateTime.UtcNow.AddMinutes(-700 + i), JobLogLevel.Info,
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}"))
            .ToArray();
        entity.LogsJson = JsonSerializer.Serialize(logs, JobEntityMapper.JsonOptions);
        entity.LogsJson!.Length.ShouldBeGreaterThan(30_000);
        entity.ETag = new ETag("v1");

        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is("log-ceiling"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(
                new JobIndexEntity { PartitionKey = "JobIndex", RowKey = "log-ceiling", Market = "Sydney" }));
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Any<string>(), Arg.Is("log-ceiling"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        JobEntity? updated = null;
        _tableClient.UpdateEntityAsync(
                Arg.Do<JobEntity>(e => updated = e),
                Arg.Any<ETag>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.AddLogAsync("log-ceiling", "final terminal marker");

        result.IsSuccess.ShouldBeTrue();
        updated.ShouldNotBeNull();
        updated!.LogsJson.ShouldNotBeNull();
        updated.LogsJson!.Length.ShouldBeLessThanOrEqualTo(30_000);
        updated.LogsJson.ShouldNotStartWith("gz:");
        // The freshly appended entry — the newest — survives the trim.
        var stored = JsonSerializer.Deserialize<List<JobLog>>(updated.LogsJson, JobEntityMapper.JsonOptions)!;
        stored.ShouldNotBeEmpty();
        stored[^1].Message.ShouldBe("final terminal marker");
        stored[^1].At.ShouldBeGreaterThan(stored[0].At);
        // The oldest pre-existing entries were dropped to make room.
        stored.Count.ShouldBeLessThan(logs.Length);
    }

    // ── Roundtrip mapping ────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGet_RoundTrips_OptionalFields()
    {
        var job = Job.Create("rt-1", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.ScriptText = "Hello traffic";
        job.AudioUrl = "https://example.com/audio.mp3";
        job.AudioSha256 = "A" + new string('B', 63);
        job.MeasuredDurationSec = 95.5;
        job.ScheduledForUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        job.PreviousJobId = Option<string>.Some("prev-job-9");
        job.PreviousScriptContext = Option<string>.Some("Earlier: M4 was slow.");
        job.IncidentChangeSummary = Option<IncidentChangeSummary>.Some(
            new IncidentChangeSummary(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), 2, 1, 0,
                Option<string>.Some("2 new, 1 updated")));
        job.NarrativeIncidentChangeSummary = Option<IncidentChangeSummary>.Some(
            new IncidentChangeSummary(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), 5, 3, 9,
                Option<string>.Some("Narrative: cleared M1 congestion")));
        job.MonitoredRoutes = new[]
        {
            new MonitoredRouteStatus("Airport Run", new CoveragePoint(-33.8, 151.2),
                new CoveragePoint(-33.9, 151.1), 42, 20, 15.5),
            new MonitoredRouteStatus("CBD Loop", new CoveragePoint(-33.87, 151.20),
                new CoveragePoint(-33.88, 151.21), 15, 12, 4.2)
        };

        // Capture the entity that gets upserted
        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.RowKey.ShouldBe("rt-1");
        captured.ScriptText.ShouldBe("Hello traffic");
        captured.AudioSha256.ShouldBe("A" + new string('B', 63));
        captured.AudioUrl.ShouldBe("https://example.com/audio.mp3");
        captured.MeasuredDurationSec.ShouldBe(95.5);
        captured.ScheduledForUtc.ShouldNotBeNull();
        captured.NarrativeIncidentChangeSummaryJson.ShouldNotBeNull();
        captured.MonitoredRoutesJson.ShouldNotBeNull();

        // Now feed the captured entity back through Get to verify domain mapping
        SeedGet(captured);
        var getResult = await _sut.GetJobAsync("rt-1");
        getResult.IsSuccess.ShouldBeTrue();
        var loaded = getResult.Value;
        loaded.ScriptText.Value.ShouldBe("Hello traffic");
        loaded.AudioSha256.ShouldBe("A" + new string('B', 63));
        loaded.AudioUrl.Value.ShouldBe("https://example.com/audio.mp3");
        loaded.MeasuredDurationSec.Value.ShouldBe(95.5);
        loaded.PreviousJobId.Value.ShouldBe("prev-job-9");
        loaded.PreviousScriptContext.Value.ShouldBe("Earlier: M4 was slow.");
        loaded.IncidentChangeSummary.Value.NewCount.ShouldBe(2);
        loaded.IncidentChangeSummary.Value.UpdatedCount.ShouldBe(1);
        // Independently persisted, not aliased with the raw summary above.
        loaded.NarrativeIncidentChangeSummary.Value.ClearedCount.ShouldBe(9);
        loaded.NarrativeIncidentChangeSummary.Value.ClearedCount.ShouldNotBe(loaded.IncidentChangeSummary.Value.ClearedCount);
        loaded.MonitoredRoutes.Count.ShouldBe(2);
        loaded.MonitoredRoutes.Single(r => r.Name == "Airport Run").DriveTimeMinutes.ShouldBe(42);
        loaded.MonitoredRoutes.Single(r => r.Name == "Airport Run").NormalDriveTimeMinutes.ShouldBe(20);
        captured.PreviousJobId.ShouldBe("prev-job-9");
        captured.IncidentChangeSummaryJson.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveAndGet_DefaultNarrativeSummaryAndMonitoredRoutes_LeavesJsonColumnsNull()
    {
        var job = Job.Create("rt-narrative-legacy", "2GB", "Sydney", 120, DateTime.UtcNow).Value;

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.NarrativeIncidentChangeSummaryJson.ShouldBeNull();
        captured.MonitoredRoutesJson.ShouldBeNull();

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-narrative-legacy")).Value;
        loaded.NarrativeIncidentChangeSummary.IsNone.ShouldBeTrue();
        loaded.MonitoredRoutes.ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_AllIncidents_AsFullPool()
    {
        var job = Job.Create("rt-inc", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });
        job.SetAllIncidents(new[]
        {
            new JobIncident("sel-1", 1, "crash on M4", "M4"),
            new JobIncident("omit-1", 1, "roadworks on M5", "M5"),
        });

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.AllIncidentsJson.ShouldNotBeNull();
        captured.AllIncidentsJson!.ShouldContain("omit-1");

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-inc")).Value;
        // The full pool survives the round trip; the bulletin subset stays distinct.
        loaded.AllIncidents.Select(i => i.Key).ShouldBe(new[] { "sel-1", "omit-1" }, ignoreOrder: true);
        loaded.Incidents.Select(i => i.Key).ShouldBe(new[] { "sel-1" });
    }

    [Fact]
    public async Task SaveJob_OversizedColumn_IsCompressedAndRoundTrips()
    {
        // An incident-heavy market (observed live: adelaide 31-33K chars on EVERY job) can
        // serialize a single JSON column past Azure Table Storage's 32K-char per-property limit.
        // Dropping the column silently blanked replay/audit data for whole markets — oversized
        // columns are now gzip-compressed ("gz:" sentinel) and must round-trip intact.
        var job = Job.Create("rt-huge", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });
        var hugePool = Enumerable.Range(0, 500)
            .Select(i => new JobIncident($"inc-{i}", 1,
                $"Roadworks plus a broken-down vehicle causing lane closures and heavy delays at point {i}",
                $"Road {i}"))
            .ToArray();
        job.SetAllIncidents(hugePool);

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        // The oversized pool column is stored compressed, within the table limit...
        captured!.AllIncidentsJson.ShouldNotBeNull();
        captured.AllIncidentsJson!.ShouldStartWith("gz:");
        captured.AllIncidentsJson.Length.ShouldBeLessThanOrEqualTo(30_000);
        // ...the small, essential columns persist plain...
        captured.IncidentsJson.ShouldNotBeNull();
        captured.IncidentsJson!.ShouldContain("sel-1");
        // ...and the compressed column round-trips intact.
        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-huge")).Value;
        loaded.AllIncidents.Count.ShouldBe(500);
        loaded.AllIncidents[499].Key.ShouldBe("inc-499");
    }

    [Fact]
    public async Task SaveJob_OversizedScriptSentences_IsCompressedAndRoundTrips()
    {
        // Long bulletins with many corridor-tagged sentences can push ScriptSentencesJson past
        // the 32K-char Table Storage cap just like the other pool columns — it must gzip-compress
        // ("gz:" sentinel) instead of being dropped, and round-trip intact.
        var job = Job.Create("rt-sentences", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });
        var hugeScript = Enumerable.Range(0, 500)
            .Select(i => new JobScriptSentence(
                $"Roadworks plus a broken-down vehicle causing lane closures and heavy delays at point {i}",
                new[] { $"Road {i}" }))
            .ToArray();
        job.SetScriptSentences(hugeScript);

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.ScriptSentencesJson.ShouldNotBeNull();
        captured.ScriptSentencesJson!.ShouldStartWith("gz:");
        captured.ScriptSentencesJson.Length.ShouldBeLessThanOrEqualTo(30_000);

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-sentences")).Value;
        loaded.ScriptSentences.Count.ShouldBe(500);
        loaded.ScriptSentences[499].CorridorKeys.ShouldBe(new[] { "Road 499" });
    }

    [Fact]
    public async Task SaveJob_IncompressiblyOversizedColumn_IsDroppedButTheJobStillPersists()
    {
        // Last-resort guard: a column too large even after compression is dropped so the
        // entity write (script, audio, selected incidents) still succeeds.
        var job = Job.Create("rt-entropy", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });
        var incompressible = Enumerable.Range(0, 1500)
            .Select(i => new JobIncident($"inc-{i}", 1,
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}{Guid.NewGuid():N}",
                $"{Guid.NewGuid():N}"))
            .ToArray();
        job.SetAllIncidents(incompressible);

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.AllIncidentsJson.ShouldBeNull();
        captured.IncidentsJson.ShouldNotBeNull();
        captured.IncidentsJson!.ShouldContain("sel-1");
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_FlowRoads_WithFlags()
    {
        var job = Job.Create("rt-flow", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.SetFlowRoads(new[]
        {
            new JobFlowRoad("M1", JamFactor: 1.5, LengthMeters: 4200, IsWeighted: true, IsSelected: true),
            new JobFlowRoad("Gore Hill Freeway", JamFactor: 0.5, LengthMeters: 1800, IsWeighted: false, IsSelected: false),
        });

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.FlowRoadsJson.ShouldNotBeNull();

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-flow")).Value;
        loaded.FlowRoads.Count.ShouldBe(2);
        var m1 = loaded.FlowRoads.Single(f => f.RoadName == "M1");
        m1.IsWeighted.ShouldBeTrue();
        m1.IsSelected.ShouldBeTrue();
        m1.JamFactor.ShouldBe(1.5);
        m1.LengthMeters.ShouldBe(4200);
        loaded.FlowRoads.Single(f => f.RoadName == "Gore Hill Freeway").IsSelected.ShouldBeFalse();
    }

    [Fact]
    public async Task GetJob_LegacyEntity_WithoutFlowRoadsJson_LoadsEmptyPool()
    {
        var job = Job.Create("rt-flow-legacy", "2GB", "Sydney", 120, DateTime.UtcNow).Value;

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);
        captured.ShouldNotBeNull();
        captured!.FlowRoadsJson.ShouldBeNull();

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-flow-legacy")).Value;
        loaded.FlowRoads.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetJob_LegacyEntity_WithoutAllIncidentsJson_LoadsEmptyPool()
    {
        var job = Job.Create("rt-legacy", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.SetIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });
        job.SetAllIncidents(new[] { new JobIncident("sel-1", 1, "crash on M4", "M4") });

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
        await _sut.SaveJobAsync(job);

        // Simulate a row written before AllIncidentsJson existed.
        captured!.AllIncidentsJson = null;
        SeedGet(captured);

        var loaded = (await _sut.GetJobAsync("rt-legacy")).Value;
        loaded.AllIncidents.ShouldBeEmpty();
        loaded.Incidents.Select(i => i.Key).ShouldBe(new[] { "sel-1" });
    }

    // ── ListJobsNeedingFallbackAsync ────────────────────────────────

    [Fact]
    public async Task ListJobsNeedingFallbackAsync_ReturnsMatchingJobs()
    {
        var entity = CreateEntity("fb-1");
        entity.FallbackDeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(-10);

        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(new[] { entity }));

        var result = await _sut.ListJobsNeedingFallbackAsync(DateTime.UtcNow);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(1);
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldContain("Status ne 'Published'");
    }

    // ── GetLatestBulletinForMarketTodayAsync ───────────────────────
    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_NoJobs_ReturnsNone()
    {
        SeedQuery(); // empty

        var result = await _sut.GetLatestBulletinForMarketTodayAsync(
            "Sydney", "2GB", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_OnlyQueriesPublishedStatus()
    {
        var entity = CreateEntity("pub-1", "Sydney", "2GB", "Published",
            DateTimeOffset.UtcNow);

        var capturedFilters = new List<string?>();
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilters.Add(f)),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(new[] { entity }));

        var result = await _sut.GetLatestBulletinForMarketTodayAsync(
            "Sydney", "2GB", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.JobId.ShouldBe("pub-1");

        // A bulletin awaiting approval (or merely approved, or fallback) has not necessarily
        // aired — only Published is ever queried as a same-day continuity baseline.
        capturedFilters.Count.ShouldBe(1);
        string filter = capturedFilters[0].ShouldNotBeNull();
        filter.ShouldContain("Status eq 'Published'");
        filter.ShouldNotContain("WaitingApproval");
        filter.ShouldNotContain("Approved");
        filter.ShouldNotContain("FallbackUsed");
    }

    // ── ListStaleRequestedJobsAsync ──────────────────────────────────

    [Fact]
    public async Task ListStaleRequestedJobsAsync_ReturnsMatchingJobs()
    {
        var entity = CreateEntity("stale-1", status: "Requested");
        entity.LastUpdatedAt = DateTimeOffset.UtcNow.AddHours(-2);
        SeedQuery(entity);

        var result = await _sut.ListStaleRequestedJobsAsync(DateTime.UtcNow.AddHours(-1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(1);
    }

    // ── GetLastSuccessfulJobForMarketAsync ────────────────────────────
    //
    // The pointer read model is OFF by default, because a pointer table that has never been
    // backfilled would answer for jobs it holds no rows for. These two cases are about that read
    // model, so they enable it explicitly rather than inheriting a default.
    private TableStorageJobRepository PointerReadSut()
    {
        var sut = new TableStorageJobRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            TimeProvider.System,
            _logger,
            Microsoft.Extensions.Options.Options.Create(
                new ArnBulletinGenerator.Domain.Configuration.JobPersistenceOptions
                {
                    LastSuccessfulReadEnabled = true,
                    LastSuccessfulWriteEnabled = true
                }));
        TableClientTestHelper.InjectMockTableClient(sut, _tableClient);
        return sut;
    }

    [Fact]
    public async Task GetLastSuccessfulJobForMarketAsync_MultipleJobs_ReturnsMostRecentWithAudio()
    {
        var olderCreatedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var older = CreateEntity("suc-1", "Sydney", "2GB", "Published", olderCreatedAt);
        older.AudioUrl = "https://example.com/older.wav";

        var newerCreatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var newer = CreateEntity("suc-2", "Sydney", "2GB", "Approved", newerCreatedAt);
        newer.AudioUrl = "https://example.com/newer.wav";

        // The indexed read path first fetches a narrow candidate projection, then hydrates the
        // winning rows by their partition/row key. Seed both halves rather than the old aggregate
        // query so this test protects the bounded read model without weakening the contract.
        var olderSummary = CreateTableEntity("suc-1", "Sydney", "2GB", "Published", olderCreatedAt);
        olderSummary["AudioUrl"] = older.AudioUrl;
        var newerSummary = CreateTableEntity("suc-2", "Sydney", "2GB", "Approved", newerCreatedAt);
        newerSummary["AudioUrl"] = newer.AudioUrl;
        SeedTableQuery(olderSummary, newerSummary);

        _tableClient.GetEntityAsync<LastSuccessfulJobEntity>(
                Arg.Is("LastSuccessful"), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Is("Job_sydney"), Arg.Is("suc-1"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(older));
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Is("Job_sydney"), Arg.Is("suc-2"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(newer));

        var result = await PointerReadSut().GetLastSuccessfulJobForMarketAsync(
            "Sydney", "2GB",
            DateTime.UtcNow.AddHours(-5),
            DateTime.UtcNow.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.JobId.ShouldBe("suc-2");
    }

    [Fact]
    public async Task GetLastSuccessfulJobForMarketAsync_NoMatchingMarket_ReturnsNone()
    {
        SeedTableQuery(); // empty narrow candidate projection
        _tableClient.GetEntityAsync<LastSuccessfulJobEntity>(
                Arg.Is("LastSuccessful"), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await PointerReadSut().GetLastSuccessfulJobForMarketAsync(
            "Melbourne", "3AW",
            DateTime.UtcNow.AddHours(-5),
            DateTime.UtcNow.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetJobAsync_CorruptGeneratorInputJson_JobStillLoadableWithAbsentInputAndLogsWarning()
    {
        var entity = CreateEntity("corrupt-gi");
        entity.GeneratorInputJson = "not valid json {{{";
        SeedGet(entity);

        var result = await _sut.GetJobAsync("corrupt-gi");

        result.IsSuccess.ShouldBeTrue();
        result.Value.GeneratorInput.ShouldBeNull();
        _logger.ReceivedWithAnyArgs().LogWarning(default, default!, default!, default!);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips_GeneratorInput()
    {
        var job = Job.Create("rt-gi", "2GB", "Sydney", 120, DateTime.UtcNow).Value;
        job.GeneratorInput = new GeneratorInputSnapshot
        {
            TargetDurationSeconds = 90,
            Location = "Sydney CBD"
        };

        JobEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<JobEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.SaveJobAsync(job);

        captured.ShouldNotBeNull();
        captured!.GeneratorInputJson.ShouldNotBeNull();
        captured.GeneratorInputJson.ShouldContain("Sydney CBD");

        SeedGet(captured);
        var loaded = (await _sut.GetJobAsync("rt-gi")).Value;
        loaded.GeneratorInput.ShouldNotBeNull();
        loaded.GeneratorInput!.TargetDurationSeconds.ShouldBe(90);
        loaded.GeneratorInput.Location.ShouldBe("Sydney CBD");
    }

    // ── New scheme: index lookup, partition-scoped queries, prefix range ──

    [Fact]
    public async Task GetJobAsync_UsesIndex_ToResolvePartition()
    {
        var entity = CreateEntity("idx-42", market: "Sydney");
        var indexEntity = new JobIndexEntity
        {
            PartitionKey = "JobIndex",
            RowKey = "idx-42",
            Market = "Sydney"
        };

        _tableClient.GetEntityAsync<JobIndexEntity>(
                Arg.Is("JobIndex"), Arg.Is("idx-42"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(indexEntity));

        string? capturedPartitionKey = null;
        _tableClient.GetEntityAsync<JobEntity>(
                Arg.Do<string>(pk => capturedPartitionKey = pk),
                Arg.Is("idx-42"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        var result = await _sut.GetJobAsync("idx-42");

        result.IsSuccess.ShouldBeTrue();
        capturedPartitionKey.ShouldBe("Job_sydney");
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_FiltersByPartition()
    {
        var entity = CreateEntity("mk-1", market: "Melbourne", station: "3AW",
            status: "Published", createdAt: DateTimeOffset.UtcNow);

        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(new[] { entity }));

        var result = await _sut.GetLatestBulletinForMarketTodayAsync(
            "Melbourne", "3AW", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        // Partition-scoped: the filter must target only the market's partition, not a full scan
        capturedFilter.ShouldContain("PartitionKey eq 'Job_melbourne'");
        capturedFilter.ShouldNotContain("Market eq");
    }

    [Fact]
    public async Task ListJobsAsync_UsesPrefixRange()
    {
        var entity1 = CreateEntity("p1", market: "Sydney");
        var entity2 = CreateEntity("p2", market: "Melbourne");

        string? capturedFilter = null;
        _tableClient.QueryAsync<JobEntity>(
                Arg.Do<string?>(f => capturedFilter = f),
                Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(new[] { entity1, entity2 }));

        var result = await _sut.ListJobsAsync();

        result.IsSuccess.ShouldBeTrue();
        capturedFilter.ShouldNotBeNull();
        capturedFilter.ShouldContain("PartitionKey ge 'Job_' and PartitionKey lt 'Job`'");
    }

    [Fact]
    public async Task SaveJobAsync_SavesIndexThenJob_InOrder()
    {
        var job = Job.Create("order-1", "2GB", "Brisbane", 90, DateTime.UtcNow).Value;

        var callLog = new List<string>();
        _tableClient.UpsertEntityAsync(Arg.Any<JobIndexEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()))
            .AndDoes(_ => callLog.Add("Index"));
        _tableClient.UpsertEntityAsync(Arg.Any<JobEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()))
            .AndDoes(_ => callLog.Add("Job"));

        var result = await _sut.SaveJobAsync(job);

        result.IsSuccess.ShouldBeTrue();
        callLog.ShouldBe(new[] { "Index", "Job" }, ignoreOrder: false);
    }
}
