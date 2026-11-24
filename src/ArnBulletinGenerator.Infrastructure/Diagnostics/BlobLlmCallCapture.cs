using System.Text.Json;
using System.Threading.Channels;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Diagnostics;

/// <summary>
///     Captures LLM attempt metadata immediately and queues optional full prompt/request/response
///     payloads for one background blob worker. The queue is bounded so a burst cannot consume
///     unbounded memory, and a caller never waits for blob I/O. Metadata remains available when
///     full payloads are disabled or the queue is full.
///     <para>
///         Full payloads are enabled by either <see cref="LlmCaptureOptions.FullPayloads" /> or the
///         <see cref="DebugOptions.CaptureLlmCalls" /> switch. Both options are always bound in the
///         host, so neither may shadow the other: an environment that opts in through the debug
///         switch (dev replay diagnostics) keeps capturing after the capture options were introduced.
///     </para>
///     <para>
///         Resolves <see cref="BlobServiceClient" /> lazily from <paramref name="serviceProvider" />
///         on the worker, rather than taking it as a direct constructor dependency. This keeps
///         capture disabled and storage-free consumers safe.
///     </para>
/// </summary>
public sealed class BlobLlmCallCapture(
    IServiceProvider serviceProvider,
    IOptions<DebugOptions> options,
    ILogger<BlobLlmCallCapture> logger,
    IOptions<LlmCaptureOptions>? captureOptions = null) : ILlmCallCapture, IDisposable, IAsyncDisposable
{
    private const string ContainerName = "llm-call-captures";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly TaskCompletionSource<bool> CompletedIdle = CreateCompletedIdle();

    private readonly DebugOptions debugOptions = options.Value;
    private readonly bool fullPayloads =
        captureOptions?.Value.FullPayloads == true || options.Value.CaptureLlmCalls;
    private readonly Channel<LlmCallRecord> queue = Channel.CreateBounded<LlmCallRecord>(
        new BoundedChannelOptions(LlmCaptureOptions.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly object queueLock = new();
    private TaskCompletionSource<bool> idle = CompletedIdle;
    private Task? worker;
    private int pending;
    private long dropped;
    private int disposed;

    /// <summary>Number of full-payload records dropped because the bounded queue was full.</summary>
    internal long DroppedCount => Interlocked.Read(ref dropped);

    /// <summary>Number of records admitted but not yet persisted by the worker.</summary>
    internal int PendingCount
    {
        get
        {
            lock (queueLock)
                return pending;
        }
    }

    /// <summary>
    ///     Records always-on metadata synchronously through the normal application logger. The
    ///     logger is the telemetry boundary here; this method deliberately has no blob dependency.
    /// </summary>
    public void RecordMetadata(in LlmCallMetadata metadata)
    {
        try
        {
            logger.LogInformation(
                "[LlmCallMetadata] JobId={JobId}, Stage={Stage}, Model={Model}, Attempt={Attempt}, " +
                "Status={StatusCode}, Success={Success}, ViaFallback={ViaFallback}, ElapsedMs={ElapsedMs}, " +
                "StartedAtUtc={StartedAtUtc}, CompletedAtUtc={CompletedAtUtc}, DeadlineUtc={DeadlineUtc}, " +
                "ResponseId={ResponseId}, InputTokens={InputTokens}, CachedInputTokens={CachedInputTokens}, " +
                "OutputTokens={OutputTokens}, ReasoningTokens={ReasoningTokens}, Error={Error}",
                metadata.JobId,
                metadata.Stage,
                metadata.Model,
                metadata.Attempt,
                metadata.StatusCode,
                metadata.Success,
                metadata.ViaFallback,
                metadata.ElapsedMs,
                metadata.StartedAtUtc,
                metadata.CompletedAtUtc,
                metadata.DeadlineUtc,
                metadata.ResponseId,
                metadata.Usage?.InputTokens,
                metadata.Usage?.CachedInputTokens,
                metadata.Usage?.OutputTokens,
                metadata.Usage?.ReasoningTokens,
                metadata.Error);
        }
        catch (Exception ex)
        {
            // Logging is diagnostic too; never let a provider call fail because its logger failed.
            logger.LogDebug(ex, "[LlmCallMetadata] Metadata exporter failed for {Stage}", metadata.Stage);
        }
    }

    /// <summary>
    ///     Records metadata now and admits full payload persistence without awaiting the worker.
    ///     The cancellation token belongs to the caller's request and is intentionally not used by
    ///     the background export: once admitted, diagnostic persistence is independent of request
    ///     cancellation.
    /// </summary>
    public Task CaptureAsync(LlmCallRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        DateTimeOffset completedAt = record.TimestampUtc;
        DateTimeOffset startedAt = completedAt - TimeSpan.FromMilliseconds(Math.Max(0, record.ElapsedMs));
        RecordMetadata(new LlmCallMetadata(
            record.JobId,
            record.Stage,
            record.Attempt,
            record.Model,
            record.ResponseId,
            record.StatusCode,
            record.Error is null && (record.StatusCode is null || record.StatusCode is >= 200 and < 300),
            record.ViaFallback,
            record.ElapsedMs,
            startedAt,
            completedAt,
            record.DeadlineUtc ?? DateTimeOffset.MaxValue,
            record.Usage,
            record.Error));

        if (!fullPayloads || string.IsNullOrWhiteSpace(record.JobId))
            return Task.CompletedTask;

        bool enqueued = false;
        bool shouldLogDrop = false;
        long dropCount = 0;
        lock (queueLock)
        {
            if (disposed != 0)
            {
                shouldLogDrop = true;
                dropCount = Interlocked.Increment(ref dropped);
            }
            else
            {
                if (pending == 0)
                    idle = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                pending++;
                if (queue.Writer.TryWrite(record))
                {
                    enqueued = true;
                    worker ??= Task.Run(ProcessQueueAsync);
                }
                else
                {
                    pending--;
                    shouldLogDrop = true;
                    dropCount = Interlocked.Increment(ref dropped);
                    if (pending == 0)
                        idle.TrySetResult(true);
                }
            }
        }

        if (shouldLogDrop)
        {
            logger.LogWarning(
                "[LlmCallCapture] Full-payload queue is full or closed; dropped record for JobId={JobId}, " +
                "Stage={Stage}, Attempt={Attempt}, Dropped={Dropped}, Capacity={Capacity}",
                record.JobId,
                record.Stage,
                record.Attempt,
                dropCount,
                LlmCaptureOptions.QueueCapacity);
        }

        // Keep the explicit local so this method's contract is obvious to reviewers and analyzers:
        // admission is a completed in-memory operation, not a task representing blob persistence.
        _ = enqueued;
        return Task.CompletedTask;
    }

    /// <summary>Test/host hook that waits for already-admitted records without adding caller latency.</summary>
    internal Task FlushAsync()
    {
        lock (queueLock)
            return pending == 0 ? Task.CompletedTask : idle.Task;
    }

    /// <summary>
    ///     Provides the synchronous disposal path required by <see cref="IDisposable.Dispose" />.
    ///     The capture worker is drained before returning so a test scope or host shutdown cannot
    ///     strand background work. Production hosts that support async disposal use
    ///     <see cref="DisposeAsync" /> directly.
    /// </summary>
    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Task? runningWorker;
        lock (queueLock)
        {
            if (disposed != 0)
                return;

            disposed = 1;
            queue.Writer.TryComplete();
            runningWorker = worker;
        }

        if (runningWorker is not null)
            await runningWorker.ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (LlmCallRecord record in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await PersistAsync(record).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "[LlmCallCapture] Failed to persist call record for job {JobId}, stage {Stage}",
                        record.JobId,
                        record.Stage);
                }
                finally
                {
                    MarkProcessed();
                }
            }
        }
        catch (Exception ex)
        {
            // A reader failure must not strand FlushAsync waiters or kill future diagnostics
            // silently. Drain any records still in the channel as dropped work.
            logger.LogError(ex, "[LlmCallCapture] Capture worker stopped unexpectedly");
            while (queue.Reader.TryRead(out _))
                MarkProcessed();
        }
    }

    private void MarkProcessed()
    {
        lock (queueLock)
        {
            if (pending > 0)
                pending--;
            if (pending == 0)
                idle.TrySetResult(true);
        }
    }

    private async Task PersistAsync(LlmCallRecord record)
    {
        var blobServiceClient = serviceProvider.GetRequiredService<BlobServiceClient>();
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync().ConfigureAwait(false);

        string blobName =
            $"{record.JobId}/{record.TimestampUtc:HHmmss.fff}-{record.Stage}-attempt{record.Attempt}.json";

        string json = JsonSerializer.Serialize(record, SerializerOptions);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            BinaryData.FromString(json),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }).ConfigureAwait(false);

        await PruneOldJobsAsync(containerClient).ConfigureAwait(false);
    }

    /// <summary>
    ///     Keeps at most <see cref="DebugOptions.MaxCapturedJobs" /> distinct job folders, newest
    ///     first by each folder's most-recently-written blob. Re-lists the container on every call
    ///     rather than keeping an in-memory index, so concurrent Function instances converge on the
    ///     same result.
    /// </summary>
    private async Task PruneOldJobsAsync(BlobContainerClient containerClient)
    {
        var newestPerJob = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        await foreach (var blobItem in containerClient.GetBlobsAsync(
                           BlobTraits.None, BlobStates.None, null, CancellationToken.None).ConfigureAwait(false))
        {
            int slashIndex = blobItem.Name.IndexOf('/');
            if (slashIndex <= 0)
                continue;
            string jobFolder = blobItem.Name[..slashIndex];
            var modified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue;
            if (!newestPerJob.TryGetValue(jobFolder, out var existing) || modified > existing)
                newestPerJob[jobFolder] = modified;
        }

        if (newestPerJob.Count <= debugOptions.MaxCapturedJobs)
            return;

        var jobsToDelete = newestPerJob
            .OrderByDescending(kv => kv.Value)
            .Skip(debugOptions.MaxCapturedJobs)
            .Select(kv => kv.Key);

        foreach (string jobFolder in jobsToDelete)
        {
            await foreach (var blobItem in containerClient.GetBlobsAsync(
                               BlobTraits.None, BlobStates.None, $"{jobFolder}/", CancellationToken.None).ConfigureAwait(false))
                await containerClient.DeleteBlobIfExistsAsync(blobItem.Name).ConfigureAwait(false);

            logger.LogInformation(
                "[LlmCallCapture] Pruned captured LLM calls for job {JobId} (cap={Cap})",
                jobFolder,
                debugOptions.MaxCapturedJobs);
        }
    }

    private static TaskCompletionSource<bool> CreateCompletedIdle()
    {
        var source = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }
}
