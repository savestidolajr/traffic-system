using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Diagnostics;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Diagnostics;

public sealed class BlobLlmCallCaptureTests
{
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _containerClient = Substitute.For<BlobContainerClient>();
    private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
    private readonly ILogger<BlobLlmCallCapture> _logger = Substitute.For<ILogger<BlobLlmCallCapture>>();
    private readonly Response _response = Substitute.For<Response>();

    private BlobLlmCallCapture CreateSut(bool captureEnabled = true, int maxCapturedJobs = 20)
    {
        // BlobServiceClient is resolved lazily via IServiceProvider.GetRequiredService<T>() —
        // mock the underlying IServiceProvider.GetService(typeof(T)) call it delegates to.
        _serviceProvider.GetService(typeof(BlobServiceClient)).Returns(_blobServiceClient);
        _blobServiceClient.GetBlobContainerClient("llm-call-captures").Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);
        return new BlobLlmCallCapture(
            _serviceProvider,
            Options.Create(new DebugOptions { CaptureLlmCalls = captureEnabled, MaxCapturedJobs = maxCapturedJobs }),
            _logger);
    }

    private static LlmCallRecord CreateRecord(string? jobId = "job-abc") => new(
        JobId: jobId,
        Stage: "Draft",
        Attempt: 1,
        Model: "gpt-4o",
        SystemInstructions: "You are a traffic bulletin writer.",
        RenderedPrompt: "Write a 30s bulletin for Sydney.",
        RequestBody: "{\"model\":\"gpt-4o\"}",
        ResponseBody: "{\"output\":[{\"content\":\"Heavy on M4\"}]}",
        ElapsedMs: 1234,
        TimestampUtc: new DateTimeOffset(2025, 6, 15, 12, 34, 56, 789, TimeSpan.Zero));

    /// <summary>
    ///     Helper to create an AsyncPageable&lt;BlobItem&gt; from an in-memory list.
    ///     BlobItem is constructed via BlobsModelFactory (the standard Azure SDK test factory);
    ///     Response is NSubstituted since Azure.Response is abstract with many members.
    /// </summary>
    private AsyncPageable<BlobItem> ToAsyncPageable(IReadOnlyList<BlobItem> items)
        => new TestAsyncPageable(items, _response);

    /// <summary>
    ///     Minimal AsyncPageable implementation that yields one page from an in-memory list.
    ///     Used because Azure's AsyncPageable&lt;T&gt; has no public constructor and GetAsyncEnumerator
    ///     is not virtual, so NSubstitute cannot mock it directly.
    /// </summary>
    private sealed class TestAsyncPageable : AsyncPageable<BlobItem>
    {
        private readonly IReadOnlyList<BlobItem> _items;
        private readonly Response _response;
        public TestAsyncPageable(IReadOnlyList<BlobItem> items, Response response) { _items = items; _response = response; }
        public override async IAsyncEnumerable<Page<BlobItem>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            await Task.CompletedTask;
            yield return Page<BlobItem>.FromValues(_items, null, _response);
        }
    }

    private static BlobItem MakeBlobItem(string name, DateTimeOffset lastModified)
        => BlobsModelFactory.BlobItem(
            name: name,
            properties: BlobsModelFactory.BlobItemProperties(
                accessTierInferred: true,
                lastModified: lastModified));

    // ── Contract 1: Off-switch is a true no-op ─────────────────────────────

    [Fact]
    public async Task CaptureAsync_CaptureDisabled_DoesNotTouchBlobStorage()
    {
        var sut = CreateSut(captureEnabled: false);
        await sut.CaptureAsync(CreateRecord());

        _blobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
        _serviceProvider.DidNotReceive().GetService(Arg.Any<Type>());

        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o!.ToString()!.Contains("[LlmCallMetadata]")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task CaptureAsync_Enabled_ReturnsBeforeBlobUploadCompletes()
    {
        var uploadRelease = new TaskCompletionSource<Response<BlobContentInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _blobClient.UploadAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<BlobUploadOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(uploadRelease.Task);

        var sut = CreateSut();
        Task admission = sut.CaptureAsync(CreateRecord());

        admission.IsCompletedSuccessfully.ShouldBeTrue();
        sut.PendingCount.ShouldBe(1);
        uploadRelease.Task.IsCompleted.ShouldBeFalse();

        uploadRelease.SetResult(Substitute.For<Response<BlobContentInfo>>());
        await sut.FlushAsync();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task CaptureAsync_DebugSwitchOn_CapturesEvenWhenCaptureOptionsAreBoundAndOff()
    {
        // Both options objects are always bound in the host. Dev opted in through Debug:CaptureLlmCalls
        // and lost every capture for 17 hours when a bound-but-false LlmCapture:FullPayloads shadowed it.
        _serviceProvider.GetService(typeof(BlobServiceClient)).Returns(_blobServiceClient);
        _blobServiceClient.GetBlobContainerClient("llm-call-captures").Returns(_containerClient);
        _containerClient.GetBlobClient(Arg.Any<string>()).Returns(_blobClient);
        _blobClient.UploadAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<BlobUploadOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<Response<BlobContentInfo>>()));
        var sut = new BlobLlmCallCapture(
            _serviceProvider,
            Options.Create(new DebugOptions { CaptureLlmCalls = true, MaxCapturedJobs = 20 }),
            _logger,
            Options.Create(new LlmCaptureOptions { FullPayloads = false }));

        await sut.CaptureAsync(CreateRecord());
        await sut.FlushAsync();
        await sut.DisposeAsync();

        await _blobClient.Received(1).UploadAsync(
            Arg.Any<BinaryData>(),
            Arg.Any<BlobUploadOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaptureAsync_QueueOverflow_DropsWithoutBlockingAndLogsDrop()
    {
        var uploadStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var uploadRelease = new TaskCompletionSource<Response<BlobContentInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _blobClient.When(b => b.UploadAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<BlobUploadOptions>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => uploadStarted.TrySetResult(true));
        _blobClient.UploadAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<BlobUploadOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(uploadRelease.Task);

        var sut = CreateSut();
        await sut.CaptureAsync(CreateRecord(jobId: "job-in-flight"));
        await uploadStarted.Task;

        for (int index = 0; index < LlmCaptureOptions.QueueCapacity; index++)
            await sut.CaptureAsync(CreateRecord(jobId: $"job-{index}"));

        Task overflowAdmission = sut.CaptureAsync(CreateRecord(jobId: "job-overflow"));
        overflowAdmission.IsCompletedSuccessfully.ShouldBeTrue();
        sut.DroppedCount.ShouldBe(1);

        uploadRelease.SetResult(Substitute.For<Response<BlobContentInfo>>());
        await sut.FlushAsync();
        await sut.DisposeAsync();

        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o!.ToString()!.Contains("queue is full or closed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ── Contract 2: Null / empty / whitespace JobId is skipped ─────────────

    [Fact]
    public async Task CaptureAsync_NullJobId_SkipsCapture()
    {
        var sut = CreateSut();
        await sut.CaptureAsync(CreateRecord(jobId: null));

        _blobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
    }

    [Fact]
    public async Task CaptureAsync_EmptyJobId_SkipsCapture()
    {
        var sut = CreateSut();
        await sut.CaptureAsync(CreateRecord(jobId: ""));

        _blobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
    }

    [Fact]
    public async Task CaptureAsync_WhitespaceJobId_SkipsCapture()
    {
        var sut = CreateSut();
        await sut.CaptureAsync(CreateRecord(jobId: "   "));

        _blobServiceClient.DidNotReceive().GetBlobContainerClient(Arg.Any<string>());
    }

    // ── Contract 3: Valid capture uploads to the exact expected blob path ───

    [Fact]
    public async Task CaptureAsync_ValidRecord_UploadsToExactBlobPath()
    {
        var sut = CreateSut();
        var record = CreateRecord();
        await sut.CaptureAsync(record);
        await sut.FlushAsync();

        // Container must be "llm-call-captures"
        _blobServiceClient.Received(1).GetBlobContainerClient("llm-call-captures");

        // Blob path must be {JobId}/{HHmmss.fff}-{Stage}-attempt{Attempt}.json
        // Record: JobId="job-abc", TimestampUtc=2025-06-15T12:34:56.789+00:00, Stage="Draft", Attempt=1
        // Expected: "job-abc/123456.789-Draft-attempt1.json"
        var expectedBlobName = $"{record.JobId}/{record.TimestampUtc:HHmmss.fff}-{record.Stage}-attempt{record.Attempt}.json";
        _containerClient.Received(1).GetBlobClient(expectedBlobName);
    }

    [Fact]
    public async Task CaptureAsync_ValidRecord_UploadsCorrectJsonContent()
    {
        var sut = CreateSut();
        var record = CreateRecord();

        // Capture BinaryData during the actual call (Arg.Do inside Received() only runs
        // during verification, not during the original invocation).
        BinaryData? capturedData = null;
        _blobClient.When(b => b.UploadAsync(
                Arg.Any<BinaryData>(),
                Arg.Any<BlobUploadOptions>(),
                Arg.Any<CancellationToken>()))
            .Do(ci => capturedData = ci.Arg<BinaryData>());
        await sut.CaptureAsync(record);
        await sut.FlushAsync();

        capturedData.ShouldNotBeNull();
        var json = capturedData!.ToString();
        var deserialized = JsonSerializer.Deserialize<LlmCallRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.ShouldNotBeNull();
        deserialized!.JobId.ShouldBe(record.JobId);
        deserialized.Stage.ShouldBe(record.Stage);
        deserialized.Attempt.ShouldBe(record.Attempt);
        deserialized.RenderedPrompt.ShouldBe(record.RenderedPrompt);
        deserialized.ResponseBody.ShouldBe(record.ResponseBody);
    }

    // ── Contract 4: Fail-soft on blob storage exception ────────────────────

    [Fact]
    public async Task CaptureAsync_BlobStorageThrows_DoesNotPropagateException()
    {
        var sut = CreateSut();
        _blobServiceClient.GetBlobContainerClient(Arg.Any<string>())
            .Throws(new InvalidOperationException("Storage unavailable"));

        var exception = await Record.ExceptionAsync(() => sut.CaptureAsync(CreateRecord()));
        await sut.FlushAsync();
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task CaptureAsync_BlobStorageThrows_LogsWarning()
    {
        var sut = CreateSut();
        _blobServiceClient.GetBlobContainerClient(Arg.Any<string>())
            .Throws(new InvalidOperationException("Storage unavailable"));

        await sut.CaptureAsync(CreateRecord());
        await sut.FlushAsync();

        // Verify logger received a Warning-level log containing the capture failure message.
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o!.ToString()!.Contains("[LlmCallCapture]")),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ── Contract 5 & 6: Pruning keeps newest MaxCapturedJobs folders ──────

    /// <summary>
    ///     Full pruning test using BlobsModelFactory + TestAsyncPageable.
    ///     Sets MaxCapturedJobs = 2 with 3 job folders. After pruning, only the 2 newest
    ///     folders should be retained; DeleteBlobIfExistsAsync is called for blobs under
    ///     the oldest (pruned) folder only.
    /// </summary>
    [Fact]
    public async Task CaptureAsync_ExceedsMaxCapturedJobs_PrunesOldestJobFolder()
    {
        var sut = CreateSut(maxCapturedJobs: 2);

        // Record for the capture that triggers pruning
        var record = CreateRecord(jobId: "job-new");
        var triggerRecord = CreateRecord(jobId: "job-new");
        await sut.CaptureAsync(record);
        await sut.FlushAsync();

        // --- Set up the pruning list returned by GetBlobsAsync ---
        // 3 job folders: job-old (oldest), job-mid (middle), job-new (newest — just uploaded)
        var allBlobs = new List<BlobItem>
        {
            MakeBlobItem("job-old/100000.000-Draft-attempt1.json",
                new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero)),
            MakeBlobItem("job-old/100100.000-Draft-attempt2.json",
                new DateTimeOffset(2025, 6, 15, 10, 1, 0, TimeSpan.Zero)),
            MakeBlobItem("job-mid/110000.000-Draft-attempt1.json",
                new DateTimeOffset(2025, 6, 15, 11, 0, 0, TimeSpan.Zero)),
            MakeBlobItem("job-mid/110100.000-Review-attempt1.json",
                new DateTimeOffset(2025, 6, 15, 11, 1, 0, TimeSpan.Zero)),
            MakeBlobItem("job-new/120000.000-Draft-attempt1.json",
                new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)),
        };

        // Blobs under the oldest folder (job-old) — these should be deleted
        var oldJobBlobs = allBlobs.Where(b => b.Name.StartsWith("job-old/")).ToList();

        // First call (prefix=null): return ALL blobs for folder discovery
        // Second+ calls (prefix="job-old/"): return only job-old blobs for deletion
        _containerClient.GetBlobsAsync(
            BlobTraits.None, BlobStates.None, Arg.Is<string?>(s => s == null), Arg.Any<CancellationToken>())
            .Returns(ToAsyncPageable(allBlobs));

        _containerClient.GetBlobsAsync(
            BlobTraits.None, BlobStates.None, Arg.Is<string?>(s => s == "job-old/"), Arg.Any<CancellationToken>())
            .Returns(ToAsyncPageable(oldJobBlobs));

        // Any other prefix → empty (shouldn't be called, but be safe)
        _containerClient.GetBlobsAsync(
            BlobTraits.None, BlobStates.None, Arg.Is<string?>(s => s != null && s != "job-old/"), Arg.Any<CancellationToken>())
            .Returns(ToAsyncPageable(new List<BlobItem>()));

        // Now trigger pruning by capturing another record for the same job
        await sut.CaptureAsync(triggerRecord);
        await sut.FlushAsync();

        // Verify DeleteBlobIfExistsAsync was called for blobs under job-old/
        foreach (var blob in oldJobBlobs)
        {
            await _containerClient.Received(1).DeleteBlobIfExistsAsync(
                blob.Name,
                cancellationToken: Arg.Any<CancellationToken>());
        }

        // Verify NO blobs from the 2 newest folders were deleted
        await _containerClient.DidNotReceive().DeleteBlobIfExistsAsync(
            Arg.Is<string>(name => name.StartsWith("job-mid/") || name.StartsWith("job-new/")),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
