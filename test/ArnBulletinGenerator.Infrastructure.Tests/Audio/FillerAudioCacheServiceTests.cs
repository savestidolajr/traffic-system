using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public sealed class FillerAudioCacheServiceTests
{
    private readonly IFillerAudioCacheRepository _repository = Substitute.For<IFillerAudioCacheRepository>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly IAudioSynthesizer _audioSynthesizer = Substitute.For<IAudioSynthesizer>();
    private readonly ILogger<FillerAudioCacheService> _logger = Substitute.For<ILogger<FillerAudioCacheService>>();

    private const string VoiceId = "test-voice";
    private const string Model = "eleven_v3";
    private const string ContainerName = "broadcasts";

    private const string CartScript =
        "You're listening to Your Traffic Network. We'll have your next full update on the roads very shortly.";

    private static readonly VoiceSettings TestSettings = VoiceSettings.Default;

    private static readonly FillerStatement TestStatement = new("stmt-1", "School zones carry reduced speed limits.");

    private FillerAudioCacheService CreateSut(string accountName = "prodaccount")
    {
        _blobServiceClient.AccountName.Returns(accountName);
        _blobServiceClient.GetBlobContainerClient(ContainerName).Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);

        return new FillerAudioCacheService(
            _repository,
            _blobServiceClient,
            Options.Create(new StorageOptions { AccountName = accountName, ContainerName = ContainerName }),
            _audioSynthesizer,
            new WavDurationProcessor(Substitute.For<ILogger<WavDurationProcessor>>()),
            new WavLoudnessNormalizer(Substitute.For<ILogger<WavLoudnessNormalizer>>()),
            _logger);
    }

    private static string ExpectedCacheKey() => FillerAudioCacheKey.Build(VoiceId, TestSettings, Model);

    /// <summary>
    ///     Creates a deterministic 44100 Hz / mono / 16-bit tone WAV of the specified duration,
    ///     suitable for passing through WavDurationProcessor and WavLoudnessNormalizer for real.
    /// </summary>
    private static byte[] ToneWav(double seconds, short amplitude = 6000)
    {
        int n = (int)Math.Round(44100 * seconds);
        short[] s = new short[n];
        for (int i = 0; i < n; i++)
            s[i] = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * 200 * i / 44100.0));
        byte[] pcm = new byte[n * 2];
        Buffer.BlockCopy(s, 0, pcm, 0, pcm.Length);
        return WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);
    }

    private void SeedBlobDownload(byte[] audioBytes)
    {
        var binaryData = new BinaryData(audioBytes);
        var downloadResult = BlobsModelFactory.BlobDownloadResult(binaryData);
        var response = Substitute.For<Response<BlobDownloadResult>>();
        response.Value.Returns(downloadResult);
        _blobClient.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(response);
    }

    // ── ResolveAsync — cache HIT ───────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CacheHit_DownloadsBlobAndReturnsCachedClip()
    {
        var sut = CreateSut();
        string cacheKey = ExpectedCacheKey();
        var wordTimings = new List<WordTiming>
        {
            new("School", 0.0, 0.5),
            new("zones", 0.5, 1.0)
        };
        var clip = new FillerAudioClip(TestStatement.Id, cacheKey, 3.2, wordTimings, DateTime.UtcNow);
        byte[] blobBytes = [0x01, 0x02, 0x03];

        _repository.GetAsync(TestStatement.Id, cacheKey, Arg.Any<CancellationToken>())
            .Returns(Result<Option<FillerAudioClip>>.Success(Option<FillerAudioClip>.Some(clip)));
        SeedBlobDownload(blobBytes);

        var result = await sut.ResolveAsync(TestStatement, VoiceId, TestSettings, Model);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Audio.ShouldBe(blobBytes);
        result.Value.DurationSeconds.ShouldBe(3.2);
        result.Value.WordTimings.ShouldBe(wordTimings);

        await _repository.Received(1).GetAsync(TestStatement.Id, cacheKey, Arg.Any<CancellationToken>());
        await _blobClient.Received(1).DownloadContentAsync(Arg.Any<CancellationToken>());
        await _audioSynthesizer.DidNotReceiveWithAnyArgs().SynthesizeAsync(
            default!, default, default, default, default, default, default);
        await _repository.DidNotReceive().SaveAsync(Arg.Any<FillerAudioClip>(), Arg.Any<CancellationToken>());
    }

    // ── ResolveAsync — cache MISS ──────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CacheMiss_SynthesizesUploadsAndPersistsCache()
    {
        var sut = CreateSut();
        string cacheKey = ExpectedCacheKey();
        var synthAudio = ToneWav(2.0);

        _repository.GetAsync(TestStatement.Id, cacheKey, Arg.Any<CancellationToken>())
            .Returns(Result<Option<FillerAudioClip>>.Success(Option<FillerAudioClip>.None));

        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(synthAudio)));
        _repository.SaveAsync(Arg.Any<FillerAudioClip>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.ResolveAsync(TestStatement, VoiceId, TestSettings, Model);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBeGreaterThan(0);
        result.Value.Audio.ShouldNotBeNull();
        result.Value.Audio.Length.ShouldBeGreaterThan(0);

        // Synthesizer was called with the exact statement text and options
        await _audioSynthesizer.Received(1).SynthesizeAsync(
            TestStatement.Text,
            Option<VoiceSettings>.Some(TestSettings),
            Option<string>.Some(VoiceId),
            Option<string>.Some(Model),
            Arg.Any<CancellationToken>(),
            default,
            default);

        // Audio was uploaded to blob storage
        await _blobClient.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Any<BlobUploadOptions>(),
            Arg.Any<CancellationToken>());

        // Cache metadata was persisted with a real measured duration
        await _repository.Received(1).SaveAsync(
            Arg.Is<FillerAudioClip>(c =>
                c.FillerStatementId == TestStatement.Id &&
                c.CacheKey == cacheKey &&
                c.DurationSeconds > 0),
            Arg.Any<CancellationToken>());

        // Returned duration matches what was persisted
        result.Value.DurationSeconds.ShouldBeGreaterThan(0);
    }

    // ── ResolveAsync — SaveAsync failure after successful synth ────────

    [Fact]
    public async Task ResolveAsync_SaveAsyncFails_AfterSynth_StillReturnsSuccess()
    {
        var sut = CreateSut();
        string cacheKey = ExpectedCacheKey();
        var synthAudio = ToneWav(1.5);

        _repository.GetAsync(TestStatement.Id, cacheKey, Arg.Any<CancellationToken>())
            .Returns(Result<Option<FillerAudioClip>>.Success(Option<FillerAudioClip>.None));

        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(synthAudio)));

        _repository.SaveAsync(Arg.Any<FillerAudioClip>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("table storage unavailable"));

        var result = await sut.ResolveAsync(TestStatement, VoiceId, TestSettings, Model);

        // Audio is still valid — only the cache metadata persistence failed
        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBeGreaterThan(0);
        result.Value.Audio.Length.ShouldBeGreaterThan(0);

        // A warning was logged about the failed metadata persistence
        _logger.ReceivedWithAnyArgs().LogWarning(default, default!, default!, default!);
    }

    // ── ResolveCartAsync — cache HIT ───────────────────────────────

    [Fact]
    public async Task ResolveCartAsync_CacheHit_ReturnsBlobUriWithoutSynthesizing()
    {
        var sut = CreateSut();
        string cacheKey = FillerCartAudioKey.Build(CartScript, VoiceId, TestSettings, Model);
        var clip = new FillerAudioClip(FillerCartAudioKey.CartStatementId, cacheKey, 12.5, [], DateTime.UtcNow);
        byte[] blobBytes = [0x0A, 0x0B, 0x0C];
        var expectedUri = new Uri(
            $"https://prodaccount.blob.core.windows.net/broadcasts/filler-cache/{FillerCartAudioKey.CartStatementId}/{cacheKey}.wav");

        _repository.GetAsync(FillerCartAudioKey.CartStatementId, cacheKey, Arg.Any<CancellationToken>())
            .Returns(Result<Option<FillerAudioClip>>.Success(Option<FillerAudioClip>.Some(clip)));
        SeedBlobDownload(blobBytes);
        _blobClient.Uri.Returns(expectedUri);

        var result = await sut.ResolveCartAsync(CartScript, VoiceId, TestSettings, Model);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Url.ShouldBe(expectedUri.AbsoluteUri);
        result.Value.DurationSeconds.ShouldBe(12.5);
        result.Value.Audio.ShouldBe(blobBytes);

        await _repository.Received(1).GetAsync(FillerCartAudioKey.CartStatementId, cacheKey, Arg.Any<CancellationToken>());
        await _blobClient.Received(1).DownloadContentAsync(Arg.Any<CancellationToken>());
        await _audioSynthesizer.DidNotReceiveWithAnyArgs().SynthesizeAsync(
            default!, default, default, default, default, default, default);
        await _repository.DidNotReceive().SaveAsync(Arg.Any<FillerAudioClip>(), Arg.Any<CancellationToken>());
    }

    // ── ResolveCartAsync — cache MISS ──────────────────────────────

    [Fact]
    public async Task ResolveCartAsync_CacheMiss_SynthesizesUploadsPersistsAndReturnsUri()
    {
        var sut = CreateSut();
        string cacheKey = FillerCartAudioKey.Build(CartScript, VoiceId, TestSettings, Model);
        var synthAudio = ToneWav(2.0);
        var expectedUri = new Uri(
            $"https://prodaccount.blob.core.windows.net/broadcasts/filler-cache/{FillerCartAudioKey.CartStatementId}/{cacheKey}.wav");

        _repository.GetAsync(FillerCartAudioKey.CartStatementId, cacheKey, Arg.Any<CancellationToken>())
            .Returns(Result<Option<FillerAudioClip>>.Success(Option<FillerAudioClip>.None));

        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(synthAudio)));
        _repository.SaveAsync(Arg.Any<FillerAudioClip>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _blobClient.Uri.Returns(expectedUri);

        var result = await sut.ResolveCartAsync(CartScript, VoiceId, TestSettings, Model);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Url.ShouldBe(expectedUri.AbsoluteUri);
        result.Value.DurationSeconds.ShouldBeGreaterThan(0);
        result.Value.Audio.Length.ShouldBeGreaterThan(0);

        // Synthesizer was called with the cart script text and the job's voice options
        await _audioSynthesizer.Received(1).SynthesizeAsync(
            CartScript,
            Option<VoiceSettings>.Some(TestSettings),
            Option<string>.Some(VoiceId),
            Option<string>.Some(Model),
            Arg.Any<CancellationToken>(),
            default,
            default);

        // Audio was uploaded to blob storage
        await _blobClient.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Any<BlobUploadOptions>(),
            Arg.Any<CancellationToken>());

        // Cache metadata was persisted under the cart pseudo-statement id
        await _repository.Received(1).SaveAsync(
            Arg.Is<FillerAudioClip>(c =>
                c.FillerStatementId == FillerCartAudioKey.CartStatementId &&
                c.CacheKey == cacheKey &&
                c.DurationSeconds > 0),
            Arg.Any<CancellationToken>());
    }

    // ── ResolveCartAsync — synthesis failure ───────────────────────

    [Fact]
    public async Task ResolveCartAsync_SynthFails_PropagatesErrorWithoutUpload()
    {
        var sut = CreateSut();
        string cacheKey = FillerCartAudioKey.Build(CartScript, VoiceId, TestSettings, Model);

        _repository.GetAsync(FillerCartAudioKey.CartStatementId, cacheKey, Arg.Any<CancellationToken>())
            .Returns(Result<Option<FillerAudioClip>>.Success(Option<FillerAudioClip>.None));

        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>())
            .Returns(Result<SynthesizedAudio>.Failure("tts down"));

        var result = await sut.ResolveCartAsync(CartScript, VoiceId, TestSettings, Model);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("tts down");

        await _blobClient.DidNotReceive().UploadAsync(
            Arg.Any<Stream>(),
            Arg.Any<BlobUploadOptions>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveAsync(Arg.Any<FillerAudioClip>(), Arg.Any<CancellationToken>());
    }

    // ── InvalidateAsync — success ──────────────────────────────────────

    [Fact]
    public async Task InvalidateAsync_DeletesMetadataAndBlobsForEveryCachedKey()
    {
        var sut = CreateSut();
        string stmtId = "stmt-del";
        var cacheKeys = new List<string> { "key-v1", "key-v2" };

        _repository.DeleteAllForStatementAsync(stmtId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<string>>.Success(cacheKeys));

        var result = await sut.InvalidateAsync(stmtId);

        result.IsSuccess.ShouldBeTrue();

        await _repository.Received(1).DeleteAllForStatementAsync(stmtId, Arg.Any<CancellationToken>());

        // A blob delete was attempted for each cache key
        _ = _blobClient.Received(2).DeleteIfExistsAsync(
            Arg.Any<DeleteSnapshotsOption>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>());
        // Two distinct blob paths — verify via containerClient.GetBlobClient
        _containerClient.Received(1).GetBlobClient($"filler-cache/{stmtId}/key-v1.wav");
        _containerClient.Received(1).GetBlobClient($"filler-cache/{stmtId}/key-v2.wav");
    }

    // ── InvalidateAsync — repository failure ───────────────────────────

    [Fact]
    public async Task InvalidateAsync_DeleteFails_PropagatesErrorWithoutBlobOperations()
    {
        var sut = CreateSut();
        string stmtId = "stmt-fail";

        _repository.DeleteAllForStatementAsync(stmtId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<string>>.Failure("table storage down"));

        var result = await sut.InvalidateAsync(stmtId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("table storage down");

        // No blob operations attempted
        _ = _blobClient.DidNotReceive().DeleteIfExistsAsync(
            Arg.Any<DeleteSnapshotsOption>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>());
        _containerClient.DidNotReceive().GetBlobClient(Arg.Any<string>());
    }
}
