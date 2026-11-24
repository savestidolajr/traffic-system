namespace ArnBulletinGenerator.Domain.Constants;

/// <summary>
///     The queue names, and the ONLY source of them. Consumed by the <c>QueueTrigger</c>
///     attributes, <c>QueuePublisher</c>, <c>QueueDepthMetricsTrigger</c>, and mirrored by
///     the queues provisioned in <c>deployment/modules/storage.bicep</c>.
///     <para>
///         There used to be a second source: a bound <c>QueueOptions</c> that the publisher and
///         depth metric read while the triggers hardcoded literals. Nothing ever set the config
///         keys to anything but these values, and divergence would have been silent — the
///         publisher writing to a queue no trigger listens on, with no error. Deleted 2026-07-31.
///     </para>
/// </summary>
public static class QueueNames
{
    public const string Broadcast = "broadcast-queue";
    public const string Regeneration = "regeneration-queue";

    /// <summary>Published operator script edits awaiting station-lesson learning.</summary>
    public const string LessonLearning = "lesson-learning-queue";

    /// <summary>Durable activity audit messages.</summary>
    public const string ActivityAudit = "activity-audit";

    /// <summary>Dead-letter queue for activity audit messages.</summary>
    public const string ActivityAuditPoison = ActivityAudit + "-poison";

    /// <summary>Dead-letter queue for lesson-learning messages.</summary>
    public const string LessonLearningPoison = LessonLearning + "-poison";

    /// <summary>Durable commands that deliver approved or covered audio to playout.</summary>
    public const string Delivery = "delivery-queue";

    /// <summary>Dead-letter queue for delivery commands.</summary>
    public const string DeliveryPoison = Delivery + "-poison";


    /// <summary>Dead-letter queue the host moves a message to after <c>maxDequeueCount</c> failures.</summary>
    public const string BroadcastPoison = Broadcast + "-poison";

    /// <inheritdoc cref="BroadcastPoison" />
    public const string RegenerationPoison = Regeneration + "-poison";
}
