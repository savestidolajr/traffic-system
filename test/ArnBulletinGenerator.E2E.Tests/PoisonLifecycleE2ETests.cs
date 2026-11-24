using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

/// <summary>
///     Proves the complete broadcast retry-to-poison lifecycle through the real Functions
///     queue listener and poison trigger: repeated dequeue attempts, retry exhaustion,
///     poison message consumption, terminal Failed job, critical alert, and poison metric.
/// </summary>
[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class PoisonLifecycleE2ETests(E2EHttpClientFixture fixture) : IAsyncLifetime
{
    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1";

    private const string TableName = "radioJobs";
    private const string QueueName = "broadcast-queue";
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(120);

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Timeout = 180_000)]
    public async Task CorruptMessage_ExhaustsRetries_MovesToPoisonQueue_FailsJob()
    {
        // ── 1. Create a normal job and let it reach WaitingApproval ──
        var jobId = await BackendE2ETestSupport.CreateBroadcastAsync(fixture);
        await BackendE2ETestSupport.WaitForJobStatusAsync(fixture, jobId, TimeSpan.FromSeconds(60), "WaitingApproval");

        // ── 2. Reset the job status back to Requested via ETag-conditioned table merge ──
        // Jobs live under per-market partitions (Job_{market}) since the 2026-07-16
        // re-partition; resolve the real partition through the JobIndex lookup row,
        // exactly like the repository does.
        var tableClient = new TableClient(AzuriteConnection, TableName);
        await tableClient.CreateIfNotExistsAsync();

        var index = await tableClient.GetEntityAsync<TableEntity>("JobIndex", jobId);
        string market = index.Value.GetString("Market") ?? "";
        string partition = "Job_" + (string.IsNullOrWhiteSpace(market) ? "unknown" : market.Trim().ToLowerInvariant());

        var entity = await tableClient.GetEntityAsync<TableEntity>(partition, jobId);
        var current = entity.Value;
        current["Status"] = "Requested";
        await tableClient.UpdateEntityAsync(current, current.ETag, TableUpdateMode.Replace);

        // ── 3. Enqueue a corrupt message: Coverage=null + RadiusKm=0 → mapping fails every dequeue ──
        var corruptMessage = new
        {
            JobId = jobId,
            MarketId = "e2e-poison-test",
            Location = "Test Location",
            TargetDurationSeconds = 30,
            Latitude = 0.0,
            Longitude = 0.0,
            RadiusKm = 0.0 // CoverageArea.Create rejects radiusKm <= 0
        };

        var queueClient = new QueueClient(AzuriteConnection, QueueName);
        await queueClient.CreateIfNotExistsAsync();
        await queueClient.SendMessageAsync(
            JsonSerializer.Serialize(corruptMessage, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

        // ── 4. Poll GET job until terminal Failed ──
        JsonElement latest = default;
        await Eventually.UntilAsync(async () =>
        {
            latest = await BackendE2ETestSupport.GetJobAsync(fixture, jobId);
            var status = latest.StringProp("status", "Status");
            return string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
        }, PollTimeout, TimeSpan.FromSeconds(2),
            $"Job {jobId} did not reach Failed. Latest status: {latest}");

        // ── 5. Assert: failure.message includes max-dequeue poison reason ──
        var failure = latest.Prop("failure", "Failure");
        failure.HasValue.ShouldBeTrue("Response should contain a failure object");
        var failMsg = failure.Value.StringProp("message", "Message");
        failMsg.ShouldNotBeNullOrWhiteSpace("failure.message should be set");
        failMsg.ShouldContain("max dequeue", Case.Insensitive);

        // ── 6. Assert: failure.stage maps FailedAtStatus.Requested to REQUESTING ──
        var failStage = failure.Value.StringProp("stage", "Stage");
        failStage.ShouldNotBeNullOrWhiteSpace("failure.stage should be set");
        failStage.ShouldBe("REQUESTING");

        // ── 7. Assert: status history contains Failed ──
        latest.ContainsText("Failed").ShouldBeTrue("Status history should contain Failed entry");

        // ── 8. Read host log and assert lifecycle markers ──
        var logPath = Environment.GetEnvironmentVariable("ARN_E2E_FUNC_LOG_PATH");
        logPath.ShouldNotBeNullOrWhiteSpace("ARN_E2E_FUNC_LOG_PATH must be set by e2e-backend.sh");

        var logContent = await ReadLogFileWithRetryAsync(logPath);

        // At least three job-specific corrupt/processing attempt markers
        int corruptCount = CountOccurrences(logContent, jobId);
        corruptCount.ShouldBeGreaterThanOrEqualTo(3,
            $"Expected at least 3 dequeue attempt log lines for job {jobId}, found {corruptCount}");

        // [PoisonQueue] consumption marker
        logContent.ShouldContain("[PoisonQueue]", Case.Insensitive,
            "Host log should contain [PoisonQueue] consumption marker");

        // [E2E-Alert] Critical marker (from FakeNotificationService)
        logContent.ShouldContain("[E2E-Alert]", Case.Insensitive,
            "Host log should contain [E2E-Alert] critical alert marker");

        // [E2E-Metric] PoisonQueue.Broadcast marker
        logContent.ShouldContain("[E2E-Metric]", Case.Insensitive,
            "Host log should contain [E2E-Metric] poison metric marker");
        logContent.ShouldContain("PoisonQueue.Broadcast", Case.Insensitive,
            "Host log should contain PoisonQueue.Broadcast metric stage");
    }

    /// <summary>
    ///     Reads the func host log with retry-safe file access to handle
    ///     concurrent writes from the running Functions host process.
    /// </summary>
    private static async Task<string> ReadLogFileWithRetryAsync(string path, int maxRetries = 5)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
            }
        }

        // Final attempt — let it throw naturally
        await using var finalStream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var finalReader = new StreamReader(finalStream, Encoding.UTF8);
        return await finalReader.ReadToEndAsync();
    }

    /// <summary>
    ///     Counts log lines containing the job ID that also contain the corrupt/processing
    ///     attempt pattern thrown by BroadcastQueueTrigger when ToBroadcastRequest fails.
    /// </summary>
    private static int CountOccurrences(string log, string jobId) =>
        log.Split('\n')
            .Count(line =>
                line.Contains(jobId, StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("Corrupt", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Corrupt broadcast queue message", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Processing broadcast", StringComparison.OrdinalIgnoreCase)));
}
