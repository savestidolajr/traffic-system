using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Output;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Output;

public sealed class PreviewAudioCleanupServiceTests
{
    [Theory]
    [InlineData("synthesize_20260902_120000.wav")]
    [InlineData("audition_job-1_20260902_120000_000.wav")]
    [InlineData("voicepreview_20260902_120000_000.wav")]
    [InlineData("voiceremix_20260902_120000_000.wav")]
    [InlineData("deliverypreview_20260902_120000_000.wav")]
    [InlineData("pronunciationpreview_20260902_120000_000.wav")]
    [InlineData("pronunciationrulepreview_20260902_120000_000.wav")]
    public void IsPreviewFileName_RecognisesOwnedPrefixes(string name)
    {
        PreviewAudioCleanupService.IsPreviewFileName(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("broadcast_20260902_120000.wav")]
    [InlineData("filler-cart.wav")]
    [InlineData("preview_20260902_120000.wav")]
    [InlineData("synthesize_20260902_120000.mp3")]
    [InlineData("../synthesize_20260902_120000.wav")]
    public void IsPreviewFileName_RejectsNonPreviewOrUnsafeNames(string name)
    {
        PreviewAudioCleanupService.IsPreviewFileName(name).ShouldBeFalse();
    }

    [Fact]
    public void Options_NeverAllowRetentionBelowSevenDays()
    {
        var options = new PreviewAudioCleanupOptions { RetentionDays = 1 };

        options.EffectiveRetentionDays.ShouldBe(7);
    }

    [Fact]
    public void Options_ClampRunAndPinBoundsToSafeMinimums()
    {
        var options = new PreviewAudioCleanupOptions
        {
            MaxBlobsPerRun = 0,
            ActiveUrlPinHours = 0
        };

        options.EffectiveMaxBlobsPerRun.ShouldBe(1);
        options.EffectiveActiveUrlPinDuration.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task PinAsync_StoresManagedPinAndTimestampTags()
    {
        var blobService = Substitute.For<BlobServiceClient>();
        var container = Substitute.For<BlobContainerClient>();
        var blob = Substitute.For<BlobClient>();
        var response = Substitute.For<Response>();
        var now = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
        var tags = new Dictionary<string, string> { ["existing"] = "keep" };
        var tagResult = BlobsModelFactory.GetBlobTagResult(tags);

        blobService.GetBlobContainerClient("broadcasts").Returns(container);
        container.GetBlobClient("voicepreview_20260902_120000_000.wav").Returns(blob);
        blob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, response));
        blob.GetTagsAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(tagResult, response));

        var sut = CreateSut(blobService, now);
        var result = await sut.PinAsync(
            "https://storage.test/broadcasts/voicepreview_20260902_120000_000.wav");

        result.IsSuccess.ShouldBeTrue();
        await blob.Received(1).SetTagsAsync(
            Arg.Is<IDictionary<string, string>>(value =>
                value["existing"] == "keep"
                && value["previewManaged"] == "true"
                && value["previewManagedAt"].StartsWith("2026-09-02T12:00:00")
                && value["previewPinnedUntil"].StartsWith("2026-09-02T13:00:00")),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupAsync_DeletesOnlyExpiredUnpinnedManagedPreviews_AndIsIdempotent()
    {
        var blobService = Substitute.For<BlobServiceClient>();
        var container = Substitute.For<BlobContainerClient>();
        var response = Substitute.For<Response>();
        var now = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
        var expired = Preview(
            "synthesize_expired.wav",
            now.AddDays(-8),
            now.AddHours(-1));
        var pinned = Preview(
            "voicepreview_pinned.wav",
            now.AddDays(-8),
            now.AddHours(1));
        var fresh = Preview(
            "audition_fresh.wav",
            now.AddDays(-1),
            now.AddHours(-1));
        var unmanaged = BlobsModelFactory.BlobItem(
            name: "voiceremix_unmanaged.wav",
            properties: BlobsModelFactory.BlobItemProperties(
                accessTierInferred: false,
                lastModified: now.AddDays(-8)));

        blobService.GetBlobContainerClient("broadcasts").Returns(container);
        container.GetBlobsAsync(
                Arg.Any<BlobTraits>(), Arg.Any<BlobStates>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new TestAsyncPageable(
                [expired, pinned, fresh, unmanaged], response));

        var sut = CreateSut(blobService, now);
        var first = await sut.CleanupAsync();
        var second = await sut.CleanupAsync();

        first.IsSuccess.ShouldBeTrue();
        first.Value.Deleted.ShouldBe(1);
        first.Value.Pinned.ShouldBe(1);
        first.Value.Skipped.ShouldBe(2);
        second.IsSuccess.ShouldBeTrue();
        await container.Received(2).DeleteBlobIfExistsAsync(
            "synthesize_expired.wav",
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    private static BlobItem Preview(
        string name, DateTimeOffset managedAt, DateTimeOffset pinUntil)
    {
        var tags = new Dictionary<string, string>
        {
            ["previewManaged"] = "true",
            ["previewManagedAt"] = managedAt.ToString("O"),
            ["previewPinnedUntil"] = pinUntil.ToString("O")
        };
        return BlobsModelFactory.BlobItem(
            name: name,
            properties: BlobsModelFactory.BlobItemProperties(
                accessTierInferred: false,
                lastModified: managedAt),
            tags: tags);
    }

    private static PreviewAudioCleanupService CreateSut(
        BlobServiceClient blobService,
        DateTimeOffset now)
        => new(
            blobService,
            Options.Create(new StorageOptions { ContainerName = "broadcasts" }),
            Options.Create(new PreviewAudioCleanupOptions()),
            new FixedTimeProvider(now),
            NullLogger<PreviewAudioCleanupService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestAsyncPageable(
        IReadOnlyList<BlobItem> items,
        Response response) : AsyncPageable<BlobItem>
    {
        public override async IAsyncEnumerable<Page<BlobItem>> AsPages(
            string? continuationToken = null,
            int? pageSizeHint = null)
        {
            await Task.CompletedTask;
            yield return Page<BlobItem>.FromValues(items, null, response);
        }
    }
}
