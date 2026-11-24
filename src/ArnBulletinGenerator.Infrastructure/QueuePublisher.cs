using System.Collections.Concurrent;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure;

/// <summary>
///     Implementation of IQueuePublisher using Azure Storage Queues.
/// </summary>
public sealed class QueuePublisher(
    QueueServiceClient queueServiceClient,
    ILogger<QueuePublisher> logger) : IQueuePublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = JsonDefaults.CaseInsensitive;

    // Queue existence is a once-per-process concern: CreateIfNotExistsAsync on an already
    // provisioned queue logs a 409 SDK warning on EVERY dispatch (the poller dispatches at
    // least once per minute). Same flag+lock shape as TableRepositoryBase; this class is
    // registered as a singleton so the set genuinely spans the process. Each queue name is
    // ensured once (the publisher serves Broadcast and Regeneration), the create still happens
    // on a cold start, and real errors still propagate.
    private readonly SemaphoreSlim _ensureQueueLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _ensuredQueues = new();

    private async Task EnsureQueueExistsAsync(QueueClient queueClient, CancellationToken cancellationToken)
    {
        if (_ensuredQueues.ContainsKey(queueClient.Name)) return;

        await _ensureQueueLock.WaitAsync(cancellationToken);
        try
        {
            if (_ensuredQueues.ContainsKey(queueClient.Name)) return;

            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _ensuredQueues.TryAdd(queueClient.Name, 0);
        }
        finally
        {
            _ensureQueueLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> PublishBroadcastMessageAsync(BroadcastQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return await PublishAsync(QueueNames.Broadcast, message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> PublishRegenerationMessageAsync(RegenerationQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return await PublishAsync(QueueNames.Regeneration, message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> PublishLessonLearningMessageAsync(LessonLearningQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return await PublishAsync(QueueNames.LessonLearning, message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> PublishDeliveryMessageAsync(DeliveryQueueCommand message,
        CancellationToken cancellationToken = default)
    {
        return await PublishAsync(QueueNames.Delivery, message, cancellationToken);
    }

    private async Task<Result> PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken)
    {
        try
        {
            var queueClient = queueServiceClient.GetQueueClient(queueName);
            await EnsureQueueExistsAsync(queueClient, cancellationToken);
            await queueClient.SendMessageAsync(
                JsonSerializer.Serialize(message, SerializerOptions), cancellationToken);
            logger.LogInformation("Successfully published message to queue '{QueueName}'", queueName);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish message to queue '{QueueName}'", queueName);
            return Result.Failure($"Failed to publish message to queue '{queueName}': {ex.Message}");
        }
    }
}
