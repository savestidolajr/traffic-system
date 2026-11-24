using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class BlobConfigImportStoreTests
{
    private readonly BlobServiceClient _service = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _container = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blob = Substitute.For<BlobClient>();
    private readonly Response _response = Substitute.For<Response>();

    private BlobConfigImportStore CreateSut(bool indexDisabled = false)
    {
        _service.GetBlobContainerClient("config-imports").Returns(_container);
        _container.GetBlobClient(Arg.Any<string>()).Returns(_blob);
        _container.CreateIfNotExistsAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<Response<BlobContainerInfo>>()));
        return new BlobConfigImportStore(
            _service,
            Options.Create(new ConfigImportOptions { ContainerName = "config-imports", IndexDisabled = indexDisabled }),
            TimeProvider.System,
            NullLogger<BlobConfigImportStore>.Instance);
    }

    private static PendingConfigImport Pending(string id = "pending-1") => new(
        id, "Requester", "object-1", new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc),
        "{\"Rows\":[]}", "{\"Schedules\":[]}");

    [Fact]
    public async Task ListPendingAsync_IndexMetadataAvoidsPendingBodyDownload()
    {
        var sut = CreateSut();
        var import = Pending();
        var indexItem = BlobsModelFactory.BlobItem(
            name: "index/pending-1.json",
            metadata: new Dictionary<string, string>
            {
                ["Id"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(import.Id)),
                ["RequestedBy"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(import.RequestedBy)),
                ["RequestedByObjectId"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(import.RequestedByObjectId)),
                ["RequestedAtUtc"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(import.RequestedAtUtc.ToString("O"))),
                ["ReportJson"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(import.ReportJson))
            });
        ConfigureListing(["pending/pending-1.json"], [indexItem]);

        var result = await sut.ListPendingAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].Id.ShouldBe(import.Id);
        await _blob.DidNotReceive().DownloadContentAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListPendingAsync_LegacyBodyIsDiscoveredAndBackfillsIndex()
    {
        var sut = CreateSut();
        var import = Pending();
        ConfigureListing(["pending/pending-1.json"], []);
        var bodyResponse = Substitute.For<Response<BlobDownloadResult>>();
        bodyResponse.Value.Returns(BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(
            System.Text.Json.JsonSerializer.Serialize(import))));
        _blob.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(bodyResponse);

        var result = await sut.ListPendingAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().Id.ShouldBe(import.Id);
        await _blob.Received(1).UploadAsync(
            Arg.Any<BinaryData>(), Arg.Is<BlobUploadOptions>(o => o.Metadata.ContainsKey("Id")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_DeletesPendingBodyAndIndex()
    {
        var sut = CreateSut();
        _blob.ExistsAsync(Arg.Any<CancellationToken>()).Returns(Response.FromValue(true, _response));
        var bodyResponse = Substitute.For<Response<BlobDownloadResult>>();
        bodyResponse.Value.Returns(BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(
            System.Text.Json.JsonSerializer.Serialize(Pending()))));
        _blob.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(bodyResponse);
        _blob.DeleteAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_response));
        _blob.DeleteIfExistsAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(true, _response)));

        var result = await sut.ResolveAsync("pending-1", "applied", "Approver", "object-2");

        result.IsSuccess.ShouldBeTrue();
        await _blob.Received(1).DeleteAsync(cancellationToken: Arg.Any<CancellationToken>());
        await _blob.Received(1).DeleteIfExistsAsync(cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListPendingAsync_IndexDisabledReadsLegacyBodiesOnly()
    {
        var sut = CreateSut(indexDisabled: true);
        var import = Pending();
        ConfigureListing(["pending/pending-1.json"], []);
        var bodyResponse = Substitute.For<Response<BlobDownloadResult>>();
        bodyResponse.Value.Returns(BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(
            System.Text.Json.JsonSerializer.Serialize(import))));
        _blob.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(bodyResponse);

        var result = await sut.ListPendingAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().Id.ShouldBe(import.Id);
        await _blob.DidNotReceive().UploadAsync(
            Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>());
    }

    private void ConfigureListing(IReadOnlyList<string> pendingNames, IReadOnlyList<BlobItem> indexItems)
    {
        var pending = new TestAsyncPageable(
            pendingNames.Select(name => BlobsModelFactory.BlobItem(name: name)).ToList(), _response);
        var indexes = new TestAsyncPageable(indexItems, _response);
        _container.GetBlobsAsync(Arg.Any<BlobTraits>(), Arg.Any<BlobStates>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<BlobTraits>(0) == BlobTraits.Metadata ? indexes : pending);
    }

    private sealed class TestAsyncPageable(IReadOnlyList<BlobItem> items, Response response) : AsyncPageable<BlobItem>
    {
        public override async IAsyncEnumerable<Page<BlobItem>> AsPages(
            string? continuationToken = null, int? pageSizeHint = null)
        {
            await Task.CompletedTask;
            yield return Page<BlobItem>.FromValues(items, null, response);
        }
    }
}
