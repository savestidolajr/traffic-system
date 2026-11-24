namespace ArnBulletinGenerator.Domain.Contracts.Messages;

/// <summary>
///     One published operator script edit awaiting station-lesson learning. Enqueued by
///     JobService.PublishJobAsync only after playout delivery succeeded; consumed best-effort
///     by LessonLearningQueueTrigger.
/// </summary>
public sealed record LessonLearningQueueMessage(
    string JobId,
    string Station,
    string OriginalScript,
    string PublishedScript,
    DateTime CapturedAtUtc,
    string By);
