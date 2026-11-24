using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Output;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public sealed class BlobAudioUrlProviderTests
{
    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly ILogger<BlobAudioUrlProvider> _logger = Substitute.For<ILogger<BlobAudioUrlProvider>>();

    private BlobAudioUrlProvider CreateSut(
        string accountName = "devstoreaccount1",
        string containerName = "broadcasts",
        BlobAudioCacheOptions? cacheOptions = null,
        TimeProvider? timeProvider = null)
    {
        _blobServiceClient.AccountName.Returns(accountName);
        _blobServiceClient.GetBlobContainerClient(containerName).Returns(_containerClient);
        _containerClient.Uri.Returns(new Uri($"https://{accountName}.blob.core.windows.net/{containerName}"));
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);
        _blobClient.Uri.Returns(new Uri($"https://{accountName}.blob.core.windows.net/{containerName}/test.wav"));

        var options = Options.Create(new StorageOptions
        {
            AccountName = accountName,
            ContainerName = containerName
        });
        return new BlobAudioUrlProvider(_blobServiceClient, options, _logger,
            cacheOptions is null ? null : Options.Create(cacheOptions), timeProvider);
    }

    private void SetupBlobExists(bool exists = true)
    {
        var existsResponse = Substitute.For<Response<bool>>();
        existsResponse.Value.Returns(exists);
        _blobClient.ExistsAsync(Arg.Any<CancellationToken>()).Returns(existsResponse);
    }

    [Fact]
    public async Task GetAudioUrlAsync_EmptyFileName_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.GetAudioUrlAsync("");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("empty");
    }

    [Fact]
    public async Task GetAudioUrlAsync_NullFileName_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.GetAudioUrlAsync(null!);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAudioUrlAsync_BlobDoesNotExist_ReturnsFailure()
    {
        var sut = CreateSut();
        SetupBlobExists(false);

        var result = await sut.GetAudioUrlAsync("missing.wav");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task GetAudioUrlAsync_LocalDev_BlobExists_ReturnsSuccessWithSasUrl()
    {
        var sut = CreateSut(StorageOptions.LocalEmulatorAccount);
        SetupBlobExists();

        var result = await sut.GetAudioUrlAsync("test.wav");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain("test.wav");
        result.Value.ShouldContain("sig="); // SAS token present
    }

    [Fact]
    public async Task GetAudioUrlAsync_RepeatedReads_ReuseExistenceMetadata()
    {
        var sut = CreateSut();
        SetupBlobExists();

        var first = await sut.GetAudioUrlAsync("test.wav");
        var second = await sut.GetAudioUrlAsync("test.wav");

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        await _blobClient.Received(1).ExistsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAudioUrlAsync_MetadataCacheCanBeDisabledIndependently()
    {
        var sut = CreateSut(cacheOptions: new BlobAudioCacheOptions
        {
            MetadataCacheEnabled = false,
            DelegationKeyCacheEnabled = true
        });
        SetupBlobExists();

        (await sut.GetAudioUrlAsync("test.wav")).IsSuccess.ShouldBeTrue();
        (await sut.GetAudioUrlAsync("test.wav")).IsSuccess.ShouldBeTrue();

        await _blobClient.Received(2).ExistsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAudioUrlAsync_UsesConfiguredContainerName()
    {
        var sut = CreateSut(containerName: "my-audio");
        SetupBlobExists(false);

        await sut.GetAudioUrlAsync("audio.wav");

        _blobServiceClient.Received().GetBlobContainerClient("my-audio");
    }

    [Fact]
    public async Task GetAudioUrlAsync_AbsoluteUrlOnDifferentHost_ReturnsUrlUnchanged()
    {
        var sut = CreateSut();
        const string externalUrl = "https://cdn.example.com/broadcasts/filler-cart.wav";

        var result = await sut.GetAudioUrlAsync(externalUrl);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(externalUrl);
        _containerClient.DidNotReceive().GetBlobClient(Arg.Any<string>());
    }

    [Fact]
    public async Task GetAudioUrlAsync_AbsoluteUrlOnSameHostDifferentContainer_ReturnsUrlUnchanged()
    {
        var sut = CreateSut();
        const string otherContainerUrl =
            "https://devstoreaccount1.blob.core.windows.net/other-container/filler-cart.wav";

        var result = await sut.GetAudioUrlAsync(otherContainerUrl);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(otherContainerUrl);
        _containerClient.DidNotReceive().GetBlobClient(Arg.Any<string>());
    }

    [Fact]
    public async Task GetAudioUrlAsync_AbsoluteUrlIntoOwnContainer_ExtractsBlobName()
    {
        var sut = CreateSut();
        SetupBlobExists();

        var result = await sut.GetAudioUrlAsync(
            "https://devstoreaccount1.blob.core.windows.net/broadcasts/filler-cart.wav");

        result.IsSuccess.ShouldBeTrue();
        _containerClient.Received().GetBlobClient("filler-cart.wav");
    }

    [Fact]
    public async Task GetAudioUrlAsync_AbsoluteUrlWithEncodedBlobName_ExtractsDecodedName()
    {
        var sut = CreateSut();
        SetupBlobExists(false);

        var result = await sut.GetAudioUrlAsync(
            "https://devstoreaccount1.blob.core.windows.net/broadcasts/filler%20cart.wav");

        _containerClient.Received().GetBlobClient("filler cart.wav");
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("filler cart.wav");
    }

    [Fact]
    public async Task OpenAudioStreamAsync_RangeStartBeyondEndOfFile_ReturnsNotSatisfiableFailure()
    {
        var sut = CreateSut();
        var propertiesResponse = Substitute.For<Response<BlobProperties>>();
        propertiesResponse.Value.Returns(
            BlobsModelFactory.BlobProperties(contentLength: 10, contentType: "audio/wav"));
        _blobClient.GetPropertiesAsync(Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(propertiesResponse);

        var result = await sut.OpenAudioStreamAsync("test.wav", new AudioByteRange(999_999_999, null));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not satisfiable");
        await _blobClient.DidNotReceive().DownloadStreamingAsync(
            Arg.Any<BlobDownloadOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAudioStreamAsync_RangeWithinBounds_ReturnsPartialSuccess()
    {
        var sut = CreateSut();
        var propertiesResponse = Substitute.For<Response<BlobProperties>>();
        propertiesResponse.Value.Returns(
            BlobsModelFactory.BlobProperties(contentLength: 1_000, contentType: "audio/wav"));
        _blobClient.GetPropertiesAsync(Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(propertiesResponse);

        var downloadResponse = Substitute.For<Response<BlobDownloadStreamingResult>>();
        downloadResponse.Value.Returns(
            BlobsModelFactory.BlobDownloadStreamingResult(content: new MemoryStream(new byte[100])));
        _blobClient.DownloadStreamingAsync(Arg.Any<BlobDownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(downloadResponse);

        var result = await sut.OpenAudioStreamAsync("test.wav", new AudioByteRange(0, 99));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RangeStart.ShouldBe(0);
        result.Value.ContentLength.ShouldBe(100);
        result.Value.TotalLength.ShouldBe(1_000);
        result.Value.IsPartial.ShouldBeTrue();
    }

    [Fact]
    public async Task OpenAudioStreamAsync_RepeatedReads_ReuseBlobProperties()
    {
        var sut = CreateSut();
        var propertiesResponse = Substitute.For<Response<BlobProperties>>();
        propertiesResponse.Value.Returns(
            BlobsModelFactory.BlobProperties(contentLength: 10, contentType: "audio/wav"));
        _blobClient.GetPropertiesAsync(Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(propertiesResponse);

        var downloadResponse = Substitute.For<Response<BlobDownloadStreamingResult>>();
        downloadResponse.Value.Returns(
            BlobsModelFactory.BlobDownloadStreamingResult(content: new MemoryStream(new byte[10])));
        _blobClient.DownloadStreamingAsync(
            options: Arg.Any<BlobDownloadOptions>(),
            cancellationToken: Arg.Any<CancellationToken>()).Returns(downloadResponse);

        var first = await sut.OpenAudioStreamAsync("test.wav", null);
        var second = await sut.OpenAudioStreamAsync("test.wav", null);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        await _blobClient.Received(1).GetPropertiesAsync(
            Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await _blobClient.Received(2).DownloadStreamingAsync(
            options: Arg.Any<BlobDownloadOptions>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAudioUrlAsync_RepeatedAzureReads_ReuseDelegationKey()
    {
        var sut = CreateSut(accountName: "prodaccount");
        SetupBlobExists();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var key = BlobsModelFactory.UserDelegationKey(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            now.AddMinutes(-10),
            now.AddHours(2),
            "b",
            "2020-12-06",
            Convert.ToBase64String(new byte[32]));
        var keyResponse = Substitute.For<Response<UserDelegationKey>>();
        keyResponse.Value.Returns(key);
        _blobServiceClient.GetUserDelegationKeyAsync(
                Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(keyResponse);

        var first = await sut.GetAudioUrlAsync("test.wav");
        var second = await sut.GetAudioUrlAsync("test.wav");

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        await _blobServiceClient.Received(1).GetUserDelegationKeyAsync(
            Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _blobClient.Received(1).ExistsAsync(Arg.Any<CancellationToken>());
    }
}
