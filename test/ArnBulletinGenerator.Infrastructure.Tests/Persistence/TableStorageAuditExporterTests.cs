using System.IO;
using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageAuditExporterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 12, 16, 0, 0, TimeSpan.Zero);

    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly TableStorageAuditExporter _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageAuditExporterTests()
    {
        _blobServiceClient.GetBlobContainerClient("audit-exports").Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);

        _sut = new TableStorageAuditExporter(
            TableClientTestHelper.StubOptions(),
            Options.Create(new AuditExportOptions()),
            TableClientTestHelper.StubCredential(),
            _blobServiceClient,
            new FixedTimeProvider(FixedNow),
            Substitute.For<ILogger<TableStorageAuditExporter>>());
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    private void SeedQuery(params TableEntity[] entities)
    {
        _tableClient.QueryAsync<TableEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    /// <summary>
    ///     Sets up <see cref="BlobClient.OpenWriteAsync" /> to return a seekable
    ///     non-closing stream that survives the exporter's <c>await using</c> disposal.
    /// </summary>
    private sealed class NonClosingMemoryStream : MemoryStream
    {
        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private NonClosingMemoryStream MockBlobStream()
    {
        var stream = new NonClosingMemoryStream();
        _blobClient.OpenWriteAsync(Arg.Any<bool>(), Arg.Any<BlobOpenWriteOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(stream));
        return stream;
    }
    [Fact]
    public async Task ExportJobsAsync_WritesDatedNdjsonSnapshot()
    {
        SeedQuery(
            new TableEntity("Job", "job-1") { ["Status"] = "Published", ["ScriptText"] = "Traffic is flowing." },
            new TableEntity("Job", "job-2") { ["Status"] = "Rejected" });

        var stream = MockBlobStream();

        var result = await _sut.ExportJobsAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
        _containerClient.Received(1).GetBlobClient("radioJobs/2026-06-12.ndjson");
        await _containerClient.Received(1).CreateIfNotExistsAsync(cancellationToken: Arg.Any<CancellationToken>());

        await _blobClient.Received(1).OpenWriteAsync(true, Arg.Any<BlobOpenWriteOptions>(), Arg.Any<CancellationToken>());

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        using var first = JsonDocument.Parse(lines[0]);
        first.RootElement.GetProperty("RowKey").GetString().ShouldBe("job-1");
        first.RootElement.GetProperty("ScriptText").GetString().ShouldBe("Traffic is flowing.");
        using var second = JsonDocument.Parse(lines[1]);
        second.RootElement.GetProperty("Status").GetString().ShouldBe("Rejected");
    }

    [Fact]
    public async Task ExportJobsAsync_EmptyTable_StillWritesSnapshot()
    {
        SeedQuery();
        var stream = MockBlobStream();

        var result = await _sut.ExportJobsAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
        await _blobClient.Received(1).OpenWriteAsync(true, Arg.Any<BlobOpenWriteOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportJobsAsync_SameDayReRun_Overwrites()
    {
        SeedQuery(new TableEntity("Job", "job-1"));
        MockBlobStream();

        await _sut.ExportJobsAsync();

        // OpenWriteAsync must be called with overwrite: true
        await _blobClient.Received(1).OpenWriteAsync(
            true, Arg.Any<BlobOpenWriteOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportJobsAsync_TableQueryFails_ReturnsFailure()
    {
        // OpenWriteAsync is called before the query; must be stubbed even on failure path
        MockBlobStream();

        _tableClient.QueryAsync<TableEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException("table unavailable"));

        var result = await _sut.ExportJobsAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("table unavailable");
    }

    [Fact]
    public async Task ExportJobsAsync_BlobOpenFails_ReturnsFailure()
    {
        SeedQuery(new TableEntity("Job", "job-1"));
        _blobClient.OpenWriteAsync(Arg.Any<bool>(), Arg.Any<BlobOpenWriteOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException("blob unavailable"));

        var result = await _sut.ExportJobsAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("blob unavailable");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
