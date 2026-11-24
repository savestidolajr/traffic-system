using System.Security.Cryptography;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Interfaces;
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

public class ZettaFileDeliveryServiceTests
{
    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly ILogger<ZettaFileDeliveryService> _logger = Substitute.For<ILogger<ZettaFileDeliveryService>>();
    private readonly IDeliveryFileWriter _fileWriter = Substitute.For<IDeliveryFileWriter>();

    private readonly StorageOptions _storageOptions = new() { AccountName = "test", ContainerName = "broadcasts" };

    private readonly ZettaFileDeliveryService _sut;
    private readonly ZettaOptions _zettaOptions;

    public ZettaFileDeliveryServiceTests()
    {
        _zettaOptions = new ZettaOptions
        {
            Enabled = true,
            DropAccountName = "ibpauetststaftp01",
            DropFileShareName = "informationbulletins",
            FileNamePattern = "{MarketId}_TRAIN_TRAFF",
            MarketCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sydney"] = "SYD",
                ["melbourne"] = "MEL",
                ["brisbane"] = "BNE",
                ["adelaide"] = "ADL",
                ["perth"] = "PER",
                ["goldcoast"] = "GLD"
            }
        };

        _blobServiceClient.GetBlobContainerClient(Arg.Any<string>()).Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);
        _fileWriter.WriteAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new ZettaFileDeliveryService(
            _blobServiceClient,
            Options.Create(_storageOptions),
            Options.Create(_zettaOptions),
            _fileWriter,
            TimeProvider.System,
            _logger);
    }

    /// <summary>
    ///     A market with no configured Zetta code must fail delivery, never fall back to the market
    ///     id. The old fallback would have shipped `CAIRNS-4CA_TRAIN_TRAFF.wav` for the second pilot
    ///     station against a convention proven as `SYD_TRAIN_TRAFF.wav` — a file Zetta was never
    ///     told to expect, which does not air and raises nothing.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_MarketWithNoConfiguredCode_FailsInsteadOfGuessingAName()
    {
        SeedBlobContent([1, 2, 3, 4]);

        var result = await _sut.DeliverAsync("broadcast.wav", "cairns-4ca", null, null, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("cairns-4ca");
        await _fileWriter.DidNotReceive()
            .WriteAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Azure rejects app-setting names containing a hyphen on Linux apps, so the pilot market
    ///     `cairns-4ca` has no expressible `Zetta__MarketCodes__*` key. The JSON map is the only way
    ///     that market can carry a code at all.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_HyphenatedMarket_TakesItsCodeFromTheJsonMap()
    {
        _zettaOptions.FileNamePattern = "{MarketId}_TRAFF";
        _zettaOptions.MarketCodesJson = """{"cairns-4ca":"[CAI]STAR1027"}""";
        SeedBlobContent([1, 2, 3, 4]);

        var result = await _sut.DeliverAsync("broadcast.wav", "cairns-4ca", null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Is("[CAI]STAR1027_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_JsonMapWins_OverAPerKeyCodeForTheSameMarket()
    {
        _zettaOptions.MarketCodesJson = """{"sydney":"[SYD]KIIS90s"}""";
        _zettaOptions.FileNamePattern = "{MarketId}_TRAFF";
        SeedBlobContent([1]);

        await _sut.DeliverAsync("broadcast.wav", "sydney");

        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Is("[SYD]KIIS90s_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_MalformedJsonMap_LeavesThePerKeyCodesIntact()
    {
        // A broken setting must not take working markets off air.
        _zettaOptions.MarketCodesJson = "{not json";
        SeedBlobContent([1]);

        var result = await _sut.DeliverAsync("broadcast.wav", "sydney");

        result.IsSuccess.ShouldBeTrue();
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Is("SYD_TRAIN_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    private void SeedBlobContent(byte[] data)
    {
        var binaryData = new BinaryData(data);
        var downloadResult = BlobsModelFactory.BlobDownloadResult(binaryData);
        var response = Substitute.For<Response<BlobDownloadResult>>();
        response.Value.Returns(downloadResult);

        _blobClient.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(response);
    }

    [Fact]
    public async Task DeliverAsync_Disabled_ReturnsSuccessWithoutDownload()
    {
        var opts = new ZettaOptions { Enabled = false };
        var sut = new ZettaFileDeliveryService(
            _blobServiceClient, Options.Create(_storageOptions),
            Options.Create(opts), _fileWriter, TimeProvider.System, _logger);

        var result = await sut.DeliverAsync("audio.wav", "sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(FileDeliveryStatus.SkippedDisabled);
        _blobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
    }

    [Fact]
    public async Task DeliverAsync_HappyPath_DownloadsFromBlobAndWritesViaDeliveryWriter()
    {
        byte[] audioBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 };
        SeedBlobContent(audioBytes);

        var result = await _sut.DeliverAsync("broadcast-123.wav", "Sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(FileDeliveryStatus.Delivered);
        await _fileWriter.Received(1).WriteAsync(
            Arg.Is<byte[]>(b => b.SequenceEqual(audioBytes)),
            Arg.Is("SYD_TRAIN_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_FileNamePattern_SubstitutesTheConfiguredCode()
    {
        SeedBlobContent([0x01]);

        var result = await _sut.DeliverAsync("audio.wav", "melbourne");

        result.IsSuccess.ShouldBeTrue();
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Is("MEL_TRAIN_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The code is ARN's own playout identifier and is written exactly as supplied. Uppercasing it
    ///     silently mangled the station part of the names ARN actually issued on 2026-07-30
    ///     (`[SYD]KIIS90s_TRAFF`, `[CAI]STAR1027_TRAFF`), and a name Zetta was not told to expect
    ///     does not air.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_MixedCaseCode_IsWrittenExactlyAsConfigured()
    {
        _zettaOptions.FileNamePattern = "{MarketId}_TRAFF";
        _zettaOptions.MarketCodes["sydney"] = "[SYD]KIIS90s";
        SeedBlobContent([0x01]);

        var result = await _sut.DeliverAsync("audio.wav", "sydney");

        result.IsSuccess.ShouldBeTrue();
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Is("[SYD]KIIS90s_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_BlobDownloadError_ReturnsFailure()
    {
        _blobClient.DownloadContentAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Blob not found"));

        var result = await _sut.DeliverAsync("missing.wav", "sydney");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Zetta delivery failed");
    }

    [Fact]
    public async Task DeliverAsync_WriteError_ReturnsFailure()
    {
        SeedBlobContent([0x01]);
        _fileWriter.WriteAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("delivery write failed"));

        var result = await _sut.DeliverAsync("audio.wav", "sydney");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Zetta delivery failed");
    }

    /// <summary>
    ///     The last line before bytes reach the live share. On 2026-08-11 a restart sweep walked 15
    ///     already-expired slots and wrote their stale audio to the production playout share. The
    ///     guards added for that live upstream in the orchestrator and the watchdog — i.e. in the
    ///     layer that made the mistake — so delivery refuses on its own as well. Deadlines here are
    ///     relative to real now, which keeps the assertions deterministic without a fake clock.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_SlotDeadlineHasPassed_RefusesAndNeverTouchesTheShare()
    {
        SeedBlobContent([0x01]);
        var expired = DateTimeOffset.UtcNow.AddHours(-1);

        var result = await _sut.DeliverAsync("audio.wav", "sydney", null, expired);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("expired slot");
        await _fileWriter.DidNotReceive().WriteAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Refused before the download, so the blob was never read either.
        _blobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
    }

    [Fact]
    public async Task DeliverAsync_SlotDeadlineStillAhead_Delivers()
    {
        SeedBlobContent([0x01]);
        var ahead = DateTimeOffset.UtcNow.AddHours(1);

        var result = await _sut.DeliverAsync("audio.wav", "sydney", null, ahead);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(FileDeliveryStatus.Delivered);
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A caller that supplies no deadline (filler cart, legacy path) still delivers — matching how
    ///     a missing SHA-256 integrity seal is treated — but the absence is warned about rather than
    ///     silently assumed safe.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_NoSlotDeadlineSupplied_DeliversSoLegacyCallersStillWork()
    {
        SeedBlobContent([0x01]);

        var result = await _sut.DeliverAsync("audio.wav", "sydney", null, null);

        result.IsSuccess.ShouldBeTrue();
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Superseded 2026-07-29. This previously asserted that an unmapped market delivered as
    ///     `DARWIN_TRAIN_TRAFF.wav` — i.e. it locked in the silent fallback as intended behaviour.
    ///     The market code is ARN's playout-side naming convention and cannot be derived from the
    ///     market id, so the correct behaviour is to refuse rather than to guess.
    /// </summary>
    [Fact]
    public async Task DeliverAsync_UnmappedMarket_RefusesRatherThanUsingTheMarketId()
    {
        SeedBlobContent([0x01]);

        var result = await _sut.DeliverAsync("audio.wav", "darwin");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("darwin");
        await _fileWriter.DidNotReceive().WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Is("DARWIN_TRAIN_TRAFF.wav"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_MatchingHash_WritesAndReturnsDelivered()
    {
        byte[] audioBytes = [0x52, 0x49, 0x46, 0x46];
        SeedBlobContent(audioBytes);
        string expectedHash = Convert.ToHexString(SHA256.HashData(audioBytes));

        var result = await _sut.DeliverAsync("broadcast-123.wav", "Sydney", expectedHash);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(FileDeliveryStatus.Delivered);
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_MismatchedHash_ReturnsFailureWithoutWriting()
    {
        byte[] audioBytes = [0x52, 0x49, 0x46, 0x46];
        SeedBlobContent(audioBytes);
        string wrongHash = Convert.ToHexString(SHA256.HashData([0xFF]));

        var result = await _sut.DeliverAsync("broadcast-123.wav", "Sydney", wrongHash);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.AudioIntegrityFailed);
        await _fileWriter.DidNotReceive().WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAsync_NullExpectedHash_WritesAndReturnsDelivered()
    {
        byte[] audioBytes = [0x52, 0x49, 0x46, 0x46];
        SeedBlobContent(audioBytes);

        var result = await _sut.DeliverAsync("broadcast-123.wav", "Sydney", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(FileDeliveryStatus.Delivered);
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // === GetTrayStatusAsync ===

    [Fact]
    public async Task GetTrayStatusAsync_Disabled_ReturnsEnabledFalseWithResolvedFileName()
    {
        var opts = new ZettaOptions
        {
            Enabled = false,
            FileNamePattern = "{MarketId}_TRAIN_TRAFF",
            MarketCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sydney"] = "SYD"
            }
        };
        var sut = new ZettaFileDeliveryService(
            _blobServiceClient, Options.Create(_storageOptions),
            Options.Create(opts), _fileWriter, TimeProvider.System, _logger);

        var result = await sut.GetTrayStatusAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enabled.ShouldBeFalse();
        result.Value.AwaitingPickup.ShouldBeFalse();
        result.Value.WrittenAtUtc.ShouldBeNull();
        result.Value.FileName.ShouldBe("SYD_TRAIN_TRAFF.wav");
        await _fileWriter.DidNotReceive().GetPendingWriteTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The swap-safety decision hinges on probing the EXACT file delivery writes — a probe
    ///     under any other name would report a tray that was never loaded (or one that already
    ///     aired) and greenlight an unsafe overwrite.
    /// </summary>
    [Fact]
    public async Task GetTrayStatusAsync_FileName_MatchesTheNameDeliverAsyncWrites()
    {
        SeedBlobContent([0x01]);
        _fileWriter.GetPendingWriteTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.None));

        var deliver = await _sut.DeliverAsync("audio.wav", "melbourne");
        deliver.IsSuccess.ShouldBeTrue();
        await _fileWriter.Received(1).WriteAsync(
            Arg.Any<byte[]>(), Arg.Is("MEL_TRAIN_TRAFF.wav"), Arg.Any<CancellationToken>());

        var status = await _sut.GetTrayStatusAsync("melbourne");

        status.IsSuccess.ShouldBeTrue();
        status.Value.FileName.ShouldBe("MEL_TRAIN_TRAFF.wav");
        await _fileWriter.Received(1).GetPendingWriteTimeAsync(
            Arg.Is("MEL_TRAIN_TRAFF.wav"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTrayStatusAsync_PendingFile_ReportsAwaitingPickupWithWriteTime()
    {
        var writtenAt = new DateTimeOffset(2026, 8, 11, 6, 14, 3, TimeSpan.Zero);
        _fileWriter.GetPendingWriteTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.Some(writtenAt)));

        var result = await _sut.GetTrayStatusAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enabled.ShouldBeTrue();
        result.Value.AwaitingPickup.ShouldBeTrue();
        result.Value.WrittenAtUtc.ShouldBe(writtenAt);
        result.Value.FileName.ShouldBe("SYD_TRAIN_TRAFF.wav");
    }

    [Fact]
    public async Task GetTrayStatusAsync_CollectedFile_ReportsNotAwaitingPickup()
    {
        _fileWriter.GetPendingWriteTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<DateTimeOffset>>.Success(Option<DateTimeOffset>.None));

        var result = await _sut.GetTrayStatusAsync("sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enabled.ShouldBeTrue();
        result.Value.AwaitingPickup.ShouldBeFalse();
        result.Value.WrittenAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GetTrayStatusAsync_MarketWithNoConfiguredCode_FailsInsteadOfGuessingAName()
    {
        var result = await _sut.GetTrayStatusAsync("darwin");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("darwin");
        await _fileWriter.DidNotReceive().GetPendingWriteTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTrayStatusAsync_WriterProbeFailure_ReturnsFailure()
    {
        _fileWriter.GetPendingWriteTimeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<DateTimeOffset>>.Failure("drop unreachable"));

        var result = await _sut.GetTrayStatusAsync("sydney");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Zetta tray probe failed");
    }
}

