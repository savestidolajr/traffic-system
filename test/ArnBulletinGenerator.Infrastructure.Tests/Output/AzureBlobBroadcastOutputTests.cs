using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Output;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public sealed class AzureBlobBroadcastOutputTests
{
    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly ILogger<AzureBlobBroadcastOutput> _logger = Substitute.For<ILogger<AzureBlobBroadcastOutput>>();

    private AzureBlobBroadcastOutput CreateSut(string accountName = "prodaccount", string containerName = "broadcasts")
    {
        _blobServiceClient.AccountName.Returns(accountName);
        _blobServiceClient.GetBlobContainerClient(containerName).Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);
        _blobClient.Name.Returns("test-id.wav");

        var options = Options.Create(new StorageOptions
        {
            AccountName = accountName,
            ContainerName = containerName
        });
        return new AzureBlobBroadcastOutput(_blobServiceClient, options, _logger);
    }

    [Fact]
    public async Task SaveAsync_Success_ReturnsSuccessResult()
    {
        var sut = CreateSut();
        byte[] audioData = new byte[] { 1, 2, 3, 4 };

        var result = await sut.SaveAsync(audioData, "test-id");

        result.IsSuccess.ShouldBeTrue();
        await _blobClient.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Any<BlobUploadOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_SetsCorrectBlobName()
    {
        var sut = CreateSut();

        await sut.SaveAsync([0xFF], "my-broadcast-123");

        _containerClient.Received(1).GetBlobClient("my-broadcast-123.wav");
    }

    [Fact]
    public async Task SaveAsync_UsesConfiguredContainerName()
    {
        var sut = CreateSut(containerName: "custom-container");

        await sut.SaveAsync([0xAA], "id1");

        _blobServiceClient.Received().GetBlobContainerClient("custom-container");
    }

    [Fact]
    public async Task SaveAsync_LocalEmulator_SetsPublicAccess()
    {
        var sut = CreateSut(StorageOptions.LocalEmulatorAccount);

        await sut.SaveAsync([0x01], "local-test");

        // Verify SetAccessPolicyAsync was called for public blob access
        await _containerClient.Received(1).SetAccessPolicyAsync(
            PublicAccessType.Blob,
            Arg.Any<IEnumerable<BlobSignedIdentifier>>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_ProductionAccount_DoesNotSetPublicAccess()
    {
        var sut = CreateSut("myazureaccount");

        await sut.SaveAsync([0x01], "prod-test");

        await _containerClient.DidNotReceive().SetAccessPolicyAsync(
            Arg.Any<PublicAccessType>(),
            Arg.Any<IEnumerable<BlobSignedIdentifier>>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_BlobUploadThrows_Propagates()
    {
        var sut = CreateSut();
        _blobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException("Blob storage unavailable"));

        var act = () => sut.SaveAsync([0x01], "fail-test");

        await Should.ThrowAsync<RequestFailedException>(act);
    }

    [Fact]
    public async Task SaveAsync_UploadsWithUnpublishedRetentionTag()
    {
        var sut = CreateSut();

        await sut.SaveAsync([0x01], "test-id");

        await _blobClient.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Is<BlobUploadOptions>(o => o.Tags != null && o.Tags["published"] == "false"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPublishedAsync_BlobExists_SetsPublishedTag()
    {
        var sut = CreateSut();
        _blobClient.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));

        var result = await sut.MarkPublishedAsync("broadcast_20260612_080000.wav");

        result.IsSuccess.ShouldBeTrue();
        _containerClient.Received(1).GetBlobClient("broadcast_20260612_080000.wav");
        await _blobClient.Received(1).SetTagsAsync(
            Arg.Is<IDictionary<string, string>>(t => t["published"] == "true"),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkPublishedAsync_BlobMissing_SucceedsWithoutTagging()
    {
        var sut = CreateSut();
        _blobClient.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(false, Substitute.For<Response>()));

        var result = await sut.MarkPublishedAsync("filler-cart-reference.wav");

        result.IsSuccess.ShouldBeTrue();
        await _blobClient.DidNotReceive().SetTagsAsync(
            Arg.Any<IDictionary<string, string>>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
