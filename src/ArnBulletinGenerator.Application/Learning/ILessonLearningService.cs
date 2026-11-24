using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Learning;

/// <summary>
///     Turns one published operator script edit into station-lesson store changes: diff the
///     scripts deterministically, ask the isolated summariser whether the edit generalises, and
///     apply the create/reinforce/supersede verdict. Expected content outcomes are acknowledged
///     as successful results; operational failures flow to the queue boundary for retry/poison
///     routing.
/// </summary>
public interface ILessonLearningService
{
    Task<Result> ProcessAsync(LessonLearningQueueMessage message, CancellationToken cancellationToken = default);
}
