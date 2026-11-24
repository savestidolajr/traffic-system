using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Interface for publishing messages to Azure Storage Queues.
///     Abstracts queue operations to maintain Clean Architecture boundaries.
/// </summary>
public interface IQueuePublisher
{
    /// <summary>
    ///     Publishes a broadcast orchestration message to the broadcast queue.
    /// </summary>
    /// <param name="message">The broadcast queue message to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> PublishBroadcastMessageAsync(BroadcastQueueMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Publishes a regeneration message to the regeneration queue.
    /// </summary>
    /// <param name="message">The regeneration queue message to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> PublishRegenerationMessageAsync(RegenerationQueueMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Publishes a published-edit observation to the lesson-learning queue.
    /// </summary>
    /// <param name="message">The lesson-learning queue message to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> PublishLessonLearningMessageAsync(LessonLearningQueueMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes a durable playout delivery command.</summary>
    Task<Result> PublishDeliveryMessageAsync(DeliveryQueueCommand message,
        CancellationToken cancellationToken = default);
}
