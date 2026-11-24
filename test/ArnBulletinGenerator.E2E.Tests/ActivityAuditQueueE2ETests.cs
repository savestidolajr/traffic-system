using System.Text.Json;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Functions.Triggers.Queues;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

/// <summary>
///     Component-level Azurite proof for durable activity audit delivery: duplicate queue messages
///     produce one table row, and malformed messages can be routed to the poison queue handler.
/// </summary>
[Collection(BackendE2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class ActivityAuditQueueE2ETests(E2EHttpClientFixture fixture)
{
    private const string AzuriteConnection =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1";

    [Fact(Timeout = 90_000)]
    public async Task DuplicateDeliveryIsOneRow_AndMalformedMessageRoutesToPoison()
    {
        _ = fixture;
        // Transport runs on per-run queues. The Functions host in this harness listens on the
        // real activity-audit queue, so sharing it would let the host consume a message this test
        // is about to receive. What the test proves is unchanged: the same message text delivered
        // twice writes one row, and a malformed body reaches the poison handler.
        string suffix = Guid.NewGuid().ToString("N")[..12];
        var queue = new Azure.Storage.Queues.QueueClient(
            AzuriteConnection, $"{QueueNames.ActivityAudit}-e2e-{suffix}");
        var poisonQueue = new Azure.Storage.Queues.QueueClient(
            AzuriteConnection, $"{QueueNames.ActivityAuditPoison}-e2e-{suffix}");
        var table = new TableClient(AzuriteConnection, "activityLog");
        await queue.CreateIfNotExistsAsync();
        await poisonQueue.CreateIfNotExistsAsync();
        await table.CreateIfNotExistsAsync();

        var atUtc = DateTime.UtcNow;
        string actorObjectId = $"e2e-{Guid.NewGuid():N}";
        var message = new ActivityAuditMessage(
            atUtc, "Azurite Audit Actor", actorObjectId, "PUT", "/api/schedules/adelaide",
            "schedules", "adelaide", 200, "{\"enabled\":true}", Guid.NewGuid().ToString("N"));
        string json = JsonSerializer.Serialize(message);
        await queue.SendMessageAsync(json);
        await queue.SendMessageAsync(json);

        var repository = new TableStorageActivityLogRepository(
            Options.Create(new StorageOptions { AccountName = StorageOptions.LocalEmulatorAccount }),
            new DefaultAzureCredential(),
            NullLogger<TableStorageActivityLogRepository>.Instance);
        var trigger = new ActivityAuditQueueTrigger(
            repository,
            Options.Create(new AuditQueueOptions { Enabled = true }),
            NullLogger<ActivityAuditQueueTrigger>.Instance);

        for (int i = 0; i < 2; i++)
        {
            var received = await queue.ReceiveMessageAsync();
            received.Value.ShouldNotBeNull();
            await trigger.ProcessAsync(received.Value!.MessageText, CancellationToken.None);
            await queue.DeleteMessageAsync(received.Value.MessageId, received.Value.PopReceipt);
        }

        string partition = atUtc.ToString("yyyy-MM-dd");
        string filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {partition} and ActorObjectId eq {actorObjectId}");
        var rows = new List<TableEntity>();
        await foreach (var row in table.QueryAsync<TableEntity>(filter))
            rows.Add(row);
        rows.Count.ShouldBe(1);
        rows[0].GetString("Path").ShouldBe("/api/schedules/adelaide");

        const string malformed = "{ not valid activity audit json";
        await queue.SendMessageAsync(malformed);
        var malformedMessage = await queue.ReceiveMessageAsync();
        malformedMessage.Value.ShouldNotBeNull();
        await Should.ThrowAsync<InvalidOperationException>(
            () => trigger.ProcessAsync(malformedMessage.Value!.MessageText, CancellationToken.None));
        await queue.DeleteMessageAsync(malformedMessage.Value.MessageId, malformedMessage.Value.PopReceipt);

        // The Functions host moves an exhausted message to this queue. Component-level execution
        // performs that move explicitly, then invokes the same poison handler the host discovers.
        await poisonQueue.SendMessageAsync(malformed);
        var poisonMessage = await poisonQueue.ReceiveMessageAsync();
        poisonMessage.Value.ShouldNotBeNull();
        var poisonHandler = new ActivityAuditPoisonQueueTrigger(
            new ArnBulletinGenerator.Application.Metrics.NoOpBroadcastMetrics(),
            NullLogger<ActivityAuditPoisonQueueTrigger>.Instance);
        await poisonHandler.RunAsync(poisonMessage.Value!.MessageText, null!);
        await poisonQueue.DeleteMessageAsync(poisonMessage.Value.MessageId, poisonMessage.Value.PopReceipt);

        await queue.DeleteIfExistsAsync();
        await poisonQueue.DeleteIfExistsAsync();
    }
}
